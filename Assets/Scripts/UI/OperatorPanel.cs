using UnityEngine;

namespace Encounter.UI
{
    public class OperatorPanel : MonoBehaviour
    {
        public Encounter.Scenario.ScenarioRunner runner;

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10,10,260,160), GUI.skin.box);
            GUILayout.Label("Operator Panel");
            if (GUILayout.Button("Run Scenario"))
            {
                runner?.RunAll();
            }
            GUILayout.EndArea();
        }
    }
}
