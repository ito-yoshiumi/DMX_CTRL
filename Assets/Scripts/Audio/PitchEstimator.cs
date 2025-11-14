using UnityEngine;

namespace Encounter.Audio
{
    public class PitchEstimator : MonoBehaviour
    {
        [Tooltip("YIN/ACFなど実装方式の切替予定用")]
        public string method = "YIN";

        [Tooltip("最小周波数（Hz）")]
        public float minFreq = 80f;

        [Tooltip("最大周波数（Hz）")]
        public float maxFreq = 1000f;

        public float EstimatePitchHz(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length < 1024) return -1f;

            // 簡易的な自己相関関数（ACF）ベースのピッチ推定
            // YINアルゴリズムはP2で実装予定
            return EstimatePitchACF(samples, sampleRate);
        }

        private float EstimatePitchACF(float[] samples, int sampleRate)
        {
            int minPeriod = Mathf.RoundToInt(sampleRate / maxFreq);
            int maxPeriod = Mathf.RoundToInt(sampleRate / minFreq);
            maxPeriod = Mathf.Min(maxPeriod, samples.Length / 2);

            // サンプルのRMSを計算（正規化用）
            float rms = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                rms += samples[i] * samples[i];
            }
            rms = Mathf.Sqrt(rms / samples.Length);

            // RMSが非常に小さい場合は検出失敗
            if (rms < 0.001f)
            {
                return -1f;
            }

            float maxCorrelation = 0f;
            int bestPeriod = 0;

            // 自己相関を計算（正規化版）
            for (int period = minPeriod; period < maxPeriod; period++)
            {
                float correlation = 0f;
                float norm1 = 0f;
                float norm2 = 0f;
                int count = 0;

                for (int i = 0; i < samples.Length - period; i++)
                {
                    float s1 = samples[i];
                    float s2 = samples[i + period];
                    correlation += s1 * s2;
                    norm1 += s1 * s1;
                    norm2 += s2 * s2;
                    count++;
                }

                if (count > 0 && norm1 > 0f && norm2 > 0f)
                {
                    // 正規化された相関（-1から1の範囲）
                    float normalizedCorrelation = correlation / Mathf.Sqrt(norm1 * norm2);
                    
                    if (normalizedCorrelation > maxCorrelation)
                    {
                        maxCorrelation = normalizedCorrelation;
                        bestPeriod = period;
                    }
                }
            }

            // デバッグログ（頻繁に出力されないように）
            if (Time.frameCount % 180 == 0) // 約3秒に1回
            {
                if (bestPeriod > 0)
                {
                    float estimatedPitch = (float)sampleRate / bestPeriod;
                    Debug.Log($"[PitchEstimator] 相関: {maxCorrelation:F3}, Period: {bestPeriod}, 推定ピッチ: {estimatedPitch:F1}Hz");
                }
                else
                {
                    Debug.Log($"[PitchEstimator] ピッチ検出失敗 (maxCorrelation: {maxCorrelation:F3}, RMS: {rms:F4})");
                }
            }

            // 相関が低い場合は検出失敗（閾値を下げる）
            // 話し声では相関が低くなりがちなので、0.15程度に設定
            if (maxCorrelation < 0.15f || bestPeriod == 0)
            {
                return -1f;
            }

            float pitchHz = (float)sampleRate / bestPeriod;
            float clampedPitch = Mathf.Clamp(pitchHz, minFreq, maxFreq);
            
            // クランプされた場合の警告（デバッグ用）
            if (Mathf.Abs(pitchHz - clampedPitch) > 1f && Time.frameCount % 180 == 0)
            {
                Debug.LogWarning($"[PitchEstimator] ピッチが範囲外: {pitchHz:F1}Hz -> {clampedPitch:F1}Hz (範囲: {minFreq}-{maxFreq}Hz)");
            }
            
            return clampedPitch;
        }
    }
}
