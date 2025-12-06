using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Encounter.Audio;

namespace Encounter.Scenario
{
    /// <summary>
    /// ハーモニー評価を行うクラス
    /// 参加者を傷つけない、個性を尊重する評価を返す
    /// </summary>
    public class HarmonyEvaluator
    {
        /// <summary>
        /// 録音された各音の評価データ
        /// </summary>
        [Serializable]
        public class RecordingEvaluation
        {
            public float averagePitchHz;      // 平均ピッチ（Hz）
            public float pitchStability;     // ピッチの安定性（0-1、高いほど安定）
            public float averageVolume;       // 平均音量（RMS、0-1）
            public float volumeStability;    // 音量の安定性（0-1、高いほど安定）
            public float energy;              // 音のエネルギー（0-1）
            public int targetMidiNote;        // 目標MIDIノート番号（-1の場合は未設定）
        }

        /// <summary>
        /// ハーモニー評価結果
        /// </summary>
        public enum HarmonyLevel
        {
            Excellent,  // 素晴らしい
            Good,       // 良い
            Nice        // 良い（個性を尊重）
        }

        /// <summary>
        /// ハーモニー評価結果
        /// </summary>
        public class HarmonyEvaluationResult
        {
            public HarmonyLevel level;
            public string message;
            public float overallScore;  // 総合スコア（0-1）
        }

        /// <summary>
        /// AudioClipから評価データを生成
        /// </summary>
        public static RecordingEvaluation EvaluateRecording(
            AudioClip clip, 
            int targetMidiNote = -1,
            AudioInputManager audioInputManager = null)
        {
            if (clip == null)
            {
                return new RecordingEvaluation
                {
                    averagePitchHz = 0f,
                    pitchStability = 0f,
                    averageVolume = 0f,
                    volumeStability = 0f,
                    energy = 0f,
                    targetMidiNote = targetMidiNote
                };
            }

            // オーディオデータを取得
            int sampleRate = clip.frequency;
            int channels = clip.channels;
            int sampleCount = clip.samples;
            
            if (sampleCount == 0)
            {
                return new RecordingEvaluation
                {
                    averagePitchHz = 0f,
                    pitchStability = 0f,
                    averageVolume = 0f,
                    volumeStability = 0f,
                    energy = 0f,
                    targetMidiNote = targetMidiNote
                };
            }

            float[] rawData = new float[sampleCount * channels];
            clip.GetData(rawData, 0);

            // モノラルに変換
            float[] monoData = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    sum += rawData[i * channels + c];
                }
                monoData[i] = sum / channels;
            }

            // RMS計算
            float rmsSum = 0f;
            float rmsSumSq = 0f;
            int validSamples = 0;
            List<float> rmsValues = new List<float>();
            
            int windowSize = Mathf.Min(4096, sampleCount);
            int stepSize = windowSize / 2;
            
            for (int i = 0; i <= sampleCount - windowSize; i += stepSize)
            {
                float[] window = new float[windowSize];
                Array.Copy(monoData, i, window, 0, windowSize);
                
                float rms = 0f;
                for (int j = 0; j < windowSize; j++)
                {
                    rms += window[j] * window[j];
                }
                rms = Mathf.Sqrt(rms / windowSize);
                
                if (rms > 0.01f) // 無音でない場合のみ
                {
                    rmsValues.Add(rms);
                    rmsSum += rms;
                    rmsSumSq += rms * rms;
                    validSamples++;
                }
            }

            float averageVolume = validSamples > 0 ? rmsSum / validSamples : 0f;
            
            // 音量の安定性（標準偏差の逆数）
            float volumeVariance = 0f;
            if (validSamples > 1 && rmsValues.Count > 1)
            {
                foreach (var rms in rmsValues)
                {
                    volumeVariance += (rms - averageVolume) * (rms - averageVolume);
                }
                volumeVariance /= (rmsValues.Count - 1);
            }
            float volumeStdDev = Mathf.Sqrt(volumeVariance);
            float volumeStability = averageVolume > 0f 
                ? Mathf.Clamp01(1f - (volumeStdDev / (averageVolume + 0.1f))) 
                : 0f;

            // ピッチ推定
            List<float> pitchValues = new List<float>();
            YinPitchEstimator pitchEstimator = new YinPitchEstimator(sampleRate, windowSize);
            
            for (int i = 0; i <= sampleCount - windowSize; i += stepSize)
            {
                float[] window = new float[windowSize];
                Array.Copy(monoData, i, window, 0, windowSize);
                
                float pitch = pitchEstimator.GetPitch(window);
                if (pitch > 0f)
                {
                    pitchValues.Add(pitch);
                }
            }

            float averagePitchHz = 0f;
            float pitchStability = 0f;
            
            if (pitchValues.Count > 0)
            {
                averagePitchHz = pitchValues.Average();
                
                // ピッチの安定性（標準偏差の逆数）
                float pitchVariance = 0f;
                if (pitchValues.Count > 1)
                {
                    foreach (var pitch in pitchValues)
                    {
                        pitchVariance += (pitch - averagePitchHz) * (pitch - averagePitchHz);
                    }
                    pitchVariance /= (pitchValues.Count - 1);
                }
                float pitchStdDev = Mathf.Sqrt(pitchVariance);
                // ピッチの安定性：標準偏差が小さいほど安定（セント単位で評価）
                float pitchStdDevCents = averagePitchHz > 0f 
                    ? 1200f * Mathf.Log(pitchStdDev / averagePitchHz + 1f, 2f) 
                    : 1000f;
                pitchStability = Mathf.Clamp01(1f - (pitchStdDevCents / 200f)); // 200セント以内なら安定
            }

            // エネルギー計算（RMSの二乗の平均）
            float energy = 0f;
            for (int i = 0; i < monoData.Length; i++)
            {
                energy += monoData[i] * monoData[i];
            }
            energy = Mathf.Sqrt(energy / monoData.Length);

            return new RecordingEvaluation
            {
                averagePitchHz = averagePitchHz,
                pitchStability = pitchStability,
                averageVolume = averageVolume,
                volumeStability = volumeStability,
                energy = energy,
                targetMidiNote = targetMidiNote
            };
        }

        /// <summary>
        /// 複数の録音からハーモニーを評価
        /// </summary>
        public static HarmonyEvaluationResult EvaluateHarmony(
            List<RecordingEvaluation> evaluations,
            List<SingingNote> referenceNotes = null)
        {
            if (evaluations == null || evaluations.Count == 0)
            {
                return new HarmonyEvaluationResult
                {
                    level = HarmonyLevel.Nice,
                    message = "みんなの声が聞こえてうれしい！",
                    overallScore = 0.5f
                };
            }

            // 各評価のスコアを計算
            List<float> individualScores = new List<float>();
            
            for (int i = 0; i < evaluations.Count; i++)
            {
                var eval = evaluations[i];
                
                // 個別スコア：音量、ピッチ安定性、エネルギーを組み合わせ
                float individualScore = 0f;
                
                // 音量が適切か（0.05以上）
                float volumeScore = Mathf.Clamp01(eval.averageVolume / 0.1f);
                
                // ピッチの安定性
                float pitchScore = eval.pitchStability;
                
                // エネルギー（音の存在感）
                float energyScore = Mathf.Clamp01(eval.energy / 0.1f);
                
                // 目標ピッチとの一致度（目標が設定されている場合）
                float pitchMatchScore = 1f;
                if (eval.targetMidiNote >= 0 && referenceNotes != null && i < referenceNotes.Count)
                {
                    var refNote = referenceNotes[i];
                    if (refNote.key >= 0 && eval.averagePitchHz > 0f)
                    {
                        // MIDIノートを周波数に変換
                        float targetFreq = MidiNoteToFrequency(refNote.key);
                        float freqRatio = eval.averagePitchHz / targetFreq;
                        // 半音以内なら満点、1オクターブ以内なら減点
                        float semitones = Mathf.Abs(12f * Mathf.Log(freqRatio, 2f));
                        pitchMatchScore = Mathf.Clamp01(1f - (semitones / 12f));
                    }
                }
                
                // 総合スコア（重み付け平均）
                individualScore = (volumeScore * 0.3f + pitchScore * 0.3f + energyScore * 0.2f + pitchMatchScore * 0.2f);
                individualScores.Add(individualScore);
            }

            // ハーモニースコア：複数の音が調和しているか
            float harmonyScore = 0f;
            if (evaluations.Count > 1)
            {
                // ピッチの分散を評価（分散が小さいほど調和している）
                List<float> pitches = new List<float>();
                foreach (var eval in evaluations)
                {
                    if (eval.averagePitchHz > 0f)
                    {
                        pitches.Add(eval.averagePitchHz);
                    }
                }
                
                if (pitches.Count > 1)
                {
                    float avgPitch = pitches.Average();
                    float pitchVariance = 0f;
                    foreach (var pitch in pitches)
                    {
                        pitchVariance += (pitch - avgPitch) * (pitch - avgPitch);
                    }
                    pitchVariance /= pitches.Count;
                    
                    // 分散が小さいほど調和している（適切な範囲内での分散）
                    harmonyScore = Mathf.Clamp01(1f - (Mathf.Sqrt(pitchVariance) / (avgPitch + 50f)));
                }
                else
                {
                    harmonyScore = 0.5f; // ピッチが検出できない場合は中間値
                }
            }
            else
            {
                harmonyScore = 1f; // 1音のみの場合は満点
            }

            // 総合スコア
            float overallScore = 0f;
            if (individualScores.Count > 0)
            {
                float avgIndividualScore = individualScores.Average();
                overallScore = (avgIndividualScore * 0.6f + harmonyScore * 0.4f);
            }
            else
            {
                overallScore = 0.5f;
            }

            // 評価レベルを決定（参加者を傷つけない、個性を尊重する評価）
            HarmonyLevel level;
            string message;
            
            if (overallScore >= 0.7f)
            {
                level = HarmonyLevel.Excellent;
                message = "すばらしいハーモニー！みんなの声が美しく響き合っているね。";
            }
            else if (overallScore >= 0.5f)
            {
                level = HarmonyLevel.Good;
                message = "いい感じだね！それぞれの声の個性が光っているよ。";
            }
            else
            {
                level = HarmonyLevel.Nice;
                message = "みんなの声が聞こえてうれしい！それぞれの声に個性があって素敵だね。";
            }

            return new HarmonyEvaluationResult
            {
                level = level,
                message = message,
                overallScore = overallScore
            };
        }

        /// <summary>
        /// MIDIノート番号を周波数（Hz）に変換
        /// </summary>
        private static float MidiNoteToFrequency(int midiNote)
        {
            // A4 (MIDI 69) = 440Hz
            return 440f * Mathf.Pow(2f, (midiNote - 69) / 12f);
        }
    }
}

