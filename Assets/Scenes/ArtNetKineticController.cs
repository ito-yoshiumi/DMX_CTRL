// ArtNetKineticController.cs
// ArtNet送信 + KineticLight制御 + Unityオブジェクト高さ追従版
// Y=-5 → LightHeight=100, Y=5 → LightHeight=0

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using UnityEngine;

public class ArtNetKineticController : MonoBehaviour
{
    [Header("Destination")]
    public string targetIp = "192.168.1.200";
    public int targetPort = 6454;

    [Header("Universe (Art-Net v4)")]
    [Range(0, 127)] public int net = 0;
    [Range(0, 255)] public int subUni = 0;

    [Header("DMX Settings")]
    [Range(1, 512)] public int dmxLength = 512;
    public bool autoSend = true;
    [Range(1, 60)] public int sendFps = 30;

    [Header("Advanced (Multi-NIC)")]
    public string bindLocalIp = "";
    public string bindInterfaceName = "";
    public bool autoSelectLocalIp = true;
    public bool logSelection = true;

    [Header("Kinetic Light Control")]
    [Tooltip("各Fixtureの開始チャンネル (1-based)")]
    public int[] starts = new int[] { 1, 17, 33, 49, 65, 81 };
    public Color testColor = Color.white;
    [Range(0, 255)] public int dimmer = 255;
    [Range(0, 255)] public int strobe = 0;
    public bool liveUpdate = true;

    [Header("Height Source (Y軸に応じて高さ制御)")]
    [Tooltip("このオブジェクトのY座標で高さを決定します。")]
    public Transform heightTarget;
    [Tooltip("Y=-5で100, Y=5で0になるようにマッピング")]
    public float minY = -5f;
    public float maxY = 5f;

    // 内部変数
    private UdpClient udp;
    private IPEndPoint remoteEP;
    private IPAddress localBindAddress;
    private byte[] dmx = new byte[512];
    private byte sequence = 0;
    private float accum;

    void Awake()
    {
        Application.runInBackground = true;
        SetupSocket();
        EnsureDMXLength();
    }

    void OnDestroy()
    {
        try { udp?.Close(); } catch { }
        udp = null;
    }

    void OnValidate()
    {
        dmxLength = Mathf.Clamp(dmxLength, 1, 512);
        targetPort = Mathf.Clamp(targetPort, 1, 65535);
        EnsureDMXLength();
        if (enabled) ApplyAll();
    }

    void Update()
    {
        if (liveUpdate)
        {
            UpdateLightHeightFromTarget();
            ApplyAll();
        }

        if (autoSend)
        {
            accum += Time.deltaTime;
            float interval = 1f / Mathf.Max(1, sendFps);
            if (accum >= interval)
            {
                accum = 0f;
                Send();
            }
        }
    }

    /// <summary>
    /// 指定したオブジェクトのY座標をDMX高さ(0-100)に変換して反映
    /// </summary>
    private void UpdateLightHeightFromTarget()
    {
        if (!heightTarget) return;

        float y = heightTarget.position.y;
        // Y=-5 → 100, Y=5 → 0 にマッピング（Clamp付き）
        float t = Mathf.InverseLerp(minY, maxY, y);
        float height = Mathf.Lerp(100f, 0f, t);
        lightHeight = Mathf.RoundToInt(height);
    }

    private int lightHeight = 100; // 内部で計算される高さ値

    private void EnsureDMXLength()
    {
        int highest = 0;
        foreach (var s in starts)
            highest = Mathf.Max(highest, s + 5);
        dmxLength = Mathf.Max(dmxLength, highest);
    }

    [ContextMenu("Rebind Socket")]
    public void SetupSocket()
    {
        try { udp?.Close(); } catch { }
        udp = null;

        if (!IPAddress.TryParse(targetIp, out var dstIp))
        {
            try
            {
                var entry = Dns.GetHostEntry(targetIp);
                dstIp = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch { }
        }
        if (dstIp == null)
        {
            Debug.LogError($"[ArtNet] 無効な宛先: {targetIp}");
            return;
        }
        remoteEP = new IPEndPoint(dstIp, targetPort);

        localBindAddress = SelectLocalIPv4(dstIp, bindLocalIp, bindInterfaceName, autoSelectLocalIp, logSelection);
        if (localBindAddress == null)
        {
            if (logSelection) Debug.LogWarning("[ArtNet] ローカルIP自動選択失敗。未バインドで送信します。");
            udp = new UdpClient(AddressFamily.InterNetwork);
        }
        else
        {
            var localEP = new IPEndPoint(localBindAddress, 0);
            udp = new UdpClient(localEP);
            if (logSelection) Debug.Log($"[ArtNet] Bind local {localBindAddress}");
        }

        try
        {
            udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            udp.Connect(remoteEP);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ArtNet] Connect警告: {e.Message}");
        }
    }

    // ---- DMX送信系 ----
    public void SetChannel(int channel1based, int value)
    {
        if (channel1based < 1 || channel1based > 512) return;
        dmx[channel1based - 1] = (byte)Mathf.Clamp(value, 0, 255);
    }

    public void Send()
    {
        if (udp == null || remoteEP == null)
        {
            Debug.LogError("[ArtNet] UDP未初期化です。SetupSocket()を確認してください。");
            return;
        }

        int length = Mathf.Clamp(dmxLength, 1, 512);
        byte[] packet = new byte[18 + length];

        packet[0] = (byte)'A'; packet[1] = (byte)'r'; packet[2] = (byte)'t'; packet[3] = (byte)'-';
        packet[4] = (byte)'N'; packet[5] = (byte)'e'; packet[6] = (byte)'t'; packet[7] = 0x00;
        packet[8] = 0x00; packet[9] = 0x50;
        packet[10] = 0x00; packet[11] = 0x0E;
        sequence = (byte)((sequence % 255) + 1);
        packet[12] = sequence;
        packet[13] = 0x00;
        packet[14] = (byte)(subUni & 0xFF);
        packet[15] = (byte)(net & 0x7F);
        packet[16] = (byte)((length >> 8) & 0xFF);
        packet[17] = (byte)(length & 0xFF);

        Buffer.BlockCopy(dmx, 0, packet, 18, length);

        try { udp.Send(packet, packet.Length); }
        catch (SocketException se) { Debug.LogError($"Art-Net send error: {se.Message}"); }
        catch (Exception e) { Debug.LogError($"Art-Net send error: {e.Message}"); }
    }

    // ---- Kinetic Light制御 ----
    public void ApplyAll()
    {
        foreach (var s in starts)
        {
            SetChannel(s + 0, Mathf.RoundToInt(testColor.r * 255f));
            SetChannel(s + 1, Mathf.RoundToInt(testColor.g * 255f));
            SetChannel(s + 2, Mathf.RoundToInt(testColor.b * 255f));
            SetChannel(s + 3, dimmer);
            SetChannel(s + 4, strobe);
            SetChannel(s + 5, Mathf.Clamp(lightHeight, 0, 100));
        }
    }

    [ContextMenu("Set Gradient Heights 0..100")]
    public void SetGradientHeights()
    {
        int n = Mathf.Max(1, starts.Length - 1);
        for (int i = 0; i < starts.Length; i++)
        {
            int h = Mathf.RoundToInt(100f * i / n);
            SetChannel(starts[i] + 5, h);
        }
        if (!autoSend) Send();
    }

    // ---- Utility ----
    private static IPAddress SelectLocalIPv4(IPAddress dstIp, string preferLocalIp, string preferInterfaceName, bool autoPick, bool log)
    {
        if (!string.IsNullOrWhiteSpace(preferLocalIp) && IPAddress.TryParse(preferLocalIp, out var explicitIp))
        {
            if (IsUsableLocalIPv4(explicitIp)) return explicitIp;
            if (log) Debug.LogWarning($"[ArtNet] 指定された bindLocalIp が見つかりません: {preferLocalIp}");
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
            if (log) Debug.LogWarning($"[ArtNet] 指定インターフェースが見つからない: {preferInterfaceName}");
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
