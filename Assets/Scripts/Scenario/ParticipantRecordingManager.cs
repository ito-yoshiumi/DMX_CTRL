using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Encounter.Audio;

namespace Encounter.Scenario
{
    public class ParticipantRecordingManager : MonoBehaviour
    {
        [Header("References")]
        public AudioInputManager audioInputManager;

        [Header("Recording Settings")]
        [Tooltip("保持する録音クリップの最大数")]
        public int maxRecordings = 5;
        [Tooltip("ミックス時にクリップを正規化するか")]
        public bool normalizeMix = true;
        
        [Header("Voice-Triggered Recording")]
        [Tooltip("音声検出による自動録音を有効にするか")]
        public bool enableVoiceTriggeredRecording = false;
        
        [Tooltip("音声検出時の最大録音時間（秒）")]
        [Range(1f, 10f)]
        public float maxRecordingDuration = 5f;

        [Header("Debug")]
        public bool enableDebugLog = true;

        private readonly List<AudioClip> _recordedClips = new();
        private Coroutine _voiceTriggeredRecordingCoroutine;

        public IReadOnlyList<AudioClip> RecordedClips => _recordedClips;

        public void ClearRecordings() => _recordedClips.Clear();

        void Start()
        {
            if (audioInputManager == null)
            {
                audioInputManager = FindFirstObjectByType<AudioInputManager>();
            }

            if (audioInputManager != null && enableVoiceTriggeredRecording)
            {
                audioInputManager.OnVoiceDetected += OnVoiceDetected;
                audioInputManager.OnVoiceEnded += OnVoiceEnded;
            }
        }

        void OnDestroy()
        {
            if (audioInputManager != null)
            {
                audioInputManager.OnVoiceDetected -= OnVoiceDetected;
                audioInputManager.OnVoiceEnded -= OnVoiceEnded;
            }

            if (_voiceTriggeredRecordingCoroutine != null)
            {
                StopCoroutine(_voiceTriggeredRecordingCoroutine);
            }
        }

        private void OnVoiceDetected()
        {
            if (!enableVoiceTriggeredRecording) return;
            if (_voiceTriggeredRecordingCoroutine != null) return; // 既に録音中

            if (enableDebugLog)
            {
                Debug.Log("[ParticipantRecordingManager] 音声検出: 録音を開始します");
            }

            _voiceTriggeredRecordingCoroutine = StartCoroutine(RecordOnVoiceDetected());
        }

        private void OnVoiceEnded()
        {
            if (!enableVoiceTriggeredRecording) return;
            if (_voiceTriggeredRecordingCoroutine == null) return; // 録音中でない

            if (enableDebugLog)
            {
                Debug.Log("[ParticipantRecordingManager] 音声終了: 録音を停止します");
            }

            // 録音は自動的に停止される（最大録音時間または音声終了で）
        }

        private IEnumerator RecordOnVoiceDetected()
        {
            if (audioInputManager == null) yield break;

            AudioClip recordedClip = null;
            float startTime = Time.time;
            bool recordingCompleted = false;

            // 録音開始
            Coroutine recordCoroutine = StartCoroutine(audioInputManager.RecordClipCoroutine(
                maxRecordingDuration,
                clip =>
                {
                    recordedClip = clip;
                    recordingCompleted = true;
                }));

            // 音声終了または最大録音時間まで待機
            while (!recordingCompleted && (Time.time - startTime) < maxRecordingDuration)
            {
                yield return null;
            }

            // 録音が完了するまで待機
            while (!recordingCompleted)
            {
                yield return null;
            }

            _voiceTriggeredRecordingCoroutine = null;

            if (recordedClip != null)
            {
                if (_recordedClips.Count >= Mathf.Max(1, maxRecordings))
                {
                    _recordedClips.RemoveAt(0);
                }

                _recordedClips.Add(recordedClip);

                if (enableDebugLog)
                {
                    Debug.Log($"[ParticipantRecordingManager] 音声検出録音を追加: {_recordedClips.Count}/{maxRecordings} (長さ: {recordedClip.length:F2}秒)");
                }
            }
        }

        public IEnumerator RecordAsync(float durationSeconds)
        {
            if (audioInputManager == null)
            {
                Debug.LogWarning("[ParticipantRecordingManager] AudioInputManager が設定されていません。");
                yield break;
            }

            AudioClip recordedClip = null;
            yield return audioInputManager.RecordClipCoroutine(
                Mathf.Max(0.1f, durationSeconds),
                clip => recordedClip = clip);

            if (recordedClip == null)
            {
                Debug.LogWarning("[ParticipantRecordingManager] 録音に失敗しました。");
                yield break;
            }

            if (_recordedClips.Count >= Mathf.Max(1, maxRecordings))
            {
                _recordedClips.RemoveAt(0);
            }

            _recordedClips.Add(recordedClip);

            if (enableDebugLog)
            {
                Debug.Log($"[ParticipantRecordingManager] 録音を追加: {_recordedClips.Count}/{maxRecordings}");
            }
        }

        public IEnumerator RecordWithTriggerAsync(float durationSeconds, float timeoutSeconds, System.Action<bool> onResult = null)
        {
            if (audioInputManager == null)
            {
                Debug.LogWarning("[ParticipantRecordingManager] AudioInputManager が設定されていません。");
                onResult?.Invoke(false);
                yield break;
            }

            if (enableDebugLog)
            {
                Debug.Log($"[ParticipantRecordingManager] 音声検出待機中... (タイムアウト: {timeoutSeconds:F1}秒)");
            }

            bool triggered = false;
            
            // すでに音声検出中の場合は即座に開始
            if (audioInputManager.IsVoiceDetected)
            {
                triggered = true;
                if (enableDebugLog) Debug.Log("[ParticipantRecordingManager] 既に音声を検出しています。即座に録音を開始します。");
            }
            else
            {
                // イベント待ち
                Action handler = () => triggered = true;
                audioInputManager.OnVoiceDetected += handler;
                
                float startTime = Time.time;
                while (!triggered && (Time.time - startTime) < timeoutSeconds)
                {
                    yield return null;
                }
                
                audioInputManager.OnVoiceDetected -= handler;
            }

            if (!triggered)
            {
                if (enableDebugLog) Debug.LogWarning("[ParticipantRecordingManager] 音声検出タイムアウト。");
                onResult?.Invoke(false);
                yield break;
            }
            else
            {
                 if (enableDebugLog) Debug.Log("[ParticipantRecordingManager] 音声を検出しました。録音を開始します。");
            }

            // 録音実行
            yield return RecordAsync(durationSeconds);
            onResult?.Invoke(true);
        }

        public AudioClip BuildMixClip(AudioClip referenceClip = null)
        {
            List<float[]> monoSources = new();
            int sampleRate = audioInputManager != null ? audioInputManager.sampleRate : 48000;
            int maxSamples = 0;

            foreach (var clip in _recordedClips)
            {
                if (clip == null) continue;
                var mono = ExtractMono(clip, out int clipRate);
                if (mono.Length == 0) continue;
                monoSources.Add(mono);
                sampleRate = clipRate;
                maxSamples = Mathf.Max(maxSamples, mono.Length);
            }

            if (referenceClip != null)
            {
                var referenceMono = ExtractMono(referenceClip, out int refRate);
                if (referenceMono.Length > 0)
                {
                    monoSources.Add(referenceMono);
                    sampleRate = refRate;
                    maxSamples = Mathf.Max(maxSamples, referenceMono.Length);
                }
            }

            if (monoSources.Count == 0 || maxSamples == 0)
            {
                return null;
            }

            float[] mix = new float[maxSamples];
            foreach (var source in monoSources)
            {
                for (int i = 0; i < source.Length && i < mix.Length; i++)
                {
                    mix[i] += source[i];
                }
            }

            if (normalizeMix)
            {
                float maxAbs = 0f;
                for (int i = 0; i < mix.Length; i++)
                {
                    maxAbs = Mathf.Max(maxAbs, Mathf.Abs(mix[i]));
                }
                if (maxAbs > 1f)
                {
                    float scale = 1f / maxAbs;
                    for (int i = 0; i < mix.Length; i++)
                    {
                        mix[i] *= scale;
                    }
                }
            }

            AudioClip mixClip = AudioClip.Create("ParticipantMix", mix.Length, 1, sampleRate, false);
            mixClip.SetData(mix, 0);
            return mixClip;
        }

        private float[] ExtractMono(AudioClip clip, out int sampleRate)
        {
            sampleRate = 0;
            if (clip == null) return Array.Empty<float>();

            sampleRate = clip.frequency;
            int channels = clip.channels;
            int sampleCount = clip.samples;

            if (sampleCount == 0) return Array.Empty<float>();

            float[] raw = new float[sampleCount * channels];
            clip.GetData(raw, 0);

            if (channels == 1)
            {
                return raw;
            }

            float[] mono = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    sum += raw[i * channels + c];
                }
                mono[i] = sum / channels;
            }

            return mono;
        }
    }
}

