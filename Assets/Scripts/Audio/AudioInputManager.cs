using UnityEngine;
using System;

namespace Encounter.Audio
{
    public class AudioInputManager : MonoBehaviour
    {
        [Header("Microphone")]
        [Tooltip("空欄=デフォルト。Microphone.devices から選択して設定")]
        public string microphoneName = "";

        [Tooltip("サンプリングレート")]
        public int sampleRate = 48000;

        [Tooltip("スペクトラム/ピッチ解析の更新間隔(秒)")]
        public float analysisInterval = 0.03f;

        [Tooltip("解析用サンプル数")]
        public int sampleLength = 4096;

        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        public event Action<float> OnRms;           // 0..1程度
        public event Action<float> OnPitchHz;       // 推定周波数(Hz)，未検出時は <=0

        public AudioClip CurrentClip { get; private set; }

        private string _currentMicName;
        private float[] _samples;
        private float _lastAnalysisTime;
        private RMSMeter _rmsMeter;
        private PitchEstimator _pitchEstimator;

        void Awake()
        {
            _samples = new float[sampleLength];
            _rmsMeter = GetComponent<RMSMeter>();
            if (_rmsMeter == null)
            {
                _rmsMeter = gameObject.AddComponent<RMSMeter>();
            }

            _pitchEstimator = GetComponent<PitchEstimator>();
            if (_pitchEstimator == null)
            {
                _pitchEstimator = gameObject.AddComponent<PitchEstimator>();
            }
        }

        void Start()
        {
            StartMicrophone();
        }

        void OnDestroy()
        {
            StopMicrophone();
        }

        void OnApplicationQuit()
        {
            StopMicrophone();
        }

        void Update()
        {
            if (CurrentClip == null) return;

            // 分析間隔ごとに処理
            if (Time.time - _lastAnalysisTime >= analysisInterval)
            {
                _lastAnalysisTime = Time.time;
                AnalyzeAudio();
            }
        }

        private void StartMicrophone()
        {
            // マイクデバイスの確認
            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[AudioInputManager] マイクデバイスが検出されませんでした。");
                return;
            }

            // マイク一覧を表示
            if (enableDebugLog)
            {
                Debug.Log("[AudioInputManager] 利用可能なマイク一覧:");
                foreach (var device in Microphone.devices)
                {
                    Debug.Log($"  - {device}");
                }
            }

            // 使用するマイクを決定
            if (string.IsNullOrEmpty(microphoneName))
            {
                _currentMicName = Microphone.devices[0];
                if (enableDebugLog)
                {
                    Debug.Log($"[AudioInputManager] デフォルトマイクを使用: {_currentMicName}");
                }
            }
            else
            {
                bool found = false;
                foreach (var device in Microphone.devices)
                {
                    if (device == microphoneName)
                    {
                        found = true;
                        _currentMicName = microphoneName;
                        break;
                    }
                }

                if (!found)
                {
                    if (enableDebugLog)
                    {
                        Debug.LogWarning($"[AudioInputManager] 指定されたマイク '{microphoneName}' が見つかりません。最初のマイクを使用します。");
                    }
                    _currentMicName = Microphone.devices[0];
                }
            }

            // マイク録音開始
            try
            {
                CurrentClip = Microphone.Start(_currentMicName, true, 1, sampleRate);
                if (CurrentClip != null && enableDebugLog)
                {
                    Debug.Log($"[AudioInputManager] マイク開始: {_currentMicName} (サンプルレート: {sampleRate}Hz)");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioInputManager] マイク開始エラー: {e.Message}");
            }
        }

        private void StopMicrophone()
        {
            if (!string.IsNullOrEmpty(_currentMicName) && Microphone.IsRecording(_currentMicName))
            {
                Microphone.End(_currentMicName);
                if (enableDebugLog)
                {
                    Debug.Log($"[AudioInputManager] マイク停止: {_currentMicName}");
                }
            }
            CurrentClip = null;
        }

        private void AnalyzeAudio()
        {
            if (CurrentClip == null || _samples == null) return;

            // マイクの現在位置を取得
            int micPosition = Microphone.GetPosition(_currentMicName);
            if (micPosition < sampleLength) return;

            // サンプルデータを取得
            int startPos = micPosition - sampleLength;
            if (startPos < 0) return;

            if (CurrentClip.GetData(_samples, startPos))
            {
                // RMS計算
                float rms = _rmsMeter.ComputeRms01(_samples);
                OnRms?.Invoke(rms);

                // ピッチ推定
                float pitchHz = _pitchEstimator.EstimatePitchHz(_samples, sampleRate);
                
                // デバッグログ（頻繁に出力されないように）
                if (enableDebugLog && Time.frameCount % 60 == 0) // 約1秒に1回
                {
                    if (pitchHz > 0f)
                    {
                        Debug.Log($"[AudioInputManager] ピッチ検出: {pitchHz:F1}Hz, RMS: {rms:F3}");
                    }
                    else if (rms > 0.01f) // 音量があるのにピッチが検出されない場合
                    {
                        Debug.LogWarning($"[AudioInputManager] 音量はあるがピッチ未検出 (RMS: {rms:F3})");
                    }
                }
                
                OnPitchHz?.Invoke(pitchHz);
            }
        }

        public void SetMicrophone(string deviceName)
        {
            if (deviceName == microphoneName) return;

            StopMicrophone();
            microphoneName = deviceName;
            StartMicrophone();
        }
    }
}
