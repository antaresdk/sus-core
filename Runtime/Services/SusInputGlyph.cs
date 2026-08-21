using System;

namespace Sharq.Core
{
    /// <summary>
    /// Display strings for semantic input actions (default Xbox layout).
    /// Downstream UI packages bind prompts from <see cref="Resolve(SusInputActionId, SusInputDeviceKind)"/>
    /// and may install a skin provider via <see cref="SetProvider"/>.
    /// </summary>
    public static class SusInputGlyph
    {
        static ISusInputGlyphProvider s_provider;

        /// <summary>
        /// Registers a glyph provider. Pass null to restore built-in defaults.
        /// </summary>
        public static void SetProvider(ISusInputGlyphProvider provider) => s_provider = provider;

        /// <summary>Resolves a semantic action to a short display glyph for the given device kind.</summary>
        public static string Resolve(SusInputActionId id, SusInputDeviceKind kind)
        {
            if (s_provider != null && s_provider.TryResolve(id, kind, out var custom) && !string.IsNullOrEmpty(custom))
                return custom;
            return DefaultResolve(id, kind);
        }

        /// <summary>Resolves a custom string action id (skins / reserved actions).</summary>
        public static string Resolve(string customId, SusInputDeviceKind kind)
        {
            if (string.IsNullOrEmpty(customId))
                return string.Empty;

            if (s_provider != null && s_provider.TryResolve(customId, kind, out var custom) && !string.IsNullOrEmpty(custom))
                return custom;

            if (Enum.TryParse(customId, ignoreCase: true, out SusInputActionId id))
                return DefaultResolve(id, kind);

            return customId;
        }

        static string DefaultResolve(SusInputActionId id, SusInputDeviceKind kind)
        {
            switch (kind)
            {
                case SusInputDeviceKind.Gamepad:
                    switch (id)
                    {
                        case SusInputActionId.Submit: return "A";
                        case SusInputActionId.Cancel: return "B";
                        case SusInputActionId.Alt: return "X";
                        case SusInputActionId.Menu: return "Y";
                        case SusInputActionId.Navigate: return "Stick";
                        default: return id.ToString();
                    }
                case SusInputDeviceKind.Keyboard:
                    switch (id)
                    {
                        case SusInputActionId.Submit: return "Enter";
                        case SusInputActionId.Cancel: return "Esc";
                        case SusInputActionId.Alt: return "Alt";
                        case SusInputActionId.Menu: return "Menu";
                        case SusInputActionId.Navigate: return "Arrows";
                        default: return id.ToString();
                    }
                default: // Pointer
                    switch (id)
                    {
                        case SusInputActionId.Submit: return "Click";
                        case SusInputActionId.Cancel: return "RMB";
                        case SusInputActionId.Alt: return "MMB";
                        case SusInputActionId.Menu: return "Click";
                        case SusInputActionId.Navigate: return "Pointer";
                        default: return id.ToString();
                    }
            }
        }

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_provider = null;
#endif
    }
}
