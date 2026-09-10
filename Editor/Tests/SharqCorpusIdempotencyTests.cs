using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Safety net under the Sharq compiler (T-3289, step 1 of
    /// ARCH-20260910-SHARQ-STYLE-LAYER.md §6, DoD §7 п.1).
    ///
    /// Every <c>.sharq</c> belonging to a downstream mutable package — any package this
    /// project has installed as a local/file: reference that declares its own generation
    /// descriptor (<see cref="SusPackageRegistry"/> / <see cref="SusPackageDescriptor"/>,
    /// same discovery <see cref="SusPackageGenerator"/> uses) — is recompiled IN MEMORY via
    /// <see cref="SharqCompilePipeline"/> and diffed byte-for-byte against the artifacts
    /// already on disk under that package's declared generated directory.
    ///
    /// Discovery is deliberately generic and names no downstream product in source: this
    /// assembly ships inside the public core package, and R25 (docs-canon/RULES.md) forbids
    /// naming paid downstream packages anywhere in tracked core/router text. Being generic is
    /// also strictly more useful here — every package sharing this compiler gets covered by
    /// the same net, not only the two the plan measured; an emitter regression can't hide
    /// behind a package this test simply never hardcoded the name of.
    ///
    /// Green here is the precondition for touching the emitter in steps 3-7: without it, a
    /// format-drifting refactor would only be caught by <see cref="SharqCompilerGoldenTests"/>,
    /// which is deliberately assertion-based and does not guard output byte-shape (plan §2.5).
    ///
    /// The compared count is asserted &gt; 0 AND logged explicitly: an empty corpus (broken
    /// package resolution, or nobody left the target packages installed) would otherwise pass
    /// trivially green with zero evidence — the exact failure mode this test exists to catch.
    /// </summary>
    public class SharqCorpusIdempotencyTests
    {
        // This package itself never ships a sharq.gen.json (verified on disk at authoring
        // time), so it would not appear among descriptors anyway — named here only so a
        // future self-descriptor can't turn this test into "compile core against itself".
        private const string SelfPackageName = "com.sharq-it.sus.core";

        private readonly struct Artifact
        {
            public readonly string Suffix;
            public readonly string Generated;
            public Artifact(string suffix, string generated) { Suffix = suffix; Generated = generated; }
        }

        [Test]
        public void Corpus_RegeneratesByteForByte()
        {
            SusPackageRegistry.Refresh();

            var descriptors = SusPackageRegistry.Packages
                .Where(d => !string.Equals(d.PackageName, SelfPackageName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Greater(descriptors.Count, 0,
                "no downstream package with a sharq.gen.json descriptor was resolved — " +
                "package/path resolution is broken (an empty corpus must never read as a green run)");

            var sharqFiles = new List<(string path, string generatedDir, string ns, string[] usings, string pkg)>();
            var perPackageCount = new Dictionary<string, int>();

            foreach (var descriptor in descriptors)
            {
                var count = 0;
                foreach (var srcDir in descriptor.AbsSourceDirs)
                {
                    Assert.IsTrue(Directory.Exists(srcDir), $"{descriptor.PackageName}: source dir missing: {srcDir}");
                    foreach (var file in Directory.GetFiles(srcDir, "*.sharq", SearchOption.AllDirectories)
                                 .OrderBy(f => f, StringComparer.Ordinal))
                    {
                        sharqFiles.Add((file, descriptor.AbsGeneratedDir, descriptor.@namespace, descriptor.usings, descriptor.PackageName));
                        count++;
                    }
                }
                perPackageCount[descriptor.PackageName] = count;
            }

            var compared = sharqFiles.Count;
            Assert.Greater(compared, 0,
                "sharq corpus resolved to 0 files across all discovered packages — " +
                "package/path resolution is broken (an empty corpus must never read as a green run)");

            var mismatches = new List<string>();
            var missingTemplate = new List<string>();
            var componentsWithMismatch = new HashSet<string>();

            foreach (var (path, generatedDir, ns, usings, pkg) in sharqFiles)
            {
                var content = File.ReadAllText(path);
                var model = SharqFileParser.Parse(content, path);

                if (string.IsNullOrEmpty(model.TemplateXml))
                {
                    missingTemplate.Add($"{pkg}: {path} — no <template>, cannot regenerate");
                    continue;
                }

                if (!string.IsNullOrEmpty(ns))
                    model.Namespace = ns;
                if (usings != null)
                {
                    foreach (var u in usings)
                    {
                        if (string.IsNullOrWhiteSpace(u)) continue;
                        var trimmed = u.Trim();
                        if (!model.Usings.Contains(trimmed))
                            model.Usings.Add(trimmed);
                    }
                }

                var artifacts = SharqCompilePipeline.Generate(model);
                var className = model.ClassName;

                var expected = new[]
                {
                    new Artifact($"{className}.g.cs", artifacts.Code),
                    new Artifact($"{className}_scoped.g.uss", artifacts.ScopedUss),
                    new Artifact($"{className}.g.uss", artifacts.GlobalUss),
                    new Artifact($"{className}_static.g.uss", artifacts.StaticUss),
                };

                foreach (var a in expected)
                {
                    var diskPath = Path.Combine(generatedDir, a.Suffix);
                    var onDisk = File.Exists(diskPath) ? File.ReadAllText(diskPath) : null;

                    if (a.Generated == null && onDisk == null)
                        continue; // both agree: no artifact of this kind

                    if (a.Generated == null)
                    {
                        mismatches.Add($"{pkg}/{a.Suffix}: regenerated NOTHING, but disk has {onDisk.Length} B — source {path}");
                        componentsWithMismatch.Add(path);
                        continue;
                    }
                    if (onDisk == null)
                    {
                        mismatches.Add($"{pkg}/{a.Suffix}: regenerated {a.Generated.Length} B, but disk has NOTHING — source {path}");
                        componentsWithMismatch.Add(path);
                        continue;
                    }
                    if (onDisk == a.Generated)
                        continue;

                    var diffAt = FirstDiffIndex(onDisk, a.Generated);
                    mismatches.Add(
                        $"{pkg}/{a.Suffix}: byte diff at offset {diffAt} (disk {onDisk.Length} B, regen {a.Generated.Length} B) — source {path}");
                    componentsWithMismatch.Add(path);
                }
            }

            var identical = compared - missingTemplate.Count - componentsWithMismatch.Count;
            var summary =
                $"[T-3289][SharqCorpusIdempotencyTests] сверено компонентов: {compared} " +
                $"({string.Join(" + ", perPackageCount.Select(kv => $"{kv.Key}={kv.Value}"))})" +
                $"; байт-в-байт идентичны: {identical}" +
                $"; разошлись: {componentsWithMismatch.Count} ({mismatches.Count} артефакт(ов))" +
                $"; без <template>: {missingTemplate.Count}";
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            if (missingTemplate.Count > 0)
                Assert.Fail(
                    $"{missingTemplate.Count}/{compared} .sharq file(s) have no <template> " +
                    "(cannot be part of the corpus baseline):\n" + string.Join("\n", missingTemplate));

            Assert.AreEqual(0, mismatches.Count,
                $"{mismatches.Count} artifact(s) across {compared} compared .sharq component(s) diverged " +
                "from Generated on disk:\n" + string.Join("\n", mismatches));
        }

        private static int FirstDiffIndex(string a, string b)
        {
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++)
                if (a[i] != b[i]) return i;
            return n;
        }
    }
}
