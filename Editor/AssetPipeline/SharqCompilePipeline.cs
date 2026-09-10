using System.IO;
using System.Text;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Shared parse→generate→style pipeline used by BOTH generation entry points:
    ///   - <see cref="SharqFileImporter"/> — project-scoped, incremental (section cache),
    ///     triggers on save;
    ///   - <see cref="SharqBatchCompiler"/> — package export, full regen (no cache).
    ///
    /// <see cref="Generate"/> is the single source of truth for artifact CONTENT, so
    /// both paths are byte-for-byte identical for the same <c>.sharq</c>. Callers decide
    /// WHERE to write and WHETHER to write incrementally (importer) or in full (batch).
    /// </summary>
    internal static class SharqCompilePipeline
    {
        /// <summary>In-memory generation artifacts for one .sharq component.</summary>
        internal struct Artifacts
        {
            /// <summary><c>{Class}.g.cs</c> — always produced.</summary>
            public string Code;
            /// <summary><c>{Class}_static.g.uss</c> — null ⇒ no inline styles (delete stale).</summary>
            public string StaticUss;
            /// <summary><c>{Class}_scoped.g.uss</c> — null ⇒ no scoped CSS.</summary>
            public string ScopedUss;
            /// <summary><c>{Class}.g.uss</c> — null ⇒ no global CSS.</summary>
            public string GlobalUss;
            /// <summary>Number of deduplicated inline-style rules folded into <see cref="StaticUss"/> (for logging).</summary>
            public int StaticRuleCount;
            /// <summary>
            /// (T-3295, plan §4.4) <c>{ScopedUss|GlobalUss filename}.map.json</c> content —
            /// null when neither <see cref="ScopedUss"/> nor <see cref="GlobalUss"/> mapped any
            /// declaration (no <c>&lt;style&gt;</c> section, or one with zero leaf rules). Never
            /// produced for <see cref="StaticUss"/> (inline <c>style="…"</c> attrs) — out of
            /// scope for this card, which covers the <c>&lt;style&gt;</c> section only.
            /// </summary>
            public string SourceMap;
        }

        /// <summary>
        /// Pure generation: <paramref name="model"/> → in-memory artifacts.
        /// MUST be called with a non-empty <c>model.TemplateXml</c> (callers guard this).
        /// Uses a per-call <c>BuildMethodGenerator</c> instance (P2.8) so code and the
        /// inline-style snapshot come from the SAME run — safe for parallel batches.
        /// </summary>
        internal static Artifacts Generate(SharqFileModel model)
        {
            var generator = new BuildMethodGenerator();
            var code = generator.GenerateCode(model);

            string staticUss = null;
            var styleCount = generator.GeneratedStyles.Count;
            if (styleCount > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"/* Auto-generated from inline style=\"...\" in {model.ClassName}.sharq */");
                foreach (var kvp in generator.GeneratedStyles)
                {
                    sb.AppendLine($".{kvp.Value} {{");
                    sb.AppendLine($"    {kvp.Key}");
                    sb.AppendLine("}");
                    sb.AppendLine();
                }
                staticUss = sb.ToString();
            }

            var styleResult = StyleParser.Parse(model);

            string sourceMap = null;
            if (styleResult.MapLines.Count > 0)
                sourceMap = SharqSourceMapWriter.Build(
                    model.SourcePath, model.SourceSha1, model.StyleBodyLine, styleResult.MapLines);

            return new Artifacts
            {
                Code = code,
                StaticUss = staticUss,
                StaticRuleCount = styleCount,
                ScopedUss = styleResult.HasScopedCss ? styleResult.ScopedCss : null,
                GlobalUss = styleResult.HasGlobalCss ? styleResult.GlobalCss : null,
                SourceMap = sourceMap,
            };
        }

        /// <summary>
        /// (T-3295) File name of <see cref="Artifacts.SourceMap"/> — mirrors whichever USS
        /// artifact it maps (<c>_scoped.g.uss</c> or <c>.g.uss</c>), the same convention a
        /// browser devtools sourcemap uses (<c>foo.js.map</c> next to <c>foo.js</c>). Null when
        /// there is nothing to write (no map, or — should both somehow be null — no USS at all).
        /// </summary>
        internal static string MapFileName(string className, in Artifacts a)
        {
            if (a.SourceMap == null) return null;
            if (a.ScopedUss != null) return $"{className}_scoped.g.uss.map.json";
            if (a.GlobalUss != null) return $"{className}.g.uss.map.json";
            return null;
        }

        /// <summary>
        /// Full write of all artifacts to <paramref name="generatedDir"/> (delete-if-null).
        /// Used by the batch/full-regen path.
        /// </summary>
        internal static void WriteAll(in Artifacts a, string className, string generatedDir)
        {
            Directory.CreateDirectory(generatedDir);
            AtomicWrite(Path.Combine(generatedDir, $"{className}.g.cs"), a.Code);
            WriteOrDelete(Path.Combine(generatedDir, $"{className}_static.g.uss"), a.StaticUss);
            WriteOrDelete(Path.Combine(generatedDir, $"{className}_scoped.g.uss"), a.ScopedUss);
            WriteOrDelete(Path.Combine(generatedDir, $"{className}.g.uss"), a.GlobalUss);
            WriteSourceMap(in a, className, generatedDir);
        }

        /// <summary>
        /// (T-3295) Writes/deletes <see cref="Artifacts.SourceMap"/> next to whichever USS it
        /// maps, same atomic path as every other artifact — both write paths
        /// (<see cref="WriteAll"/> and the incremental callers) go through this so the map's
        /// lifecycle can never drift from the USS it describes: same call, same file, same
        /// delete-when-null rule. Deletes BOTH possible map names when writing nothing, so a
        /// component that flips from scoped ↔ global (or drops <c>&lt;style&gt;</c> entirely)
        /// never leaves a map pointing at a USS suffix that no longer exists.
        /// </summary>
        internal static void WriteSourceMap(in Artifacts a, string className, string generatedDir)
        {
            var scopedMapPath = Path.Combine(generatedDir, $"{className}_scoped.g.uss.map.json");
            var globalMapPath = Path.Combine(generatedDir, $"{className}.g.uss.map.json");
            var target = MapFileName(className, in a);

            WriteOrDelete(scopedMapPath, target == Path.GetFileName(scopedMapPath) ? a.SourceMap : null);
            WriteOrDelete(globalMapPath, target == Path.GetFileName(globalMapPath) ? a.SourceMap : null);
        }

        /// <summary>Writes <paramref name="content"/> if non-null (atomically); otherwise deletes the file if present.</summary>
        internal static void WriteOrDelete(string path, string content)
        {
            if (content != null)
                AtomicWrite(path, content);
            else if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// Mirrors generated USS files (<c>_static</c>/<c>_scoped</c>/global) from
        /// <paramref name="generatedDir"/> into <paramref name="resourcesDir"/> for runtime
        /// <c>Resources.Load</c>.
        ///
        /// P2.9: the copy is unavoidable — player builds only include assets under a
        /// <c>Resources/</c> folder, and the canonical <c>Generated/</c> output lives outside
        /// it. To keep the mirror exact and desync-free this method now:
        ///  • prunes a stale Resources copy when its <c>Generated</c> source no longer exists
        ///    (e.g. inline styles removed → no more <c>_static.g.uss</c>);
        ///  • writes atomically (temp file + replace) so a crash mid-write can't leave a
        ///    half-written USS behind;
        ///  • still skips writes when content is unchanged (no needless AssetDatabase churn).
        /// Only the three generated <c>.g.uss</c> suffixes are touched — hand-written companion
        /// USS (e.g. <c>SusButton.uss</c>) is never affected.
        /// </summary>
        internal static void SyncUssToResources(string className, string generatedDir, string resourcesDir)
        {
            if (string.IsNullOrEmpty(resourcesDir)) return;
            Directory.CreateDirectory(resourcesDir);

            string[] suffixes = { "_static.g.uss", "_scoped.g.uss", ".g.uss" };
            foreach (var suf in suffixes)
            {
                var genPath = Path.Combine(generatedDir, $"{className}{suf}");
                var resPath = Path.Combine(resourcesDir, $"{className}{suf}");

                // Source gone → drop the stale mirror so Resources never keeps orphans.
                if (!File.Exists(genPath))
                {
                    if (File.Exists(resPath)) File.Delete(resPath);
                    continue;
                }

                var genContent = File.ReadAllText(genPath);
                if (File.Exists(resPath) && File.ReadAllText(resPath) == genContent)
                    continue;

                AtomicWrite(resPath, genContent);
            }
        }

        /// <summary>
        /// Writes via a sibling temp file then replaces the target, so readers never see a
        /// partial file. Retries / falls back to overwrite-copy when Unity holds the asset
        /// open (Windows sharing violation / EBUSY on tracked Resources companions —).
        /// </summary>
        private static void AtomicWrite(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Encoding.UTF8);

            const int attempts = 8;
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(tmp, path, null);
                        }
                        catch (IOException)
                        {
                            // File.Replace needs exclusive access; AssetDatabase often holds
                            // StyleSheet imports. Overwrite-copy then drop the temp.
                            File.Copy(tmp, path, overwrite: true);
                            File.Delete(tmp);
                        }
                    }
                    else
                    {
                        File.Move(tmp, path);
                    }
                    return;
                }
                catch (IOException) when (i < attempts - 1)
                {
                    System.Threading.Thread.Sleep(40 * (i + 1));
                }
            }
        }
    }
}
