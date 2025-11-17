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

        [Tooltip("移動速度（DMX値/秒、Unity画面のシミュレーション用。DMX機器には影響しません）")]
        public float speed = 50f; // 等速運動の速度（Unity画面のシミュレーション用）

        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        private float _currentDmx = 0f; // シミュレーション用の現在位置（Unity画面表示用）
        private float _lastTargetDmx = float.NaN; // 最後の目標DMX値（NaN = まだ音が検出されていない）
        private float _lastPitchHz = 0f; // 最後に処理したピッチ値
        private float _startTime = 0f; // 開始時刻（デバッグ用）
        private System.Collections.Generic.List<float> _pitchHistory = new System.Collections.Generic.List<float>(); // 最初の数秒間のピッチ履歴

        /// <summary>
        /// ピッチから最新の目標DMX値を取得（ディレイなし、DMX機器送信用）
        /// </summary>
        public int GetTargetDmxValue(float pitchHz)
        {
            UpdateTargetDmx(pitchHz);
            
            // 最新の目標値をそのまま返す（ディレイなし）
            if (float.IsNaN(_lastTargetDmx))
            {
                return Mathf.RoundToInt(_currentDmx);
            }
            return Mathf.RoundToInt(_lastTargetDmx);
        }

        /// <summary>
        /// ピッチからDMX値を取得（等速運動で移動した現在の値、Unity画面表示用）
        /// </summary>
        public int MapPitchHzToDmx(float pitchHz)
        {
            // 目標値を更新
            UpdateTargetDmx(pitchHz);
            
            // 等速運動で目標位置に向かって移動（Unity画面のシミュレーション用）
            UpdateSimulation();
            
            return Mathf.RoundToInt(_currentDmx);
        }

        /// <summary>
        /// ピッチから目標DMX値を計算して更新
        /// </summary>
        private void UpdateTargetDmx(float pitchHz)
        {
            // 開始時刻を記録（最初の呼び出し時のみ）
            if (_startTime == 0f)
            {
                _startTime = Time.time;
            }

            _lastPitchHz = pitchHz;
            float elapsedTime = Time.time - _startTime;

            if (pitchHz <= 0f)
            {
                // 最初の5秒間はピッチ履歴を記録
                if (elapsedTime < 5f)
                {
                    _pitchHistory.Add(pitchHz);
                }

                // 無効なピッチ値の場合は、最後の目標値を維持（新しい目標値は設定しない）
                if (float.IsNaN(_lastTargetDmx))
                {
                    // まだ音が検出されていない場合は、目標値を更新しない
                    if (enableDebugLog && elapsedTime < 5f && Time.frameCount % 30 == 0)
                    {
                        Debug.Log($"[PitchToHeightMapper] 無音 (経過時間: {elapsedTime:F2}秒) - 目標値未設定");
                    }
                    return;
                }
                // 最後の目標値を維持（targetDmxは更新しない）
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
                        // まだ有効なピッチが検出されていない場合は、目標値を更新しない
                        if (enableDebugLog && elapsedTime < 5f && Time.frameCount % 30 == 0)
                        {
                            Debug.Log($"[PitchToHeightMapper] 範囲外のピッチ: {pitchHz:F1}Hz (範囲: {pitchMinHz}-{pitchMaxHz}) - 無視 (経過時間: {elapsedTime:F2}秒)");
                        }
                        return;
                    }
                    // 最後の目標値を維持（targetDmxは更新しない）
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
                    float targetDmx = Mathf.Lerp(dmxMax, dmxMin, t);
                    _lastTargetDmx = targetDmx; // 最後の目標値を保存（範囲内のピッチのみ）

                    // デバッグログ（最初の5秒間は頻繁に、その後は1秒に1回）
                    int logInterval = elapsedTime < 5f ? 30 : 60;
                    if (enableDebugLog && Time.frameCount % logInterval == 0)
                    {
                        Debug.Log($"[PitchToHeightMapper] ピッチ: {pitchHz:F1}Hz (範囲: {pitchMinHz}-{pitchMaxHz}) → t: {t:F3} → DMX: {targetDmx:F1} (経過時間: {elapsedTime:F2}秒)");
                    }
                }
            }

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
        }

        /// <summary>
        /// 等速運動で目標位置に向かって移動（Unity画面のシミュレーション用）
        /// </summary>
        private void UpdateSimulation()
        {
            // 無音時で、まだ有効なピッチが検出されていない場合は移動しない
            if (_lastPitchHz <= 0f && float.IsNaN(_lastTargetDmx))
            {
                return;
            }

            // 無音時で、最後の目標値が0（最上段）の場合は、現在位置を維持（最上段に戻らない）
            if (_lastPitchHz <= 0f && !float.IsNaN(_lastTargetDmx) && _lastTargetDmx <= dmxMin + 0.1f)
            {
                return;
            }

            // 目標値が設定されている場合のみ移動
            if (!float.IsNaN(_lastTargetDmx))
            {
                _currentDmx = MoveTowardsTarget(_lastTargetDmx);
            }
        }

        private float MoveTowardsTarget(float targetDmx)
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return _currentDmx;

            // 速度が0の場合は即座に反映
            if (speed <= 0f)
            {
                return Mathf.Clamp(targetDmx, dmxMin, dmxMax);
            }

            // 目標位置までの距離
            float distance = targetDmx - _currentDmx;
            float distanceAbs = Mathf.Abs(distance);

            // 1フレームで移動できる距離
            float maxMoveDistance = speed * deltaTime;

            // 目標位置までの距離が移動可能距離より大きい場合は、移動可能距離だけ移動
            // そうでない場合は、目標位置に到達
            float newDmx;
            if (distanceAbs > maxMoveDistance)
            {
                newDmx = _currentDmx + Mathf.Sign(distance) * maxMoveDistance;
            }
            else
            {
                newDmx = targetDmx;
            }

            // 範囲クランプ
            return Mathf.Clamp(newDmx, dmxMin, dmxMax);
        }

        /// <summary>
        /// 目標DMX値を直接設定（グラフィックイコライザー風の計算結果などを設定する場合に使用）
        /// </summary>
        public void SetTargetDmxValue(int targetDmx)
        {
            _lastTargetDmx = Mathf.Clamp((float)targetDmx, dmxMin, dmxMax);
        }

        public void Reset()
        {
            _currentDmx = (float)dmxMin; // 上段から開始（DMX 0 = 約3メートル）
            _lastTargetDmx = float.NaN; // 最後の目標値をリセット（まだ音が検出されていない状態）
            _lastPitchHz = 0f; // 最後のピッチ値をリセット
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

        void Update()
        {
            // 毎フレーム、シミュレーションを更新（Unity画面表示用）
            UpdateSimulation();
        }
    }
}

