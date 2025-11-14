using UnityEngine;

namespace Encounter.Safety
{
    public class EStopHandler : MonoBehaviour
    {
        public KeyCode eStopKey = KeyCode.Space;
        public SafetyManager safety;

        void Update()
        {
            if (Input.GetKeyDown(eStopKey))
            {
                safety?.TriggerEStop();
            }
        }
    }
}
