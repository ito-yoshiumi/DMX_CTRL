using UnityEngine;
using Encounter.DMX;

namespace Encounter.Testing
{
    /// <summary>
    /// DMX送信の代わりにログ出力するテスト用コンポーネント
    /// </summary>
    public class DMXDebugLogger : MonoBehaviour
    {
        [Header("References")]
        public Encounter.DMX.KineticLightController lightController;

        [Header("Log Settings")]
        [Tooltip("ログ出力間隔（秒）")]
        public float logInterval = 0.5f;
        [Tooltip("DMX値をログ出力")]
        public bool logDmxValues = true;
        [Tooltip("非ゼロのチャンネルのみ出力")]
        public bool logNonZeroOnly = true;

        private float _lastLogTime;
        private byte[] _lastDmx = new byte[512];

        void Start()
        {
            if (lightController == null)
            {
                lightController = FindFirstObjectByType<Encounter.DMX.KineticLightController>();
            }

            if (lightController == null)
            {
                Debug.LogWarning("[DMXDebugLogger] KineticLightControllerが見つかりません。");
            }
            else
            {
                Debug.Log("[DMXDebugLogger] DMXデバッグロガー開始");
            }
        }

        void Update()
        {
            if (lightController == null || !logDmxValues) return;

            if (Time.time - _lastLogTime >= logInterval)
            {
                LogDmxValues();
                _lastLogTime = Time.time;
            }
        }

        private void LogDmxValues()
        {
            // KineticLightControllerのDMXバッファに直接アクセスできないため、
            // フィクスチャの状態をログ出力
            if (lightController.fixtures != null && lightController.fixtures.Count > 0)
            {
                string log = "[DMXDebugLogger] Fixtures: ";
                for (int i = 0; i < lightController.fixtures.Count; i++)
                {
                    var fixture = lightController.fixtures[i];
                    log += $"F{i}(Addr:{fixture.startAddress}) ";
                }
                Debug.Log(log);
            }
        }

        /// <summary>
        /// ArtNetClientのSendDmxをフックしてログ出力（拡張用）
        /// </summary>
        public void OnDmxSent(byte[] dmx)
        {
            if (dmx == null || !logDmxValues) return;

            bool hasChanges = false;
            for (int i = 0; i < dmx.Length && i < _lastDmx.Length; i++)
            {
                if (dmx[i] != _lastDmx[i])
                {
                    hasChanges = true;
                    break;
                }
            }

            if (!hasChanges && logNonZeroOnly) return;

            string log = "[DMXDebugLogger] DMX Values: ";
            int nonZeroCount = 0;
            for (int i = 0; i < dmx.Length; i++)
            {
                if (dmx[i] > 0)
                {
                    if (logNonZeroOnly)
                    {
                        log += $"Ch{i + 1}={dmx[i]} ";
                        nonZeroCount++;
                        if (nonZeroCount >= 20) // 最大20チャンネルまで
                        {
                            log += "...";
                            break;
                        }
                    }
                }
            }

            if (nonZeroCount > 0 || !logNonZeroOnly)
            {
                Debug.Log(log);
            }

            System.Array.Copy(dmx, _lastDmx, Mathf.Min(dmx.Length, _lastDmx.Length));
        }
    }
}

