using UnityEngine;
using Encounter.Audio;

namespace Encounter.UI
{
    /// <summary>
    /// 音声入力レベルをリアルタイムで表示するUIコンポーネント
    /// </summary>
    public class AudioLevelMeter : MonoBehaviour
    {
        [Header("References")]
        public AudioInputManager audioInputManager;

        [Header("Display Settings")]
        [Tooltip("表示位置（左上からのオフセット）")]
        public Vector2 position = new Vector2(10, 180);
        
        [Tooltip("表示サイズ")]
        public Vector2 size = new Vector2(300, 100);
        
        [Tooltip("バーの幅")]
        public float barWidth = 20f;
        
        [Tooltip("バーの間隔")]
        public float barSpacing = 5f;
        
        [Tooltip("バーの最大高さ")]
        public float maxBarHeight = 80f;
        
        [Tooltip("閾値表示の色")]
        public Color thresholdColor = Color.yellow;
        
        [Tooltip("バーの色（閾値以下）")]
        public Color barColorLow = Color.green;
        
        [Tooltip("バーの色（閾値以上）")]
        public Color barColorHigh = Color.red;

        private float _currentRms = 0f;
        private float _smoothedRms = 0f;
        private const float SMOOTH_FACTOR = 0.3f;

        void Start()
        {
            if (audioInputManager == null)
            {
                audioInputManager = FindFirstObjectByType<AudioInputManager>();
            }

            if (audioInputManager != null)
            {
                audioInputManager.OnRms += OnRmsReceived;
            }
        }

        void OnDestroy()
        {
            if (audioInputManager != null)
            {
                audioInputManager.OnRms -= OnRmsReceived;
            }
        }

        private void OnRmsReceived(float rms)
        {
            _currentRms = rms;
            // スムージング（視覚的に見やすくするため）
            _smoothedRms = Mathf.Lerp(_smoothedRms, rms, SMOOTH_FACTOR);
        }

        void OnGUI()
        {
            if (audioInputManager == null) return;

            // 背景ボックス
            Rect boxRect = new Rect(position.x, position.y, size.x, size.y);
            GUI.Box(boxRect, "Audio Level Meter");

            // RMS値の表示
            float threshold = audioInputManager.voiceDetectionThreshold;
            float barHeight = _smoothedRms * maxBarHeight;
            float thresholdY = position.y + size.y - (threshold * maxBarHeight) - 10;

            // バーを描画
            float barX = position.x + 10;
            float barY = position.y + size.y - 10;
            Rect barRect = new Rect(barX, barY - barHeight, barWidth, barHeight);
            
            // 閾値以上かどうかで色を変更
            Color barColor = _smoothedRms >= threshold ? barColorHigh : barColorLow;
            GUI.color = barColor;
            GUI.DrawTexture(barRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 閾値ラインを描画
            Rect thresholdRect = new Rect(barX, thresholdY, barWidth, 2f);
            GUI.color = thresholdColor;
            GUI.DrawTexture(thresholdRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 数値表示
            float labelX = barX + barWidth + barSpacing;
            GUI.Label(new Rect(labelX, position.y + 10, 200, 20), $"RMS: {_smoothedRms:F3}");
            GUI.Label(new Rect(labelX, position.y + 30, 200, 20), $"Threshold: {threshold:F3}");
            GUI.Label(new Rect(labelX, position.y + 50, 200, 20), $"Status: {(_smoothedRms >= threshold ? "VOICE" : "SILENT")}");
        }
    }
}

