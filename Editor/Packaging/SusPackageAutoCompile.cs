using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Auto-compiles <c>.sharq</c> files of every descriptor-declared package on save.
    ///
    /// Play-mode routing:
    ///   • style-only / template-only → <see cref="SharqBatchCompiler.SharqBatchMode.HotReloadSafe"/>
    ///     (no .g.cs write → no domain reload; raises SharqCompileEvents for live reload)
    ///   • script change → deferred until exit from Play, then full Generate
    /// Edit mode: full package Generate (existing behaviour).
    /// </summary>
    [InitializeOnLoad]
    public static class SusPackageAutoCompile
    {
        private static readonly List<FileSystemWatcher> s_watchers = new();
        /// <summary>packageName → absolute .sharq paths pending compile.</summary>
        private static readonly Dictionary<string, HashSet<string>> s_pendingFiles = new();
        /// <summary>Packages that need full Generate after Play (script changes).</summary>
        private static readonly HashSet<string> s_deferredFullPackages = new();
        /// <summary>Deleted class names per package (for OnUssDeleted).</summary>
        private static readonly Dictionary<string, HashSet<string>> s_pendingDeletes = new();
        private static readonly object s_lock = new();
        private static float s_debounceTimer;
        private static bool s_firstCheck = true;

        static SusPackageAutoCompile()
        {
            EditorApplication.update += OnUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeWatchers;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            StartWatchers();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;

            string[] deferred;
            lock (s_lock)
            {
                deferred = s_deferredFullPackages.ToArray();
                s_deferredFullPackages.Clear();
            }

            foreach (var packageName in deferred)
            {
                var d = SusPackageRegistry.Find(packageName);
                if (d == null) continue;
                Debug.Log($"[SusPackages] {d.displayName}: flushing deferred <script> changes after Play…");
                SusPackageGenerator.Generate(d);
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += () =>
            {
                if (!s_firstCheck) return;
                s_firstCheck = false;
                CheckFreshnessOnStartup();
            };
        }

        private static void CheckFreshnessOnStartup()
        {
            foreach (var d in SusPackageRegistry.Packages)
            {
                if (!IsStale(d)) continue;

                Debug.Log($"[SusPackages] {d.displayName}: .sharq sources newer than generated output — regenerating…");
                SusPackageGenerator.Generate(d);
            }
        }

        private static bool IsStale(SusPackageDescriptor d)
        {
            var genDir = d.AbsGeneratedDir;
            foreach (var srcDir in d.AbsSourceDirs)
            {
                foreach (var sharq in Directory.GetFiles(srcDir, "*.sharq", SearchOption.AllDirectories))
                {
                    var genCs = Path.Combine(genDir, Path.GetFileNameWithoutExtension(sharq) + ".g.cs");
                    if (!File.Exists(genCs)) return true;
                    if (File.GetLastWriteTimeUtc(sharq) > File.GetLastWriteTimeUtc(genCs)) return true;
                }
            }
            return false;
        }

        public static void RestartWatchers()
        {
            DisposeWatchers();
            SusPackageRegistry.Refresh();
            StartWatchers();
        }

        private static void StartWatchers()
        {
            foreach (var d in SusPackageRegistry.Packages)
            {
                if (!d.watch) continue;

                foreach (var srcDir in d.AbsSourceDirs)
                {
                    if (!Directory.Exists(srcDir)) continue;

                    try
                    {
                        var watcher = new FileSystemWatcher(srcDir, "*.sharq")
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                            EnableRaisingEvents = true
                        };

                        var packageName = d.PackageName;
                        watcher.Changed += (_, e) => OnSharqChanged(packageName, e.FullPath);
                        watcher.Created += (_, e) => OnSharqChanged(packageName, e.FullPath);
                        watcher.Deleted += (_, e) => OnSharqDeleted(packageName, e.FullPath);
                        watcher.Renamed += (_, e) =>
                        {
                            OnSharqDeleted(packageName, e.OldFullPath);
                            OnSharqChanged(packageName, e.FullPath);
                        };

                        s_watchers.Add(watcher);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"[SusPackages] {d.displayName}: failed to start watcher on '{srcDir}': {ex.Message}");
                    }
                }
            }
        }

        private static void DisposeWatchers()
        {
            foreach (var w in s_watchers)
                w.Dispose();
            s_watchers.Clear();
        }

        private static bool IsGeneratedPath(string fullPath)
        {
            var p = fullPath.Replace('\\', '/');
            return p.Contains("/Generated/") || p.Contains("/Resources/");
        }

        private static void OnSharqChanged(string packageName, string fullPath)
        {
            if (IsGeneratedPath(fullPath)) return;

            lock (s_lock)
            {
                if (!s_pendingFiles.TryGetValue(packageName, out var set))
                {
                    set = new HashSet<string>();
                    s_pendingFiles[packageName] = set;
                }
                set.Add(Path.GetFullPath(fullPath));
                s_debounceTimer = 0.5f;
            }
        }

        private static void OnSharqDeleted(string packageName, string fullPath)
        {
            if (IsGeneratedPath(fullPath)) return;

            var className = Path.GetFileNameWithoutExtension(fullPath);
            lock (s_lock)
            {
                if (!s_pendingDeletes.TryGetValue(packageName, out var set))
                {
                    set = new HashSet<string>();
                    s_pendingDeletes[packageName] = set;
                }
                set.Add(className);
                s_debounceTimer = 0.5f;
            }
        }

        private static void OnUpdate()
        {
            bool hasPending;
            lock (s_lock)
            {
                hasPending = s_pendingFiles.Count > 0 || s_pendingDeletes.Count > 0;
            }
            if (!hasPending) return;

            lock (s_lock)
            {
                s_debounceTimer -= Time.unscaledDeltaTime;
                if (s_debounceTimer > 0f) return;
            }

            Dictionary<string, string[]> files;
            Dictionary<string, string[]> deletes;
            lock (s_lock)
            {
                files = s_pendingFiles.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
                s_pendingFiles.Clear();
                deletes = s_pendingDeletes.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
                s_pendingDeletes.Clear();
            }

            var playing = EditorApplication.isPlayingOrWillChangePlaymode;

            foreach (var kv in deletes)
            {
                var d = SusPackageRegistry.Find(kv.Key);
                if (d == null) continue;
                foreach (var className in kv.Value)
                    CleanupDeletedComponent(d, className);
            }

            foreach (var kv in files)
            {
                var packageName = kv.Key;
                var d = SusPackageRegistry.Find(packageName);
                if (d == null) continue;

                if (!playing)
                {
                    // Edit mode: full package generate (stable, covers multi-file).
                    SusPackageGenerator.Generate(d);
                    continue;
                }

                // Play mode: per-file HotReloadSafe or defer script changes.
                var needsFullDefer = false;
                foreach (var sharqPath in kv.Value)
                {
                    if (!File.Exists(sharqPath))
                        continue;

                    var changed = SharqBatchCompiler.PeekChanges(sharqPath, d.AbsGeneratedDir);

                    if (changed.ScriptChanged)
                    {
                        needsFullDefer = true;
                        var className = SharqBatchCompiler.PeekClassName(sharqPath);
                        Debug.LogWarning(
                            $"[SusPackages] {className}: <script> changed during Play — deferred until exit (no domain reload mid-session).");
                        continue;
                    }

                    if (changed.OnlyStyle || (changed.TemplateChanged && !changed.ScriptChanged) || changed.StyleChanged)
                    {
                        SharqBatchCompiler.CompileFile(
                            sharqPath,
                            d.AbsGeneratedDir,
                            d.AbsResourcesDir,
                            SharqBatchCompiler.SharqBatchMode.HotReloadSafe,
                            d.AbsGeneratedDir,
                            log: true,
                            classNamespace: d.@namespace,
                            extraUsings: d.usings);
                    }
                }

                if (needsFullDefer)
                {
                    lock (s_lock)
                        s_deferredFullPackages.Add(packageName);
                }
            }
        }

        private static void CleanupDeletedComponent(SusPackageDescriptor d, string className)
        {
            if (string.IsNullOrEmpty(className)) return;

            string[] suffixes =
            {
                ".g.cs", ".g.uss", "_scoped.g.uss", "_static.g.uss",
                ".sections.json", ".sharq.hash"
            };

            foreach (var suf in suffixes)
            {
                var gen = Path.Combine(d.AbsGeneratedDir, className + suf);
                var res = string.IsNullOrEmpty(d.AbsResourcesDir)
                    ? null
                    : Path.Combine(d.AbsResourcesDir, className + suf);
                try
                {
                    if (File.Exists(gen)) File.Delete(gen);
                    if (res != null && File.Exists(res)) File.Delete(res);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[SusPackages] Cleanup {className}{suf}: {ex.Message}");
                }
            }

            SharqSectionCache.Clear(className, d.AbsGeneratedDir);
            SharqCompileEvents.RaiseUssDeleted(className);
            Debug.Log($"[SusPackages] Deleted generated artifacts for {className}");
        }
    }
}
