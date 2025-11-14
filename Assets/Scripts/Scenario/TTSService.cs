using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Encounter.Scenario
{
    public class TTSService : MonoBehaviour
    {
        private readonly Dictionary<string, AudioClip> _cache = new();

        public IEnumerator PrewarmAsync(ScenarioFile scenario)
        {
            // TODO: type=="tts" を抽出し、合成→AudioClip化→_cache
            yield break;
        }

        public AudioClip GetCachedClip(string text)
        {
            _cache.TryGetValue(text, out var clip);
            return clip;
        }
    }
}
