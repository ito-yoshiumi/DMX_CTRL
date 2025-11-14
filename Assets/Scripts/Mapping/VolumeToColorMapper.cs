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

        public Color MapRmsToColor(float rms01)
        {
            float h = Mathf.Lerp(hueLow, hueHigh, Mathf.Clamp01(rms01));
            Color c = Color.HSVToRGB(h, 1f, 1f);
            return c;
        }
    }
}
