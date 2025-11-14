using UnityEngine;

namespace Encounter.Mapping
{
    public class PitchToHeightMapper : MonoBehaviour
    {
        [Header("Pitch Range (Hz)")]
        [Tooltip("最低音の周波数（Hz）。大人の男性の音域の下限")]
        public float pitchMinHz = 80f; // 大人の男性の音域の下限
        [Tooltip("最高音の周波数（Hz）。小学生の音域の上限")]
        public float pitchMaxHz = 350f; // 小学生の音域の上限

        [Header("DMX Height")]
        [Tooltip("上限（0 = 一番上、約3メートル）")]
        public int dmxMin = 0;
        [Tooltip("下限（100 = 一番下、床）")]
        public int dmxMax = 100;

        [Tooltip("高さスムージング秒（小さいほど反応が速い、0で即座に反映）")]
        public float smoothing = 0.1f; // 実際のホイストの動きに合わせて調整

        [Tooltip("速度リミット（DMX値/秒、0で無制限）。実際のホイストの動きに合わせて調整")]
        public float maxVelocity = 200f; // 実際のホイストの動きに合わせて設定

        [Tooltip("加速度リミット（DMX値/秒²、0で無制限）。ホイストの物理的な加速性能に合わせて調整")]
        public float maxAcceleration = 800f; // 実際のホイストの動きに合わせて設定

        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        private float _currentDmx = 0f; // 初期値を上段に設定（DMX 0 = 約3メートル）。Start()またはReset()で正しく設定される
        private float _currentVelocity = 0f;
        private float _lastTargetDmx = float.NaN; // 最後の目標DMX値（NaN = まだ音が検出されていない）
        private float _startTime = 0f; // 開始時刻（デバッグ用）
        private System.Collections.Generic.List<float> _pitchHistory = new System.Collections.Generic.List<float>(); // 最初の数秒間のピッチ履歴

        public int MapPitchHzToDmx(float pitchHz)
        {
            // 開始時刻を記録（最初の呼び出し時のみ）
            if (_startTime == 0f)
            {
                _startTime = Time.time;
            }

            float targetDmx;
            float elapsedTime = Time.time - _startTime;

            if (pitchHz <= 0f)
            {
                // 最初の5秒間はピッチ履歴を記録
                if (elapsedTime < 5f)
                {
                    _pitchHistory.Add(pitchHz);
                }

                // 無効なピッチ値の場合は、最後の目標値が設定されている場合のみその値に向かって移動
                // まだ音が検出されていない場合は、現在位置を維持（移動しない）
                if (float.IsNaN(_lastTargetDmx))
                {
                    // まだ音が検出されていない場合は、現在位置を維持
                    if (enableDebugLog && elapsedTime < 5f && Time.frameCount % 30 == 0) // 最初の5秒間は頻繁にログ出力
                    {
                        Debug.Log($"[PitchToHeightMapper] 無音 (経過時間: {elapsedTime:F2}秒) - 現在位置を維持: DMX {_currentDmx:F1}");
                    }
                    return Mathf.RoundToInt(_currentDmx);
                }
                else
                {
                    // 最後の目標値に向かって移動し続ける
                    targetDmx = _lastTargetDmx;
                    if (enableDebugLog && elapsedTime < 5f && Time.frameCount % 30 == 0) // 最初の5秒間は頻繁にログ出力
                    {
                        Debug.Log($"[PitchToHeightMapper] 無音 (経過時間: {elapsedTime:F2}秒) - 最後の目標値({_lastTargetDmx:F1})に向かって移動中, 現在: {_currentDmx:F1}");
                    }
                }
            }
            else
            {
                // 最初の5秒間はピッチ履歴を記録
                if (elapsedTime < 5f)
                {
                    _pitchHistory.Add(pitchHz);
                }

                // ピッチが範囲内かチェック（範囲外の場合は無視）
                if (pitchHz < pitchMinHz || pitchHz > pitchMaxHz)
                {
                    // 範囲外のピッチは無視し、最後の目標値を維持
                    if (float.IsNaN(_lastTargetDmx))
                    {
                        // まだ有効なピッチが検出されていない場合は、現在位置を維持
                        if (enableDebugLog && elapsedTime < 5f && Time.frameCount % 30 == 0)
                        {
                            Debug.Log($"[PitchToHeightMapper] 範囲外のピッチ: {pitchHz:F1}Hz (範囲: {pitchMinHz}-{pitchMaxHz}) - 無視 (経過時間: {elapsedTime:F2}秒)");
                        }
                        return Mathf.RoundToInt(_currentDmx);
                    }
                    else
                    {
                        // 最後の目標値を維持
                        targetDmx = _lastTargetDmx;
                        if (enableDebugLog && elapsedTime < 5f && Time.frameCount % 30 == 0)
                        {
                            Debug.Log($"[PitchToHeightMapper] 範囲外のピッチ: {pitchHz:F1}Hz (範囲: {pitchMinHz}-{pitchMaxHz}) - 最後の目標値({_lastTargetDmx:F1})を維持 (経過時間: {elapsedTime:F2}秒)");
                        }
                    }
                }
                else
                {
                    // 範囲内のピッチのみ処理
                    // 1. Hz -> 0..1: t = saturate((pitchHz - pitchMinHz) / (pitchMaxHz - pitchMinHz))
                    // 高い音（pitchMaxHz）→ t = 1 → DMX 0（上段）
                    // 低い音（pitchMinHz）→ t = 0 → DMX 100（下段）
                    float t = Mathf.Clamp01((pitchHz - pitchMinHz) / (pitchMaxHz - pitchMinHz));

                    // 2. 0..1 -> dmxMin..dmxMax（逆マッピング：高い音 → 低いDMX値）
                    // t = 1（高い音）→ DMX 0（上段）
                    // t = 0（低い音）→ DMX 100（下段）
                    targetDmx = Mathf.Lerp(dmxMax, dmxMin, t);
                    _lastTargetDmx = targetDmx; // 最後の目標値を保存（範囲内のピッチのみ）

                    // デバッグログ（最初の5秒間は頻繁に、その後は1秒に1回）
                    int logInterval = elapsedTime < 5f ? 30 : 60;
                    if (enableDebugLog && Time.frameCount % logInterval == 0)
                    {
                        Debug.Log($"[PitchToHeightMapper] ピッチ: {pitchHz:F1}Hz (範囲: {pitchMinHz}-{pitchMaxHz}) → t: {t:F3} → DMX: {targetDmx:F1} (経過時間: {elapsedTime:F2}秒)");
                    }
                }
            }

            // 3. スムージングと速度/加速度リミットを適用
            // 無音時で、まだ有効なピッチが検出されていない場合は移動しない
            if (pitchHz <= 0f && float.IsNaN(_lastTargetDmx))
            {
                return Mathf.RoundToInt(_currentDmx);
            }

            // 無音時で、最後の目標値が0（最上段）の場合は、現在位置を維持（最上段に戻らない）
            if (pitchHz <= 0f && !float.IsNaN(_lastTargetDmx) && _lastTargetDmx <= dmxMin + 0.1f)
            {
                // 最後の目標値が最上段付近の場合は、現在位置を維持
                return Mathf.RoundToInt(_currentDmx);
            }

            _currentDmx = ApplySmoothingAndLimits(targetDmx);

            // 最初の5秒間が終了したら、ピッチ履歴をログ出力
            if (enableDebugLog && elapsedTime >= 5f && _pitchHistory.Count > 0)
            {
                Debug.Log($"[PitchToHeightMapper] === 最初の5秒間のピッチ履歴 (合計{_pitchHistory.Count}サンプル) ===");
                int validPitchCount = 0;
                float minPitch = float.MaxValue;
                float maxPitch = float.MinValue;
                float sumPitch = 0f;
                foreach (float p in _pitchHistory)
                {
                    if (p > 0f)
                    {
                        validPitchCount++;
                        if (p < minPitch) minPitch = p;
                        if (p > maxPitch) maxPitch = p;
                        sumPitch += p;
                    }
                }
                if (validPitchCount > 0)
                {
                    float avgPitch = sumPitch / validPitchCount;
                    Debug.Log($"[PitchToHeightMapper] 有効なピッチ: {validPitchCount}個, 最小: {minPitch:F1}Hz, 最大: {maxPitch:F1}Hz, 平均: {avgPitch:F1}Hz");
                }
                else
                {
                    Debug.Log($"[PitchToHeightMapper] 有効なピッチが検出されませんでした");
                }
                _pitchHistory.Clear(); // 履歴をクリア（再出力を防ぐ）
            }

            return Mathf.RoundToInt(_currentDmx);
        }

        private float ApplySmoothingAndLimits(float targetDmx)
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return _currentDmx;

            // スムージングが0の場合は即座に反映
            if (smoothing <= 0f)
            {
                _currentDmx = targetDmx;
                _currentVelocity = 0f;
                return Mathf.Clamp(_currentDmx, dmxMin, dmxMax);
            }

            // 目標への差分
            float delta = targetDmx - _currentDmx;

            // スムージング（指数移動平均）
            float smoothingFactor = 1f - Mathf.Exp(-deltaTime / smoothing);
            float smoothedDelta = delta * smoothingFactor;

            // 速度リミット（0の場合は無制限）
            if (maxVelocity > 0f)
            {
                float maxDeltaByVelocity = maxVelocity * deltaTime;
                if (Mathf.Abs(smoothedDelta) > maxDeltaByVelocity)
                {
                    smoothedDelta = Mathf.Sign(smoothedDelta) * maxDeltaByVelocity;
                }
            }

            // 加速度リミット（0の場合は無制限）
            if (maxAcceleration > 0f)
            {
                float targetVelocity = smoothedDelta / deltaTime;
                float velocityDelta = targetVelocity - _currentVelocity;
                float maxVelocityDelta = maxAcceleration * deltaTime;
                if (Mathf.Abs(velocityDelta) > maxVelocityDelta)
                {
                    velocityDelta = Mathf.Sign(velocityDelta) * maxVelocityDelta;
                }
                _currentVelocity += velocityDelta;
            }
            else
            {
                // 加速度リミットがない場合は直接速度を設定
                _currentVelocity = smoothedDelta / deltaTime;
            }

            // 速度を適用
            float newDmx = _currentDmx + _currentVelocity * deltaTime;

            // 範囲クランプ
            return Mathf.Clamp(newDmx, dmxMin, dmxMax);
        }

        public void Reset()
        {
            _currentDmx = (float)dmxMin; // 上段から開始（DMX 0 = 約3メートル）
            _currentVelocity = 0f;
            _lastTargetDmx = float.NaN; // 最後の目標値をリセット（まだ音が検出されていない状態）
            _startTime = 0f; // 開始時刻をリセット
            _pitchHistory.Clear(); // ピッチ履歴をクリア
        }

        void Start()
        {
            // 初期値を上段に設定（DMX 0 = 約3メートル）
            if (_currentDmx < 0f || _currentDmx > 100f)
            {
                _currentDmx = (float)dmxMin; // 上段から開始
            }
        }
    }
}

