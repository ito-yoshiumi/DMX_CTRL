using UnityEngine;

namespace Encounter.Mapping
{
    public class VolumeToColorMapper : MonoBehaviour
    {
        [Header("RMS -> Hue")]
        [Tooltip("小音量時のHue（例: 0.6=Blue）")]
        public float hueLow = 0.6f;
        [Tooltip("大音量時のHue（例: 0.0=Red）")]
        public float hueHigh = 0.0f;

        [Header("Sensitivity")]
        [Tooltip("RMS感度ゲイン（大きいほど小さな音量でも反応）")]
        [Range(1f, 10f)]
        public float rmsGain = 3f;
        
        [Tooltip("非線形マッピングの強度（0=線形、1=対数的、大きいほど派手）")]
        [Range(0f, 2f)]
        public float nonLinearPower = 1.5f;

        public Color MapRmsToColor(float rms01)
        {
            // ゲインをかけて感度を上げる
            float boostedRms = rms01 * rmsGain;
            
            // 非線形マッピング（べき乗）で派手に見えるように
            // 小さい値でも大きく反応するようにする
            float mappedRms = Mathf.Pow(Mathf.Clamp01(boostedRms), 1f / nonLinearPower);
            
            float h = Mathf.Lerp(hueLow, hueHigh, mappedRms);
            Color c = Color.HSVToRGB(h, 1f, 1f);
            return c;
        }
    }
}
