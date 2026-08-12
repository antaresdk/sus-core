#if UNITY_EDITOR || DEVELOPMENT_BUILD || SUS_RUNTIME_MCP
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Runtime hot-reload receiver (E4): apply USS text / template XML to live SusComponents.
    /// Available in Editor and DEVELOPMENT_BUILD only.
    ///
    /// USS-from-text requires a StyleSheet factory (registered by Editor via
    /// <see cref="StyleSheetFromUss"/>). Standalone players without the factory still
    /// support <see cref="ApplyTemplate"/> only — <c>ui.hotreload.uss</c> is Editor-scoped.
    /// </summary>
    public static class SusRuntimeHotReload
    {
        /// <summary>
        /// Optional factory: USS text → StyleSheet. Editor registers
        /// <c>StyleSheetImporterImpl</c>-based implementation on load.
        /// </summary>
        public static Func<string, string, StyleSheet> StyleSheetFromUss;

        /// <summary>
        /// Apply USS text to all live instances of <paramref name="className"/>.
        /// <paramref name="suffix"/> is companion suffix: ".g", "_scoped.g", "_static.g".
        /// Returns number of components updated.
        /// </summary>
        public static int ApplyUss(string className, string suffix, string ussText)
        {
            if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(ussText))
                return 0;

            if (string.IsNullOrEmpty(suffix))
                suffix = ".g";

            if (StyleSheetFromUss == null)
            {
                SusLog.Warn(
                    "[SusRuntimeHotReload] ApplyUss: no StyleSheetFromUss factory " +
                    "(Editor registers it; player builds cannot parse USS without it).");
                return 0;
            }

            StyleSheet sheet;
            try
            {
                sheet = StyleSheetFromUss(ussText, $"{className}{suffix}");
            }
            catch (Exception ex)
            {
                SusLog.Error($"[SusRuntimeHotReload] StyleSheet parse failed: {ex.Message}");
                return 0;
            }

            if (sheet == null) return 0;

            var count = 0;
            ForEachLiveComponent(className, comp =>
            {
                comp.ApplyHotReloadStyleSheet(suffix, sheet);
                count++;
            });

            SusLog.Verbose($"[SusRuntimeHotReload] ApplyUss {className}{suffix} → {count} instance(s)");
            return count;
        }

        /// <summary>
        /// Apply template XML via <see cref="SharqTemplateInterpreter.TryApply"/> to all
        /// live instances of <paramref name="className"/>.
        /// Returns (applied, fallback) counts.
        /// </summary>
        public static (int applied, int fallback) ApplyTemplate(string className, string templateXml)
        {
            if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(templateXml))
                return (0, 0);

            var applied = 0;
            var fallback = 0;
            ForEachLiveComponent(className, comp =>
            {
                if (SharqTemplateInterpreter.TryApply(comp, templateXml))
                    applied++;
                else
                    fallback++;
            });

            SusLog.Verbose(
                $"[SusRuntimeHotReload] ApplyTemplate {className} → applied={applied} fallback={fallback}");
            return (applied, fallback);
        }

        /// <summary>Visit every SusComponent in active UIDocuments (+ EditorWindows in Editor).</summary>
        public static void ForEachLiveComponent(string className, Action<SusComponent> visit)
        {
            if (visit == null) return;

            foreach (var root in EnumerateLiveRoots())
            {
                if (root == null) continue;
                root.Query<SusComponent>().ForEach(comp =>
                {
                    if (comp != null && comp.GetType().Name == className)
                        visit(comp);
                });
            }
        }

        public static IEnumerable<VisualElement> EnumerateLiveRoots()
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            foreach (var doc in docs)
            {
                if (doc != null && doc.rootVisualElement != null)
                    yield return doc.rootVisualElement;
            }

#if UNITY_EDITOR
            foreach (var window in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
            {
                if (window?.rootVisualElement != null)
                    yield return window.rootVisualElement;
            }
#endif
        }
    }
}
#endif
