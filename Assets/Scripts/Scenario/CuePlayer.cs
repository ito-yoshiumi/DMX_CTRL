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

        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        private Coroutine _currentPlayback;
        private Dictionary<int, FixtureState> _currentStates = new Dictionary<int, FixtureState>();

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
