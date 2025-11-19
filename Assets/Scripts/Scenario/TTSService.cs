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

            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] 事前合成開始: {ttsEntries.Count}件のテキスト");
            }

            foreach (var entry in ttsEntries)
            {
                // 既にキャッシュされている場合はスキップ
                if (_cache.ContainsKey(entry.text))
                {
                    continue;
                }

                yield return StartCoroutine(SynthesizeAndCache(entry.text));
            }

            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] 事前合成完了: {_cache.Count}件のクリップをキャッシュ");
            }
        }

        private IEnumerator SynthesizeAndCache(string text)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[TTSService] 音声合成開始: \"{text}\" (話者ID: {speakerId})");
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

                // Step 2: synthesis で音声を生成
                string synthesisUrl = $"{engineBaseUrl}/synthesis?speaker={speakerId}";
                
                if (enableDebugLog)
                {
                    Debug.Log($"[TTSService] synthesis リクエスト: {synthesisUrl}");
                }
                
                using (UnityWebRequest synthRequest = CreatePostRequest(synthesisUrl, queryJson))
                {
                    synthRequest.timeout = (int)connectionTimeout;
                    yield return synthRequest.SendWebRequest();

                    if (synthRequest.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[TTSService] synthesis 失敗: \"{text}\"");
                        Debug.LogError($"[TTSService] エラー詳細: {synthRequest.error}");
                        Debug.LogError($"[TTSService] レスポンスコード: {synthRequest.responseCode}");
                        Debug.LogError($"[TTSService] URL: {synthesisUrl}");
                        yield break;
                    }

                    // Step 3: WAVバイナリをAudioClipに変換
                    byte[] wavData = synthRequest.downloadHandler.data;
                    
                    if (enableDebugLog)
                    {
                        Debug.Log($"[TTSService] synthesis 成功: {wavData.Length}バイトのWAVデータ");
                    }
                    
                    AudioClip clip = WavToAudioClip(wavData, text);

                    if (clip != null)
                    {
                        _cache[text] = clip;
                        if (enableDebugLog)
                        {
                            Debug.Log($"[TTSService] キャッシュ追加: \"{text}\" ({clip.length:F2}秒)");
                        }
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

        public AudioClip GetCachedClip(string text)
        {
            _cache.TryGetValue(text, out var clip);
            return clip;
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
    }
}
