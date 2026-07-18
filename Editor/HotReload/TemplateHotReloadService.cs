#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Editor-only template hot reload service (E3).
    /// Subscribes to <see cref="SharqCompileEvents.OnTemplateChanged"/> and attempts
    /// an in-place tree rebuild of all active instances of the changed component using
    /// <see cref="SharqTemplateInterpreter"/>.
    ///
    /// Triggered when &lt;template&gt; changes but &lt;script&gt; does not — the interpreter
    /// can handle the rebuild without a domain reload (~0 ms vs 5-30 s).
    ///
    /// On fallback (event binding, v-for, complex expression) the service leaves the
    /// component intact and logs a diagnostic; normal .g.cs regeneration + domain reload
    /// will pick it up on next script edit or manual reimport.
    ///
    /// Cycle:
    /// .sharq saved (template only) → SharqFileImporter → SharqCompileEvents.OnTemplateChanged
    ///   → TemplateHotReloadService (debounced) → SharqTemplateInterpreter.TryApply (per instance)
    ///   → if ok: UI rebuilt ~1-2 s, no domain reload
    ///   → if fallback: component unchanged, log "needs recompile" (expected for complex templates)
    /// </summary>
    [InitializeOnLoad]
    public static class TemplateHotReloadService
    {
        // className → template XML; accumulated during debounce window
        private static readonly Dictionary<string, string> Pending = new();
        private static double _nextProcessTime;
        private const double DebounceSeconds = 0.25;

        static TemplateHotReloadService()
        {
            SharqCompileEvents.OnTemplateChanged += OnTemplateChanged;
        }

        private static void OnTemplateChanged(string className, string templateXml)
        {
            if (string.IsNullOrEmpty(className)) return;
            Pending[className] = templateXml; // last write wins within debounce window
            _nextProcessTime = EditorApplication.timeSinceStartup + DebounceSeconds;
            EditorApplication.update -= ProcessPending;
            EditorApplication.update += ProcessPending;
        }

        private static void ProcessPending()
        {
            if (EditorApplication.timeSinceStartup < _nextProcessTime) return;
            EditorApplication.update -= ProcessPending;
            if (Pending.Count == 0) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var applied = 0;
            var fallback = 0;
            var fallbackReasons = new List<string>();

            ForEachLiveRoot(root =>
            {
                var comps = root.Query<SusComponent>().Build().ToList();
                foreach (var comp in comps)
                {
                    var name = comp.GetType().Name;
                    if (!Pending.TryGetValue(name, out var xml)) continue;

                    if (SharqTemplateInterpreter.TryApply(comp, xml))
                        applied++;
                    else
                    {
                        fallback++;
                        fallbackReasons.Add(name);
                    }
                }
            });

            sw.Stop();

            if (applied > 0)
                Debug.Log($"[TemplateHotReload] ✓ {applied} instance(s) rebuilt in {sw.ElapsedMilliseconds}ms: " +
                          string.Join(", ", Pending.Keys));

            if (fallback > 0)
                Debug.Log($"[TemplateHotReload] {fallback} instance(s) need full recompile " +
                          $"(complex template): {string.Join(", ", fallbackReasons)}. " +
                          "Waiting for next .g.cs regeneration.");

            Pending.Clear();
        }

        private static void ForEachLiveRoot(Action<VisualElement> visit)
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            foreach (var doc in docs)
            {
                if (doc.rootVisualElement != null)
                    visit(doc.rootVisualElement);
            }

            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var w in windows)
            {
                if (w.rootVisualElement != null)
                    visit(w.rootVisualElement);
            }
        }
    }
}
#endif
