#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Editor-only USS hot reload service.
    /// Subscribes to <see cref="SharqCompileEvents"/> (published by BOTH generation
    /// contours — project <see cref="SharqFileImporter"/> and package
    /// <see cref="SharqBatchCompiler"/>) and pushes updated stylesheets to all
    /// active SusComponents without Domain Reload.
    ///
    /// Cycle:
    /// .sharq saved (only &lt;style&gt; changed)
    ///   → importer/batch regenerates .g.uss
    ///   → SharqCompileEvents.OnUssGenerated(className, ussPaths)
    ///   → UssHotReloadService catches the event
    ///   → AssetDatabase.ImportAsset (Generated + Resources mirror)
    ///   → SusComponent.ReloadCompanionStyleSheets(onlySuffixes) on all active
    ///   → styles are visible in Game View for &lt; 1 seconds
    ///
    /// Partial replace: only those companion shields whose files are reloaded
    /// actually arrived in the event (for example, only "_scoped.g"), the rest are not touched.
    /// OnUssDeleted → removing stale shields from living components.
    /// </summary>
    [InitializeOnLoad]
    public static class UssHotReloadService
    {
        // className → modified suffixes ("_static.g" / "_scoped.g" / ".g"); null value = all.
        private static readonly Dictionary<string, HashSet<string>> PendingUssReloads = new();
        private static readonly HashSet<string> PendingUssRemovals = new();
        private static double _nextProcessTime;
        private const double DebounceSeconds = 0.2;

        static UssHotReloadService()
        {
            SharqCompileEvents.OnUssGenerated += OnUssGenerated;
            SharqCompileEvents.OnUssDeleted += OnUssDeleted;
        }

        private static void OnUssGenerated(string className, string[] ussPaths)
        {
            if (string.IsNullOrEmpty(className)) return;

            if (!PendingUssReloads.TryGetValue(className, out var suffixes) || suffixes == null)
                PendingUssReloads[className] = suffixes = new HashSet<string>();

            foreach (var path in ussPaths ?? Array.Empty<string>())
            {
                var suf = SuffixOf(className, path);
                if (suf != null) suffixes.Add(suf);
            }

            Schedule();
        }

        private static void OnUssDeleted(string className)
        {
            if (string.IsNullOrEmpty(className)) return;
            PendingUssRemovals.Add(className);
            PendingUssReloads.Remove(className);
            Schedule();
        }

        private static void Schedule()
        {
            _nextProcessTime = EditorApplication.timeSinceStartup + DebounceSeconds;
            EditorApplication.update -= ProcessPending;
            EditorApplication.update += ProcessPending;
        }

        /// <summary>Maps an absolute uss path to its companion suffix ("_scoped.g" etc.), or null.</summary>
        private static string SuffixOf(string className, string ussPath)
        {
            if (string.IsNullOrEmpty(ussPath)) return null;
            var file = System.IO.Path.GetFileName(ussPath);
            if (file == $"{className}_static.g.uss") return "_static.g";
            if (file == $"{className}_scoped.g.uss") return "_scoped.g";
            if (file == $"{className}.g.uss") return ".g";
            return null;
        }

        private static void ProcessPending()
        {
            if (EditorApplication.timeSinceStartup < _nextProcessTime)
                return;

            EditorApplication.update -= ProcessPending;

            if (PendingUssReloads.Count == 0 && PendingUssRemovals.Count == 0) return;

            // Force Unity to re-import USS assets so Resources.Load picks up new content
            foreach (var kv in PendingUssReloads)
            {
                foreach (var assetPath in FindUssAssetPaths(kv.Key, kv.Value))
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reloaded = 0;
            var removed = 0;

            ForEachLiveRoot(root => ApplyToTree(root, ref reloaded, ref removed));

            sw.Stop();
            var parts = new List<string>();
            if (PendingUssReloads.Count > 0)
                parts.Add($"{reloaded} reloaded ({string.Join(", ", PendingUssReloads.Keys)})");
            if (PendingUssRemovals.Count > 0)
                parts.Add($"{removed} stale-removed ({string.Join(", ", PendingUssRemovals)})");
            Debug.Log($"[UssHotReload] \u2713 {string.Join("; ", parts)} in {sw.ElapsedMilliseconds}ms");

            PendingUssReloads.Clear();
            PendingUssRemovals.Clear();
        }

        private static void ForEachLiveRoot(Action<VisualElement> visit)
        {
            // Scene: all UIDocuments
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(UnityEngine.FindObjectsSortMode.None);
            foreach (var doc in docs)
            {
                if (doc.rootVisualElement != null)
                    visit(doc.rootVisualElement);
            }

            // EditorWindow components
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in windows)
            {
                var root = window.rootVisualElement;
                if (root != null)
                    visit(root);
            }
        }

        private static void ApplyToTree(VisualElement root, ref int reloaded, ref int removed)
        {
            var allComponents = root.Query<SusComponent>().Build().ToList();
            foreach (var component in allComponents)
            {
                var name = component.GetType().Name;

                if (PendingUssReloads.TryGetValue(name, out var suffixes))
                {
                    component.ReloadCompanionStyleSheets(suffixes.Count > 0 ? suffixes : null);
                    reloaded++;
                }
                else if (PendingUssRemovals.Contains(name))
                {
                    component.RemoveCompanionStyleSheets();
                    component.MarkDirtyRepaint();
                    removed++;
                }
            }
        }

        /// <summary>
        /// Resolves AssetDatabase paths of the changed companion .uss files for a class:
        /// project Generated dir (sus.config.json) and every package with a
        /// sharq.gen.json descriptor (<see cref="SusPackageRegistry"/>) — both the
        /// Generated dir and the Resources mirror.
        /// </summary>
        private static IEnumerable<string> FindUssAssetPaths(string className, HashSet<string> suffixes)
        {
            var fileNames = new List<string>();
            foreach (var suf in (suffixes != null && suffixes.Count > 0)
                         ? (IEnumerable<string>)suffixes
                         : new[] { "_static.g", "_scoped.g", ".g" })
                fileNames.Add($"{className}{suf}.uss");

            // Project contour (relative Assets/ paths from sus.config.json)
            var config = SusConfig.Instance;
            if (config != null)
            {
                foreach (var dir in new[] { config.GeneratedDirectory, config.ResourcesDirectory })
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    foreach (var fileName in fileNames)
                    {
                        var p = $"{dir.TrimEnd('/', '\\')}/{fileName}".Replace('\\', '/');
                        if (System.IO.File.Exists(p))
                            yield return p;
                    }
                }
            }

            // Package contour (descriptor dirs are absolute → convert to Packages/<name>/… )
            foreach (var d in SusPackageRegistry.Packages)
            {
                foreach (var absDir in new[] { d.AbsGeneratedDir, d.AbsResourcesDir })
                {
                    if (string.IsNullOrEmpty(absDir)) continue;
                    foreach (var fileName in fileNames)
                    {
                        var absPath = $"{absDir.TrimEnd('/')}/{fileName}";
                        if (!System.IO.File.Exists(absPath)) continue;

                        var root = d.PackageRoot.TrimEnd('/');
                        if (absPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            yield return $"Packages/{d.PackageName}{absPath.Substring(root.Length)}";
                    }
                }
            }
        }
    }
}
#endif
