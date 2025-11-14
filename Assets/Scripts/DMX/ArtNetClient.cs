using UnityEngine;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;

namespace Encounter.DMX
{
    public class ArtNetClient : MonoBehaviour
    {
        [Header("Art-Net")]
        [Tooltip("送信先 IP。ユニキャストを推奨（例: 192.168.1.200）")]
        public string targetIp = "192.168.0.10";
        [Tooltip("Art-Net 既定ポート = 6454")]
        public int targetPort = 6454;

        [Header("Universe (Art-Net v4)")]
        [Range(0, 127)] public int net = 0;        // 上位7bit
        [Range(0, 15)] public int subnet = 0;      // Subnet (4bit)
        [Range(0, 15)] public int universe = 0;    // Universe (4bit)

        [Header("Advanced (Multi-NIC)")]
        [Tooltip("送信元IPを明示的に指定（空欄で自動選択）")]
        public string bindLocalIp = "";
        [Tooltip("送信元インターフェース名を指定（例: \"USB\", \"Ethernet\"）")]
        public string bindInterfaceName = "";
        [Tooltip("宛先IPと同一サブネットのNICを自動選択")]
        public bool autoSelectLocalIp = true;
        [Tooltip("IP選択のログを表示")]
        public bool logSelection = false;

        private UdpClient _udp;
        private IPEndPoint _remoteEP;
        private IPAddress _localBindAddress;
        private byte _sequence = 0;

        void Awake()
        {
            Application.runInBackground = true;
            SetupSocket();
        }

        void OnDestroy()
        {
            try { _udp?.Close(); } catch { /* ignore */ }
            _udp = null;
        }

        void OnValidate()
        {
            targetPort = Mathf.Clamp(targetPort, 1, 65535);
        }

        private void SetupSocket()
        {
            // 既存をクローズ
            try { _udp?.Close(); } catch { }
            _udp = null;

            // 宛先IPの解決
            IPAddress dstIp = null;
            if (!IPAddress.TryParse(targetIp, out dstIp))
            {
                try
                {
                    var entry = System.Net.Dns.GetHostEntry(targetIp);
                    dstIp = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                }
                catch { }
            }
            if (dstIp == null)
            {
                Debug.LogError($"[ArtNetClient] 無効な宛先: {targetIp}");
                return;
            }
            _remoteEP = new IPEndPoint(dstIp, targetPort);

            // ローカルIPの選択
            _localBindAddress = SelectLocalIPv4(dstIp, bindLocalIp, bindInterfaceName, autoSelectLocalIp, logSelection);
            
            // UDPクライアント作成
            try
            {
                if (_localBindAddress == null)
                {
                    if (logSelection) Debug.LogWarning("[ArtNetClient] ローカルIP自動選択失敗。未バインドで送信します。");
                    _udp = new UdpClient(AddressFamily.InterNetwork);
                }
                else
                {
                    var localEP = new IPEndPoint(_localBindAddress, 0);
                    _udp = new UdpClient(localEP);
                    if (logSelection) Debug.Log($"[ArtNetClient] Bind local {_localBindAddress}");
                }

                _udp.EnableBroadcast = true;
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                _udp.Connect(_remoteEP);
                Debug.Log($"[ArtNetClient] 接続: {targetIp}:{targetPort}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArtNetClient] ソケット初期化エラー: {e.Message}");
            }
        }

        public void SendDmx(byte[] dmx512)
        {
            if (_udp == null || _remoteEP == null)
            {
                Debug.LogError("[ArtNetClient] UDP未初期化です。");
                return;
            }

            int length = Mathf.Clamp(dmx512?.Length ?? 512, 1, 512);
            byte[] packet = new byte[18 + length];

            // ID "Art-Net\0"
            packet[0] = (byte)'A'; packet[1] = (byte)'r'; packet[2] = (byte)'t'; packet[3] = (byte)'-';
            packet[4] = (byte)'N'; packet[5] = (byte)'e'; packet[6] = (byte)'t'; packet[7] = 0x00;

            // OpCode 0x5000 (little-endian: low, high)
            packet[8] = 0x00; packet[9] = 0x50;

            // Protocol Version (big-endian), 14 = 0x000E
            packet[10] = 0x00; packet[11] = 0x0E;

            // Sequence（1..255）
            _sequence = (byte)((_sequence % 255) + 1);
            packet[12] = _sequence;

            // Physical port（不明なら0）
            packet[13] = 0x00;

            // SubUni (low 8 bits): subnet (upper 4 bits) + universe (lower 4 bits)
            // Net (high 7 bits)
            byte subUni = (byte)((subnet << 4) | (universe & 0x0F));
            packet[14] = subUni;
            packet[15] = (byte)(net & 0x7F);

            // Length (big-endian)
            packet[16] = (byte)((length >> 8) & 0xFF); // Hi
            packet[17] = (byte)(length & 0xFF);        // Lo

            // DMX payload
            if (dmx512 != null && dmx512.Length >= length)
            {
                Buffer.BlockCopy(dmx512, 0, packet, 18, length);
            }
            else
            {
                // dmx512がnullまたは短い場合はゼロで埋める
                for (int i = 18; i < packet.Length; i++)
                {
                    packet[i] = 0;
                }
            }

            try
            {
                _udp.Send(packet, packet.Length);
            }
            catch (SocketException se)
            {
                Debug.LogError($"[ArtNetClient] Art-Net送信エラー: {se.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArtNetClient] Art-Net送信エラー: {e.Message}");
            }
        }

        // ---- Utility Methods ----
        private static IPAddress SelectLocalIPv4(IPAddress dstIp, string preferLocalIp, string preferInterfaceName, bool autoPick, bool log)
        {
            if (!string.IsNullOrWhiteSpace(preferLocalIp) && IPAddress.TryParse(preferLocalIp, out var explicitIp))
            {
                if (IsUsableLocalIPv4(explicitIp)) return explicitIp;
                if (log) Debug.LogWarning($"[ArtNetClient] 指定された bindLocalIp が見つかりません: {preferLocalIp}");
            }

            var ifaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToArray();

            if (!string.IsNullOrWhiteSpace(preferInterfaceName))
            {
                var nic = ifaces.FirstOrDefault(n =>
                    n.Name.IndexOf(preferInterfaceName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.Description.IndexOf(preferInterfaceName, StringComparison.OrdinalIgnoreCase) >= 0);
                var ip = PickIPv4FromNic(nic);
                if (ip != null) return ip;
                if (log) Debug.LogWarning($"[ArtNetClient] 指定インターフェースが見つからない: {preferInterfaceName}");
            }

            if (!autoPick) return null;

            foreach (var nic in ifaces)
            {
                foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var local = uni.Address;
                    var mask = uni.IPv4Mask;
                    if (mask != null && SameNetwork(local, dstIp, mask)) return local;
                }
            }

            var anyPrivate = ifaces.Select(PickIPv4FromNic).FirstOrDefault(ip => ip != null && IsPrivateIPv4(ip));
            return anyPrivate;
        }

        private static bool IsUsableLocalIPv4(IPAddress ip)
        {
            if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .Any(nic => nic.GetIPProperties().UnicastAddresses
                    .Any(uni => uni.Address.AddressFamily == AddressFamily.InterNetwork && uni.Address.Equals(ip)));
        }

        private static IPAddress PickIPv4FromNic(NetworkInterface nic)
        {
            if (nic == null) return null;
            var uni = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
            return uni?.Address;
        }

        private static bool IsPrivateIPv4(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return (b[0] == 10) || (b[0] == 172 && (b[1] >= 16 && b[1] <= 31)) || (b[0] == 192 && b[1] == 168);
        }

        private static bool SameNetwork(IPAddress a, IPAddress b, IPAddress mask)
        {
            var A = a.GetAddressBytes(); var B = b.GetAddressBytes(); var M = mask.GetAddressBytes();
            for (int i = 0; i < 4; i++) if ((A[i] & M[i]) != (B[i] & M[i])) return false;
            return true;
        }
    }
}
