using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Encounter.DMX;
using Encounter.Audio;
using Encounter.Utils;

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
            OperationLogger.Instance?.Log("Scenario", "Initialized", $"Resource: {scenarioResource}");
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
            OperationLogger.Instance?.Log("Scenario", "Started");
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
            OperationLogger.Instance?.Log("Scenario", "Stopped");
        }

        private IEnumerator CoRun()
        {
            for (int i = 0; i < _scenario.entries.Count; i++)
            {
                var e = _scenario.entries[i];
                bool retryEntry = false;

                do
                {
                    retryEntry = false;
                    OperationLogger.Instance?.Log("Scenario", "ProcessEntry", $"ID:{e.id}, Type:{e.type}, Text:{e.text}");
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
                    clip = ttsService.GetCachedClip(e.text, e.pitchNotes, e.speedScale, e.labFilePath);
                    if (clip == null && enableDebugLog)
                    {
                        Debug.LogWarning($"[ScenarioRunner] TTSクリップが取得できません: {e.text}");
                    }
                }
                else if ((e.type == "singing" || (e.singingNotes != null && e.singingNotes.Length > 0)) && ttsService != null)
                {
                    clip = ttsService.GetCachedSingingClip(e.singingNotes);
                    if (clip == null && enableDebugLog)
                    {
                        Debug.LogWarning($"[ScenarioRunner] 歌声合成クリップが取得できません");
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

                    if (enableDebugLog)
                    {
                        Debug.Log($"[ScenarioRunner] 音声再生開始: \"{e.text ?? e.path}\" ({clip.length:F2}秒, サンプルレート: {clip.frequency}Hz)");
                    }
                    
                    audioSource.clip = clip;
                    audioSource.Play();
                    OperationLogger.Instance?.Log("Scenario", "AudioPlay", $"Clip:{clip.name}, Duration:{clip.length:F2}s");
                    clipDuration = clip.length;
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[ScenarioRunner] 音声再生中: {clipDuration:F2}秒待機します");
                    }
                    
                    yield return new WaitForSeconds(clipDuration);
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[ScenarioRunner] 音声再生完了: \"{e.text ?? e.path}\"");
                    }
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
                            Debug.Log($"[ScenarioRunner] 参加者録音処理開始: {e.recordSeconds}秒");
                        }
                        OperationLogger.Instance?.Log("Scenario", "RecordStart", $"Duration:{e.recordSeconds}s");
                        
                        if (e.waitForVoiceTrigger)
                        {
                            float timeout = e.voiceTriggerTimeout > 0f ? e.voiceTriggerTimeout : 10f;
                            bool isSuccess = false;
                            
                            yield return participantRecorder.RecordWithTriggerAsync(e.recordSeconds, timeout, (success) => {
                                isSuccess = success;
                            });
                            
                            if (!isSuccess)
                            {
                                if (enableDebugLog)
                                {
                                    Debug.Log($"[ScenarioRunner] 音声検出タイムアウト。エントリ '{e.id}' をリトライします。");
                                }
                                retryEntry = true;
                                yield return new WaitForSeconds(0.5f);
                                continue;
                            }
                        }
                        else
                        {
                            yield return participantRecorder.RecordAsync(e.recordSeconds);
                        }
                        
                        OperationLogger.Instance?.Log("Scenario", "RecordEnd");
                    }
                    else
                    {
                        if (enableDebugLog)
                        {
                            Debug.LogWarning("[ScenarioRunner] ParticipantRecordingManagerが設定されていません。録音をスキップします。");
                        }
                    }
                }

                if (retryEntry) continue;

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
                                referenceClip = ttsService.GetCachedClip(referenceEntry.text, referenceEntry.pitchNotes, referenceEntry.speedScale, referenceEntry.labFilePath);
                            }
                            else if (referenceEntry.type == "singing" && ttsService != null && referenceEntry.singingNotes != null && referenceEntry.singingNotes.Length > 0)
                            {
                                referenceClip = ttsService.GetCachedSingingClip(referenceEntry.singingNotes);
                            }
                            
                            if (referenceClip == null && enableDebugLog)
                            {
                                Debug.LogWarning($"[ScenarioRunner] 参照エントリ '{e.mixReferenceEntryId}' の音声が取得できません。");
                            }
                            else if (enableDebugLog)
                            {
                                Debug.Log($"[ScenarioRunner] 参照エントリ '{e.mixReferenceEntryId}' の音声を再生します。");
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

                    // リズムを合わせた再生：お手本音声の各音のタイミングに合わせて参加者の録音を同時に再生
                    var recordedClips = participantRecorder.RecordedClips;
                    if (referenceClip != null && recordedClips != null && recordedClips.Count > 0 && audioSource != null)
                    {
                        // 参照エントリからsingingNotesを取得してタイミングを計算
                        // mixReferenceEntryIdが指定されている場合のみ検索
                        ScenarioEntry referenceEntry = null;
                        if (!string.IsNullOrEmpty(e.mixReferenceEntryId))
                        {
                            referenceEntry = _scenario.entries.Find(entry => entry.id == e.mixReferenceEntryId);
                        }
                        
                        if (referenceEntry != null && referenceEntry.singingNotes != null && referenceEntry.singingNotes.Length > 0)
                        {
                            // お手本音声を再生開始
                            if (enableDebugLog)
                            {
                                Debug.Log("[ScenarioRunner] お手本音声と参加者録音をリズムに合わせて同時再生します。");
                            }
                            
                            audioSource.clip = referenceClip;
                            audioSource.Play();
                            
                            // 追加のAudioSourceを作成（参加者録音用）
                            List<AudioSource> participantAudioSources = new List<AudioSource>();
                            for (int j = 0; j < recordedClips.Count && j < 5; j++)
                            {
                                GameObject audioObj = new GameObject($"ParticipantAudioSource_{j}");
                                audioObj.transform.SetParent(transform);
                                AudioSource participantSource = audioObj.AddComponent<AudioSource>();
                                participantSource.playOnAwake = false;
                                participantSource.volume = audioSource.volume; // メインAudioSourceと同じ音量を設定
                                participantAudioSources.Add(participantSource);
                            }
                            
                            // 各音のタイミングを計算（1フレーム = 約0.01秒、100fps想定）
                            const float frameToSeconds = 0.01f;
                            float currentTime = 0f;
                            int recordedIndex = 0;
                            
                            foreach (var note in referenceEntry.singingNotes)
                            {
                                // 歌詞がある音（key != -1）のタイミングで参加者録音を再生
                                if (note.key != -1 && !string.IsNullOrEmpty(note.lyric) && recordedIndex < recordedClips.Count)
                                {
                                    float noteStartTime = currentTime;
                                    float noteDuration = note.frame_length * frameToSeconds;
                                    
                                    // このタイミングで参加者録音を再生
                                    if (recordedIndex < participantAudioSources.Count)
                                    {
                                        var participantClip = recordedClips[recordedIndex];
                                        if (participantClip != null)
                                        {
                                            StartCoroutine(PlayAtTime(participantAudioSources[recordedIndex], participantClip, noteStartTime));
                                            if (enableDebugLog)
                                            {
                                                Debug.Log($"[ScenarioRunner] 参加者音声 {recordedIndex + 1} を {noteStartTime:F2}秒後に再生開始（{note.lyric}のタイミング）");
                                            }
                                        }
                                        recordedIndex++;
                                    }
                                }
                                
                                currentTime += note.frame_length * frameToSeconds;
                            }
                            
                            // お手本音声の再生が終わるまで待機
                            yield return new WaitForSeconds(referenceClip.length);
                            
                            // 追加したAudioSourceをクリーンアップ
                            foreach (var source in participantAudioSources)
                            {
                                if (source != null && source.gameObject != null)
                                {
                                    Destroy(source.gameObject);
                                }
                            }
                        }
                        else
                        {
                            // singingNotesがない場合は従来通り順番に再生
                            if (enableDebugLog)
                            {
                                Debug.Log("[ScenarioRunner] お手本音声を再生します。");
                            }
                            audioSource.clip = referenceClip;
                            audioSource.Play();
                            yield return new WaitForSeconds(referenceClip.length + 0.1f);
                            
                            if (enableDebugLog)
                            {
                                Debug.Log($"[ScenarioRunner] 参加者{recordedClips.Count}音を順番に再生します。");
                            }
                            
                            for (int j = 0; j < recordedClips.Count; j++)
                            {
                                var recordedClip = recordedClips[j];
                                if (recordedClip != null)
                                {
                                    if (enableDebugLog)
                                    {
                                        Debug.Log($"[ScenarioRunner] 参加者音声 {j + 1}/{recordedClips.Count} を再生します。");
                                    }
                                    audioSource.clip = recordedClip;
                                    audioSource.Play();
                                    yield return new WaitForSeconds(recordedClip.length + 0.1f);
                                }
                            }
                        }
                    }
                    else if (referenceClip != null && audioSource != null)
                    {
                        // 参加者録音がない場合はお手本音声のみ再生
                        if (enableDebugLog)
                        {
                            Debug.Log("[ScenarioRunner] お手本音声を再生します。");
                        }
                        audioSource.clip = referenceClip;
                        audioSource.Play();
                        yield return new WaitForSeconds(referenceClip.length);
                    }
                    else if (enableDebugLog)
                    {
                        Debug.LogWarning("[ScenarioRunner] 再生できる音声がありません。");
                    }
                }

                if (waitAfter > 0f)
                {
                    yield return new WaitForSeconds(waitAfter);
                }
                
                } while (retryEntry);
            }

            if (enableDebugLog)
            {
                Debug.Log("[ScenarioRunner] シナリオ再生完了");
            }
            OperationLogger.Instance?.Log("Scenario", "Completed");
            _isRunning = false; // 実行完了
        }

        /// <summary>
        /// 指定した時間後にAudioClipを再生するコルーチン
        /// </summary>
        private IEnumerator PlayAtTime(AudioSource source, AudioClip clip, float delayTime)
        {
            if (delayTime > 0f)
            {
                yield return new WaitForSeconds(delayTime);
            }
            
            if (source != null && clip != null)
            {
                source.clip = clip;
                source.Play();
            }
        }
    }
}
