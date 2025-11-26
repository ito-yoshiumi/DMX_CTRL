using UnityEngine;
using Encounter.Audio;

namespace Encounter.UI
{
    public class OperatorPanel : MonoBehaviour
    {
        [Header("References")]
        public Encounter.Scenario.ScenarioRunner runner;
        public AudioInputManager audioInputManager;

        [Header("UI Settings")]
        [Tooltip("パネルの位置")]
        public Vector2 panelPosition = new Vector2(10, 10);
        
        [Tooltip("パネルのサイズ")]
        public Vector2 panelSize = new Vector2(300, 280);
        
        [Tooltip("音量（0-1）")]
        [Range(0f, 1f)]
        public float volume = 1f;

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
        }

        void OnGUI()
        {
            // シナリオコントロールパネル
            GUILayout.BeginArea(new Rect(panelPosition.x, panelPosition.y, panelSize.x, panelSize.y), GUI.skin.box);
            GUILayout.Label("Operator Panel");
            
            if (GUILayout.Button("Run Scenario (Space)"))
            {
                runner?.RunAll();
            }
            
            if (GUILayout.Button("Stop Scenario (R)"))
            {
                runner?.Stop();
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
            }
            
            GUILayout.EndArea();
        }
    }
}
