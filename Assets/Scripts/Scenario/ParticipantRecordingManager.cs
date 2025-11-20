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

        [Header("Debug")]
        public bool enableDebugLog = true;

        private readonly List<AudioClip> _recordedClips = new();

        public IReadOnlyList<AudioClip> RecordedClips => _recordedClips;

        public void ClearRecordings() => _recordedClips.Clear();

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

