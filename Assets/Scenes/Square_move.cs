using UnityEngine;

public class SmoothHeightFollow2D : MonoBehaviour
{
    [Header("追従する対象 (2D)")]
    public Transform target; // 追従対象

    [Header("追従スピード（小さいほど遅い）")]
    [Range(0.1f, 10f)]
    public float followSpeed = 3f;

    [Header("イージング強度（0で線形、1で強い減速感）")]
    [Range(0f, 1f)]
    public float easingStrength = 0.3f;

    [Header("高さオフセット")]
    public float heightOffset = 0f;

    private float velocityY = 0f;

    void Update()
    {
        if (target == null) return;

        // 目標の高さ
        float targetY = target.position.y + heightOffset;

        // イージング付きのスムーズ追従
        float easedY = Mathf.SmoothDamp(
            transform.position.y,
            targetY,
            ref velocityY,
            1f / followSpeed,
            Mathf.Infinity,
            Time.deltaTime
        );

        // easingStrength でイージングの効き具合を調整
        float smoothedY = Mathf.Lerp(transform.position.y, easedY, 1f - Mathf.Pow(1f - easingStrength, Time.deltaTime * 60f));

        // Y軸だけ更新（2D）
        transform.position = new Vector3(transform.position.x, smoothedY, transform.position.z);
    }
}
