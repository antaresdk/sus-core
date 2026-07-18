using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Public batch compiler for pre-generating .sharq output into an explicit
    /// target (e.g. a UPM package that ships ready-to-use components).
    ///
    /// Unlike <see cref="SharqFileImporter"/> — which is project-scoped to
    /// <c>SusConfig.SharqDirectory</c> and triggers on save — this API compiles a
    /// caller-supplied source directory into caller-supplied output directories.
    ///
    /// Modes:
    ///   <see cref="SharqBatchMode.Full"/> — write all artifacts (default Generate path).
    ///   <see cref="SharqBatchMode.HotReloadSafe"/> — incremental; never writes .g.cs
    ///     unless script changed (Play-mode style/template hot reload).
    /// </summary>
    public static class SharqBatchCompiler
    {
        public enum SharqBatchMode
        {
            /// <summary>Full WriteAll — used by package Generate / Edit-mode flush.</summary>
            Full,
            /// <summary>
            /// Incremental writes for Play hot reload: style → USS only; template →
            /// static USS + RaiseTemplateChanged; never touches .g.cs when script unchanged.
            /// </summary>
            HotReloadSafe,
        }

        public struct Result
        {
            public int Compiled;
            public int Failed;
        }

        /// <summary>
        /// Compiles every <c>*.sharq</c> under <paramref name="sourceDir"/> into
        /// <paramref name="generatedDir"/> (<c>.g.cs</c> + USS) and mirrors the USS
        /// into <paramref name="resourcesDir"/> for runtime <c>Resources.Load</c>.
        /// </summary>
        public static Result CompileDirectory(
            string sourceDir, string generatedDir, string resourcesDir, bool log = true)
        {
            var result = new Result();

            if (!Directory.Exists(sourceDir))
            {
                Debug.LogWarning($"[SharqBatch] Source directory not found: {sourceDir}");
                return result;
            }

            Directory.CreateDirectory(generatedDir);
            if (!string.IsNullOrEmpty(resourcesDir))
                Directory.CreateDirectory(resourcesDir);

            var genFull = Path.GetFullPath(generatedDir).Replace("\\", "/").TrimEnd('/') + "/";

            foreach (var file in Directory.GetFiles(sourceDir, "*.sharq", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(file).Replace("\\", "/");
                if (full.StartsWith(genFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (CompileFile(file, generatedDir, resourcesDir, SharqBatchMode.Full, generatedDir, log))
                    result.Compiled++;
                else
                    result.Failed++;
            }

            if (log)
                Debug.Log(
                    $"[SharqBatch] Compiled {result.Compiled} component(s) from '{sourceDir}' → '{generatedDir}'" +
                    (result.Failed > 0 ? $" ({result.Failed} failed)" : ""));

            return result;
        }

        /// <summary>
        /// Compiles a single <c>.sharq</c> file into the given output dirs.
        /// Returns <c>true</c> on success, <c>false</c> if skipped/failed.
        /// </summary>
        public static bool CompileFile(
            string sharqPath, string generatedDir, string resourcesDir, bool log = true)
        {
            return CompileFile(sharqPath, generatedDir, resourcesDir, SharqBatchMode.Full, generatedDir, log);
        }

        /// <summary>
        /// Compiles a single file with explicit <paramref name="mode"/> and section-cache directory.
        /// </summary>
        public static bool CompileFile(
            string sharqPath,
            string generatedDir,
            string resourcesDir,
            SharqBatchMode mode,
            string cacheDir,
            bool log = true)
        {
            var fullPath = Path.GetFullPath(sharqPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[SharqBatch] File not found: {sharqPath}");
                return false;
            }

            var content = File.ReadAllText(fullPath);
            var model = SharqFileParser.Parse(content, fullPath);

            if (string.IsNullOrEmpty(model.TemplateXml))
            {
                Debug.LogWarning($"[SharqBatch] No <template> in {Path.GetFileName(sharqPath)} — skipped");
                return false;
            }

            var messages = SharqValidator.Validate(model);
            if (messages.Count > 0)
                SharqValidator.LogMessages(model.ClassName, messages);

            var cache = string.IsNullOrEmpty(cacheDir) ? generatedDir : cacheDir;
            var changed = SharqSectionCache.WhatChanged(
                model.ClassName, model.TemplateXml, model.ScriptBody, model.StyleBody, cache);

            var artifacts = SharqCompilePipeline.Generate(model);
            Directory.CreateDirectory(generatedDir);

            if (mode == SharqBatchMode.Full)
            {
                SharqCompilePipeline.WriteAll(in artifacts, model.ClassName, generatedDir);
                SharqCompilePipeline.SyncUssToResources(model.ClassName, generatedDir, resourcesDir);

                // Raise USS only when style (or static from template) actually changed.
                RaiseUssIfNeeded(model.ClassName, generatedDir, changed, writeStatic: true);
                if (changed.TemplateChanged && !changed.ScriptChanged)
                    SharqCompileEvents.RaiseTemplateChanged(model.ClassName, model.TemplateXml);

                SharqSectionCache.StoreHashes(model.ClassName, changed.NewHashes, cache);

                if (log)
                    Debug.Log($"[SharqBatch] Generated {model.ClassName}.g.cs");
                return true;
            }

            // ── HotReloadSafe: never write .g.cs unless script changed ──
            if (changed.ScriptChanged)
            {
                // Caller should defer; writing .cs would domain-reload.
                if (log)
                    Debug.LogWarning(
                        $"[SharqBatch] HotReloadSafe: {model.ClassName} has <script> changes — refusing to write .g.cs (defer full Generate).");
                return false;
            }

            if (!changed.Any)
            {
                if (log)
                    Debug.Log($"[SharqBatch] HotReloadSafe: {model.ClassName} unchanged — skip");
                return true;
            }

            // Template-only: update _static.g.uss (inline styles), do NOT write .g.cs
            if (changed.TemplateChanged)
            {
                SharqCompilePipeline.WriteOrDelete(
                    Path.Combine(generatedDir, $"{model.ClassName}_static.g.uss"),
                    artifacts.StaticUss);
            }

            // Style-only (or also style with template): scoped + global USS
            if (changed.StyleChanged)
            {
                SharqCompilePipeline.WriteOrDelete(
                    Path.Combine(generatedDir, $"{model.ClassName}_scoped.g.uss"),
                    artifacts.ScopedUss);
                SharqCompilePipeline.WriteOrDelete(
                    Path.Combine(generatedDir, $"{model.ClassName}.g.uss"),
                    artifacts.GlobalUss);
            }

            SharqCompilePipeline.SyncUssToResources(model.ClassName, generatedDir, resourcesDir);

            RaiseUssIfNeeded(model.ClassName, generatedDir, changed, writeStatic: changed.TemplateChanged);

            if (changed.TemplateChanged && !changed.ScriptChanged)
                SharqCompileEvents.RaiseTemplateChanged(model.ClassName, model.TemplateXml);

            SharqSectionCache.StoreHashes(model.ClassName, changed.NewHashes, cache);

            if (log)
            {
                var parts = new List<string>();
                if (changed.StyleChanged) parts.Add("style");
                if (changed.TemplateChanged) parts.Add("template");
                Debug.Log($"[SharqBatch] HotReloadSafe {model.ClassName}: {string.Join("+", parts)}");
            }

            return true;
        }

        /// <summary>
        /// Inspect section changes for a .sharq without writing (Play routing).
        /// </summary>
        internal static SectionChanged PeekChanges(string sharqPath, string cacheDir)
        {
            var fullPath = Path.GetFullPath(sharqPath);
            if (!File.Exists(fullPath))
                return new SectionChanged();

            var content = File.ReadAllText(fullPath);
            var model = SharqFileParser.Parse(content, fullPath);
            return SharqSectionCache.WhatChanged(
                model.ClassName, model.TemplateXml, model.ScriptBody, model.StyleBody, cacheDir);
        }

        /// <summary>Class name from a .sharq path (for delete cleanup).</summary>
        public static string PeekClassName(string sharqPath)
        {
            try
            {
                var content = File.ReadAllText(sharqPath);
                return SharqFileParser.Parse(content, sharqPath).ClassName;
            }
            catch
            {
                return Path.GetFileNameWithoutExtension(sharqPath);
            }
        }

        private static void RaiseUssIfNeeded(
            string className, string generatedDir, SectionChanged changed, bool writeStatic)
        {
            if (!changed.StyleChanged && !writeStatic)
                return;
            // Full mode: only raise when style changed (fix E-WARN-2).
            // HotReloadSafe template-only may rewrite static USS → raise with that suffix.
            if (!changed.StyleChanged && !changed.TemplateChanged)
                return;

            var ussPaths = new List<string>();
            void AddIfExists(string suffix)
            {
                var p = Path.Combine(generatedDir, $"{className}{suffix}");
                if (File.Exists(p)) ussPaths.Add(Path.GetFullPath(p));
            }

            if (changed.StyleChanged)
            {
                AddIfExists(".g.uss");
                AddIfExists("_scoped.g.uss");
            }
            if (changed.StyleChanged || (writeStatic && changed.TemplateChanged))
                AddIfExists("_static.g.uss");

            if (ussPaths.Count > 0)
                SharqCompileEvents.RaiseUssGenerated(className, ussPaths.ToArray());
        }
    }
}
