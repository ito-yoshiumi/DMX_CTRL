// ArtNetSender.cs
// Unity 2021/2022/6 LTS で動作確認想定
// 複数NIC環境向け：送信元のローカルIPを明示バインド or 自動推定して "No route to host" を回避
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using UnityEngine;

public class ArtNetSender : MonoBehaviour
{
    [Header("Destination")]
    [Tooltip("送信先 IP。ユニキャストを推奨（例: 192.168.1.200）。/24ブロードキャストは 192.168.1.255。汎用ブロードキャストは 255.255.255.255。")]
    public string targetIp = "192.168.1.200";
    [Tooltip("Art-Net 既定ポート = 6454")]
    public int targetPort = 6454;

    [Header("Universe (Art-Net v4)")]
    [Range(0, 127)] public int net = 0;        // 上位7bit
    [Range(0, 255)] public int subUni = 0;     // 下位8bit（機器の Subnet/Universe に相当）

    [Header("DMX Settings")]
    [Range(1, 512)] public int dmxLength = 512;   // 実送信ch数（1〜512）
    [Tooltip("毎フレーム送る（true）/ 手動Sendのみ（false）")]
    public bool autoSend = true;
    [Tooltip("自動送信時のフレームレート（推奨 30〜44fps）")]
    [Range(1, 60)] public int sendFps = 30;

    [Header("Advanced (Multi-NIC)")]
    [Tooltip("送信元にバインドするローカルIPv4（例: 192.168.1.10）。空なら自動推定。")]
    public string bindLocalIp = "";
    [Tooltip("この名前を含むNICからIPv4を自動選択（例: \"USB\", \"Ethernet\", \"Thunderbolt\"）。bindLocalIp が空で有効。")]
    public string bindInterfaceName = "";
    [Tooltip("bindLocalIp未指定時に、宛先と同一サブネットのNICを自動推定します。")]
    public bool autoSelectLocalIp = true;
    [Tooltip("選択結果や警告をConsoleに出します。")]
    public bool logSelection = true;

    // 内部
    private UdpClient udp;
    private IPEndPoint remoteEP;
    private IPAddress localBindAddress;
    private byte[] dmx = new byte[512]; // ch1は dmx[0]
    private byte sequence = 0;          // 1..255でインクリメント（0=未使用）
    private float accum;

    void Awake()
    {
        Application.runInBackground = true;
        SetupSocket();
    }

    void OnDestroy()
    {
        try { udp?.Close(); } catch { /* ignore */ }
        udp = null;
    }

    void OnValidate()
    {
        dmxLength = Mathf.Clamp(dmxLength, 1, 512);
        targetPort = Mathf.Clamp(targetPort, 1, 65535);
    }

    void Update()
    {
        if (!autoSend) return;
        accum += Time.deltaTime;
        float interval = 1f / Mathf.Max(1, sendFps);
        if (accum >= interval)
        {
            accum = 0f;
            Send();
        }
    }

    /// <summary>ソケットを（再）初期化。IP/インターフェース変更時に呼べます。</summary>
    [ContextMenu("Rebind Socket")]
    public void SetupSocket()
    {
        // 既存をクローズ
        try { udp?.Close(); } catch { }
        udp = null;

        // 宛先
        if (!IPAddress.TryParse(targetIp, out var dstIp))
        {
            // DNS名指定が来た場合も一応解決を試みる
            try
            {
                var entry = Dns.GetHostEntry(targetIp);
                dstIp = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch { }
        }
        if (dstIp == null)
        {
            Debug.LogError($"[ArtNetSender] 無効な宛先: {targetIp}");
            return;
        }
        remoteEP = new IPEndPoint(dstIp, targetPort);

        // 送信元ローカルIPの決定
        localBindAddress = SelectLocalIPv4(dstIp, bindLocalIp, bindInterfaceName, autoSelectLocalIp, logSelection);
        if (localBindAddress == null)
        {
            // 最後の手段：未バインドで作成（OSのデフォルト経路に委ねる）
            if (logSelection) Debug.LogWarning("[ArtNetSender] ローカルIPの自動選択に失敗。未バインドで送信します（経路に依存）。");
            udp = new UdpClient(AddressFamily.InterNetwork);
        }
        else
        {
            var localEP = new IPEndPoint(localBindAddress, 0);
            udp = new UdpClient(localEP);
            if (logSelection) Debug.Log($"[ArtNetSender] Bind local {localBindAddress} で送信します。");
        }

        // ブロードキャスト許可
        try
        {
            udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
        }
        catch { /* ignore */ }

        // （任意）ConnectしておくとOSが経路を事前決定しやすい
        try { udp.Connect(remoteEP); } catch (Exception e) { Debug.LogWarning($"[ArtNetSender] Connect警告: {e.Message}"); }
    }

    /// <summary>DMXチャンネル（1-512）に 0-255 をセット</summary>
    public void SetChannel(int channel1based, int value)
    {
        if (channel1based < 1 || channel1based > 512) return;
        dmx[channel1based - 1] = (byte)Mathf.Clamp(value, 0, 255);
    }

    /// <summary>0.0〜1.0 を 0-255 に変換してセット</summary>
    public void SetChannel01(int channel1based, float value01)
    {
        int v = Mathf.RoundToInt(Mathf.Clamp01(value01) * 255f);
        SetChannel(channel1based, v);
    }

    /// <summary>RGBと任意ディマー</summary>
    public void SetRGB(int rCh, int gCh, int bCh, Color color, int dimmerCh = -1, float dimmer01 = 1f)
    {
        SetChannel01(rCh, color.r);
        SetChannel01(gCh, color.g);
        SetChannel01(bCh, color.b);
        if (dimmerCh >= 1) SetChannel01(dimmerCh, dimmer01);
    }

    /// <summary>ArtDMX を送信</summary>
    public void Send()
    {
        if (udp == null || remoteEP == null) { Debug.LogError("[ArtNetSender] UDP未初期化です。SetupSocket()を確認してください。"); return; }

        int length = Mathf.Clamp(dmxLength, 1, 512);
        byte[] packet = new byte[18 + length];

        // ID "Art-Net\0"
        packet[0] = (byte)'A'; packet[1] = (byte)'r'; packet[2] = (byte)'t'; packet[3] = (byte)'-';
        packet[4] = (byte)'N'; packet[5] = (byte)'e'; packet[6] = (byte)'t'; packet[7] = 0x00;

        // OpCode 0x5000 (little-endian: low, high)
        packet[8] = 0x00; packet[9] = 0x50;

        // Protocol Version (big-endian), 14 = 0x000E
        packet[10] = 0x00; packet[11] = 0x0E;

        // Sequence（1..255）
        sequence = (byte)((sequence % 255) + 1);
        packet[12] = sequence;

        // Physical port（不明なら0）
        packet[13] = 0x00;

        // SubUni (low 8 bits), Net (high 7 bits)
        packet[14] = (byte)(subUni & 0xFF);
        packet[15] = (byte)(net & 0x7F);

        // Length (big-endian)
        packet[16] = (byte)((length >> 8) & 0xFF); // Hi
        packet[17] = (byte)(length & 0xFF);        // Lo

        // DMX payload
        Buffer.BlockCopy(dmx, 0, packet, 18, length);

        try
        {
            // Connect 済みなので EP 省略版でも可
            udp.Send(packet, packet.Length);
        }
        catch (SocketException se)
        {
            Debug.LogError($"Art-Net send error: {se.Message}");
            // 典型: No route to host（経路/バインド誤り） → Rebind を提案
        }
        catch (Exception e)
        {
            Debug.LogError($"Art-Net send error: {e.Message}");
        }
    }

    // ======= 送信元IP選択ロジック =======
    private static IPAddress SelectLocalIPv4(
        IPAddress dstIp,
        string preferLocalIp,
        string preferInterfaceName,
        bool autoPick,
        bool log)
    {
        // 1) 明示指定が最優先
        if (!string.IsNullOrWhiteSpace(preferLocalIp) && IPAddress.TryParse(preferLocalIp, out var explicitIp))
        {
            if (IsUsableLocalIPv4(explicitIp))
                return explicitIp;
            if (log) Debug.LogWarning($"[ArtNetSender] 指定された bindLocalIp がローカルで見つかりません: {preferLocalIp}");
        }

        // NIC 一覧
        var ifaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic =>
                nic.OperationalStatus == OperationalStatus.Up &&
                nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .ToArray();

        // 2) インターフェース名での優先（部分一致, 大文字小文字無視）
        if (!string.IsNullOrWhiteSpace(preferInterfaceName))
        {
            var nic = ifaces.FirstOrDefault(n =>
                n.Name.IndexOf(preferInterfaceName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.Description.IndexOf(preferInterfaceName, StringComparison.OrdinalIgnoreCase) >= 0);
            var ip = PickIPv4FromNic(nic);
            if (ip != null) return ip;
            if (log) Debug.LogWarning($"[ArtNetSender] 指定インターフェースが見つからない/IPv4なし: {preferInterfaceName}");
        }

        if (!autoPick) return null;

        // 3) 宛先と同一サブネット候補を自動選択
        //    - Unicast宛先: 各NICの UnicastIPv4 とサブネットマスクで照合
        //    - Broadcast宛先(192.168.1.255等): /24 推定で同第1〜3オクテット一致を優先
        bool isGenericBroadcast = dstIp.Equals(IPAddress.Broadcast);
        foreach (var nic in ifaces)
        {
            foreach (var uni in nic.GetIPProperties().UnicastAddresses)
            {
                if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var local = uni.Address;

                if (isGenericBroadcast)
                {
                    // 255.255.255.255 → プライベートIPv4を優先
                    if (IsPrivateIPv4(local)) return local;
                    continue;
                }

                // 特定サブネットのブロードキャスト（x.y.z.255）を簡易判定
                if (IsLikelyClassCSubnetBroadcast(dstIp, out var cBroadcastBase))
                {
                    if (SameFirst3Octets(local, cBroadcastBase)) return local;
                }

                // マスクが取得できるならネットワーク一致を厳密判定
                var mask = uni.IPv4Mask;
                if (mask != null && !mask.Equals(IPAddress.Any))
                {
                    if (SameNetwork(local, dstIp, mask)) return local;
                }
                else
                {
                    // 取得できない場合のフォールバック：/24で判定
                    var classCMask = IPAddress.Parse("255.255.255.0");
                    if (SameNetwork(local, dstIp, classCMask)) return local;
                }
            }
        }

        // 4) それでも見つからない → 最初に見つかったプライベートIPv4を返す（弱フォールバック）
        var anyPrivate = ifaces
            .Select(PickIPv4FromNic)
            .FirstOrDefault(ip => ip != null && IsPrivateIPv4(ip));
        if (anyPrivate != null) return anyPrivate;

        // 5) 何もなければ null
        return null;
    }

    private static bool IsUsableLocalIPv4(IPAddress ip)
    {
        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Any(nic => nic.GetIPProperties().UnicastAddresses
                .Any(uni => uni.Address.AddressFamily == AddressFamily.InterNetwork &&
                            uni.Address.Equals(ip)));
    }

    private static IPAddress PickIPv4FromNic(NetworkInterface nic)
    {
        if (nic == null) return null;
        var uni = nic.GetIPProperties().UnicastAddresses
            .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
        return uni?.Address;
    }

    private static bool IsPrivateIPv4(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        // 10.0.0.0/8
        if (b[0] == 10) return true;
        // 172.16.0.0/12
        if (b[0] == 172 && (b[1] >= 16 && b[1] <= 31)) return true;
        // 192.168.0.0/16
        if (b[0] == 192 && b[1] == 168) return true;
        return false;
    }

    private static bool SameFirst3Octets(IPAddress a, IPAddress b)
    {
        var A = a.GetAddressBytes();
        var B = b.GetAddressBytes();
        return A[0] == B[0] && A[1] == B[1] && A[2] == B[2];
    }

    private static bool IsLikelyClassCSubnetBroadcast(IPAddress ip, out IPAddress baseAddr)
    {
        var b = ip.GetAddressBytes();
        if (b[3] == 255)
        {
            baseAddr = IPAddress.Parse($"{b[0]}.{b[1]}.{b[2]}.0");
            return true;
        }
        baseAddr = null;
        return false;
    }

    private static bool SameNetwork(IPAddress a, IPAddress b, IPAddress mask)
    {
        var A = a.GetAddressBytes();
        var B = b.GetAddressBytes();
        var M = mask.GetAddressBytes();
        for (int i = 0; i < 4; i++)
        {
            if ((A[i] & M[i]) != (B[i] & M[i])) return false;
        }
        return true;
    }
}
