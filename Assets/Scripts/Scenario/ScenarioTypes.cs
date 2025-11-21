using System;
using System.Collections.Generic;
using UnityEngine;

namespace Encounter.Scenario
{
    [Serializable]
    public class ScenarioEntry
    {
        public string id;
        public string type;       // "wav" | "tts"
        public string path;       // wav のときの Resources 相対 or Addressables key
        public string text;       // tts のとき
        public string dmxCue;     // Cues/xxx.json
        public float waitAfter;   // 再生後に待つ秒数(任意)
        public bool record;       // 録音するか
        public float recordSeconds = 0f;
        public bool playRecordedMix; // 録音済み音声を再生するか
        public string mixReferenceClip; // ミックス時に重ねる参照クリップ(Resources)
        public string mixReferenceEntryId; // ミックス時に重ねる参照エントリID（TTS/WAV）
    }

    [Serializable]
    public class ScenarioFile
    {
        public List<ScenarioEntry> entries = new();
    }

    [Serializable]
    public class DmxKeyframe
    {
        public float t;
        public List<FixtureState> fixtures = new();
    }

    [Serializable]
    public class FixtureState
    {
        public int index; // 0-based fixture index
        public int height = 128;
        public int r = 0, g = 0, b = 0;
    }

    [Serializable]
    public class DmxCue
    {
        public float length = 0f;
        public List<DmxKeyframe> keyframes = new();
    }
}
