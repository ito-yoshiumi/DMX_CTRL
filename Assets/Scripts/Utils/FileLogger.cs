using UnityEngine;
using System.IO;
using System;

namespace Encounter.Utils
{
    /// <summary>
    /// デバッグログをファイルに出力するユーティリティ
    /// </summary>
    public class FileLogger : MonoBehaviour
    {
        private static FileLogger _instance;
        private StreamWriter _logWriter;
        private string _logFilePath;

        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeLogFile();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void InitializeLogFile()
        {
            try
            {
                // ログファイルのパスを設定
                string logDir = Path.Combine(Application.persistentDataPath, "Logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(logDir, $"unity_log_{timestamp}.txt");
                
                _logWriter = new StreamWriter(_logFilePath, true, System.Text.Encoding.UTF8);
                _logWriter.AutoFlush = true;
                
                _logWriter.WriteLine($"=== Unity Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _logWriter.WriteLine($"Log File: {_logFilePath}");
                _logWriter.WriteLine($"Application.persistentDataPath: {Application.persistentDataPath}");
                _logWriter.WriteLine();
                
                // Unityのログをキャプチャ
                Application.logMessageReceived += HandleLog;
                
                Debug.Log($"[FileLogger] ログファイルを開始しました: {_logFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileLogger] ログファイルの初期化に失敗しました: {e.Message}");
            }
        }

        void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (_logWriter != null)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logType = type.ToString().ToUpper();
                
                _logWriter.WriteLine($"[{timestamp}] [{logType}] {logString}");
                
                if (type == LogType.Error || type == LogType.Exception)
                {
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        _logWriter.WriteLine($"Stack Trace: {stackTrace}");
                    }
                }
            }
        }

        void OnDestroy()
        {
            if (_logWriter != null)
            {
                _logWriter.WriteLine();
                _logWriter.WriteLine($"=== Unity Log Ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _logWriter.Close();
                _logWriter = null;
            }
            
            Application.logMessageReceived -= HandleLog;
        }

        void OnApplicationQuit()
        {
            OnDestroy();
        }

        public static string GetLogFilePath()
        {
            return _instance?._logFilePath ?? "ログファイルが初期化されていません";
        }
    }
}

