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
        
        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        private Coroutine _currentPlayback;
        private Dictionary<int, FixtureState> _currentStates = new Dictionary<int, FixtureState>();
        // 各フィクスチャの現在の高さを追跡（0-100の範囲、floatで滑らかな移動を実現）
        private Dictionary<int, float> _currentFixtureHeights = new Dictionary<int, float>();

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
            if (!_currentFixtureHeights.ContainsKey(fixtureIndex))
            {
                _currentFixtureHeights[fixtureIndex] = 0f;
            }
            
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
            while (Mathf.Abs(_currentFixtureHeights[fixtureIndex] - targetHeight) > 0.1f)
            {
                float deltaTime = Time.deltaTime;
                if (deltaTime <= 0f) 
                {
                    yield return null;
                    continue;
                }
                
                // 1フレームで移動できる距離
                float maxMoveDistance = maxMoveSpeed * deltaTime;
                
                // 目標位置までの距離
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
            
            // 最終位置を確定
            _currentFixtureHeights[fixtureIndex] = targetHeight;
            controller.SetFixtureHeight(fixtureIndex, Mathf.RoundToInt(targetHeight));
            controller.Apply();
            
            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] MoveFixtureToHeight 完了: Fixture {fixtureIndex}, Height: {targetHeight:F1}");
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

            // 全ての有効なノートを抽出して、各フィクスチャを対応する位置に移動
            List<(int fixtureIndex, float targetHeight, SingingNote note)> noteAssignments = new List<(int, float, SingingNote)>();
            int noteIndex = 0;
            
            foreach (var note in notes)
            {
                if (!note.IsKeyNull && !string.IsNullOrEmpty(note.lyric))
                {
                    int targetFixture = noteIndex % fixtureCount;
                    
                    // 高さを計算（低音→低い、高音→高い）
                    float t = Mathf.InverseLerp(minMidiNote, maxMidiNote, note.key);
                    float targetHeight = Mathf.Lerp(100f, 0f, t);
                    
                    noteAssignments.Add((targetFixture, targetHeight, note));
                    noteIndex++;
                }
            }

            if (noteAssignments.Count == 0)
            {
                yield break;
            }

            if (enableDebugLog)
            {
                Debug.Log($"[CuePlayer] {noteAssignments.Count}個のノートを検出。各フィクスチャを事前に移動します。");
            }

            // 全てのフィクスチャを並列で移動（全て完了するまで待機）
            List<Coroutine> moveCoroutines = new List<Coroutine>();
            foreach (var assignment in noteAssignments)
            {
                // 移動中は消灯
                controller.SetFixtureColor(assignment.fixtureIndex, Color.black);
                
                // 各フィクスチャの移動を並列で開始
                Coroutine moveCoroutine = StartCoroutine(MoveFixtureToHeight(assignment.fixtureIndex, assignment.targetHeight));
                moveCoroutines.Add(moveCoroutine);
            }
            controller.Apply();

            // 全ての移動が完了するまで待機
            foreach (var moveCoroutine in moveCoroutines)
            {
                yield return moveCoroutine;
            }

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
                controller.Apply();
                
                // デバッグ: 実際にDMXに送信された値を確認
                if (enableDebugLog)
                {
                    var dmxValues = controller.GetFixtureDmxValues(timing.fixtureIndex);
                    Debug.Log($"[CuePlayer] DMX送信確認: Fixture {timing.fixtureIndex}, R:{dmxValues.r} G:{dmxValues.g} B:{dmxValues.b}");
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
    }
}
