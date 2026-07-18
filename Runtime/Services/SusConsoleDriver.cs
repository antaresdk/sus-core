using UnityEngine;

namespace Sharq.Core
{
    /// <summary>
    /// Driver MonoBehaviour for SusConsoleService — polls hotkey and drains
    /// the thread-safe log queue into the main-thread buffer.
    /// Created automatically by SusConsoleService.Attach().
    /// </summary>
    public class SusConsoleDriver : MonoBehaviour
    {
        public SusConsoleService Service;

        private void Update()
        {
            if (Service == null) return;

            // Poll toggle key (detect on key down, not during input field focus)
            if (Input.GetKeyDown(Service.ToggleKey) && !IsInputFocused())
                Service.Toggle();

            // Drain background-thread log entries into main-thread buffer
            Service.DrainPendingEntries();
        }

        private static bool IsInputFocused()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Don't toggle when typing in a TextField
            var focused = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (focused != null)
            {
                var input = focused.GetComponent<UnityEngine.UI.InputField>();
                if (input != null && input.isFocused) return true;
            }
#endif
            return false;
        }

        private void OnDestroy()
        {
            Service?.Detach();
        }
    }
}
