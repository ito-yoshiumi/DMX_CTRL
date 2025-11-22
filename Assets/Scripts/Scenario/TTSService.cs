using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine.Networking;

namespace Encounter.Scenario
{
    public class TTSService : MonoBehaviour
    {
        [Header("VOICEVOX/COEIROINK Settings")]
        [Tooltip("TTSエンジンのベースURL（デフォルト: http://127.0.0.1:50021）")]
        public string engineBaseUrl = "http://127.0.0.1:50021";
        
        [Tooltip("話者ID（VOICEVOX: 0-3など、COEIROINK: 0-7など）")]
        public int speakerId = 0;
        
        [Tooltip("歌声合成用の話者ID（クエリ生成用、例：波音リツ（ノーマル）のID:6000）")]
        public int singingSpeakerId = 6000;
        
        [Tooltip("ハミング機能用の話者ID（音声生成用、話者ID xx の場合は 30xx、例：話者ID 2 の場合は 3002）")]
        public int hummingSpeakerId = 3002;
        
        [Tooltip("エンジン接続タイムアウト（秒）")]
        public float connectionTimeout = 5f;
        
        [Header("Debug")]
        public bool enableDebugLog = true;

        private readonly Dictionary<string, AudioClip> _cache = new();
        private bool _isEngineAvailable = false;
        private bool _isCheckingEngine = false;

        void Start()
        {
            // エンジンの可用性を確認
            StartCoroutine(CheckEngineAvailability());
        }

        private IEnumerator CheckEngineAvailability()
        {
            _isCheckingEngine = true;
            string url = $"{engineBaseUrl}/speakers";
            
            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] エンジン接続確認開始: {url}");
            }
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = (int)connectionTimeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    _isEngineAvailable = true;
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] TTSエンジンに接続成功: {engineBaseUrl}");
                        Debug.Log($"[TTSService] レスポンスコード: {request.responseCode}");
                    }
                }
                else
                {
                    _isEngineAvailable = false;
                    Debug.LogError($"[TTSService] TTSエンジンに接続できません: {engineBaseUrl}");
                    Debug.LogError($"[TTSService] エラー詳細: {request.error}");
                    Debug.LogError($"[TTSService] レスポンスコード: {request.responseCode}");
                    Debug.LogError($"[TTSService] ダウンロードハンドラー: {request.downloadHandler?.text ?? "null"}");
                    Debug.LogWarning("[TTSService] VOICEVOX/COEIROINKが起動しているか確認してください。");
                    Debug.LogWarning($"[TTSService] ブラウザで {url} にアクセスしてエンジンが応答するか確認してください。");
                }
            }
            _isCheckingEngine = false;
        }

        public IEnumerator PrewarmAsync(ScenarioFile scenario)
        {
            // エンジンの可用性チェックが完了するまで待機
            while (_isCheckingEngine)
            {
                yield return null;
            }

            // チェックが完了していない場合は再確認
            if (!_isEngineAvailable)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[TTSService] エンジンの可用性を再確認中...");
                }
                yield return StartCoroutine(CheckEngineAvailability());
            }

            if (!_isEngineAvailable)
            {
                Debug.LogWarning("[TTSService] エンジンが利用できないため、事前合成をスキップします。");
                yield break;
            }

            // type=="tts"のエントリを抽出
            var ttsEntries = scenario.entries
                .Where(e => e.type == "tts" && !string.IsNullOrEmpty(e.text))
                .ToList();

            // type=="singing"またはsingingNotesがあるエントリを抽出
            var singingEntries = scenario.entries
                .Where(e => (e.type == "singing" || (e.singingNotes != null && e.singingNotes.Length > 0)) && e.singingNotes != null && e.singingNotes.Length > 0)
                .ToList();

            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] 事前合成開始: {ttsEntries.Count}件のTTS、{singingEntries.Count}件の歌声合成");
            }

            foreach (var entry in ttsEntries)
            {
                // キャッシュキーは text + pitchNotes + speedScale + labFilePath の組み合わせ
                string cacheKey = GetCacheKey(entry.text, entry.pitchNotes, entry.speedScale, entry.labFilePath);
                if (_cache.ContainsKey(cacheKey))
                {
                    continue;
                }

                yield return StartCoroutine(SynthesizeAndCache(entry.text, entry.pitchNotes, entry.speedScale, entry.labFilePath));
            }

            foreach (var entry in singingEntries)
            {
                string cacheKey = GetSingingCacheKey(entry.singingNotes);
                if (_cache.ContainsKey(cacheKey))
                {
                    continue;
                }

                yield return StartCoroutine(SynthesizeSingingAndCache(entry.singingNotes));
            }

            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] 事前合成完了: {_cache.Count}件のクリップをキャッシュ");
            }
        }

        private IEnumerator SynthesizeAndCache(string text, string[] pitchNotes = null, float speedScale = 1.0f, string labFilePath = null)
        {
            string cacheKey = GetCacheKey(text, pitchNotes, speedScale, labFilePath);
            
            if (enableDebugLog)
            {
                if (pitchNotes != null && pitchNotes.Length > 0)
                {
                    Debug.Log($"[TTSService] 音声合成開始（音程指定あり）: \"{text}\" 音程: [{string.Join(", ", pitchNotes)}] (話者ID: {speakerId})");
                }
                else
                {
                    Debug.Log($"[TTSService] 音声合成開始: \"{text}\" (話者ID: {speakerId})");
                }
            }
            
            // Step 1: audio_query を取得
            string queryUrl = $"{engineBaseUrl}/audio_query?text={UnityWebRequest.EscapeURL(text)}&speaker={speakerId}";
            
            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] audio_query リクエスト: {queryUrl}");
            }
            
            using (UnityWebRequest queryRequest = CreatePostRequest(queryUrl))
            {
                queryRequest.timeout = (int)connectionTimeout;
                yield return queryRequest.SendWebRequest();

                if (queryRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[TTSService] audio_query 失敗: \"{text}\"");
                    Debug.LogError($"[TTSService] エラー詳細: {queryRequest.error}");
                    Debug.LogError($"[TTSService] レスポンスコード: {queryRequest.responseCode}");
                    Debug.LogError($"[TTSService] URL: {queryUrl}");
                    yield break;
                }

                string queryJson = queryRequest.downloadHandler.text;
                
                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] audio_query 成功: {queryJson.Length}バイト");
                }

                // .labファイル指定がある場合は、そのタイミング情報を優先（pitchNotesとspeedScaleは無視）
                // .labファイルがない場合のみ、pitchNotesとspeedScaleを適用
                bool needsModification = !string.IsNullOrEmpty(labFilePath) || 
                    ((pitchNotes != null && pitchNotes.Length > 0) || speedScale != 1.0f);
                if (needsModification)
                {
                    string originalJson = queryJson;
                    
                    if (!string.IsNullOrEmpty(labFilePath))
                    {
                        // .labファイルがある場合は、.labファイルのタイミング情報のみを適用
                        queryJson = ApplyLabFileTiming(queryJson, labFilePath);
                        if (enableDebugLog)
                        {
                            Debug.Log($"[TTSService] .labファイルを適用しました: {labFilePath} (pitchNotesとspeedScaleは無視されます)");
                        }
                    }
                    else
                    {
                        // .labファイルがない場合のみ、pitchNotesとspeedScaleを適用
                        queryJson = ApplyAudioQueryModifications(queryJson, pitchNotes, speedScale, null);
                        if (enableDebugLog)
                        {
                            if (pitchNotes != null && pitchNotes.Length > 0)
                            {
                                Debug.Log($"[TTSService] 音程を適用しました: [{string.Join(", ", pitchNotes)}]");
                            }
                            if (speedScale != 1.0f)
                            {
                                Debug.Log($"[TTSService] 発話速度を適用しました: {speedScale:F2}倍");
                            }
                        }
                    }
                }

                // Step 2: synthesis で音声を生成
                string synthesisUrl = $"{engineBaseUrl}/synthesis?speaker={speakerId}";
                
                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] synthesis リクエスト開始: {synthesisUrl}");
                    Debug.Log($"[TTSService] リクエストJSONサイズ: {queryJson.Length}バイト");
                }
                
                float synthesisStartTime = Time.realtimeSinceStartup;
                using (UnityWebRequest synthRequest = CreatePostRequest(synthesisUrl, queryJson))
                {
                    // 音程指定や.labファイルがある場合は、処理時間が長くなる可能性があるためタイムアウトを延長
                    int timeoutSeconds = (pitchNotes != null && pitchNotes.Length > 0) || !string.IsNullOrEmpty(labFilePath)
                        ? (int)connectionTimeout * 10  // 音程指定がある場合は10倍
                        : (int)connectionTimeout;
                    
                    synthRequest.timeout = timeoutSeconds;
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] synthesis タイムアウト設定: {timeoutSeconds}秒");
                    }
                    
                    yield return synthRequest.SendWebRequest();
                    
                    float synthesisElapsedTime = Time.realtimeSinceStartup - synthesisStartTime;
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] synthesis リクエスト完了: {synthesisElapsedTime:F2}秒経過");
                    }

                    if (synthRequest.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[TTSService] synthesis 失敗: \"{text}\"");
                        Debug.LogError($"[TTSService] エラー詳細: {synthRequest.error}");
                        Debug.LogError($"[TTSService] レスポンスコード: {synthRequest.responseCode}");
                        Debug.LogError($"[TTSService] URL: {synthesisUrl}");
                        Debug.LogError($"[TTSService] 処理時間: {synthesisElapsedTime:F2}秒");
                        
                        // タイムアウトの場合は詳細を表示
                        if (synthRequest.result == UnityWebRequest.Result.ConnectionError || 
                            synthRequest.error.Contains("timeout"))
                        {
                            Debug.LogError($"[TTSService] タイムアウトの可能性があります。VOICEVOXエンジンが正常に動作しているか確認してください。");
                        }
                        
                        yield break;
                    }

                    // Step 3: WAVバイナリをAudioClipに変換
                    byte[] wavData = synthRequest.downloadHandler.data;
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] synthesis 成功: {wavData?.Length ?? 0}バイトのWAVデータ");
                        Debug.Log($"[TTSService] 総処理時間: {synthesisElapsedTime:F2}秒");
                    }
                    
                    if (wavData == null || wavData.Length == 0)
                    {
                        Debug.LogError($"[TTSService] WAVデータが空です。VOICEVOXエンジンが正しく音声を生成できていない可能性があります。");
                        yield break;
                    }
                    
                    AudioClip clip = WavToAudioClip(wavData, cacheKey);

                    if (clip != null)
                    {
                        // 音声データが実際に含まれているか確認
                        float[] samples = new float[clip.samples * clip.channels];
                        clip.GetData(samples, 0);
                        float maxAmplitude = 0f;
                        for (int i = 0; i < samples.Length; i++)
                        {
                            float abs = Mathf.Abs(samples[i]);
                            if (abs > maxAmplitude) maxAmplitude = abs;
                        }
                        
                        _cache[cacheKey] = clip;
                        if (enableDebugLog)
                        {
                            Debug.Log($"[TTSService] キャッシュ追加: \"{cacheKey}\" ({clip.length:F2}秒, サンプル数: {clip.samples}, チャンネル: {clip.channels}, 最大振幅: {maxAmplitude:F4})");
                            if (maxAmplitude < 0.001f)
                            {
                                Debug.LogWarning($"[TTSService] 警告: 音声データの最大振幅が非常に小さいです（{maxAmplitude:F4}）。無音の可能性があります。");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogError($"[TTSService] AudioClipへの変換に失敗しました。");
                    }
                }
            }
        }

        private AudioClip WavToAudioClip(byte[] wavData, string clipName)
        {
            // WAVファイルのヘッダーを解析
            try
            {
                // WAVヘッダー解析（44バイトヘッダー想定）
                if (wavData.Length < 44)
                {
                    Debug.LogError("[TTSService] WAVデータが短すぎます");
                    return null;
                }

                // RIFFヘッダー確認
                string riff = System.Text.Encoding.ASCII.GetString(wavData, 0, 4);
                if (riff != "RIFF")
                {
                    Debug.LogError("[TTSService] 無効なWAVフォーマット");
                    return null;
                }

                // サンプルレート、チャンネル数、ビット深度を取得
                int sampleRate = System.BitConverter.ToInt32(wavData, 24);
                int channels = System.BitConverter.ToInt16(wavData, 22);
                int bitsPerSample = System.BitConverter.ToInt16(wavData, 34);

                // データチャンクの位置を探す
                int dataOffset = 44; // 通常は44バイト目から
                for (int i = 0; i < wavData.Length - 4; i++)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wavData, i, 4);
                    if (chunkId == "data")
                    {
                        dataOffset = i + 8;
                        break;
                    }
                }

                int dataSize = wavData.Length - dataOffset;
                int sampleCount = dataSize / (channels * (bitsPerSample / 8));

                // 16bit PCM想定でfloat配列に変換
                float[] samples = new float[sampleCount * channels];
                int sampleIndex = 0;
                for (int i = dataOffset; i < wavData.Length - 1; i += 2)
                {
                    short sample = System.BitConverter.ToInt16(wavData, i);
                    samples[sampleIndex++] = sample / 32768f;
                    if (sampleIndex >= samples.Length) break;
                }

                AudioClip clip = AudioClip.Create(clipName, sampleCount, channels, sampleRate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TTSService] WAV変換エラー: {e.Message}");
                return null;
            }
        }

        public AudioClip GetCachedClip(string text, string[] pitchNotes = null, float speedScale = 1.0f, string labFilePath = null)
        {
            string cacheKey = GetCacheKey(text, pitchNotes, speedScale, labFilePath);
            _cache.TryGetValue(cacheKey, out var clip);
            return clip;
        }

        public AudioClip GetCachedSingingClip(SingingNote[] singingNotes)
        {
            if (singingNotes == null || singingNotes.Length == 0)
            {
                if (enableDebugLog)
                {
                    Debug.LogWarning("[TTSService] GetCachedSingingClip: singingNotesがnullまたは空です");
                }
                return null;
            }
            string cacheKey = GetSingingCacheKey(singingNotes);
            bool found = _cache.TryGetValue(cacheKey, out var clip);
            if (enableDebugLog)
            {
                if (found)
                {
                    Debug.Log($"[TTSService] GetCachedSingingClip: キャッシュから取得成功 (キー: {cacheKey})");
                }
                else
                {
                    Debug.LogWarning($"[TTSService] GetCachedSingingClip: キャッシュに存在しません (キー: {cacheKey}, キャッシュ数: {_cache.Count})");
                }
            }
            return clip;
        }

        private string GetCacheKey(string text, string[] pitchNotes, float speedScale = 1.0f, string labFilePath = null)
        {
            List<string> keyParts = new List<string> { text };
            if (pitchNotes != null && pitchNotes.Length > 0)
            {
                keyParts.Add($"pitch:{string.Join(",", pitchNotes)}");
            }
            if (speedScale != 1.0f)
            {
                keyParts.Add($"speed:{speedScale:F2}");
            }
            if (!string.IsNullOrEmpty(labFilePath))
            {
                keyParts.Add($"lab:{labFilePath}");
            }
            return string.Join("|", keyParts);
        }

        private string ApplyAudioQueryModifications(string queryJson, string[] pitchNotes, float speedScale, string labFilePath)
        {
            // まず.labファイルのタイミング情報を適用
            if (!string.IsNullOrEmpty(labFilePath))
            {
                queryJson = ApplyLabFileTiming(queryJson, labFilePath);
            }
            
            // 次に速度を適用
            if (speedScale != 1.0f)
            {
                queryJson = ApplySpeedScale(queryJson, speedScale);
            }
            
            // 最後に音程を適用
            if (pitchNotes != null && pitchNotes.Length > 0)
            {
                queryJson = ApplyPitchNotes(queryJson, pitchNotes);
            }
            
            return queryJson;
        }

        private string ApplySpeedScale(string queryJson, float speedScale)
        {
            try
            {
                // "speedScale": 数値 のパターンを探して置換
                System.Text.RegularExpressions.Regex speedRegex = 
                    new System.Text.RegularExpressions.Regex(@"""speedScale""\s*:\s*([0-9]+\.?[0-9]*)");
                
                string modifiedJson = speedRegex.Replace(queryJson, match =>
                {
                    return $"\"speedScale\": {speedScale:F2}";
                });

                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] 発話速度を {speedScale:F2}倍 に設定しました");
                }

                return modifiedJson;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TTSService] 発話速度適用エラー: {e.Message}");
                return queryJson; // エラー時は元のJSONを返す
            }
        }

        private string ApplyPitchNotes(string queryJson, string[] pitchNotes)
        {
            try
            {
                // 各音程に対応する周波数を計算
                float[] frequencies = new float[pitchNotes.Length];
                for (int i = 0; i < pitchNotes.Length; i++)
                {
                    frequencies[i] = NoteToFrequency(pitchNotes[i]);
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] 音程{i + 1}: {pitchNotes[i]} = {frequencies[i]:F2}Hz");
                    }
                }

                // VOICEVOX/COEIROINKのpitchフィールドは周波数（Hz）を直接指定する
                // デバッグ用: 元のJSONからpitch値を確認
                if (enableDebugLog)
                {
                    System.Text.RegularExpressions.Regex pitchRegex = 
                        new System.Text.RegularExpressions.Regex(@"""pitch""\s*:\s*([0-9]+\.?[0-9]*)");
                    var originalPitches = pitchRegex.Matches(queryJson);
                    Debug.Log($"[TTSService] 元のJSON内のpitchフィールド数: {originalPitches.Count}");
                    for (int i = 0; i < Mathf.Min(5, originalPitches.Count); i++)
                    {
                        Debug.Log($"[TTSService]   元のpitch[{i}]: {originalPitches[i].Groups[1].Value}");
                    }
                }

                // moras配列内のpitchフィールドのみを置換
                int noteIndex = 0;
                System.Text.StringBuilder result = new System.Text.StringBuilder();
                int lastIndex = 0;
                
                // "moras"配列を探す
                System.Text.RegularExpressions.Regex morasArrayRegex = 
                    new System.Text.RegularExpressions.Regex(@"""moras""\s*:\s*\[");
                
                var morasMatches = morasArrayRegex.Matches(queryJson);
                
                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] moras配列の数: {morasMatches.Count}");
                }
                
                foreach (System.Text.RegularExpressions.Match morasMatch in morasMatches)
                {
                    // moras配列の開始位置から、対応する閉じ括弧までを探す
                    int arrayStart = morasMatch.Index + morasMatch.Length;
                    int bracketDepth = 1;
                    int arrayEnd = arrayStart;
                    
                    for (int i = arrayStart; i < queryJson.Length && bracketDepth > 0; i++)
                    {
                        if (queryJson[i] == '[') bracketDepth++;
                        else if (queryJson[i] == ']') bracketDepth--;
                        if (bracketDepth == 0) arrayEnd = i;
                    }
                    
                    // moras配列内のpitchフィールドを置換
                    string morasArrayContent = queryJson.Substring(arrayStart, arrayEnd - arrayStart);
                    System.Text.RegularExpressions.Regex pitchInMoraRegex = 
                        new System.Text.RegularExpressions.Regex(@"""pitch""\s*:\s*([0-9]+\.?[0-9]*)");
                    
                    int moraPitchCount = 0;
                    // 元のpitch値を取得（デフォルト値として使用）
                    float basePitch = 0f;
                    int basePitchCount = 0;
                    System.Text.RegularExpressions.Regex basePitchRegex = 
                        new System.Text.RegularExpressions.Regex(@"""pitch""\s*:\s*([0-9]+\.?[0-9]*)");
                    var basePitchMatches = basePitchRegex.Matches(morasArrayContent);
                    foreach (System.Text.RegularExpressions.Match m in basePitchMatches)
                    {
                        if (float.TryParse(m.Groups[1].Value, out float p) && p > 0f)
                        {
                            basePitch += p;
                            basePitchCount++;
                        }
                    }
                    if (basePitchCount > 0)
                    {
                        basePitch /= basePitchCount;
                    }
                    else
                    {
                        basePitch = 5.0f; // デフォルト値
                    }
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] このmoras配列の基準pitch値: {basePitch:F2}");
                    }
                    
                    string modifiedMorasContent = pitchInMoraRegex.Replace(morasArrayContent, match =>
                    {
                        moraPitchCount++;
                        if (noteIndex < frequencies.Length)
                        {
                            // 周波数から相対的なpitch値に変換
                            // VOICEVOXのpitchは相対値なので、基準周波数（A4=440Hz）からの倍率を計算
                            float targetFreq = frequencies[noteIndex];
                            float baseFreq = 440f; // A4を基準
                            float pitchRatio = targetFreq / baseFreq;
                            
                            // 基準pitch値に対して倍率を適用
                            float newPitch = basePitch * pitchRatio;
                            
                            if (enableDebugLog)
                            {
                                Debug.Log($"[TTSService] モーラ{noteIndex + 1}: {pitchNotes[noteIndex]} ({targetFreq:F2}Hz) -> pitch比率: {pitchRatio:F3} -> pitch: {newPitch:F2} (基準: {basePitch:F2})");
                            }
                            
                            noteIndex++;
                            return $"\"pitch\": {newPitch:F2}";
                        }
                        return match.Value; // 音程指定が足りない場合は元の値を保持
                    });
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] このmoras配列内のpitchフィールド数: {moraPitchCount}, 適用した音程数: {noteIndex}");
                    }
                    
                    // 結果に追加
                    result.Append(queryJson.Substring(lastIndex, morasMatch.Index + morasMatch.Length - lastIndex));
                    result.Append(modifiedMorasContent);
                    lastIndex = arrayEnd;
                }
                
                // 残りの部分を追加
                result.Append(queryJson.Substring(lastIndex));

                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] 音程適用完了: 合計{noteIndex}個のモーラに音程を適用しました");
                    // 修正後のJSONの一部を表示
                    string modifiedJson = result.ToString();
                    System.Text.RegularExpressions.Regex modifiedPitchRegex = 
                        new System.Text.RegularExpressions.Regex(@"""pitch""\s*:\s*([0-9]+\.?[0-9]*)");
                    var modifiedPitches = modifiedPitchRegex.Matches(modifiedJson);
                    Debug.Log($"[TTSService] 修正後のJSON内のpitchフィールド数: {modifiedPitches.Count}");
                    for (int i = 0; i < Mathf.Min(5, modifiedPitches.Count); i++)
                    {
                        Debug.Log($"[TTSService]   修正後のpitch[{i}]: {modifiedPitches[i].Groups[1].Value}");
                    }
                }

                return result.ToString();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TTSService] 音程適用エラー: {e.Message}\nスタックトレース: {e.StackTrace}");
                return queryJson; // エラー時は元のJSONを返す
            }
        }

        private string ApplyLabFileTiming(string queryJson, string labFilePath)
        {
            try
            {
                // Resourcesから.labファイルを読み込み
                // Resources.Loadは拡張子を含めないので、拡張子を除去
                string resourcePath = labFilePath;
                if (resourcePath.EndsWith(".lab", System.StringComparison.OrdinalIgnoreCase))
                {
                    resourcePath = resourcePath.Substring(0, resourcePath.Length - 4);
                }
                else if (resourcePath.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase))
                {
                    resourcePath = resourcePath.Substring(0, resourcePath.Length - 4);
                }
                
                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] .labファイルを読み込み中: {resourcePath} (元のパス: {labFilePath})");
                }
                
                TextAsset labAsset = Resources.Load<TextAsset>(resourcePath);
                if (labAsset == null)
                {
                    // デバッグ: Resourcesフォルダ内のすべてのTextAssetを確認
                    if (enableDebugLog)
                    {
                        Debug.LogWarning($"[TTSService] .labファイルが見つかりません: {resourcePath} (元のパス: {labFilePath})");
                        
                        // Resources/Labフォルダ内のすべてのファイルを確認
                        TextAsset[] allTextAssets = Resources.LoadAll<TextAsset>("Lab");
                        Debug.LogWarning($"[TTSService] Resources/Labフォルダ内のファイル数: {allTextAssets.Length}");
                        if (allTextAssets.Length == 0)
                        {
                            Debug.LogWarning($"[TTSService] Resources/Labフォルダにファイルが見つかりません。");
                            Debug.LogWarning($"[TTSService] ファイルパス: Assets/Resources/Lab/001_zundamon_normal.lab が存在するか確認してください。");
                        }
                        else
                        {
                            foreach (var asset in allTextAssets)
                            {
                                Debug.LogWarning($"[TTSService]   見つかったファイル: {asset.name} (パス: Lab/{asset.name})");
                            }
                        }
                        
                        // 直接ファイル名で試す
                        Debug.LogWarning($"[TTSService] 直接ファイル名で試行: 001_zundamon_normal");
                        TextAsset directAsset = Resources.Load<TextAsset>("Lab/001_zundamon_normal");
                        if (directAsset != null)
                        {
                            Debug.LogWarning($"[TTSService] 直接ファイル名で読み込み成功！");
                            labAsset = directAsset;
                        }
                        else
                        {
                            Debug.LogWarning($"[TTSService] 直接ファイル名でも読み込み失敗。");
                        }
                    }
                    
                    if (labAsset == null)
                    {
                        Debug.LogWarning($"[TTSService] Resourcesフォルダ内に配置されているか確認してください。");
                        return queryJson;
                    }
                }

                // .labファイルをパース
                List<LabPhoneme> labPhonemes = ParseLabFile(labAsset.text);
                if (labPhonemes == null || labPhonemes.Count == 0)
                {
                    Debug.LogWarning($"[TTSService] .labファイルのパースに失敗しました: {labFilePath}");
                    return queryJson;
                }

                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] .labファイルを読み込み: {labPhonemes.Count}個の音素");
                }

                // audio_queryのJSONのmoras配列にタイミング情報を適用
                // 簡易実装: moras配列内のvowel_lengthとconsonant_lengthを.labファイルのタイミングから設定
                System.Text.StringBuilder result = new System.Text.StringBuilder();
                int lastIndex = 0;
                
                // "moras"配列を探す
                System.Text.RegularExpressions.Regex morasArrayRegex = 
                    new System.Text.RegularExpressions.Regex(@"""moras""\s*:\s*\[");
                
                var morasMatches = morasArrayRegex.Matches(queryJson);
                int moraIndex = 0;
                
                foreach (System.Text.RegularExpressions.Match morasMatch in morasMatches)
                {
                    int arrayStart = morasMatch.Index + morasMatch.Length;
                    int bracketDepth = 1;
                    int arrayEnd = arrayStart;
                    
                    for (int i = arrayStart; i < queryJson.Length && bracketDepth > 0; i++)
                    {
                        if (queryJson[i] == '[') bracketDepth++;
                        else if (queryJson[i] == ']') bracketDepth--;
                        if (bracketDepth == 0) arrayEnd = i;
                    }
                    
                    string morasArrayContent = queryJson.Substring(arrayStart, arrayEnd - arrayStart);
                    
                    // vowel_lengthとconsonant_lengthを置換
                    // 簡易実装: .labファイルの音素を順番にモーラにマッピング
                    int phonemeIndex = 0;
                    System.Text.RegularExpressions.Regex vowelLengthRegex = 
                        new System.Text.RegularExpressions.Regex(@"""vowel_length""\s*:\s*([0-9]+\.?[0-9]*)");
                    System.Text.RegularExpressions.Regex consonantLengthRegex = 
                        new System.Text.RegularExpressions.Regex(@"""consonant_length""\s*:\s*([0-9]+\.?[0-9]*|null)");
                    
                    // vowel_lengthを置換
                    string modifiedContent = vowelLengthRegex.Replace(morasArrayContent, match =>
                    {
                        if (phonemeIndex < labPhonemes.Count)
                        {
                            // 母音の長さを設定（秒単位に変換: 100ナノ秒 = 0.0000001秒）
                            float duration = (labPhonemes[phonemeIndex].endTime - labPhonemes[phonemeIndex].startTime) * 0.0000001f;
                            phonemeIndex++;
                            return $"\"vowel_length\": {duration:F4}";
                        }
                        return match.Value;
                    });
                    
                    // consonant_lengthを置換（子音がある場合）
                    phonemeIndex = 0;
                    modifiedContent = consonantLengthRegex.Replace(modifiedContent, match =>
                    {
                        if (phonemeIndex < labPhonemes.Count)
                        {
                            var phoneme = labPhonemes[phonemeIndex];
                            // 子音かどうかを判定（簡易版: pau以外で短い音素は子音と仮定）
                            if (phoneme.phoneme != "pau" && (phoneme.endTime - phoneme.startTime) < 10000000) // 0.001秒未満
                            {
                                float duration = (phoneme.endTime - phoneme.startTime) * 0.0000001f;
                                phonemeIndex++;
                                return $"\"consonant_length\": {duration:F4}";
                            }
                            phonemeIndex++;
                            return match.Value; // nullのまま
                        }
                        return match.Value;
                    });
                    
                    result.Append(queryJson.Substring(lastIndex, morasMatch.Index + morasMatch.Length - lastIndex));
                    result.Append(modifiedContent);
                    lastIndex = arrayEnd;
                    moraIndex++;
                }
                
                result.Append(queryJson.Substring(lastIndex));

                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] .labファイルのタイミング情報を適用しました");
                }

                return result.ToString();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TTSService] .labファイル適用エラー: {e.Message}\nスタックトレース: {e.StackTrace}");
                return queryJson;
            }
        }

        private List<LabPhoneme> ParseLabFile(string labContent)
        {
            List<LabPhoneme> phonemes = new List<LabPhoneme>();
            
            string[] lines = labContent.Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                
                string[] parts = trimmed.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    if (long.TryParse(parts[0], out long startTime) && 
                        long.TryParse(parts[1], out long endTime))
                    {
                        string phoneme = parts[2];
                        phonemes.Add(new LabPhoneme
                        {
                            startTime = startTime,
                            endTime = endTime,
                            phoneme = phoneme
                        });
                    }
                }
            }
            
            return phonemes;
        }

        private class LabPhoneme
        {
            public long startTime; // 100ナノ秒単位
            public long endTime;   // 100ナノ秒単位
            public string phoneme;
        }

        private float ExtractDefaultPitch(string queryJson)
        {
            // JSONからpitch値を抽出して平均を計算
            try
            {
                System.Text.RegularExpressions.Regex pitchRegex = 
                    new System.Text.RegularExpressions.Regex(@"""pitch""\s*:\s*([0-9]+\.?[0-9]*)");
                
                var matches = pitchRegex.Matches(queryJson);
                if (matches.Count == 0)
                {
                    return 5.0f; // デフォルト値
                }

                float sum = 0f;
                int count = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (float.TryParse(match.Groups[1].Value, out float pitch) && pitch > 0f)
                    {
                        sum += pitch;
                        count++;
                    }
                }

                return count > 0 ? sum / count : 5.0f;
            }
            catch
            {
                return 5.0f; // エラー時はデフォルト値
            }
        }

        private float NoteToFrequency(string note)
        {
            // 音名（例: "C4", "D4", "E4", "F4", "G4"）を周波数に変換
            // A4 = 440Hz を基準とする
            if (string.IsNullOrEmpty(note) || note.Length < 2)
            {
                return 261.63f; // C4をデフォルト
            }

            char noteName = note[0];
            int octave = int.Parse(note.Substring(1));

            // 音名から半音数を計算（C=0, D=2, E=4, F=5, G=7, A=9, B=11）
            int semitones = 0;
            switch (noteName)
            {
                case 'C': semitones = 0; break;
                case 'D': semitones = 2; break;
                case 'E': semitones = 4; break;
                case 'F': semitones = 5; break;
                case 'G': semitones = 7; break;
                case 'A': semitones = 9; break;
                case 'B': semitones = 11; break;
                default: semitones = 0; break;
            }

            // シャープ/フラットの処理（簡易版）
            if (note.Length > 2)
            {
                if (note[1] == '#')
                {
                    semitones++;
                    octave = int.Parse(note.Substring(2));
                }
                else if (note[1] == 'b')
                {
                    semitones--;
                    octave = int.Parse(note.Substring(2));
                }
            }

            // A4 (440Hz) からの半音数を計算
            int semitonesFromA4 = (octave - 4) * 12 + semitones - 9;
            
            // 周波数を計算: f = 440 * 2^(n/12)
            return 440f * Mathf.Pow(2f, semitonesFromA4 / 12f);
        }

        [Serializable]
        public class SpeakerStyleInfo
        {
            public int id;
            public string name;
        }

        [Serializable]
        public class SpeakerInfo
        {
            public string name;
            public string speaker_uuid;
            public SpeakerStyleInfo[] styles;
        }

        [Serializable]
        public class AudioQuery
        {
            public AccentPhrase[] accent_phrases;
            public float speedScale = 1.0f;
            public float pitchScale = 0.0f;
            public float intonationScale = 1.0f;
            public float volumeScale = 1.0f;
            public float prePhonemeLength = 0.1f;
            public float postPhonemeLength = 0.1f;
            public int outputSamplingRate = 24000;
            public bool outputStereo = false;
        }

        [Serializable]
        public class AccentPhrase
        {
            public Mora[] moras;
            public int accent;
            public Mora pause_mora;
            public bool is_interrogative;
        }

        [Serializable]
        public class Mora
        {
            public string text;
            public string consonant;
            public float? consonant_length;
            public string vowel;
            public float vowel_length;
            public float pitch;
        }

        private UnityWebRequest CreatePostRequest(string url, string jsonBody = null)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyRaw = string.IsNullOrEmpty(jsonBody)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(jsonBody);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        public bool IsEngineAvailable => _isEngineAvailable;
        public int CacheCount => _cache.Count;

        private string GetSingingCacheKey(SingingNote[] singingNotes)
        {
            if (singingNotes == null || singingNotes.Length == 0)
            {
                return "singing:empty";
            }
            List<string> noteStrings = new List<string>();
            foreach (var note in singingNotes)
            {
                string keyStr = (note.key == -1) ? "null" : note.key.ToString();
                noteStrings.Add($"{keyStr}_{note.frame_length}_{note.lyric ?? ""}");
            }
            return $"singing:{string.Join(",", noteStrings)}";
        }

        private IEnumerator SynthesizeSingingAndCache(SingingNote[] singingNotes)
        {
            if (singingNotes == null || singingNotes.Length == 0)
            {
                Debug.LogError("[TTSService] 歌声合成: singingNotesが空です");
                yield break;
            }

            string cacheKey = GetSingingCacheKey(singingNotes);
            
            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] 歌声合成開始: {singingNotes.Length}個のノート (話者ID: {singingSpeakerId})");
            }

            // Step 1: 楽譜データをJSON形式で作成
            // lyricフィールドをJSONエスケープ
            // 注意: lyricが空文字列の場合、keyはnullである必要があります
            StringBuilder notesJson = new StringBuilder();
            notesJson.Append("{\"notes\":[");
            for (int i = 0; i < singingNotes.Length; i++)
            {
                if (i > 0) notesJson.Append(",");
                // lyricフィールドをJSONエスケープ
                string escapedLyric = singingNotes[i].lyric ?? "";
                escapedLyric = escapedLyric
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
                
                // lyricが空文字列の場合、keyはnullにする必要がある
                // keyが-1の場合はnullとして扱う（JsonUtilityの制限のため-1を使用）
                string keyString;
                if (string.IsNullOrEmpty(escapedLyric) || singingNotes[i].key == -1)
                {
                    keyString = "null";
                }
                else
                {
                    keyString = singingNotes[i].key.ToString();
                }
                
                notesJson.Append($"{{\"key\":{keyString},\"frame_length\":{singingNotes[i].frame_length},\"lyric\":\"{escapedLyric}\"}}");
            }
            notesJson.Append("]}");
            string notesJsonString = notesJson.ToString();

            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] 楽譜データ: {notesJsonString}");
                // 各ノートのkeyとlyricを確認
                for (int i = 0; i < singingNotes.Length; i++)
                {
                    string keyStr = (singingNotes[i].key == -1) ? "null" : singingNotes[i].key.ToString();
                    Debug.Log($"[TTSService] ノート{i + 1}: key={keyStr}, frame_length={singingNotes[i].frame_length}, lyric=\"{singingNotes[i].lyric ?? ""}\"");
                }
            }

            // Step 2: /sing_frame_audio_query で音声合成クエリを生成
            // 注意: VOICEVOXの歌声合成機能は特定の話者ID（例：波音リツ（ノーマル）のID:6000）でのみ動作する可能性があります
            string queryUrl = $"{engineBaseUrl}/sing_frame_audio_query?speaker={singingSpeakerId}";
            
            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] sing_frame_audio_query リクエスト: {queryUrl}");
                Debug.Log($"[TTSService] 注意: 話者ID {singingSpeakerId} が歌声合成をサポートしているか確認してください（例：波音リツ（ノーマル）のID:6000）");
            }

            using (UnityWebRequest queryRequest = CreatePostRequest(queryUrl, notesJsonString))
            {
                queryRequest.timeout = (int)connectionTimeout * 10; // 歌声合成は時間がかかる可能性があるため延長
                yield return queryRequest.SendWebRequest();

                if (queryRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[TTSService] sing_frame_audio_query 失敗");
                    Debug.LogError($"[TTSService] エラー詳細: {queryRequest.error}");
                    Debug.LogError($"[TTSService] レスポンスコード: {queryRequest.responseCode}");
                    if (queryRequest.downloadHandler != null && !string.IsNullOrEmpty(queryRequest.downloadHandler.text))
                    {
                        Debug.LogError($"[TTSService] エラーレスポンス: {queryRequest.downloadHandler.text}");
                    }
                    Debug.LogError($"[TTSService] 送信したJSON: {notesJsonString}");
                    Debug.LogError($"[TTSService] 可能性のある原因:");
                    Debug.LogError($"[TTSService] 1. 話者ID {singingSpeakerId} が歌声合成をサポートしていない可能性があります");
                    Debug.LogError($"[TTSService] 2. VOICEVOXエンジンのバージョンが歌声合成機能をサポートしていない可能性があります");
                    Debug.LogError($"[TTSService] 3. /sing_frame_audio_query エンドポイントが存在しない可能性があります");
                    yield break;
                }

                string queryJson = queryRequest.downloadHandler.text;
                
                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] sing_frame_audio_query 成功: {queryJson.Length}バイト");
                    
                    // f0フィールドの値を確認（デバッグ用）
                    System.Text.RegularExpressions.Regex f0Regex = 
                        new System.Text.RegularExpressions.Regex(@"""f0""\s*:\s*\[([^\]]+)\]");
                    var f0Match = f0Regex.Match(queryJson);
                    if (f0Match.Success)
                    {
                        string f0Values = f0Match.Groups[1].Value;
                        string[] f0Array = f0Values.Split(',');
                        Debug.Log($"[TTSService] f0フィールドの最初の10個の値: {string.Join(", ", f0Array.Take(10))}");
                        
                        // f0の最小値、最大値、平均値を計算
                        List<float> f0List = new List<float>();
                        foreach (string f0Str in f0Array)
                        {
                            if (float.TryParse(f0Str.Trim(), out float f0Val) && f0Val > 0)
                            {
                                f0List.Add(f0Val);
                            }
                        }
                        if (f0List.Count > 0)
                        {
                            float minF0 = f0List.Min();
                            float maxF0 = f0List.Max();
                            float avgF0 = f0List.Average();
                            Debug.Log($"[TTSService] f0統計: 最小={minF0:F2}Hz, 最大={maxF0:F2}Hz, 平均={avgF0:F2}Hz, 有効値数={f0List.Count}");
                        }
                    }
                }

                // consonant_lengths[0]を0に修正（VOICEVOXの要求）
                // 正規表現で "consonant_lengths":[5, のパターンを "consonant_lengths":[0, に置換
                System.Text.RegularExpressions.Regex consonantLengthsRegex = 
                    new System.Text.RegularExpressions.Regex(@"""consonant_lengths""\s*:\s*\[(\d+)");
                string originalQueryJson = queryJson;
                queryJson = consonantLengthsRegex.Replace(queryJson, match =>
                {
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] consonant_lengths[0]を{match.Groups[1].Value}から0に修正します");
                    }
                    return "\"consonant_lengths\":[0";
                });
                
                if (queryJson != originalQueryJson && enableDebugLog)
                {
                    Debug.Log($"[TTSService] consonant_lengths[0]を0に修正しました");
                }

                // Step 3: /frame_synthesis で音声を生成
                // ハミング機能を使用する場合は、ハミング用の話者ID（30xx）を使用
                string synthesisUrl = $"{engineBaseUrl}/frame_synthesis?speaker={hummingSpeakerId}";
                
                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] ハミング機能を使用: 話者ID {hummingSpeakerId} (クエリ生成: {singingSpeakerId})");
                }
                
                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] frame_synthesis リクエスト開始: {synthesisUrl}");
                }
                
                float synthesisStartTime = Time.realtimeSinceStartup;
                using (UnityWebRequest synthRequest = CreatePostRequest(synthesisUrl, queryJson))
                {
                    synthRequest.timeout = (int)connectionTimeout * 10;
                    
                    yield return synthRequest.SendWebRequest();
                    
                    float synthesisElapsedTime = Time.realtimeSinceStartup - synthesisStartTime;
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] frame_synthesis リクエスト完了: {synthesisElapsedTime:F2}秒経過");
                    }

                    if (synthRequest.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[TTSService] frame_synthesis 失敗");
                        Debug.LogError($"[TTSService] エラー詳細: {synthRequest.error}");
                        Debug.LogError($"[TTSService] レスポンスコード: {synthRequest.responseCode}");
                        yield break;
                    }

                    // Step 4: WAVバイナリをAudioClipに変換
                    byte[] wavData = synthRequest.downloadHandler.data;
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] frame_synthesis 成功: {wavData.Length}バイトのWAVデータ");
                    }

                    if (wavData == null || wavData.Length == 0)
                    {
                        Debug.LogError($"[TTSService] frame_synthesis 成功しましたが、WAVデータが空です");
                        yield break;
                    }
                    
                    AudioClip clip = WavToAudioClip(wavData, cacheKey);

                    if (clip != null)
                    {
                        // 音声データが実際に含まれているか確認
                        float[] samples = new float[clip.samples * clip.channels];
                        clip.GetData(samples, 0);
                        float maxAmplitude = 0f;
                        for (int i = 0; i < samples.Length; i++)
                        {
                            float abs = Mathf.Abs(samples[i]);
                            if (abs > maxAmplitude) maxAmplitude = abs;
                        }
                        
                        _cache[cacheKey] = clip;
                        if (enableDebugLog)
                        {
                            Debug.Log($"[TTSService] 歌声合成キャッシュ追加: \"{cacheKey}\" ({clip.length:F2}秒, サンプル数: {clip.samples}, チャンネル: {clip.channels}, 最大振幅: {maxAmplitude:F4})");
                            if (maxAmplitude < 0.001f)
                            {
                                Debug.LogWarning($"[TTSService] 警告: 音声データの最大振幅が非常に小さいです（{maxAmplitude:F4}）。無音の可能性があります。");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogError($"[TTSService] WAVデータからAudioClipへの変換に失敗しました");
                    }
                }
            }
        }
    }
}
