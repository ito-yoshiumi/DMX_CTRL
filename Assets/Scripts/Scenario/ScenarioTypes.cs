using System;
using System.Collections.Generic;
using UnityEngine;

namespace Encounter.Scenario
{
    [Serializable]
    public class SingingNote
    {
        // JsonUtilityはint?を正しく処理しないため、int型を使用し、-1をnullの代わりに使用
        public int key;           // MIDIノート番号（60=C4, 62=D4, 64=E4, 65=F4, 67=G4など）。lyricが空の場合は-1（nullの代わり）
        public int frame_length;  // フレーム長（通常45フレーム程度）
        public string lyric;      // 歌詞（例: "ド", "レ", "ミ"）。空文字列の場合はkeyは-1（nullの代わり）である必要がある
        
        // keyが-1の場合はnullとして扱う（JSON生成時）
        public bool IsKeyNull => key == -1;
    }

    [Serializable]
    public class FrameAudioQuery
    {
        public float[] f0;
        public float[] volume;
        public int[] phonemes;
        public float[] volumeScale;
        public int[] consonantLengths;
        public int[] vowelLengths;
    }

    [Serializable]
    public class ScenarioEntry
    {
        public string id;
        public string type;       // "wav" | "tts" | "singing"
        public string path;       // wav のときの Resources 相対 or Addressables key
        public string text;       // tts のとき
        public string dmxCue;     // Cues/xxx.json
        public float waitAfter;   // 再生後に待つ秒数(任意)
        public bool record;       // 録音するか
        public float recordSeconds = 0f;
        public bool playRecordedMix; // 録音済み音声を再生するか
        public string mixReferenceClip; // ミックス時に重ねる参照クリップ(Resources)
        public string mixReferenceEntryId; // ミックス時に重ねる参照エントリID（TTS/WAV）
        public string[] pitchNotes; // 音程指定（例: ["C4", "D4", "E4", "F4", "G4"]）
        public float speedScale = 1.0f; // 発話速度（1.0=通常、0.5=半分の速度、2.0=2倍の速度）
        public string labFilePath; // .labファイルのパス（Resources相対、例: "Lab/001_ずんだもん（ノーマル）_無名トラック"）
        public SingingNote[] singingNotes; // 歌声合成用のノート配列（type="singing"のとき使用）
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
