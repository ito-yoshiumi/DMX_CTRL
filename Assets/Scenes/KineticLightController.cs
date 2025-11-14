// KineticLightController.cs
// 前提：同じシーンに ArtNetSender が存在（前回のスクリプト）
using UnityEngine;

public class KineticLightController : MonoBehaviour
{
    [Header("References")]
    public ArtNetSender art; // 未指定なら自動で検索

    [Header("Patch (Start Addresses per fixture)")]
    public int[] starts = new int[] { 1, 17, 33, 49, 65, 81 }; // 6台

    [Header("Test Values")]
    public Color testColor = Color.white; // R,G,B (0-255相当)
    [Range(0, 100)] public int lightHeight = 100; // DMX: 0-100 に制限して送る
    [Range(0, 255)] public int dimmer = 255;      // 常時 255
    [Range(0, 255)] public int strobe = 0;        // 常時 0（無効）

    [Header("Apply every frame")]
    public bool liveUpdate = true;

    void Awake()
    {
        if (!art) art = FindFirstObjectByType<ArtNetSender>();
        if (!art)
        {
            Debug.LogError("ArtNetSender が見つかりません。先に ArtNetSender.cs をシーンへ。");
            enabled = false; return;
        }

        // 必要ch数に合わせて送信長を確保（最後の機器のCh6まで）
        int highest = 0;
        foreach (var s in starts) highest = Mathf.Max(highest, s + 5); // +5 = Ch6
        art.dmxLength = Mathf.Max(art.dmxLength, highest);
    }

    void OnEnable()  { ApplyAll(); if (!art.autoSend) art.Send(); }
    void OnValidate(){ if (enabled) { ApplyAll(); } }

    void Update()
    {
        if (!liveUpdate) return;
        ApplyAll();
        if (!art.autoSend) art.Send();
    }

    public void ApplyAll()
    {
        // 指定の色・高さ・ディマー・ストロボを全灯に反映
        foreach (var s in starts)
        {
            SetCh(s + 0, Mathf.RoundToInt(testColor.r * 255f)); // R (Ch1)
            SetCh(s + 1, Mathf.RoundToInt(testColor.g * 255f)); // G (Ch2)
            SetCh(s + 2, Mathf.RoundToInt(testColor.b * 255f)); // B (Ch3)
            SetCh(s + 3, dimmer);                               // Dimmer (Ch4)
            SetCh(s + 4, strobe);                               // Strobe (Ch5)
            SetCh(s + 5, Mathf.Clamp(lightHeight, 0, 100));     // Height (Ch6) 0〜100に制限
        }
    }

    private void SetCh(int channel1based, int value01_255)
    {
        art.SetChannel(channel1based, Mathf.Clamp(value01_255, 0, 255));
    }

    // 例：段階的な高さで静止配置したいときに呼ぶ
    [ContextMenu("Set Gradient Heights 0..100")]
    public void SetGradientHeights()
    {
        int n = Mathf.Max(1, starts.Length - 1);
        for (int i = 0; i < starts.Length; i++)
        {
            int h = Mathf.RoundToInt(100f * i / n); // 0,20,40,60,80,100
            SetCh(starts[i] + 5, h);
        }
        if (!art.autoSend) art.Send();
    }
}
