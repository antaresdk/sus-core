using System;
using UnityEngine;

namespace Sharq.Core
{
    /// <summary>
    /// Tracks the last-active input device family and applies cursor / hover policy.
    /// Installed idempotently from <see cref="SusBootstrap.EnsureEventSystem"/>.
    /// </summary>
    public static class SusInputDevice
    {
        const string DriverObjectName = "__SusInputDevice__";

        static SusInputDeviceKind s_activeKind = SusInputDeviceKind.Pointer;
        static bool s_autoHideCursor = true;
        static bool s_installed;
        static bool s_cursorHiddenByUs;
        static bool s_savedCursorVisible = true;
        static SusInputDeviceDriver s_driver;

        // Cached Input System reflection (no hard package reference — R25 / asmdef).
        static bool s_inputSystemProbed;
        static bool s_inputSystemAvailable;
        static Type s_keyboardType;
        static Type s_mouseType;
        static Type s_pointerType;
        static Type s_gamepadType;
        static Type s_touchscreenType;
        /// <summary>Last device family that produced activity.</summary>
        public static SusInputDeviceKind ActiveKind => s_activeKind;

        /// <summary>
        /// Fired after <see cref="ActiveKind"/> changes. Args: previous, next.
        /// </summary>
        public static event Action<SusInputDeviceKind, SusInputDeviceKind> Changed;

        /// <summary>
        /// When true (default), Cursor.visible is forced off for Keyboard/Gamepad
        /// and restored when returning to Pointer. Does not touch lockState.
        /// </summary>
        public static bool AutoHideCursor
        {
            get => s_autoHideCursor;
            set
            {
                if (s_autoHideCursor == value) return;
                s_autoHideCursor = value;
                ApplyCursorPolicy();
            }
        }

        /// <summary>
        /// True while the active kind is Pointer — juice / parallax / tooltip-delay
        /// should gate pointer hover on this flag rather than hardcoding device checks.
        /// </summary>
        public static bool AllowsPointerHover => s_activeKind == SusInputDeviceKind.Pointer;

        /// <summary>
        /// Records activity of the given kind (tests, remappers, synthetic input).
        /// No-op when kind equals the current <see cref="ActiveKind"/>.
        /// </summary>
        public static void NotifyActivity(SusInputDeviceKind kind)
        {
            if (kind == s_activeKind) return;

            var prev = s_activeKind;
            s_activeKind = kind;
            ApplyCursorPolicy();
            Changed?.Invoke(prev, kind);
        }

        /// <summary>
        /// Creates the poll driver and (when available) hooks Input System device
        /// activity. Idempotent. Called from <see cref="SusBootstrap.EnsureEventSystem"/>.
        /// </summary>
        public static void EnsureInstalled()
        {
            if (s_installed && s_driver != null)
                return;

            EnsureDriver();
            TryHookInputSystem();
            s_installed = true;
            ApplyCursorPolicy();
        }

        internal static void PollLegacy()
        {
            // Prefer Input System classification when the package is present.
            if (TryClassifyFromInputSystem(out var fromIs))
            {
                NotifyActivity(fromIs);
                return;
            }

            if (TryClassifyLegacy(out var legacy))
                NotifyActivity(legacy);
        }

        static void EnsureDriver()
        {
            if (s_driver != null) return;

            var existing = GameObject.Find(DriverObjectName);
            if (existing != null)
            {
                s_driver = existing.GetComponent<SusInputDeviceDriver>();
                if (s_driver == null)
                    s_driver = existing.AddComponent<SusInputDeviceDriver>();
                return;
            }

            var go = new GameObject(DriverObjectName);
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            s_driver = go.AddComponent<SusInputDeviceDriver>();
        }

        static void ApplyCursorPolicy()
        {
            if (!s_autoHideCursor)
            {
                if (s_cursorHiddenByUs)
                {
                    Cursor.visible = s_savedCursorVisible;
                    s_cursorHiddenByUs = false;
                }
                return;
            }

            if (s_activeKind == SusInputDeviceKind.Pointer)
            {
                if (s_cursorHiddenByUs)
                {
                    Cursor.visible = s_savedCursorVisible;
                    s_cursorHiddenByUs = false;
                }
            }
            else
            {
                if (!s_cursorHiddenByUs)
                {
                    s_savedCursorVisible = Cursor.visible;
                    Cursor.visible = false;
                    s_cursorHiddenByUs = true;
                }
            }
        }

        static void ProbeInputSystem()
        {
            if (s_inputSystemProbed) return;
            s_inputSystemProbed = true;

            var inputSystemType = Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
            if (inputSystemType == null)
            {
                s_inputSystemAvailable = false;
                return;
            }

            s_keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
            s_mouseType = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
            s_pointerType = Type.GetType("UnityEngine.InputSystem.Pointer, Unity.InputSystem");
            s_gamepadType = Type.GetType("UnityEngine.InputSystem.Gamepad, Unity.InputSystem");
            s_touchscreenType = Type.GetType("UnityEngine.InputSystem.Touchscreen, Unity.InputSystem");

            s_inputSystemAvailable = s_keyboardType != null || s_gamepadType != null || s_mouseType != null;
        }

        static void TryHookInputSystem()
        {
            // Polling via driver is enough; high-frequency onEvent is intentionally unused.
            ProbeInputSystem();
        }

        static bool TryClassifyFromInputSystem(out SusInputDeviceKind kind)
        {
            kind = SusInputDeviceKind.Pointer;
            ProbeInputSystem();
            if (!s_inputSystemAvailable) return false;

            // Any recent button / stick on gamepad?
            if (DeviceWasUpdatedRecently(s_gamepadType, out _) ||
                DeviceWasUpdatedRecently(Type.GetType("UnityEngine.InputSystem.Joystick, Unity.InputSystem"), out _))
            {
                kind = SusInputDeviceKind.Gamepad;
                return true;
            }

            if (DeviceWasUpdatedRecently(s_keyboardType, out _))
            {
                kind = SusInputDeviceKind.Keyboard;
                return true;
            }

            if (DeviceWasUpdatedRecently(s_mouseType, out _) ||
                DeviceWasUpdatedRecently(s_touchscreenType, out _) ||
                DeviceWasUpdatedRecently(s_pointerType, out _))
            {
                kind = SusInputDeviceKind.Pointer;
                return true;
            }

            return false;
        }

        static bool DeviceWasUpdatedRecently(Type deviceType, out object device)
        {
            device = null;
            if (deviceType == null) return false;

            var currentProp = deviceType.GetProperty("current",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            device = currentProp?.GetValue(null);
            if (device == null) return false;

            // Check wasUpdatedThisFrame / wasUpdatedThisFrame-like members.
            var updatedProp = device.GetType().GetProperty("wasUpdatedThisFrame");
            if (updatedProp != null && updatedProp.PropertyType == typeof(bool))
                return (bool)updatedProp.GetValue(device);

            // Fallback: check allControls for any isPressed / wasPressedThisFrame via reflection is heavy —
            // use lastUpdateTime vs current time when available.
            var lastUpdateProp = device.GetType().GetProperty("lastUpdateTime");
            if (lastUpdateProp != null)
            {
                var last = lastUpdateProp.GetValue(device);
                if (last is double d)
                {
                    // InputSystem time is in seconds (same clock as Time.realtimeSinceStartupAsDouble when available).
                    var now = (double)Time.realtimeSinceStartup;
                    return (now - d) < 0.05;
                }
            }

            return false;
        }

        static bool TryClassifyLegacy(out SusInputDeviceKind kind)
        {
            kind = SusInputDeviceKind.Pointer;

            // Mouse / touch movement or buttons.
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) ||
                Math.Abs(Input.GetAxisRaw("Mouse X")) > 0.01f ||
                Math.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f ||
                Input.touchCount > 0)
            {
                kind = SusInputDeviceKind.Pointer;
                return true;
            }

            // Joystick / gamepad axes & buttons (legacy).
            var pads = Input.GetJoystickNames();
            bool anyPad = false;
            if (pads != null)
            {
                for (int i = 0; i < pads.Length; i++)
                {
                    if (!string.IsNullOrEmpty(pads[i]))
                    {
                        anyPad = true;
                        break;
                    }
                }
            }

            if (anyPad)
            {
                for (int b = 0; b < 20; b++)
                {
                    try
                    {
                        if (Input.GetKey((KeyCode)((int)KeyCode.JoystickButton0 + b)))
                        {
                            kind = SusInputDeviceKind.Gamepad;
                            return true;
                        }
                    }
                    catch (ArgumentException)
                    {
                        break;
                    }
                }

                try
                {
                    if (Math.Abs(Input.GetAxisRaw("Horizontal")) > 0.25f ||
                        Math.Abs(Input.GetAxisRaw("Vertical")) > 0.25f)
                    {
                        // Ambiguous with keyboard WASD on the same axes — prefer keyboard if a key is down.
                        if (!KeyboardKeyDown())
                        {
                            kind = SusInputDeviceKind.Gamepad;
                            return true;
                        }
                    }
                }
                catch (ArgumentException)
                {
                    // Axis not defined in Input Manager — ignore.
                }
            }

            if (KeyboardKeyDown())
            {
                kind = SusInputDeviceKind.Keyboard;
                return true;
            }

            return false;
        }

        static bool KeyboardKeyDown()
        {
            if (!Input.anyKey) return false;
            // anyKey includes mouse buttons — exclude them.
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
                return false;
            return Input.inputString.Length > 0 ||
                   Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) ||
                   Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) ||
                   Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Escape) ||
                   Input.GetKey(KeyCode.Tab) || Input.GetKey(KeyCode.Space) ||
                   Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                   Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                   Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
                   Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                   Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            s_activeKind = SusInputDeviceKind.Pointer;
            s_autoHideCursor = true;
            s_installed = false;
            s_cursorHiddenByUs = false;
            s_savedCursorVisible = true;
            s_driver = null;
            s_inputSystemProbed = false;
            s_inputSystemAvailable = false;
            s_keyboardType = null;
            s_mouseType = null;
            s_pointerType = null;
            s_gamepadType = null;
            s_touchscreenType = null;
        }
#endif
    }

    /// <summary>Polls legacy / Input System device activity each frame.</summary>
    [AddComponentMenu("")]
    sealed class SusInputDeviceDriver : MonoBehaviour
    {
        void Update() => SusInputDevice.PollLegacy();
    }
}
