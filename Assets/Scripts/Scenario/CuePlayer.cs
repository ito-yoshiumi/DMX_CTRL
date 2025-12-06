using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Encounter.Scenario
{
    public class CuePlayer : MonoBehaviour
    {
        [Header("References")]
        public Encounter.DMX.KineticLightController controller;
        [Tooltip("音声入力の音量に応じて振幅を調整するために使用（オプション）")]
        public Encounter.Audio.AudioInputManager audioInputManager;

        [Header("Playback Settings")]
        [Tooltip("更新レート（fps）")]
        [Range(10, 60)]
        public int updateRate = 30;

        [Header("Note Mapping Settings")]
        [Tooltip("MIDIノート番号の最小値（高さ100/色変化の始点）")]
        public int minMidiNote = 48;
        [Tooltip("MIDIノート番号の最大値（高さ0/色変化の終点）")]
        public int maxMidiNote = 72;
        [Tooltip("ライトの最大移動速度（1秒あたりの高さ変化、0-100の範囲）")]
        [Range(10, 200)]
        public float maxMoveSpeed = 50f;
        
        [Header("TTS Wave Motion Settings")]
        [Tooltip("波の周波数（Hz）。大きいほど速く波打つ")]
        [Range(0.05f, 2.0f)]
        public float ttsWaveFrequency = 0.075f; // フィクスチャの物理的な動きに合わせてさらにゆっくりに
        [Tooltip("高さの波の最大振幅（0-100の範囲、音声入力時）")]
        [Range(10, 50)]
        public float ttsWaveHeightAmplitude = 30f;
        [Tooltip("無音時の振幅倍率（0-1の範囲、通常は0.25で4分の1）")]
        [Range(0f, 1f)]
        public float ttsSilentAmplitudeMultiplier = 0.25f; // 無音時は4分の1
        [Tooltip("高さの中央値（0-100の範囲）")]
        [Range(0, 100)]
        public float ttsWaveHeightCenter = 50f;
        [Tooltip("色の変化速度（色相の変化速度）")]
        [Range(0.1f, 2.0f)]
        public float ttsColorSpeed = 0.1f; // 色の変化もゆっくりに
        [Tooltip("高さの補間速度（0-1の範囲、大きいほど速く追従）")]
        [Range(0.1f, 1.0f)]
        public float ttsHeightLerpSpeed = 0.15f; // 高さの変化をより滑らかに
        [Tooltip("音声入力のRMS閾値（これ以下は無音とみなす）")]
        [Range(0f, 0.1f)]
        public float ttsVoiceThreshold = 0.02f; // 音声検出の閾値
        
        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        private Coroutine _currentPlayback;
        private Coroutine _currentTTSWaveMotion;
        private Dictionary<int, FixtureState> _currentStates = new Dictionary<int, FixtureState>();
        // 各フィクスチャの現在の高さを追跡（0-100の範囲、floatで滑らかな移動を実現）
        private Dictionary<int, float> _currentFixtureHeights = new Dictionary<int, float>();
        // 各フィクスチャに対して実行中の移動コルーチンを追跡（レースコンディション防止）
        private Dictionary<int, Coroutine> _activeMoveCoroutines = new Dictionary<int, Coroutine>();

        public void PlayCue(string resourcesPath)
        {
            // 既存の再生を停止
            if (_currentPlayback != null)
            {
                StopCoroutine(_currentPlayback);
                _currentPlayback = null;
            }

            // ResourcesからJSONを読み込み
            TextAsset ta = Resources.Load<TextAsset>(resourcesPath);
            if (ta == null)
            {
                Debug.LogError($"[CuePlayer] リソースが見つかりません: {resourcesPath}");
                return;
            }

            DmxCue cue = JsonUtility.FromJson<DmxCue>(ta.text);
            if (cue == null || cue.keyframes == null || cue.keyframes.Count == 0)
            {
                Debug.LogError($"[CuePlayer] 無効なキュー: {resourcesPath}");
                return;
            }

            if (controller == null)
            {
                Debug.LogError("[CuePlayer] KineticLightControllerが設定されていません。");
                return;
            }

            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] キュー読み込み完了: {resourcesPath} (キーフレーム数: {cue.keyframes.Count}, 長さ: {cue.length}秒)");
            }

            _currentPlayback = StartCoroutine(CoPlay(cue));
        }

        public void Stop()
        {
            if (_currentPlayback != null)
            {
                StopCoroutine(_currentPlayback);
                _currentPlayback = null;
            }
            StopTTSWaveMotion();
            _currentStates.Clear();
            // 高さの追跡は保持（次の再生で使用するため）
        }

        private IEnumerator CoPlay(DmxCue cue)
        {
            if (cue.keyframes.Count == 0) yield break;

            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] キュー再生開始 (キーフレーム数: {cue.keyframes.Count}, 長さ: {cue.length}秒)");
            }

            float startTime = Time.time;
            float interval = 1f / updateRate;
            float elapsed = 0f;

            // 最初のキーフレームで初期化
            if (cue.keyframes.Count > 0)
            {
                ApplyKeyframe(cue.keyframes[0]);
                if (enableDebugLog)
                {
                    Debug.Log($"[CuePlayer] 最初のキーフレーム適用 (t: {cue.keyframes[0].t}秒, フィクスチャ数: {cue.keyframes[0].fixtures?.Count ?? 0})");
                }
            }

            while (elapsed < cue.length)
            {
                elapsed = Time.time - startTime;
                float t = Mathf.Clamp01(elapsed / cue.length);

                // 現在の時刻に該当するキーフレーム間を補間
                InterpolateKeyframes(cue.keyframes, t * cue.length);

                // DMX送信
                controller.Apply();

                yield return new WaitForSeconds(interval);
            }

            // 最後のキーフレームを適用
            if (cue.keyframes.Count > 0)
            {
                ApplyKeyframe(cue.keyframes[cue.keyframes.Count - 1]);
                controller.Apply();
                if (enableDebugLog)
                {
                    Debug.Log($"[CuePlayer] 最後のキーフレーム適用 (t: {cue.keyframes[cue.keyframes.Count - 1].t}秒)");
                }
            }

            if (enableDebugLog)
            {
                Debug.Log("[CuePlayer] キュー再生完了");
            }

            _currentPlayback = null;
        }

        /// <summary>
        /// 全フィクスチャの高さをリセット（初期位置0に設定）
        /// </summary>
        public void ResetFixtureHeights()
        {
            if (controller == null)
            {
                Debug.LogError("[CuePlayer] ResetFixtureHeights: controller が null です");
                return;
            }

            int fixtureCount = controller.fixtures.Count;
            for (int i = 0; i < fixtureCount; i++)
            {
                // 高さを0（上）にリセット
                _currentFixtureHeights[i] = 0f;
                controller.SetFixtureHeight(i, 0);
                controller.SetFixtureColor(i, Color.black);
            }
            controller.Apply();
            
            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] ResetFixtureHeights: 全フィクスチャ（{fixtureCount}個）の高さを0にリセットしました");
            }
        }

        /// <summary>
        /// フィクスチャを目標位置まで等速運動で移動させるコルーチン
        /// </summary>
        private IEnumerator MoveFixtureToHeight(int fixtureIndex, float targetHeight)
        {
            // 現在の高さを取得（他のコルーチンが変更している可能性があるため、最新の値を取得）
            if (!_currentFixtureHeights.ContainsKey(fixtureIndex))
            {
                _currentFixtureHeights[fixtureIndex] = 0f;
            }
            
            // このコルーチン開始時点の高さを記録（移動中に他のコルーチンが変更する可能性があるため）
            float currentHeight = _currentFixtureHeights[fixtureIndex];
            float distance = Mathf.Abs(targetHeight - currentHeight);
            
            if (distance < 0.1f)
            {
                // 既に目標位置に近い場合は即座に完了
                _currentFixtureHeights[fixtureIndex] = targetHeight;
                controller.SetFixtureHeight(fixtureIndex, Mathf.RoundToInt(targetHeight));
                controller.Apply();
                yield break;
            }
            
            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] MoveFixtureToHeight: Fixture {fixtureIndex}, {currentHeight:F1} → {targetHeight:F1}, Distance: {distance:F1}, Speed: {maxMoveSpeed}");
            }
            
            // 等速運動で目標位置まで移動
            // 最初のフレームを待機して、コルーチン参照が確実に設定されるようにする
            yield return null;
            
            while (Mathf.Abs(_currentFixtureHeights[fixtureIndex] - targetHeight) > 0.1f)
            {
                // このコルーチンがまだ有効かどうかを確認（StopCoroutineで停止された場合）
                // この時点で、_activeMoveCoroutines[fixtureIndex]にはこのコルーチンの参照が設定されているはず
                // ただし、StopCoroutineが呼ばれると、辞書から削除されるか、別のコルーチンに置き換えられる
                // 直接比較はできないため、辞書にエントリが存在し、nullでないことを確認する
                if (!_activeMoveCoroutines.ContainsKey(fixtureIndex) || _activeMoveCoroutines[fixtureIndex] == null)
                {
                    // このコルーチンが停止された（別のコルーチンに置き換えられた、または削除された）
                    if (enableDebugLog)
                    {
                        Debug.Log($"[CuePlayer] MoveFixtureToHeight: Fixture {fixtureIndex}のコルーチンが停止されました（置き換えられました）。");
                    }
                    yield break;
                }
                
                float deltaTime = Time.deltaTime;
                if (deltaTime <= 0f) 
                {
                    yield return null;
                    continue;
                }
                
                // 1フレームで移動できる距離
                float maxMoveDistance = maxMoveSpeed * deltaTime;
                
                // 目標位置までの距離（最新の値を取得）
                float remainingDistance = targetHeight - _currentFixtureHeights[fixtureIndex];
                float remainingDistanceAbs = Mathf.Abs(remainingDistance);
                
                // 移動
                if (remainingDistanceAbs > maxMoveDistance)
                {
                    // 移動可能距離だけ移動
                    _currentFixtureHeights[fixtureIndex] += Mathf.Sign(remainingDistance) * maxMoveDistance;
                }
                else
                {
                    // 目標位置に到達
                    _currentFixtureHeights[fixtureIndex] = targetHeight;
                }
                
                // 範囲クランプ（0-100）
                _currentFixtureHeights[fixtureIndex] = Mathf.Clamp(_currentFixtureHeights[fixtureIndex], 0f, 100f);
                
                // DMXに送信
                int dmxHeight = Mathf.RoundToInt(_currentFixtureHeights[fixtureIndex]);
                controller.SetFixtureHeight(fixtureIndex, dmxHeight);
                controller.Apply();
                
                yield return null;
            }
            
            // 最終位置を確定（このコルーチンがまだ有効な場合のみ）
            if (_activeMoveCoroutines.ContainsKey(fixtureIndex) && _activeMoveCoroutines[fixtureIndex] != null)
            {
                _currentFixtureHeights[fixtureIndex] = targetHeight;
                controller.SetFixtureHeight(fixtureIndex, Mathf.RoundToInt(targetHeight));
                controller.Apply();
                
                if (enableDebugLog)
                {
                    Debug.Log($"[CuePlayer] MoveFixtureToHeight 完了: Fixture {fixtureIndex}, Height: {targetHeight:F1}");
                }
            }
        }

        /// <summary>
        /// ノート配列に基づいてキネティックライトの高さを事前に設定する（非推奨：移動時間を考慮しない）
        /// 現在はPlayNoteSequenceで移動時間を考慮した処理を行うため、このメソッドは簡素化
        /// </summary>
        public void PrewarmNotes(SingingNote[] notes)
        {
            if (enableDebugLog)
            {
                Debug.Log("[CuePlayer] PrewarmNotes() が呼ばれました（簡素化版）");
            }
            
            // フィクスチャの高さをリセット
            ResetFixtureHeights();
        }

        /// <summary>
        /// ノート配列に基づいてライトを移動させる（移動完了まで待機）
        /// </summary>
        public IEnumerator MoveFixturesToNotePositions(SingingNote[] notes)
        {
            if (controller == null || notes == null)
            {
                yield break;
            }
            
            int fixtureCount = controller.fixtures.Count;
            if (fixtureCount == 0)
            {
                yield break;
            }

            // 全ての有効なノートを抽出
            // 同じフィクスチャに複数のノートが割り当てられる場合、最後のノートの位置を使用（レースコンディション防止）
            Dictionary<int, (float targetHeight, SingingNote note)> fixtureTargets = new Dictionary<int, (float, SingingNote)>();
            int noteIndex = 0;
            int arrayIndex = 0;
            
            foreach (var note in notes)
            {
                if (!note.IsKeyNull && !string.IsNullOrEmpty(note.lyric))
                {
                    int targetFixture = noteIndex % fixtureCount;
                    
                    // 高さを計算（低音→低い、高音→高い）
                    float t = Mathf.InverseLerp(minMidiNote, maxMidiNote, note.key);
                    float targetHeight = Mathf.Lerp(100f, 0f, t);
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[CuePlayer] MoveFixturesToNotePositions: ノート[{arrayIndex}] '{note.lyric}' (key={note.key}) → フィクスチャ{targetFixture}, 高さ={targetHeight:F1}");
                    }
                    
                    // 同じフィクスチャに複数のノートが割り当てられる場合、最後のノートの位置で上書き
                    if (fixtureTargets.ContainsKey(targetFixture))
                    {
                        if (enableDebugLog)
                        {
                            Debug.Log($"[CuePlayer] 警告: フィクスチャ{targetFixture}は既にノート '{fixtureTargets[targetFixture].note.lyric}' に割り当てられています。'{note.lyric}' で上書きします。");
                        }
                    }
                    fixtureTargets[targetFixture] = (targetHeight, note);
                    noteIndex++;
                }
                arrayIndex++;
            }

            if (fixtureTargets.Count == 0)
            {
                yield break;
            }

            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] {fixtureTargets.Count}個のフィクスチャを移動します（ノート数: {noteIndex}個）。");
            }

            // 各フィクスチャに対して移動コルーチンを開始（レースコンディション防止のため、同じフィクスチャに対しては1つだけ）
            List<Coroutine> moveCoroutines = new List<Coroutine>();
            foreach (var kvp in fixtureTargets)
            {
                int fixtureIndex = kvp.Key;
                float targetHeight = kvp.Value.targetHeight;
                string noteLyric = kvp.Value.note.lyric;
                
                if (enableDebugLog)
                {
                    Debug.Log($"[CuePlayer] フィクスチャ{fixtureIndex}を高さ{targetHeight:F1}に移動開始（ノート: '{noteLyric}'）");
                }
                
                // 同じフィクスチャに対して既に実行中のコルーチンがあれば停止（レースコンディション防止）
                if (_activeMoveCoroutines.ContainsKey(fixtureIndex) && _activeMoveCoroutines[fixtureIndex] != null)
                {
                    Coroutine oldCoroutine = _activeMoveCoroutines[fixtureIndex];
                    StopCoroutine(oldCoroutine);
                    // 辞書から削除して、停止されたコルーチンが検知できるようにする
                    _activeMoveCoroutines.Remove(fixtureIndex);
                    if (enableDebugLog)
                    {
                        Debug.Log($"[CuePlayer] フィクスチャ{fixtureIndex}の既存の移動コルーチンを停止しました。");
                    }
                }
                
                // 移動中は消灯
                controller.SetFixtureColor(fixtureIndex, Color.black);
                
                // 各フィクスチャの移動を並列で開始
                Coroutine moveCoroutine = StartCoroutine(MoveFixtureToHeight(fixtureIndex, targetHeight));
                _activeMoveCoroutines[fixtureIndex] = moveCoroutine; // 実行中のコルーチンを記録
                moveCoroutines.Add(moveCoroutine);
            }
            controller.Apply();

            // 全ての移動が完了するまで待機
            foreach (var moveCoroutine in moveCoroutines)
            {
                yield return moveCoroutine;
            }

            // 完了したコルーチンをクリア
            _activeMoveCoroutines.Clear();

            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] 全フィクスチャの移動完了");
            }
        }

        /// <summary>
        /// ノート配列に基づいてライトを点灯させるコルーチンを開始
        /// </summary>
        public void PlayNoteSequence(SingingNote[] notes, float audioStartTime = 0f)
        {
            if (notes == null || controller == null) return;
            
            Stop();
            // 音声再生開始時刻が指定されていない場合は現在時刻を使用
            if (audioStartTime <= 0f)
            {
                audioStartTime = Time.time;
            }
            _currentPlayback = StartCoroutine(CoPlayNotes(notes, audioStartTime));
        }

        private IEnumerator CoPlayNotes(SingingNote[] notes, float audioStartTime)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] ノートシーケンス再生開始 (ノート数: {notes.Length}, 音声開始時刻: {audioStartTime:F3}s)");
            }

            int fixtureCount = controller.fixtures.Count;
            if (fixtureCount == 0)
            {
                Debug.LogError("[CuePlayer] CoPlayNotes: フィクスチャが0個です");
                yield break;
            }

            float frameToSeconds = 0.01f; // 1フレーム = 10ms (Voicevox/Neutrino仕様によるが、通常10ms)

            // ノートとタイミングのリストを作成（順序を保持）
            // 休符も含めて全てのノートのタイミングを計算
            List<(int fixtureIndex, float startTime, float duration, SingingNote note, bool isValidNote)> noteTimings = new List<(int, float, float, SingingNote, bool)>();
            float currentTime = 0f;
            int noteIndex = 0;
            
            foreach (var note in notes)
            {
                float duration = note.frame_length * frameToSeconds;
                
                if (!note.IsKeyNull && !string.IsNullOrEmpty(note.lyric))
                {
                    // 左から順番にフィクスチャを割り当て（0, 1, 2, 3, 4...）
                    int targetFixture = noteIndex % fixtureCount;
                    noteTimings.Add((targetFixture, currentTime, duration, note, true));
                    noteIndex++;
                }
                else
                {
                    // 休符も記録（タイミング計算のため）
                    noteTimings.Add((-1, currentTime, duration, note, false));
                }
                
                currentTime += duration;
            }

            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] ノートタイミング計算完了: {noteTimings.Count}個のタイミング（有効ノート: {noteIndex}個）");
                foreach (var timing in noteTimings)
                {
                    if (timing.isValidNote)
                    {
                        Debug.Log($"[CuePlayer]   '{timing.note.lyric}': Fixture {timing.fixtureIndex}, Start: {timing.startTime:F3}s, Duration: {timing.duration:F3}s");
                    }
                }
            }
            
            // 各ノートのタイミングで点灯する処理
            foreach (var timing in noteTimings)
            {
                if (!timing.isValidNote)
                {
                    // 休符の場合は待機のみ
                    yield return new WaitForSeconds(timing.duration);
                    continue;
                }
                
                // このノートの開始時刻まで待機
                float elapsedTime = Time.time - audioStartTime;
                float waitTime = timing.startTime - elapsedTime;
                
                if (waitTime > 0.001f) // 1ms以上の待機が必要な場合
                {
                    if (enableDebugLog)
                    {
                        Debug.Log($"[CuePlayer] ノート '{timing.note.lyric}' まで待機: {waitTime:F3}s (経過: {elapsedTime:F3}s, 目標: {timing.startTime:F3}s)");
                    }
                    yield return new WaitForSeconds(waitTime);
                }
                else if (waitTime < -0.1f)
                {
                    // タイミングが大幅に遅れている場合は警告
                    if (enableDebugLog)
                    {
                        Debug.LogWarning($"[CuePlayer] ノート '{timing.note.lyric}' のタイミングが遅れています: {Mathf.Abs(waitTime):F3}s");
                    }
                }
                
                // 色を計算（各ノートごとに異なる色を割り当て）
                // 左から順番に異なる色相を割り当て（虹色のグラデーション）
                // 5つのノートに対して、0.0(赤)から0.8(青紫)まで均等に色相を割り当て
                float hueStep = 0.8f / Mathf.Max(1, fixtureCount - 1); // フィクスチャ数で分割
                float hue = (timing.fixtureIndex * hueStep) % 1.0f; // フィクスチャインデックスに基づく色相
                Color color = Color.HSVToRGB(hue, 1.0f, 1.0f);

                if (enableDebugLog)
                {
                    float actualTime = Time.time - audioStartTime;
                    Debug.Log($"[CuePlayer] 点灯: '{timing.note.lyric}' (key={timing.note.key}), Fixture: {timing.fixtureIndex}, Color: {color} (Hue: {hue:F2}), Time: {actualTime:F3}s (目標: {timing.startTime:F3}s)");
                }

                // 点灯（明るく発光）
                controller.SetFixtureColor(timing.fixtureIndex, color);
                
                // SetFixtureColor直後にDMX値を確認（デバッグ時のみ）
                if (enableDebugLog)
                {
                    var dmxValuesBeforeApply = controller.GetFixtureDmxValues(timing.fixtureIndex);
                    Debug.Log($"[CuePlayer] 点灯処理: '{timing.note.lyric}' Fixture {timing.fixtureIndex}, Color: {color}, DMX値(Apply前): R:{dmxValuesBeforeApply.r} G:{dmxValuesBeforeApply.g} B:{dmxValuesBeforeApply.b}");
                }
                
                controller.Apply();
                
                // Apply直後にDMX値を確認（デバッグ時のみ）
                if (enableDebugLog)
                {
                    var dmxValuesAfterApply = controller.GetFixtureDmxValues(timing.fixtureIndex);
                    Debug.Log($"[CuePlayer] Apply直後: Fixture {timing.fixtureIndex}, DMX値: R:{dmxValuesAfterApply.r} G:{dmxValuesAfterApply.g} B:{dmxValuesAfterApply.b}");
                }

                // 音の長さだけ待機
                yield return new WaitForSeconds(timing.duration);

                // 消灯
                controller.SetFixtureColor(timing.fixtureIndex, Color.black);
                controller.Apply();
            }

            if (enableDebugLog)
            {
                Debug.Log("[CuePlayer] ノートシーケンス再生完了");
            }
            _currentPlayback = null;
        }

        private void InterpolateKeyframes(List<DmxKeyframe> keyframes, float currentTime)
        {
            if (keyframes.Count == 0) return;

            // 現在時刻より前の最後のキーフレームと、次のキーフレームを見つける
            DmxKeyframe prevKf = null;
            DmxKeyframe nextKf = null;
            int prevIndex = -1;

            for (int i = 0; i < keyframes.Count; i++)
            {
                if (keyframes[i].t <= currentTime)
                {
                    prevKf = keyframes[i];
                    prevIndex = i;
                }
                else
                {
                    nextKf = keyframes[i];
                    break;
                }
            }

            // キーフレームが1つだけ、または最後のキーフレームを超えている場合
            if (nextKf == null)
            {
                if (prevKf != null)
                {
                    ApplyKeyframe(prevKf);
                }
                return;
            }

            // 線形補間
            float t = 0f;
            if (nextKf.t > prevKf.t)
            {
                t = (currentTime - prevKf.t) / (nextKf.t - prevKf.t);
            }

            // 各フィクスチャの状態を補間
            Dictionary<int, FixtureState> interpolated = new Dictionary<int, FixtureState>();

            // 前のキーフレームの全フィクスチャをコピー
            if (prevKf.fixtures != null)
            {
                foreach (var fs in prevKf.fixtures)
                {
                    interpolated[fs.index] = new FixtureState
                    {
                        index = fs.index,
                        height = fs.height,
                        r = fs.r,
                        g = fs.g,
                        b = fs.b
                    };
                }
            }

            // 次のキーフレームのフィクスチャと補間
            if (nextKf.fixtures != null)
            {
                foreach (var nextFs in nextKf.fixtures)
                {
                    if (interpolated.ContainsKey(nextFs.index))
                    {
                        var prevFs = prevKf.fixtures?.FirstOrDefault(f => f.index == nextFs.index);
                        if (prevFs != null)
                        {
                            interpolated[nextFs.index].height = Mathf.RoundToInt(Mathf.Lerp(prevFs.height, nextFs.height, t));
                            interpolated[nextFs.index].r = Mathf.RoundToInt(Mathf.Lerp(prevFs.r, nextFs.r, t));
                            interpolated[nextFs.index].g = Mathf.RoundToInt(Mathf.Lerp(prevFs.g, nextFs.g, t));
                            interpolated[nextFs.index].b = Mathf.RoundToInt(Mathf.Lerp(prevFs.b, nextFs.b, t));
                        }
                    }
                    else
                    {
                        // 前のキーフレームにない場合は次のキーフレームの値をそのまま使用
                        interpolated[nextFs.index] = new FixtureState
                        {
                            index = nextFs.index,
                            height = nextFs.height,
                            r = nextFs.r,
                            g = nextFs.g,
                            b = nextFs.b
                        };
                    }
                }
            }

            // 補間された状態を適用
            foreach (var state in interpolated.Values)
            {
                ApplyFixtureState(state);
            }
        }

        private void ApplyKeyframe(DmxKeyframe keyframe)
        {
            if (keyframe.fixtures == null) return;

            foreach (var fs in keyframe.fixtures)
            {
                ApplyFixtureState(fs);
            }
        }

        private void ApplyFixtureState(FixtureState state)
        {
            if (controller == null) return;

            // 高さを0-100の範囲にクランプ（DMX値は0-100）
            int height = Mathf.Clamp(state.height, 0, 100);
            controller.SetFixtureHeight(state.index, height);

            // 色を適用（0-255の範囲）
            Color color = new Color(
                Mathf.Clamp01(state.r / 255f),
                Mathf.Clamp01(state.g / 255f),
                Mathf.Clamp01(state.b / 255f)
            );
            controller.SetFixtureColor(state.index, color);
        }

        // TTSモーションの開始時刻と継続時間を追跡（連続するTTSエントリ間でスムーズに継続するため）
        private float _ttsWaveMotionStartTime = 0f;
        private float _ttsWaveMotionEndTime = 0f;

        /// <summary>
        /// TTS用の波打つような美しいモーションを開始
        /// </summary>
        /// <param name="duration">モーションの継続時間（秒）。0以下の場合は無期限に継続</param>
        /// <param name="extendIfRunning">既存のモーションが実行中の場合は継続時間を延長する（連続するTTSエントリ間でスムーズに継続）</param>
        public void StartTTSWaveMotion(float duration = 0f, bool extendIfRunning = true)
        {
            if (controller == null)
            {
                Debug.LogError("[CuePlayer] KineticLightControllerが設定されていません。");
                return;
            }

            // 既存のモーションが実行中で、延長が有効な場合
            if (extendIfRunning && _currentTTSWaveMotion != null && duration > 0f)
            {
                // 現在時刻から新しい終了時刻を計算
                float currentTime = Time.time;
                float newEndTime = currentTime + duration;
                
                // 既存の終了時刻より後になる場合のみ更新
                if (newEndTime > _ttsWaveMotionEndTime)
                {
                    _ttsWaveMotionEndTime = newEndTime;
                    if (enableDebugLog)
                    {
                        float remainingTime = _ttsWaveMotionEndTime - currentTime;
                        Debug.Log($"[CuePlayer] TTS波打つモーション継続時間を延長: 残り{remainingTime:F2}秒");
                    }
                    return; // 既存のモーションを継続
                }
            }

            // 既存のTTSモーションを停止（延長しない場合、または新しいモーションを開始する場合）
            StopTTSWaveMotion();

            if (enableDebugLog)
            {
                if (duration > 0f)
                {
                    Debug.Log($"[CuePlayer] TTS波打つモーション開始: {duration:F2}秒");
                }
                else
                {
                    Debug.Log("[CuePlayer] TTS波打つモーション開始: 無期限");
                }
            }

            _ttsWaveMotionStartTime = Time.time;
            _ttsWaveMotionEndTime = duration > 0f ? Time.time + duration : float.MaxValue;
            _currentTTSWaveMotion = StartCoroutine(CoTTSWaveMotion(duration));
        }

        /// <summary>
        /// TTS用の波打つモーションを停止
        /// </summary>
        public void StopTTSWaveMotion()
        {
            if (_currentTTSWaveMotion != null)
            {
                StopCoroutine(_currentTTSWaveMotion);
                _currentTTSWaveMotion = null;
                _ttsWaveMotionStartTime = 0f;
                _ttsWaveMotionEndTime = 0f;
                if (enableDebugLog)
                {
                    Debug.Log("[CuePlayer] TTS波打つモーション停止");
                }
            }
        }

        /// <summary>
        /// TTS用の波打つモーションのコルーチン
        /// </summary>
        private IEnumerator CoTTSWaveMotion(float duration)
        {
            if (controller == null || controller.fixtures == null || controller.fixtures.Count == 0)
            {
                yield break;
            }

            int fixtureCount = controller.fixtures.Count;
            float startTime = _ttsWaveMotionStartTime; // 開始時刻を使用（連続するTTSエントリ間で継続するため）
            float interval = 1f / updateRate;
            float elapsed = 0f;

            // 各フィクスチャの初期位相を設定（波が連続して見えるように）
            float[] fixturePhases = new float[fixtureCount];
            for (int i = 0; i < fixtureCount; i++)
            {
                // 各フィクスチャに異なる位相を設定（0から2πまで均等に分散）
                fixturePhases[i] = (float)i / fixtureCount * 2f * Mathf.PI;
            }

            // 現在の高さを初期化（既存の高さを保持）
            for (int i = 0; i < fixtureCount; i++)
            {
                if (!_currentFixtureHeights.ContainsKey(i))
                {
                    _currentFixtureHeights[i] = ttsWaveHeightCenter;
                }
            }

            // 継続時間が0以下の場合は無期限に継続
            // それ以外の場合は、終了時刻まで継続（連続するTTSエントリ間で延長される可能性がある）
            while (duration <= 0f || Time.time < _ttsWaveMotionEndTime)
            {
                elapsed = Time.time - startTime;
                float time = elapsed;

                // 音声入力の音量を取得（AudioInputManagerが設定されている場合）
                float currentRms = 0f;
                bool hasAudioInput = false;
                if (audioInputManager != null)
                {
                    currentRms = audioInputManager.CurrentRms;
                    hasAudioInput = currentRms >= ttsVoiceThreshold;
                }

                // 音量に応じて振幅を調整
                // 無音時: 振幅を4分の1に
                // 音声入力時: RMS値に応じて振幅を増やす（RMSが大きいほど振幅も大きくなる）
                float amplitudeMultiplier = hasAudioInput 
                    ? Mathf.Lerp(ttsSilentAmplitudeMultiplier, 1f, Mathf.Clamp01(currentRms / 0.3f)) // RMS 0.3を最大として正規化
                    : ttsSilentAmplitudeMultiplier;
                float currentAmplitude = ttsWaveHeightAmplitude * amplitudeMultiplier;

                // 各フィクスチャに対して波を適用
                for (int i = 0; i < fixtureCount; i++)
                {
                    // 高さの波（サイン波）
                    float heightWave = Mathf.Sin(time * ttsWaveFrequency * 2f * Mathf.PI + fixturePhases[i]);
                    float targetHeight = ttsWaveHeightCenter + heightWave * currentAmplitude;
                    targetHeight = Mathf.Clamp(targetHeight, 0f, 100f);

                    // 現在の高さから目標高さへ滑らかに移動
                    float currentHeight = _currentFixtureHeights.ContainsKey(i) ? _currentFixtureHeights[i] : ttsWaveHeightCenter;
                    float newHeight = Mathf.Lerp(currentHeight, targetHeight, ttsHeightLerpSpeed); // 滑らかな補間
                    _currentFixtureHeights[i] = newHeight;
                    controller.SetFixtureHeight(i, Mathf.RoundToInt(newHeight));

                    // 色の波（色相を時間と位相に基づいて変化）
                    float colorPhase = time * ttsColorSpeed + fixturePhases[i] / (2f * Mathf.PI);
                    float hue = (colorPhase % 1f + 1f) % 1f; // 0-1の範囲に正規化
                    Color color = Color.HSVToRGB(hue, 1.0f, 1.0f);
                    controller.SetFixtureColor(i, color);
                }

                controller.Apply();
                yield return new WaitForSeconds(interval);
            }

            if (enableDebugLog)
            {
                Debug.Log("[CuePlayer] TTS波打つモーション完了");
            }

            _currentTTSWaveMotion = null;
        }
    }
}
