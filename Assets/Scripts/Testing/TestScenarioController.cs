using UnityEngine;
using Encounter.Scenario;

namespace Encounter.Testing
{
    /// <summary>
    /// シナリオ実行をテストするための簡単なUIコントローラー
    /// </summary>
    public class TestScenarioController : MonoBehaviour
    {
        [Header("References")]
        public ScenarioRunner scenarioRunner;

        [Header("Controls")]
        [Tooltip("スペースキーでシナリオ開始/停止")]
        public KeyCode startKey = KeyCode.Space;
        [Tooltip("Rキーでリセット")]
        public KeyCode resetKey = KeyCode.R;

        void Start()
        {
            if (scenarioRunner == null)
            {
                scenarioRunner = FindFirstObjectByType<ScenarioRunner>();
            }

            if (scenarioRunner == null)
            {
                Debug.LogWarning("[TestScenarioController] ScenarioRunnerが見つかりません。");
            }
            else
            {
                Debug.Log("[TestScenarioController] シナリオコントローラー準備完了");
                Debug.Log($"  - {startKey}キー: シナリオ開始/停止");
                Debug.Log($"  - {resetKey}キー: リセット");
            }
        }

        void Update()
        {
            if (scenarioRunner == null) return;

            // 初期化が完了していない場合は無視
            if (!scenarioRunner.IsInitialized)
            {
                return;
            }

            if (Input.GetKeyDown(startKey))
            {
                if (!scenarioRunner.IsRunning)
                {
                    scenarioRunner.RunAll();
                    Debug.Log($"[TestScenarioController] シナリオ開始 (経過時間: {Time.time:F2}秒)");
                }
                else
                {
                    scenarioRunner.Stop();
                    Debug.Log($"[TestScenarioController] シナリオ停止 (経過時間: {Time.time:F2}秒)");
                }
            }

            if (Input.GetKeyDown(resetKey))
            {
                scenarioRunner.Stop();
                Debug.Log($"[TestScenarioController] リセット (経過時間: {Time.time:F2}秒)");
            }
        }

    }
}

