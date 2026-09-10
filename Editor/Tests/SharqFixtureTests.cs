using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Exact golden-file harness for the Sharq compiler (T-3290, step 2 of
    /// ARCH-20260910-SHARQ-STYLE-LAYER.md §6, DoD §7 п.3).
    ///
    /// <see cref="SharqCompilerGoldenTests"/> is deliberately assertion-based ("survive
    /// cosmetic formatting changes", see its header) and <see cref="SharqCorpusIdempotencyTests"/>
    /// (T-3289) only proves the compiler agrees with itself between two runs — neither pins down
    /// what the compiler is supposed to emit. This harness does: every fixture folder under
    /// <c>Fixtures/&lt;name&gt;/</c> carries an <c>input.sharq</c> and one or more committed
    /// <c>expected.*</c> files; a byte-for-byte mismatch fails loudly with a diff, so an
    /// emitter change that only shifts formatting (which the golden tests would miss) is
    /// still caught here.
    ///
    /// Fixture layout mirrors the artifact suffixes <see cref="SharqCompilePipeline.WriteAll"/>
    /// writes to disk (same four kinds <see cref="SharqCorpusIdempotencyTests"/> compares):
    ///   expected.g.cs           ← Artifacts.Code           (always present — every fixture has a &lt;template&gt;)
    ///   expected_static.g.uss   ← Artifacts.StaticUss      (inline style="..." folded to a class)
    ///   expected_scoped.g.uss   ← Artifacts.ScopedUss      (&lt;style scoped&gt;)
    ///   expected.g.uss          ← Artifacts.GlobalUss      (&lt;style&gt; without scoped)
    /// Only the kinds a fixture actually exercises need an expected file; a kind the pipeline
    /// produces without a matching expected file, or vice versa, is itself a failure (silently
    /// skipping an artifact kind would hide exactly the drift this harness exists to catch).
    ///
    /// First three fixtures cover what the compiler already does, so the harness is proven
    /// BEFORE anything in steps 3+ touches the emitter (plan §6, step 2 note):
    ///   scoped-hash  — &lt;style scoped&gt; → per-class .s-&lt;hash&gt; selector suffix (ScopedCssGenerator)
    ///   class-map    — :class="{ active: IsActive }" → BindClass(...) class-map codegen
    ///   media-query  — @media inside &lt;style scoped&gt; is kept verbatim, inner selectors are scoped
    ///
    /// Re-baselining (<see cref="WriteAllBaselines"/>) is reachable ONLY via the guarded editor
    /// menu item below — never automatically, and never from this test class's own [Test]
    /// methods. A fixture harness that can silently repair its own expectations on a red run is
    /// not a safety net; DoD §7 п.3 requires it be an explicit, reviewed action.
    /// </summary>
    public class SharqFixtureTests
    {
        private static string ThisFileDir(
            [System.Runtime.CompilerServices.CallerFilePath] string path = null)
            => Path.GetDirectoryName(path);

        // Resolved from THIS SOURCE FILE's own compile-time path, not Application.dataPath —
        // works the same whether sus-core is installed as a UPM cache copy or a file: link
        // (mirrors why SharqCorpusIdempotencyTests walks descriptor.AbsSourceDirs rather than
        // guessing from the active project's Assets folder).
        private static string FixturesRoot() => Path.Combine(ThisFileDir(), "Fixtures");

        private static readonly (string File, Func<SharqCompilePipeline.Artifacts, string> Pick)[] Kinds =
        {
            ("expected.g.cs", a => a.Code),
            ("expected_static.g.uss", a => a.StaticUss),
            ("expected_scoped.g.uss", a => a.ScopedUss),
            ("expected.g.uss", a => a.GlobalUss),
        };

        [Test]
        public void Fixture_MatchesExpectedByteForByte()
        {
            var root = FixturesRoot();
            Assert.IsTrue(Directory.Exists(root), $"fixtures root missing: {root}");

            var fixtureDirs = Directory.GetDirectories(root)
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToList();
            Assert.Greater(fixtureDirs.Count, 0, "no fixture directories found under " + root);

            var failures = new List<string>();
            var checkedFixtures = 0;
            var checkedArtifacts = 0;

            foreach (var dir in fixtureDirs)
            {
                var name = Path.GetFileName(dir);
                var inputPath = Path.Combine(dir, "input.sharq");
                if (!File.Exists(inputPath))
                {
                    failures.Add($"{name}: missing input.sharq");
                    continue;
                }

                var model = SharqFileParser.Parse(File.ReadAllText(inputPath), inputPath);
                if (string.IsNullOrEmpty(model.TemplateXml))
                {
                    failures.Add($"{name}: input.sharq has no <template> — cannot compile");
                    continue;
                }

                var artifacts = SharqCompilePipeline.Generate(model);
                var sawAnyKind = false;

                foreach (var (file, pick) in Kinds)
                {
                    var generated = pick(artifacts);
                    var expectedPath = Path.Combine(dir, file);
                    var hasExpectedFile = File.Exists(expectedPath);

                    if (!hasExpectedFile && generated == null)
                        continue; // both sides agree: this fixture doesn't exercise this kind

                    sawAnyKind = true;
                    checkedArtifacts++;

                    if (!hasExpectedFile)
                    {
                        failures.Add(
                            $"{name}/{file}: pipeline generated {generated.Length} B but no expected file is committed");
                        continue;
                    }
                    if (generated == null)
                    {
                        failures.Add(
                            $"{name}/{file}: expected file is committed but the pipeline generated NOTHING for this input");
                        continue;
                    }

                    var expected = File.ReadAllText(expectedPath);
                    if (expected == generated)
                        continue;

                    var diffAt = FirstDiffIndex(expected, generated);
                    failures.Add(
                        $"{name}/{file}: byte diff at offset {diffAt} (expected {expected.Length} B, got {generated.Length} B)\n" +
                        DiffContext(expected, generated, diffAt));
                }

                Assert.IsTrue(sawAnyKind,
                    $"{name}: pipeline produced no artifact AND no expected file is committed — fixture proves nothing");
                checkedFixtures++;
            }

            var summary =
                $"[T-3290][SharqFixtureTests] фикстур: {checkedFixtures}; артефактов сверено: {checkedArtifacts}; расхождений: {failures.Count}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            Assert.AreEqual(0, failures.Count, string.Join("\n\n", failures));
        }

        private static int FirstDiffIndex(string a, string b)
        {
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++)
                if (a[i] != b[i]) return i;
            return n;
        }

        private static string DiffContext(string expected, string actual, int at)
        {
            var start = Math.Max(0, at - 40);
            var expLen = Math.Min(80, expected.Length - start);
            var actLen = Math.Min(80, actual.Length - start);
            var expSnip = expLen > 0 ? expected.Substring(start, expLen) : "";
            var actSnip = actLen > 0 ? actual.Substring(start, actLen) : "";
            return $"  expected …{Escape(expSnip)}…\n  actual   …{Escape(actSnip)}…";
        }

        private static string Escape(string s) => s.Replace("\n", "\\n").Replace("\r", "\\r");

        // ─────────────────────────────────────────────────────────────
        //  Re-baselining — explicit human action ONLY (DoD §7 п.3: "никогда автоматически").
        //  Not a [Test]. Reachable from Unity's Tools menu; never invoked by this file's own
        //  tests or by any automatic pipeline.
        // ─────────────────────────────────────────────────────────────

        [UnityEditor.MenuItem("Tools/Sharq/Tests/Regenerate Fixture Baselines (T-3290)")]
        private static void RegenerateBaselinesMenuItem()
        {
            if (!UnityEditor.EditorUtility.DisplayDialog(
                    "Regenerate Sharq fixture baselines",
                    "This OVERWRITES every expected.* file under Editor/Tests/Fixtures with " +
                    "whatever the compiler produces RIGHT NOW. Only confirm after reviewing the " +
                    "emitter diff by hand — this harness exists to catch exactly the drift this " +
                    "action would erase. The resulting `git diff` in sus-core IS the review.",
                    "Overwrite baselines", "Cancel"))
                return;

            var written = WriteAllBaselines();
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[T-3290] regenerated {written} baseline file(s) under {FixturesRoot()} — review with `git diff` in sus-core before committing.");
        }

        /// <summary>
        /// Writes (or deletes, for a kind that no longer applies) every expected.* file from
        /// the compiler's CURRENT output. Internal (not private) so a role driving Unity via
        /// MCP execute_code/reflection can bootstrap or refresh fixtures without a UI dialog —
        /// the human review step is the `git diff` the caller is expected to read afterward,
        /// not this method's own confirmation prompt.
        /// </summary>
        internal static int WriteAllBaselines()
        {
            var root = FixturesRoot();
            var written = 0;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var inputPath = Path.Combine(dir, "input.sharq");
                if (!File.Exists(inputPath)) continue;

                var model = SharqFileParser.Parse(File.ReadAllText(inputPath), inputPath);
                if (string.IsNullOrEmpty(model.TemplateXml)) continue;

                var artifacts = SharqCompilePipeline.Generate(model);
                foreach (var (file, pick) in Kinds)
                {
                    var path = Path.Combine(dir, file);
                    var generated = pick(artifacts);
                    if (generated == null)
                    {
                        if (File.Exists(path)) File.Delete(path);
                        continue;
                    }
                    File.WriteAllText(path, generated, Encoding.UTF8);
                    written++;
                }
            }

            return written;
        }
    }
}
