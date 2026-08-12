using UnityEngine;

namespace Sharq.Core
{
    /// <summary>
    /// Auto-registers the <see cref="PhosphorIconProvider"/> with <see cref="SusIconRegistry"/>.
    /// Appended (NOT highest priority) so the minimal built-in
    /// <c>CoreIconProvider</c> and any project-registered providers keep precedence for
    /// overlapping names, while Phosphor supplies the long tail (~9000 icons) for projects
    /// that imported the optional <c>PhosphorIcons</c> sample.
    ///
    /// Runs both at runtime startup and on editor load, so icons resolve in play mode,
    /// edit-mode tooling and tests. Idempotent (RegisterProvider dedups by instance).
    /// </summary>
    public static class PhosphorIconBootstrap
    {
        private static PhosphorIconProvider s_provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register()
        {
            if (s_provider != null) return;
            s_provider = new PhosphorIconProvider();
            SusIconRegistry.RegisterProvider(s_provider, asHighestPriority: false);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void EditorRegister() => Register();
#endif
    }
}
