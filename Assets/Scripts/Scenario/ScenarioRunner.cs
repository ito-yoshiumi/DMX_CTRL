using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Encounter.DMX;
using Encounter.Audio;
using Encounter.Utils;
using Encounter.Testing;

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
        [Tooltip("シナリオ再生中に無効化したいVisualizer（競合回避のため）")]
        public List<MonoBehaviour> conflictingVisualizers;

        [Header("Start Settings")]
        [Tooltip("スペースキーでシナリオを開始するか（falseの場合は自動開始）")]
        public bool waitForSpaceKey = true;

        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        private ScenarioFile _scenario;
        private bool _isRunning = false; // シナリオ実行中かどうか
        private bool _waitingForStart = false; // スペースキー待ち中かどうか
        private bool _isInitialized = false; // 初期化完了フラグ

        public bool IsRunning => _isRunning; // 実行状態を取得するプロパティ
        public bool IsInitialized => _isInitialized; // 初期化完了状態を取得するプロパティ

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

            // 初期化完了フラグを設定
            _isInitialized = true;
            
            if (enableDebugLog)
            {
                if (waitForSpaceKey)
                {
                    Debug.Log($"[ScenarioRunner] 初期化完了。スペースキーを押すとシナリオを開始します。 (経過時間: {Time.time:F2}秒)");
                }
                else
                {
                    Debug.Log($"[ScenarioRunner] 初期化完了。RunAll()を呼び出してシナリオを開始してください。 (経過時間: {Time.time:F2}秒)");
                }
            }
            
            // スペースキー待ちモードの場合
            if (waitForSpaceKey)
            {
                _waitingForStart = true;
                // ビジュアライザは有効のまま（無効化しない）
            }
            else
            {
                // 自動再生しない。外部から RunAll() を叩く。
            }
            
            OperationLogger.Instance?.Log("Scenario", "Initialized", $"Resource: {scenarioResource}");
        }

        void Update()
        {
            // 初期化が完了していない場合は無視
            if (!_isInitialized)
            {
                return;
            }
            
            // スペースキー待ち中で、スペースキーが押されたらシナリオ開始
            if (_waitingForStart && !_isRunning && Input.GetKeyDown(KeyCode.Space))
            {
                if (enableDebugLog)
                {
                    Debug.Log("[ScenarioRunner] スペースキーが押されました。シナリオを開始します。");
                }
                _waitingForStart = false;
                RunAll();
            }
        }

        public void RunAll()
        {
            Debug.Log("========================================");
            Debug.Log("[ScenarioRunner] RunAll() が呼ばれました");
            Debug.Log("========================================");
            
            // 初期化が完了していない場合は実行しない
            if (!_isInitialized)
            {
                Debug.LogWarning("[ScenarioRunner] 初期化が完了していません。シナリオを開始できません。");
                return;
            }
            
            if (_scenario == null)
            {
                Debug.LogError("[ScenarioRunner] シナリオが読み込まれていません。");
                return;
            }
            if (_isRunning)
            {
                Debug.LogWarning("[ScenarioRunner] シナリオは既に実行中です。");
                return;
            }
            
            Debug.Log("[ScenarioRunner] 競合するVisualizerを無効化します...");
            // 競合するVisualizerを無効化
            SetVisualizersEnabled(false);

            _isRunning = true;
            Debug.Log("[ScenarioRunner] CoRun() コルーチンを開始します");
            StartCoroutine(CoRun());
            Debug.Log("[ScenarioRunner] シナリオ開始");
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

            // Visualizerを復帰
            SetVisualizersEnabled(true);

            if (enableDebugLog)
            {
                Debug.Log("[ScenarioRunner] シナリオ停止");
            }
            OperationLogger.Instance?.Log("Scenario", "Stopped");
        }
        
        private void SetVisualizersEnabled(bool enabled)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[ScenarioRunner] SetVisualizersEnabled({enabled}) 呼び出し");
            }

            // Testing名前空間のVisualizerを動的に探して無効化（Inspector設定漏れ対策）
            if (!enabled)
            {
                // 型で直接検索（より確実）
                var pitchStaffVisualizers = FindObjectsOfType<PitchStaffVisualizer>();
                var audioInputVisualizers = FindObjectsOfType<AudioInputVisualizer>();
                
                if (enableDebugLog)
                {
                    Debug.Log($"[ScenarioRunner] 検出されたVisualizer: PitchStaffVisualizer={pitchStaffVisualizers.Length}個, AudioInputVisualizer={audioInputVisualizers.Length}個");
                }
                
                if (conflictingVisualizers == null)
                {
                     conflictingVisualizers = new List<MonoBehaviour>();
                }

                // PitchStaffVisualizer を無効化
                foreach (var v in pitchStaffVisualizers)
                {
                    if (v != null)
                    {
                        if (enableDebugLog) Debug.Log($"[ScenarioRunner] PitchStaffVisualizer ({v.gameObject.name}) を無効化します。現在の状態: enabled={v.enabled}, sendHeightToDmx={v.sendHeightToDmx}, sendColorToDmx={v.sendColorToDmx}");
                        
                        // DMX送信を停止
                        v.sendHeightToDmx = false;
                        v.sendColorToDmx = false;
                        
                        // Squareの色をリセット（黒に設定）して、KineticLightControllerの色設定が反映されるようにする
                        // KineticLightControllerのfixtureObjectsからSquareを取得
                        if (v.kineticLightController != null && v.kineticLightController.fixtureObjects != null)
                        {
                            for (int i = 0; i < v.kineticLightController.fixtureObjects.Count; i++)
                            {
                                if (v.kineticLightController.fixtureObjects[i] != null)
                                {
                                    var spriteRenderer = v.kineticLightController.fixtureObjects[i].GetComponent<SpriteRenderer>();
                                    if (spriteRenderer != null)
                                    {
                                        spriteRenderer.color = Color.black;
                                        if (enableDebugLog)
                                        {
                                            Debug.Log($"[ScenarioRunner] PitchStaffVisualizer の Square[{i}] の色をリセットしました。");
                                        }
                                    }
                                }
                            }
                        }
                        
                        // コンポーネント自体も無効化
                        v.enabled = false;
                        
                        // conflictingVisualizersに動的に追加して、後で復帰できるようにする
                        if (!conflictingVisualizers.Contains(v))
                        {
                            conflictingVisualizers.Add(v);
                            if (enableDebugLog) Debug.Log($"[ScenarioRunner] PitchStaffVisualizer を conflictingVisualizers に追加しました。");
                        }
                    }
                }

                // AudioInputVisualizer を無効化
                foreach (var v in audioInputVisualizers)
                {
                    if (v != null && v.enabled)
                    {
                        if (enableDebugLog) Debug.Log($"[ScenarioRunner] AudioInputVisualizer ({v.gameObject.name}) を無効化します。");
                        v.enabled = false;
                        
                        if (!conflictingVisualizers.Contains(v))
                        {
                            conflictingVisualizers.Add(v);
                        }
                    }
                }
            }
            else
            {
                // 有効化時: PitchStaffVisualizer の DMX送信フラグも復帰
                if (conflictingVisualizers != null)
                {
                    foreach (var v in conflictingVisualizers)
                    {
                        if (v is PitchStaffVisualizer psv)
                        {
                            if (enableDebugLog) Debug.Log($"[ScenarioRunner] PitchStaffVisualizer ({psv.gameObject.name}) のDMX送信フラグを復帰します。");
                            // 元の値は保持されていないので、デフォルト値（true）に戻す
                            // 必要に応じて、無効化前に値を保存する実装も可能
                            psv.sendHeightToDmx = true;
                            psv.sendColorToDmx = true;
                        }
                    }
                }
            }

            // Inspectorで設定されたVisualizerも処理
            if (conflictingVisualizers != null)
            {
                if (enableDebugLog)
                {
                    Debug.Log($"[ScenarioRunner] conflictingVisualizers リスト: {conflictingVisualizers.Count}個");
                }

                foreach (var v in conflictingVisualizers)
                {
                    if (v != null)
                    {
                        if (enableDebugLog)
                        {
                            Debug.Log($"[ScenarioRunner] {v.GetType().Name} ({v.gameObject.name}) を enabled={enabled} に設定");
                        }
                        v.enabled = enabled;
                    }
                }
            }
        }

        private IEnumerator CoRun()
        {
            int voiceTriggerTimeoutCount = 0; // タイムアウト回数をカウント
            
            // 最初のsingingエントリを探して、その前にフィクスチャの高さをリセット
            bool hasResetHeights = false;
            for (int i = 0; i < _scenario.entries.Count; i++)
            {
                var e = _scenario.entries[i];
                if ((e.type == "singing" || (e.singingNotes != null && e.singingNotes.Length > 0)) && 
                    e.singingNotes != null && e.singingNotes.Length > 0)
                {
                    // 最初のsingingエントリの前にリセット
                    if (!hasResetHeights && cuePlayer != null)
                    {
                        if (enableDebugLog)
                        {
                            Debug.Log("[ScenarioRunner] 最初のsingingエントリの前にフィクスチャの高さをリセットします");
                        }
                        cuePlayer.ResetFixtureHeights();
                        hasResetHeights = true;
                    }
                    break;
                }
            }
            
            for (int i = 0; i < _scenario.entries.Count; i++)
            {
                var e = _scenario.entries[i];
                
                // システムメッセージ（timeout_やrestart_messageなど）はメインループではスキップする
                if (e.id.StartsWith("timeout_") || e.id.StartsWith("restart_message"))
                {
                    continue;
                }

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
                    Debug.Log($"[ScenarioRunner] ===== エントリ処理開始: id={e.id}, type={e.type} =====");
                    
                    // DMXキュー処理
                    bool isDynamicNotes = (e.dmxCue == "DynamicNotes");
                    Debug.Log($"[ScenarioRunner] DMXCue: '{e.dmxCue}', Dynamic: {isDynamicNotes}");
                    Debug.Log($"[ScenarioRunner] singingNotes: {(e.singingNotes != null ? $"{e.singingNotes.Length}個" : "null")}");
                    if (e.singingNotes != null && e.singingNotes.Length > 0)
                    {
                        for (int ni = 0; ni < e.singingNotes.Length; ni++)
                        {
                            var n = e.singingNotes[ni];
                            Debug.Log($"[ScenarioRunner]   Note[{ni}]: key={n.key}, lyric='{n.lyric}', frame_length={n.frame_length}");
                        }
                    }

                    if (cuePlayer == null)
                    {
                        Debug.LogError("[ScenarioRunner] cuePlayer が null です！");
                    }
                    else
                    {
                        Debug.Log($"[ScenarioRunner] cuePlayer は有効です。controller: {(cuePlayer.controller != null ? "有効" : "null")}");
                        
                        if (isDynamicNotes && e.singingNotes != null && e.singingNotes.Length > 0)
                        {
                            // 動的ノートモード: フィクスチャの高さを初期化
                            // 実際の移動はPlayNoteSequence内で各ノートの前に処理される
                            Debug.Log($"[ScenarioRunner] >>> DynamicNotesモード: 初期化中... (ノート数: {e.singingNotes.Length})");
                            cuePlayer.PrewarmNotes(e.singingNotes);
                            Debug.Log($"[ScenarioRunner] 初期化完了（移動は各ノートの前に処理されます）");
                        }
                        else if (!string.IsNullOrEmpty(e.dmxCue))
                        {
                            // 通常のDMXキュー再生
                            Debug.Log($"[ScenarioRunner] 通常DMXキュー再生: {e.dmxCue}");
                            cuePlayer.PlayCue(e.dmxCue);
                        }
                        else
                        {
                            Debug.Log($"[ScenarioRunner] DMXキューは設定されていません");
                        }
                    }

                    // 動的ノートモード: ライトの移動を完了してから音声を再生
                    if (isDynamicNotes && cuePlayer != null && e.singingNotes != null && e.singingNotes.Length > 0)
                    {
                        Debug.Log($"[ScenarioRunner] >>> DynamicNotes: ライト移動を開始します (ノート数: {e.singingNotes.Length})");
                        
                        // ライトの移動完了を待機
                        yield return StartCoroutine(cuePlayer.MoveFixturesToNotePositions(e.singingNotes));
                        
                        Debug.Log($"[ScenarioRunner] ライト移動完了。音声再生を開始します。");
                    }

                    if (enableDebugLog)
                    {
                        Debug.Log($"[ScenarioRunner] 音声再生開始: \"{e.text ?? e.path}\" ({clip.length:F2}秒, サンプルレート: {clip.frequency}Hz)");
                    }
                    
                    // 音声再生開始時刻を記録
                    float audioStartTime = Time.time;
                    
                    audioSource.clip = clip;
                    audioSource.Play();

                    // 動的ノートモード: 音声再生と同時にノートシーケンスを開始（既に移動は完了している）
                    if (isDynamicNotes && cuePlayer != null && e.singingNotes != null && e.singingNotes.Length > 0)
                    {
                        Debug.Log($"[ScenarioRunner] >>> DynamicNotes: ノートシーケンス再生開始 (ノート数: {e.singingNotes.Length}, 音声開始時刻: {audioStartTime:F3}s)");
                        // 移動は完了しているので、点灯シーケンスのみ開始
                        // 音声再生開始時刻を渡して同期
                        cuePlayer.PlayNoteSequence(e.singingNotes, audioStartTime);
                        Debug.Log($"[ScenarioRunner] PlayNoteSequence 呼び出し完了");
                    }
                    else
                    {
                        if (isDynamicNotes)
                        {
                            Debug.LogWarning($"[ScenarioRunner] DynamicNotesモードですが、条件が満たされていません: cuePlayer={cuePlayer != null}, singingNotes={e.singingNotes != null && e.singingNotes.Length > 0}");
                        }
                    }

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
                                voiceTriggerTimeoutCount++;
                                
                                if (enableDebugLog)
                                {
                                    Debug.Log($"[ScenarioRunner] 音声検出タイムアウト ({voiceTriggerTimeoutCount}回目)。エントリ '{e.id}' をリトライします。");
                                }
                                
                                // 2回タイムアウトしたらシナリオの最初から再開
                                if (voiceTriggerTimeoutCount >= 2)
                                {
                                    if (enableDebugLog)
                                    {
                                        Debug.Log("[ScenarioRunner] 音声検出タイムアウトが2回発生しました。シナリオの最初から再開します。");
                                    }
                                    OperationLogger.Instance?.Log("Scenario", "RestartFromBeginning", "VoiceTriggerTimeout x2");
                                    
                                    // 2回目のタイムアウトメッセージを再生
                                    if (ttsService != null && audioSource != null)
                                    {
                                        string timeoutMessage2 = "あれ、誰もいないみたい。";
                                        // シナリオからメッセージを取得してキャッシュを利用
                                        var timeoutEntry = _scenario.entries.Find(entry => entry.id == "timeout_2");
                                        if (timeoutEntry != null && !string.IsNullOrEmpty(timeoutEntry.text))
                                        {
                                            timeoutMessage2 = timeoutEntry.text;
                                        }

                                        AudioClip timeoutClip2 = ttsService.GetCachedClip(timeoutMessage2);
                                        
                                        if (timeoutClip2 != null)
                                        {
                                            if (enableDebugLog)
                                            {
                                                Debug.Log($"[ScenarioRunner] タイムアウトメッセージ再生: \"{timeoutMessage2}\"");
                                            }
                                            audioSource.clip = timeoutClip2;
                                            audioSource.Play();
                                            yield return new WaitForSeconds(timeoutClip2.length + 0.3f);
                                        }
                                        else if (enableDebugLog)
                                        {
                                            Debug.LogWarning($"[ScenarioRunner] タイムアウトメッセージのクリップが取得できません: \"{timeoutMessage2}\"");
                                        }
                                        
                                        // リスタートメッセージを再生
                                        string restartMessage = "もういちど最初からはじめるね";
                                        var restartEntry = _scenario.entries.Find(entry => entry.id == "restart_message");
                                        if (restartEntry != null && !string.IsNullOrEmpty(restartEntry.text))
                                        {
                                            restartMessage = restartEntry.text;
                                        }
                                        
                                        AudioClip restartClip = ttsService.GetCachedClip(restartMessage);
                                        
                                        if (restartClip != null)
                                        {
                                            if (enableDebugLog)
                                            {
                                                Debug.Log($"[ScenarioRunner] リスタートメッセージ再生: \"{restartMessage}\"");
                                            }
                                            audioSource.clip = restartClip;
                                            audioSource.Play();
                                            yield return new WaitForSeconds(restartClip.length + 0.3f);
                                        }
                                        else if (enableDebugLog)
                                        {
                                            Debug.LogWarning($"[ScenarioRunner] リスタートメッセージのクリップが取得できません: \"{restartMessage}\"");
                                        }
                                    }
                                    
                                    // タイムアウトカウンターをリセット
                                    voiceTriggerTimeoutCount = 0;
                                    
                                    // 1拍（約1秒）待ってからシナリオの最初から再開
                                    yield return new WaitForSeconds(1.0f);
                                    
                                    // シナリオの最初から再開
                                    i = -1; // 次のループで i++ されるので -1 にする
                                    break; // 現在のエントリのループを抜ける
                                }
                                
                                // 1回目のタイムアウトメッセージを再生
                                if (voiceTriggerTimeoutCount == 1 && ttsService != null && audioSource != null)
                                {
                                    string timeoutMessage1 = "だれもいないのかな？もうすこしマイクの近くで大きい声でうたってみて。";
                                    // シナリオからメッセージを取得してキャッシュを利用
                                    var timeoutEntry = _scenario.entries.Find(entry => entry.id == "timeout_1");
                                    if (timeoutEntry != null && !string.IsNullOrEmpty(timeoutEntry.text))
                                    {
                                        timeoutMessage1 = timeoutEntry.text;
                                    }

                                    AudioClip timeoutClip1 = ttsService.GetCachedClip(timeoutMessage1);
                                    
                                    if (timeoutClip1 != null)
                                    {
                                        if (enableDebugLog)
                                        {
                                            Debug.Log($"[ScenarioRunner] タイムアウトメッセージ再生: \"{timeoutMessage1}\"");
                                        }
                                        audioSource.clip = timeoutClip1;
                                        audioSource.Play();
                                        yield return new WaitForSeconds(timeoutClip1.length + 0.3f);
                                    }
                                    else if (enableDebugLog)
                                    {
                                        Debug.LogWarning($"[ScenarioRunner] タイムアウトメッセージのクリップが取得できません: \"{timeoutMessage1}\"");
                                    }
                                }
                                
                                retryEntry = true;
                                yield return new WaitForSeconds(0.5f);
                                continue;
                            }
                            else
                            {
                                // 成功したらタイムアウトカウンターをリセット
                                voiceTriggerTimeoutCount = 0;
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
                Debug.Log("[ScenarioRunner] シナリオ再生完了。5秒後に再開します。");
            }
            OperationLogger.Instance?.Log("Scenario", "Completed", "Restarting in 5s");
            
            yield return new WaitForSeconds(5.0f);
            
            // 再帰呼び出しではなく、コルーチンを再開する形にする
            // ただし、単純にStartCoroutineするとスタックオーバーフローの可能性があるため、
            // ループ構造にするのが望ましいが、ここでは簡易的に自分自身を再度呼び出す形にする
            // 現在のコルーチンは終了し、新しいコルーチンを開始する
            StartCoroutine(CoRun());
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
