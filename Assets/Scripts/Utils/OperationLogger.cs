using UnityEngine;
using System.IO;
using System;
using System.Text;
using System.Collections.Generic;

namespace Encounter.Utils
{
    /// <summary>
    /// システムの操作ログをCSV形式で保存するクラス
    /// </summary>
    public class OperationLogger : MonoBehaviour
    {
        public static OperationLogger Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("ログを保存するフォルダ名")]
        public string logFolderName = "SessionLogs";
        
        [Tooltip("CSVの区切り文字")]
        public string csvDelimiter = ",";

        [Tooltip("デバッグログにも出力するか")]
        public bool echoToConsole = true;

        private string _filePath;
        private StreamWriter _writer;
        private float _startTime;
        private bool _isInitialized = false;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeLog();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void InitializeLog()
        {
            try
            {
                string folderPath = Path.Combine(Application.persistentDataPath, logFolderName);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string fileName = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                _filePath = Path.Combine(folderPath, fileName);

                // BOM付きUTF-8で書き込み（Excel互換性のため）
                _writer = new StreamWriter(_filePath, true, Encoding.UTF8);
                _writer.AutoFlush = true;

                // ヘッダー書き込み
                string[] headers = { "Timestamp", "TimeSinceStart", "Category", "Action", "Details" };
                _writer.WriteLine(string.Join(csvDelimiter, headers));

                _startTime = Time.time;
                _isInitialized = true;

                Log("System", "LogStarted", $"Log file created at {_filePath}");
                Debug.Log($"[OperationLogger] Log initialized: {_filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[OperationLogger] Initialization failed: {e.Message}");
            }
        }

        /// <summary>
        /// ログを記録します
        /// </summary>
        /// <param name="category">機能カテゴリ (Ex: Scenario, Audio, DMX)</param>
        /// <param name="action">アクション名 (Ex: Start, Stop, Error)</param>
        /// <param name="details">詳細情報 (JSON形式など)</param>
        public void Log(string category, string action, string details = "")
        {
            if (!_isInitialized || _writer == null) return;

            try
            {
                float timeSinceStart = Time.time - _startTime;
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                
                // CSVエスケープ処理（カンマや改行を含む場合）
                string escapedDetails = EscapeCsv(details);

                string line = string.Join(csvDelimiter, new string[] {
                    timestamp,
                    timeSinceStart.ToString("F3"),
                    category,
                    action,
                    escapedDetails
                });

                _writer.WriteLine(line);

                if (echoToConsole)
                {
                    Debug.Log($"[Log:{category}] {action}: {details}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[OperationLogger] Write failed: {e.Message}");
            }
        }

        private string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            
            if (field.Contains(csvDelimiter) || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                // ダブルクォートをエスケープし、全体をダブルクォートで囲む
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }

        private void OnDestroy()
        {
            if (_writer != null)
            {
                Log("System", "LogEnded", "Session ended");
                _writer.Close();
                _writer = null;
            }
        }

        private void OnApplicationQuit()
        {
            if (_writer != null)
            {
                _writer.Close();
                _writer = null;
            }
        }
    }
}

