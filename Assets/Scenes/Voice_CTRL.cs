using UnityEngine;

public class MicVolumeMover : MonoBehaviour
{
    private AudioClip micClip;
    private string currentMicName;
    private const int sampleLength = 1024;
    private float[] samples = new float[sampleLength];

    [Header("Microphone Settings")]
    [Tooltip("使用するマイク機器の名前（空欄の場合は最初のマイクを使用）")]
    public string micDeviceName = "";

    [Header("Movement Settings")]
    [SerializeField, Range(0.1f, 10f)] private float sensitivity = 5f;
    [SerializeField, Range(0.1f, 10f)] private float smoothSpeed = 5f;
    [SerializeField] private float moveRange = 5f;
    [SerializeField] private float baseHeight = 0f;

    private float targetY;
    private float currentY;

    void Start()
    {
        currentY = baseHeight;

        // マイク一覧を表示
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("マイクデバイスが検出されませんでした。");
            return;
        }

        Debug.Log("利用可能なマイク一覧:");
        foreach (var device in Microphone.devices)
        {
            Debug.Log(" - " + device);
        }

        // 使用するマイクを決定
        if (string.IsNullOrEmpty(micDeviceName))
        {
            currentMicName = Microphone.devices[0];
            Debug.Log("micDeviceName が空のため、最初のマイクを使用します: " + currentMicName);
        }
        else
        {
            bool found = false;
            foreach (var device in Microphone.devices)
            {
                if (device == micDeviceName)
                {
                    found = true;
                    currentMicName = micDeviceName;
                    break;
                }
            }

            if (!found)
            {
                Debug.LogWarning($"指定されたマイク '{micDeviceName}' が見つかりません。最初のマイクを使用します。");
                currentMicName = Microphone.devices[0];
            }
        }

        // マイク録音開始
        micClip = Microphone.Start(currentMicName, true, 1, 44100);
        Debug.Log("使用マイク: " + currentMicName);
    }

    void Update()
    {
        if (micClip == null) return;

        int micPosition = Microphone.GetPosition(currentMicName) - sampleLength + 1;
        if (micPosition < 0) return;
        micClip.GetData(samples, micPosition);

        // 音量を計算（RMS）
        float level = 0f;
        for (int i = 0; i < sampleLength; i++)
        {
            level += samples[i] * samples[i];
        }
        level = Mathf.Sqrt(level / sampleLength) * sensitivity;

        // 移動処理
        targetY = baseHeight + Mathf.Clamp(level * moveRange, 0f, moveRange);
        currentY = Mathf.Lerp(currentY, targetY, Time.deltaTime * smoothSpeed);

        transform.position = new Vector3(transform.position.x, currentY, transform.position.z);
    }

    void OnApplicationQuit()
    {
        if (micClip != null)
        {
            Microphone.End(currentMicName);
        }
    }
}
