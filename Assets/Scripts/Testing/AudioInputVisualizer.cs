using UnityEngine;
using Encounter.Audio;
using Encounter.Mapping;

namespace Encounter.Testing
{
    /// <summary>
    /// マイク入力のRMS/ピッチをSquareスプライトに可視化するテスト用コンポーネント
    /// </summary>
    public class AudioInputVisualizer : MonoBehaviour
    {
        [Header("References")]
        public AudioInputManager audioInputManager;
        public VolumeToColorMapper volumeMapper;
        public PitchToHeightMapper pitchMapper;

        [Header("Visualization Target")]
        [Tooltip("色と高さを反映するSpriteRenderer")]
        public SpriteRenderer targetSprite;

        [Header("Height Visualization")]
        [Tooltip("ピッチに応じたY座標の範囲")]
        public float minY = -5f;
        public float maxY = 5f;

        [Header("Debug")]
        [Tooltip("コンソールにRMS/ピッチを出力")]
        public bool logValues = false;
        public float logInterval = 1f;

        private float _lastLogTime;
        private Vector3 _initialPosition;

        void Start()
        {
            if (audioInputManager == null)
            {
                audioInputManager = FindFirstObjectByType<AudioInputManager>();
            }

            if (volumeMapper == null)
            {
                volumeMapper = GetComponent<VolumeToColorMapper>();
                if (volumeMapper == null)
                {
                    volumeMapper = gameObject.AddComponent<VolumeToColorMapper>();
                }
            }

            if (pitchMapper == null)
            {
                pitchMapper = GetComponent<PitchToHeightMapper>();
                if (pitchMapper == null)
                {
                    pitchMapper = gameObject.AddComponent<PitchToHeightMapper>();
                }
            }

            if (targetSprite == null)
            {
                targetSprite = GetComponent<SpriteRenderer>();
            }

            if (targetSprite != null)
            {
                _initialPosition = targetSprite.transform.position;
            }

            // イベント購読
            if (audioInputManager != null)
            {
                audioInputManager.OnRms += OnRmsReceived;
                audioInputManager.OnPitchHz += OnPitchReceived;
            }
        }

        void OnDestroy()
        {
            if (audioInputManager != null)
            {
                audioInputManager.OnRms -= OnRmsReceived;
                audioInputManager.OnPitchHz -= OnPitchReceived;
            }
        }

        private void OnRmsReceived(float rms)
        {
            if (!enabled) return; // 無効化されている場合は処理をスキップ
            if (targetSprite == null || volumeMapper == null) return;

            // 音量に応じて色を変更
            Color color = volumeMapper.MapRmsToColor(rms);
            targetSprite.color = color;

            if (logValues && Time.time - _lastLogTime >= logInterval)
            {
                Debug.Log($"[AudioInputVisualizer] RMS: {rms:F3}, Color: {color}");
                _lastLogTime = Time.time;
            }
        }

        private void OnPitchReceived(float pitchHz)
        {
            if (!enabled) return; // 無効化されている場合は処理をスキップ
            if (targetSprite == null || pitchMapper == null) return;

            // ピッチに応じて高さを変更
            int dmxHeight = pitchMapper.MapPitchHzToDmx(pitchHz);
            
            // DMX値(100-255)をY座標(-5 to 5)にマッピング
            float t = (dmxHeight - 100f) / (255f - 100f);
            float y = Mathf.Lerp(minY, maxY, t);
            
            Vector3 pos = _initialPosition;
            pos.y = y;
            targetSprite.transform.position = pos;

            if (logValues && Time.time - _lastLogTime >= logInterval && pitchHz > 0)
            {
                Debug.Log($"[AudioInputVisualizer] Pitch: {pitchHz:F1}Hz, DMX Height: {dmxHeight}, Y: {y:F2}");
                _lastLogTime = Time.time;
            }
        }
    }
}

