using UnityEngine;
using System.Collections;
using Encounter.DMX;
using Encounter.Audio;

namespace Encounter.Scenario
{
    public class ScenarioRunner : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Resources内のシナリオJSON。例: Scenario/scenario_ja")]
        public string scenarioResource = "Scenario/scenario_ja";

        [Header("Refs")]
        public AudioSource audioSource;
        public TTSService ttsService;
        public CuePlayer cuePlayer;
        public AudioInputManager audioInputManager;
        public ParticipantRecordingManager participantRecorder;

        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        private ScenarioFile _scenario;
        private bool _isRunning = false; // シナリオ実行中かどうか

        public bool IsRunning => _isRunning; // 実行状態を取得するプロパティ

        IEnumerator Start()
        {
            if (enableDebugLog)
            {
                Debug.Log($"[ScenarioRunner] Start() 呼び出し (経過時間: {Time.time:F2}秒)");
                Debug.Log($"[ScenarioRunner] シナリオリソースパス: {scenarioResource}");
            }
            
            // シナリオ読込
            TextAsset ta = Resources.Load<TextAsset>(scenarioResource);
            if (ta != null)
            {
                _scenario = JsonUtility.FromJson<ScenarioFile>(ta.text);
                if (enableDebugLog)
                {
                    Debug.Log($"[ScenarioRunner] シナリオ読み込み完了: {_scenario.entries.Count}エントリ (経過時間: {Time.time:F2}秒)");
                    for (int i = 0; i < _scenario.entries.Count; i++)
                    {
                        var e = _scenario.entries[i];
                        Debug.Log($"[ScenarioRunner]  エントリ{i + 1}: id={e.id}, type={e.type}, path={e.path}, text={e.text}");
                    }
                }
            }
            else
            {
                Debug.LogError($"[ScenarioRunner] シナリオファイルが見つかりません: {scenarioResource} (経過時間: {Time.time:F2}秒)");
            }

            if (participantRecorder == null)
            {
                participantRecorder = FindFirstObjectByType<ParticipantRecordingManager>();
            }

            // TTS 事前合成（必要なら）
            if (ttsService != null)
            {
                if (enableDebugLog)
                {
                    Debug.Log($"[ScenarioRunner] TTS事前合成開始 (経過時間: {Time.time:F2}秒)");
                }
                yield return ttsService.PrewarmAsync(_scenario);
                if (enableDebugLog)
                {
                    Debug.Log($"[ScenarioRunner] TTS事前合成完了 (経過時間: {Time.time:F2}秒)");
                }
            }
            else
            {
                if (enableDebugLog)
                {
                    Debug.Log($"[ScenarioRunner] TTSサービスが設定されていません (経過時間: {Time.time:F2}秒)");
                }
            }

            if (enableDebugLog)
            {
                Debug.Log($"[ScenarioRunner] 初期化完了。RunAll()を呼び出してシナリオを開始してください。 (経過時間: {Time.time:F2}秒)");
            }
            // 自動再生しない。外部から RunAll() を叩く。
        }

        public void RunAll()
        {
            if (_scenario == null)
            {
                Debug.LogError("[ScenarioRunner] シナリオが読み込まれていません。");
                return;
            }
            if (_isRunning)
            {
                if (enableDebugLog)
                {
                    Debug.LogWarning("[ScenarioRunner] シナリオは既に実行中です。");
                }
                return;
            }
            _isRunning = true;
            StartCoroutine(CoRun());
            if (enableDebugLog)
            {
                Debug.Log("[ScenarioRunner] シナリオ開始");
            }
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return; // 既に停止している
            }
            _isRunning = false;
            StopAllCoroutines();
            if (cuePlayer != null)
            {
                cuePlayer.Stop();
            }
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            participantRecorder?.ClearRecordings();
            if (enableDebugLog)
            {
                Debug.Log("[ScenarioRunner] シナリオ停止");
            }
        }

        private IEnumerator CoRun()
        {
            foreach (var e in _scenario.entries)
            {
                AudioClip clip = null;

                if (e.type == "wav" && !string.IsNullOrEmpty(e.path))
                {
                    // ResourcesからWAVファイルをロード
                    clip = Resources.Load<AudioClip>(e.path);
                    if (clip == null && enableDebugLog)
                    {
                        Debug.LogWarning($"[ScenarioRunner] WAVファイルが見つかりません: {e.path}");
                    }
                }
                else if (e.type == "tts" && ttsService != null)
                {
                    clip = ttsService.GetCachedClip(e.text);
                    if (clip == null && enableDebugLog)
                    {
                        Debug.LogWarning($"[ScenarioRunner] TTSクリップが取得できません: {e.text}");
                    }
                }

                float waitAfter = e.waitAfter;
                float clipDuration = 0f;

                if (clip != null && audioSource != null)
                {
                    // DMXキュー開始
                    if (!string.IsNullOrEmpty(e.dmxCue) && cuePlayer != null)
                    {
                        cuePlayer.PlayCue(e.dmxCue);
                    }

                    audioSource.clip = clip;
                    audioSource.Play();
                    clipDuration = clip.length;
                    yield return new WaitForSeconds(clipDuration);
                }
                else if (e.type == "tts" && ttsService == null)
                {
                    // TTSサービスがない場合は待機時間だけ待つ
                    yield return new WaitForSeconds(waitAfter);
                    continue;
                }

                // 録音機能
                if (e.record && e.recordSeconds > 0f)
                {
                    if (participantRecorder != null)
                    {
                        if (enableDebugLog)
                        {
                            Debug.Log($"[ScenarioRunner] 参加者録音開始: {e.recordSeconds}秒");
                        }
                        yield return participantRecorder.RecordAsync(e.recordSeconds);
                    }
                    else
                    {
                        if (enableDebugLog)
                        {
                            Debug.LogWarning("[ScenarioRunner] ParticipantRecordingManagerが設定されていません。録音をスキップします。");
                        }
                    }
                }

                if (e.playRecordedMix && participantRecorder != null)
                {
                    AudioClip referenceClip = null;
                    
                    // mixReferenceEntryIdが指定されている場合、シナリオ内のエントリを検索
                    if (!string.IsNullOrEmpty(e.mixReferenceEntryId))
                    {
                        var referenceEntry = _scenario.entries.Find(entry => entry.id == e.mixReferenceEntryId);
                        if (referenceEntry != null)
                        {
                            if (referenceEntry.type == "wav" && !string.IsNullOrEmpty(referenceEntry.path))
                            {
                                referenceClip = Resources.Load<AudioClip>(referenceEntry.path);
                            }
                            else if (referenceEntry.type == "tts" && ttsService != null && !string.IsNullOrEmpty(referenceEntry.text))
                            {
                                referenceClip = ttsService.GetCachedClip(referenceEntry.text);
                            }
                            
                            if (referenceClip == null && enableDebugLog)
                            {
                                Debug.LogWarning($"[ScenarioRunner] 参照エントリ '{e.mixReferenceEntryId}' の音声が取得できません。");
                            }
                            else if (enableDebugLog)
                            {
                                Debug.Log($"[ScenarioRunner] 参照エントリ '{e.mixReferenceEntryId}' の音声をミックスに追加します。");
                            }
                        }
                        else if (enableDebugLog)
                        {
                            Debug.LogWarning($"[ScenarioRunner] 参照エントリ '{e.mixReferenceEntryId}' が見つかりません。");
                        }
                    }
                    // mixReferenceClipが指定されている場合、Resourcesからロード
                    else if (!string.IsNullOrEmpty(e.mixReferenceClip))
                    {
                        referenceClip = Resources.Load<AudioClip>(e.mixReferenceClip);
                        if (referenceClip == null && enableDebugLog)
                        {
                            Debug.LogWarning($"[ScenarioRunner] ミックス参照クリップが見つかりません: {e.mixReferenceClip}");
                        }
                    }

                    AudioClip mixClip = participantRecorder.BuildMixClip(referenceClip);
                    if (mixClip != null && audioSource != null)
                    {
                        if (enableDebugLog)
                        {
                            int recordingCount = participantRecorder.RecordedClips.Count;
                            string mixInfo = referenceClip != null 
                                ? $"（参加者{recordingCount}音 + お手本）" 
                                : $"（参加者{recordingCount}音）";
                            Debug.Log($"[ScenarioRunner] 録音ミックスを再生します{mixInfo}。");
                        }
                        audioSource.clip = mixClip;
                        audioSource.Play();
                        yield return new WaitForSeconds(mixClip.length);
                    }
                    else if (enableDebugLog)
                    {
                        Debug.LogWarning("[ScenarioRunner] 再生できるミックスがありません。");
                    }
                }

                if (waitAfter > 0f)
                {
                    yield return new WaitForSeconds(waitAfter);
                }
            }

            if (enableDebugLog)
            {
                Debug.Log("[ScenarioRunner] シナリオ再生完了");
            }
            _isRunning = false; // 実行完了
        }
    }
}
