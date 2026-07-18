using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Auto-generates C# partial classes from .sharq files on save.
    /// Watches SusConfig.SharqDirectory/**/*.sharq and writes generated
    /// files to SusConfig.GeneratedDirectory (independent from the sources).
    /// </summary>
    public class SharqFileImporter : AssetPostprocessor
    {
        private static string GeneratedDir => SusConfig.Instance.GeneratedDirectory;
        private static string SharqDir => SusConfig.Instance.SharqDirectory;

        /// <summary>
        /// Fired when USS files for a component are regenerated (style-only change).
        /// className: e.g. "SusTabs"
        /// ussPaths: absolute paths to generated .uss files
        /// Legacy per-contour event; prefer <see cref="SharqCompileEvents.OnUssGenerated"/>,
        /// which covers the package contour (SharqBatchCompiler) as well.
        /// </summary>
        public static event Action<string, string[]> OnUssGenerated;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // ─── Handle deleted .sharq: remove all generated artifacts ────
            foreach (var asset in deletedAssets)
            {
                if (!asset.EndsWith(".sharq") || !asset.StartsWith(SharqDir))
                    continue;

                var className = Path.GetFileNameWithoutExtension(asset);
                CleanupGeneratedFiles(className);
                if (SusConfig.Instance.LogGeneratedFiles)
                    UnityEngine.Debug.Log($"[Sharq] Cleaned up generated files for deleted {className}.sharq");
            }

            // ─── Handle moved/renamed .sharq: remove old artifacts ────────
            for (int i = 0; i < movedAssets.Length; i++)
            {
                if (!movedAssets[i].EndsWith(".sharq") || !movedAssets[i].StartsWith(SharqDir))
                    continue;

                var oldClassName = Path.GetFileNameWithoutExtension(movedFromAssetPaths[i]);
                var newClassName = Path.GetFileNameWithoutExtension(movedAssets[i]);

                if (oldClassName != newClassName)
                {
                    CleanupGeneratedFiles(oldClassName);
                    if (SusConfig.Instance.LogGeneratedFiles)
                        UnityEngine.Debug.Log($"[Sharq] Cleaned up generated files for renamed {oldClassName}.sharq → {newClassName}.sharq");
                }
            }

            // ─── Handle new/changed .sharq ────────────────────────────────
            foreach (var asset in importedAssets)
            {
                if (!asset.EndsWith(".sharq") || !asset.StartsWith(SharqDir))
                    continue;

                ProcessSharq(asset);
            }
        }

        [MenuItem("Window/SUS/Sharq/Regenerate All Prototype Components", false, 203)]
        public static void RegenerateAll()
        {
            if (!Directory.Exists(SharqDir)) return;

            foreach (var file in Directory.GetFiles(SharqDir, "*.sharq", SearchOption.AllDirectories))
            {
                if (IsUnderGenerated(file)) continue;
                ProcessSharq(file);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// True if <paramref name="path"/> lives inside the configured generated directory.
        /// Robust to the generated folder being independent from or nested within the sources.
        /// </summary>
        internal static bool IsUnderGenerated(string path)
        {
            var genFull = Path.GetFullPath(GeneratedDir).Replace("\\", "/").TrimEnd('/') + "/";
            var full = Path.GetFullPath(path).Replace("\\", "/");
            return full.StartsWith(genFull, System.StringComparison.OrdinalIgnoreCase);
        }

        private static void ProcessSharq(string assetPath)
        {
            var fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath)) return;

            var sharqContent = File.ReadAllText(fullPath);

            // Content hash check: skip regeneration if unchanged
            var hash = ComputeHash(sharqContent);
            if (IsHashMatch(assetPath, hash))
            {
                if (SusConfig.Instance.LogGeneratedFiles)
                    UnityEngine.Debug.Log($"[Sharq] Skipped {Path.GetFileName(assetPath)} — content unchanged");
                return;
            }

            var model = SharqFileParser.Parse(sharqContent, fullPath);

            if (string.IsNullOrEmpty(model.TemplateXml))
            {
                UnityEngine.Debug.LogWarning($"[Sharq] No <template> in {Path.GetFileName(assetPath)}");
                return;
            }

            // ─── Section-level diff for incremental regeneration ─────
            var changed = SharqSectionCache.WhatChanged(
                model.ClassName,
                model.TemplateXml,
                model.ScriptBody,
                model.StyleBody);

            // ─── Validation ──────────────────────────────────────────
            var messages = SharqValidator.Validate(model);
            if (messages.Count > 0)
                SharqValidator.LogMessages(model.ClassName, messages);

            Directory.CreateDirectory(GeneratedDir);

            // Generate all artifacts once (shared pipeline); write incrementally below.
            var artifacts = SharqCompilePipeline.Generate(model);

            // Only regenerate .g.cs if template or script changed
            if (changed.TemplateChanged || changed.ScriptChanged)
            {
                var outputPath = Path.Combine(GeneratedDir, $"{model.ClassName}.g.cs");
                File.WriteAllText(outputPath, artifacts.Code, Encoding.UTF8);
                if (SusConfig.Instance.LogGeneratedFiles)
                    UnityEngine.Debug.Log($"[Sharq] Generated {model.ClassName}.g.cs");

                // ─── Write generated static styles to USS (or delete stale) ──
                var staticUssPath = Path.Combine(GeneratedDir, $"{model.ClassName}_static.g.uss");
                SharqCompilePipeline.WriteOrDelete(staticUssPath, artifacts.StaticUss);
                if (artifacts.StaticUss != null && SusConfig.Instance.LogGeneratedFiles)
                    UnityEngine.Debug.Log($"[Sharq] Generated {model.ClassName}_static.g.uss ({artifacts.StaticRuleCount} rules)");
            }

            // Only regenerate .uss if style changed (hot reload)
            if (changed.StyleChanged)
            {
                if (artifacts.ScopedUss != null)
                {
                    var ussPath = Path.Combine(GeneratedDir, $"{model.ClassName}_scoped.g.uss");
                    File.WriteAllText(ussPath, artifacts.ScopedUss, Encoding.UTF8);
                    if (SusConfig.Instance.LogGeneratedFiles)
                        UnityEngine.Debug.Log($"[Sharq] USS hot-reloaded: {model.ClassName}_scoped.g.uss");
                }
                if (artifacts.GlobalUss != null)
                {
                    var globalUssPath = Path.Combine(GeneratedDir, $"{model.ClassName}.g.uss");
                    File.WriteAllText(globalUssPath, artifacts.GlobalUss, Encoding.UTF8);
                    if (SusConfig.Instance.LogGeneratedFiles)
                        UnityEngine.Debug.Log($"[Sharq] USS hot-reloaded: {model.ClassName}.g.uss");
                }
            }

            // Store section hashes for next diff
            SharqSectionCache.StoreHashes(model.ClassName, changed.NewHashes);

            // Store content hash
            WriteHash(assetPath, hash);

            // ─── Copy USS to Resources for runtime loading ──────
            SharqCompilePipeline.SyncUssToResources(model.ClassName, GeneratedDir, ResourcesDir);

            // ─── USS hot reload notification ──────────────────────
            if (changed.StyleChanged)
            {
                var ussPaths = new System.Collections.Generic.List<string>();
                var genDir = GeneratedDir;
                if (File.Exists(Path.Combine(genDir, $"{model.ClassName}.g.uss")))
                    ussPaths.Add(Path.Combine(genDir, $"{model.ClassName}.g.uss"));
                if (File.Exists(Path.Combine(genDir, $"{model.ClassName}_scoped.g.uss")))
                    ussPaths.Add(Path.Combine(genDir, $"{model.ClassName}_scoped.g.uss"));
                if (File.Exists(Path.Combine(genDir, $"{model.ClassName}_static.g.uss")))
                    ussPaths.Add(Path.Combine(genDir, $"{model.ClassName}_static.g.uss"));

                if (ussPaths.Count > 0)
                {
                    var arr = ussPaths.ToArray();
                    OnUssGenerated?.Invoke(model.ClassName, arr);
                    SharqCompileEvents.RaiseUssGenerated(model.ClassName, arr);
                }
            }

            // ─── Template hot reload notification ─────────────────
            // Only when template changed but script didn't — safe to interpret in-place.
            if (changed.TemplateChanged && !changed.ScriptChanged)
            {
                SharqCompileEvents.RaiseTemplateChanged(model.ClassName, model.TemplateXml);
            }
        }

        /// <summary>
        /// Removes ALL generated artifacts for a deleted/renamed .sharq component.
        /// Mirrors Vue's behaviour: removing a .vue file removes its compiled output.
        /// </summary>
        internal static void CleanupGeneratedFiles(string className)
        {
            // ─── 1. Remove from generated/ directory ─────────────────
            string[] generatedSuffixes = { ".g.cs", ".g.uss", "_scoped.g.uss", "_static.g.uss", ".sections.json", ".sharq.hash" };
            foreach (var suffix in generatedSuffixes)
            {
                var genPath = Path.Combine(GeneratedDir, $"{className}{suffix}");
                DeleteFileAndMeta(genPath);
            }

            // ─── 2. Remove from Resources/SusRuntime/ ──────────────
            string[] resourceSuffixes = { ".g.uss", "_scoped.g.uss", "_static.g.uss" };
            foreach (var suffix in resourceSuffixes)
            {
                var resPath = Path.Combine(ResourcesDir, $"{className}{suffix}");
                DeleteFileAndMeta(resPath);
            }

            // ─── 3. Remove section cache entry ───────────────────────
            SharqSectionCache.Clear(className);

            // ─── 4. Notify subscribers (hot reload drops stale sheets) ──
            SharqCompileEvents.RaiseUssDeleted(className);
        }

        private static void DeleteFileAndMeta(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                var metaPath = fullPath + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
            }
        }
        // ─── Resources sync (for runtime USS loading) ────────────────
        // Runtime loads via Resources.Load("SusRuntime/..") so this must end with
        // ".../Resources/SusRuntime". Configurable via SusConfig.ResourcesDirectory.
        // Sync itself lives in SharqCompilePipeline (shared with SharqBatchCompiler).
        private static string ResourcesDir => SusConfig.Instance.ResourcesDirectory;

        // ─── Content hash (caching) ──────────────────────────────────

        internal static string ComputeHash(string content)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        internal static bool IsHashMatch(string sharqPath, string currentHash)
        {
            var hashPath = GetHashPath(sharqPath);
            if (!File.Exists(hashPath)) return false;
            var stored = File.ReadAllText(hashPath).Trim();
            return stored == currentHash;
        }

        internal static void WriteHash(string sharqPath, string hash)
        {
            var hashPath = GetHashPath(sharqPath);
            var dir = Path.GetDirectoryName(hashPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(hashPath, hash, Encoding.UTF8);
        }

        internal static string GetHashPath(string sharqPath)
        {
            var className = Path.GetFileNameWithoutExtension(sharqPath);
            var generatedDir = Path.GetFullPath(GeneratedDir);
            return Path.Combine(generatedDir, $"{className}.sharq.hash");
        }
    }
}
