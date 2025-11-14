using UnityEngine;
using Encounter.DMX;

namespace Encounter.Safety
{
    public class SafetyManager : MonoBehaviour
    {
        [Header("References")]
        public Encounter.DMX.KineticLightController lightController;

        [Header("E-Stop Settings")]
        [Tooltip("非常停止時の高さ（DMX値、100-255）")]
        [Range(100, 255)]
        public int eStopHeight = 100;

        [Tooltip("非常停止時の色")]
        public Color eStopColor = Color.red;

        public bool emergencyStop { get; private set; } = false;

        void Awake()
        {
            // 自動検索
            if (lightController == null)
            {
                lightController = FindFirstObjectByType<Encounter.DMX.KineticLightController>();
            }
        }

        void Update()
        {
            // 非常停止中は出力を固定
            if (emergencyStop && lightController != null)
            {
                ApplyEStopState();
            }
        }

        public void TriggerEStop()
        {
            if (emergencyStop) return; // 既に停止中

            emergencyStop = true;
            Debug.LogWarning("[SafetyManager] 非常停止が発動しました。");

            // 即座に出力をゼロ化
            if (lightController != null)
            {
                ApplyEStopState();
                lightController.Apply();
            }
        }

        public void ResetEStop()
        {
            if (!emergencyStop) return; // 既に解除済み

            emergencyStop = false;
            Debug.Log("[SafetyManager] 非常停止が解除されました。");

            // 復帰処理（必要に応じて）
            // ここでは何もしない（外部から制御を再開）
        }

        private void ApplyEStopState()
        {
            if (lightController == null || lightController.fixtures == null) return;

            // 全フィクスチャを安全な状態に設定
            for (int i = 0; i < lightController.fixtures.Count; i++)
            {
                // 高さを安全位置に
                lightController.SetFixtureHeight(i, eStopHeight);
                // 色を警告色に
                lightController.SetFixtureColor(i, eStopColor);
            }
        }

        /// <summary>
        /// 非常停止状態かどうかを確認（外部から呼び出し可能）
        /// </summary>
        public bool IsEStopActive()
        {
            return emergencyStop;
        }
    }
}
