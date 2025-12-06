using UnityEngine;
using Encounter.Audio;
using System.Collections.Generic;

namespace Encounter.UI
{
    public class OperatorPanel : MonoBehaviour
    {
        [Header("References")]
        public Encounter.Scenario.ScenarioRunner runner;
        public AudioInputManager audioInputManager;
        public AudioLevelMeter audioLevelMeter;

        [Header("UI Settings")]
        [Tooltip("パネルの位置")]
        public Vector2 panelPosition = new Vector2(10, 10);
        
        [Tooltip("パネルのサイズ")]
        public Vector2 panelSize = new Vector2(300, 500);
        
        [Tooltip("音量（0-1）")]
        [Range(0f, 1f)]
        public float volume = 1f;

        [Header("Visibility")]
        [Tooltip("Uキーで表示/非表示を切り替え")]
        public bool isPanelVisible = true;

        [Header("Console Log")]
        [Tooltip("画面下部にコンソールログを表示するか")]
        public bool showConsoleLog = true;
        [Tooltip("表示する最大行数")]
        public int maxLogLines = 8;

        private Queue<string> _logQueue = new Queue<string>();
        private string _logText = "";
        private Vector2 _logScrollPosition;

        void Start()
        {
            if (audioInputManager == null)
            {
                audioInputManager = FindFirstObjectByType<AudioInputManager>();
            }
            
            // ScenarioRunnerのAudioSourceの音量を初期化
            if (runner != null && runner.audioSource != null)
            {
                runner.audioSource.volume = volume;
            }

            if (audioLevelMeter == null)
            {
                audioLevelMeter = FindFirstObjectByType<AudioLevelMeter>();
            }
        }

        void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        void HandleLog(string logString, string stackTrace, LogType type)
        {
            string color = "white";
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    color = "red";
                    break;
                case LogType.Warning:
                    color = "yellow";
                    break;
            }
            
            string formattedLog = $"<color={color}>[{System.DateTime.Now:HH:mm:ss}] {logString}</color>";
            
            _logQueue.Enqueue(formattedLog);
            while (_logQueue.Count > maxLogLines)
            {
                _logQueue.Dequeue();
            }
            _logText = string.Join("\n", _logQueue);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.U))
            {
                isPanelVisible = !isPanelVisible;
                if (audioLevelMeter != null)
                {
                    audioLevelMeter.isPanelVisible = isPanelVisible;
                }
            }
        }

        void OnGUI()
        {
            if (!isPanelVisible) return;

            // コンソールログ表示 (常に表示、または設定による)
            if (showConsoleLog)
            {
                float logHeight = 150f;
                GUILayout.BeginArea(new Rect(10, Screen.height - logHeight - 10, Screen.width - 20, logHeight), GUI.skin.box);
                GUILayout.Label("Console Log");
                
                // スタイル設定（リッチテキスト有効化）
                GUIStyle logStyle = new GUIStyle(GUI.skin.label);
                logStyle.richText = true;
                logStyle.wordWrap = true;
                
                GUILayout.Label(_logText, logStyle);
                GUILayout.EndArea();
            }

            // シナリオコントロールパネル
            GUILayout.BeginArea(new Rect(panelPosition.x, panelPosition.y, panelSize.x, panelSize.y), GUI.skin.box);
            GUILayout.Label("Operator Panel (Toggle: U)");
            
            // 初期化が完了していない場合はボタンを無効化
            bool isInitialized = runner != null && runner.IsInitialized;
            GUI.enabled = isInitialized;
            
            if (GUILayout.Button("Run Scenario (Space)"))
            {
                if (isInitialized)
                {
                    runner?.RunAll();
                }
            }
            
            if (GUILayout.Button("Stop Scenario (R)"))
            {
                if (isInitialized)
                {
                    runner?.Stop();
                }
            }
            
            GUI.enabled = true;
            
            // 初期化中の場合、メッセージを表示
            if (!isInitialized && runner != null)
            {
                GUILayout.Space(5);
                GUILayout.Label("初期化中...", GUI.skin.label);
            }
            
            GUILayout.Space(10);
            
            // 音量調整
            GUILayout.Label("Volume", GUI.skin.label);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Volume: {volume:F2}", GUILayout.Width(150));
            float newVolume = GUILayout.HorizontalSlider(volume, 0f, 1f, GUILayout.Width(100));
            if (Mathf.Abs(newVolume - volume) > 0.001f)
            {
                volume = newVolume;
                if (runner != null && runner.audioSource != null)
                {
                    runner.audioSource.volume = volume;
                }
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // 音声検出設定
            if (audioInputManager != null)
            {
                GUILayout.Label("Voice Detection Settings", GUI.skin.label);
                
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Threshold: {audioInputManager.voiceDetectionThreshold:F3}", GUILayout.Width(150));
                float newThreshold = GUILayout.HorizontalSlider(
                    audioInputManager.voiceDetectionThreshold, 
                    0f, 
                    1f, 
                    GUILayout.Width(100)
                );
                if (Mathf.Abs(newThreshold - audioInputManager.voiceDetectionThreshold) > 0.001f)
                {
                    audioInputManager.voiceDetectionThreshold = newThreshold;
                }
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Min Duration: {audioInputManager.voiceDetectionMinDuration:F2}s", GUILayout.Width(150));
                float newMinDuration = GUILayout.HorizontalSlider(
                    audioInputManager.voiceDetectionMinDuration, 
                    0f, 
                    1f, 
                    GUILayout.Width(100)
                );
                if (Mathf.Abs(newMinDuration - audioInputManager.voiceDetectionMinDuration) > 0.001f)
                {
                    audioInputManager.voiceDetectionMinDuration = newMinDuration;
                }
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                GUILayout.Label($"End Silence: {audioInputManager.voiceEndSilenceDuration:F2}s", GUILayout.Width(150));
                float newEndSilence = GUILayout.HorizontalSlider(
                    audioInputManager.voiceEndSilenceDuration, 
                    0f, 
                    2f, 
                    GUILayout.Width(100)
                );
                if (Mathf.Abs(newEndSilence - audioInputManager.voiceEndSilenceDuration) > 0.001f)
                {
                    audioInputManager.voiceEndSilenceDuration = newEndSilence;
                }
                GUILayout.EndHorizontal();
                
                GUILayout.Space(10);

                // ピッチ推定設定
                GUILayout.Label("Pitch Estimation", GUI.skin.label);
                GUILayout.Label($"Current Pitch: {audioInputManager.LastDetectedPitch:F1} Hz");
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(audioInputManager.estimationMode == AudioInputManager.PitchEstimationMode.ACF ? "[ACF]" : "ACF"))
                {
                    audioInputManager.estimationMode = AudioInputManager.PitchEstimationMode.ACF;
                }
                if (GUILayout.Button(audioInputManager.estimationMode == AudioInputManager.PitchEstimationMode.YIN ? "[YIN]" : "YIN"))
                {
                    audioInputManager.estimationMode = AudioInputManager.PitchEstimationMode.YIN;
                }
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndArea();
        }
    }
}
