using UnityEngine;

namespace Encounter.Audio
{
    public class RMSMeter : MonoBehaviour
    {
        public float ComputeRms01(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0f;

            // RMS計算
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            float rms = Mathf.Sqrt(sum / samples.Length);

            // 0..1に正規化（経験的な閾値を使用）
            // 通常の音声入力ではRMSは0.01-0.1程度、大きな音で0.3程度
            // より大きな値も考慮して、0.5を上限として正規化
            float normalized = Mathf.Clamp01(rms * 2f);
            return normalized;
        }
    }
}
