using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for the generator's USS output directory (ARCH-20260917-PKG-REFACTOR-I §5 S1,
    /// T-3591): a package descriptor's <c>"uss": "resources"</c> makes
    /// <see cref="SharqCompilePipeline.WriteAll"/> write <c>.g.uss</c> straight into the
    /// resources mirror instead of the legacy <c>Generated</c> + copy dance, and
    /// <see cref="SharqCompilePipeline.SyncUssToResources"/> then only prunes a stale
    /// <c>Generated</c> leftover instead of copying.
    ///
    /// Exercises the pipeline directly with a hand-built <see cref="SharqCompilePipeline.Artifacts"/>
    /// (no real <c>.sharq</c>/parser involved) — the WriteAll/SyncUssToResources contract is what
    /// this card changes, and this keeps the fixture independent of style-parsing behavior.
    /// </summary>
    public class SharqUssDirModeTests
    {
        private string _root;
        private string _generatedDir;
        private string _resourcesDir;
        private const string ClassName = "TestUssDirWidget";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sus-ussdir-test-" + Path.GetRandomFileName());
            _generatedDir = Path.Combine(_root, "Runtime", "Generated");
            _resourcesDir = Path.Combine(_root, "Runtime", "Resources", "SusRuntime");
            Directory.CreateDirectory(_generatedDir);
            Directory.CreateDirectory(_resourcesDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static SharqCompilePipeline.Artifacts MakeArtifacts(string globalCss) =>
            new SharqCompilePipeline.Artifacts
            {
                Code = $"public partial class {ClassName} {{ }}",
                GlobalUss = globalCss,
            };

        // ─── (a) uss=resources → style only in Resources, never in Generated ──────

        [Test]
        public void WriteAll_UssDirIsResources_UssOnlyInResourcesNotGenerated()
        {
            var artifacts = MakeArtifacts(".box { color: red; }");

            SharqCompilePipeline.WriteAll(in artifacts, ClassName, _generatedDir, _resourcesDir);
            SharqCompilePipeline.SyncUssToResources(ClassName, _generatedDir, _resourcesDir, _resourcesDir);

            var genUss = Path.Combine(_generatedDir, $"{ClassName}.g.uss");
            var resUss = Path.Combine(_resourcesDir, $"{ClassName}.g.uss");
            var genCs = Path.Combine(_generatedDir, $"{ClassName}.g.cs");

            Assert.IsTrue(File.Exists(genCs), ".g.cs always lands in generatedDir");
            Assert.IsTrue(File.Exists(resUss), ".g.uss must land directly in the resources dir");
            Assert.IsFalse(File.Exists(genUss), ".g.uss must NOT also exist in Generated — no second copy");
            Assert.AreEqual(".box { color: red; }", File.ReadAllText(resUss));
        }

        // ─── (b) stale Generated copy is deleted, together with its .meta ─────────

        [Test]
        public void SyncUssToResources_UssDirIsResources_PrunesStaleGeneratedCopyAndMeta()
        {
            // Simulate a leftover from a prior "generated"-mode run (or an older tool):
            // a .g.uss + .meta sitting in Generated even though this package is now "resources".
            var staleUss = Path.Combine(_generatedDir, $"{ClassName}.g.uss");
            var staleMeta = staleUss + ".meta";
            File.WriteAllText(staleUss, ".stale { color: blue; }");
            File.WriteAllText(staleMeta, "fileFormatVersion: 2\nguid: deadbeefdeadbeefdeadbeefdeadbeef\n");

            var artifacts = MakeArtifacts(".box { color: red; }");
            SharqCompilePipeline.WriteAll(in artifacts, ClassName, _generatedDir, _resourcesDir);
            SharqCompilePipeline.SyncUssToResources(ClassName, _generatedDir, _resourcesDir, _resourcesDir);

            Assert.IsFalse(File.Exists(staleUss), "stale Generated/*.g.uss must be pruned");
            Assert.IsFalse(File.Exists(staleMeta), "its .meta must be pruned together with it");
            Assert.IsTrue(File.Exists(Path.Combine(_resourcesDir, $"{ClassName}.g.uss")),
                "the real copy in Resources must still be written");
        }

        // ─── (c) two regenerations never touch the Resources .meta (GUID stable) ──

        [Test]
        public void WriteAll_UssDirIsResources_TwoRegenerations_ResourcesMetaUntouched()
        {
            var resUss = Path.Combine(_resourcesDir, $"{ClassName}.g.uss");
            var resMeta = resUss + ".meta";
            const string fakeMeta = "fileFormatVersion: 2\nguid: cafebabecafebabecafebabecafebabe\n";

            var artifacts1 = MakeArtifacts(".box { color: red; }");
            SharqCompilePipeline.WriteAll(in artifacts1, ClassName, _generatedDir, _resourcesDir);
            SharqCompilePipeline.SyncUssToResources(ClassName, _generatedDir, _resourcesDir, _resourcesDir);

            // Unity would have chosen a GUID for the newly-seen asset by now; simulate that.
            File.WriteAllText(resMeta, fakeMeta);

            // Second regeneration with DIFFERENT content — same asset path, no delete/recreate.
            var artifacts2 = MakeArtifacts(".box { color: green; }");
            SharqCompilePipeline.WriteAll(in artifacts2, ClassName, _generatedDir, _resourcesDir);
            SharqCompilePipeline.SyncUssToResources(ClassName, _generatedDir, _resourcesDir, _resourcesDir);

            Assert.AreEqual(".box { color: green; }", File.ReadAllText(resUss), "content must update");
            Assert.IsTrue(File.Exists(resMeta), ".meta must survive regeneration");
            Assert.AreEqual(fakeMeta, File.ReadAllText(resMeta), "GUID/.meta content must be untouched");
        }

        // ─── (d) uss default ("generated") → byte-identical to pre-§5-S1 behavior ──

        [Test]
        public void WriteAll_UssDirDefaultsToGenerated_MirroredIntoResourcesAsBefore()
        {
            var artifacts = MakeArtifacts(".box { color: red; }");

            // Default mode: ussDir == generatedDir (legacy call shape).
            SharqCompilePipeline.WriteAll(in artifacts, ClassName, _generatedDir, _generatedDir);
            SharqCompilePipeline.SyncUssToResources(ClassName, _generatedDir, _generatedDir, _resourcesDir);

            var genUss = Path.Combine(_generatedDir, $"{ClassName}.g.uss");
            var resUss = Path.Combine(_resourcesDir, $"{ClassName}.g.uss");

            Assert.IsTrue(File.Exists(genUss), "default mode still writes .g.uss into Generated");
            Assert.IsTrue(File.Exists(resUss), "default mode still mirrors it into Resources");
            Assert.AreEqual(File.ReadAllText(genUss), File.ReadAllText(resUss), "mirror must be byte-identical");
        }

        // ─── Descriptor: uss mode parsing/validation (SusPackageDescriptor) ────────

        [Test]
        public void Descriptor_UssResources_AbsUssDirIsResourcesDir()
        {
            var pkgRoot = Path.Combine(_root, "pkg");
            Directory.CreateDirectory(Path.Combine(pkgRoot, "Components"));
            var jsonPath = Path.Combine(pkgRoot, "sharq.gen.json");
            File.WriteAllText(jsonPath,
                "{\"sources\":[\"Components\"],\"generated\":\"Runtime/Generated\"," +
                "\"resources\":\"Runtime/Resources/SusRuntime\",\"uss\":\"resources\"}");

            var d = SusPackageDescriptor.Load(jsonPath, "com.test.pkg", pkgRoot);

            Assert.NotNull(d);
            Assert.IsTrue(d.UssInResourcesOnly);
            Assert.AreEqual(d.AbsResourcesDir, d.AbsUssDir);
        }

        [Test]
        public void Descriptor_UssOmitted_AbsUssDirIsGeneratedDir()
        {
            var pkgRoot = Path.Combine(_root, "pkg-default");
            Directory.CreateDirectory(Path.Combine(pkgRoot, "Components"));
            var jsonPath = Path.Combine(pkgRoot, "sharq.gen.json");
            File.WriteAllText(jsonPath,
                "{\"sources\":[\"Components\"],\"generated\":\"Runtime/Generated\"," +
                "\"resources\":\"Runtime/Resources/SusRuntime\"}");

            var d = SusPackageDescriptor.Load(jsonPath, "com.test.pkg", pkgRoot);

            Assert.NotNull(d);
            Assert.IsFalse(d.UssInResourcesOnly);
            Assert.AreEqual(d.AbsGeneratedDir, d.AbsUssDir);
        }

        [Test]
        public void Descriptor_UssResourcesWithoutResourcesField_ReturnsNull()
        {
            var pkgRoot = Path.Combine(_root, "pkg-bad");
            Directory.CreateDirectory(Path.Combine(pkgRoot, "Components"));
            var jsonPath = Path.Combine(pkgRoot, "sharq.gen.json");
            File.WriteAllText(jsonPath,
                "{\"sources\":[\"Components\"],\"generated\":\"Runtime/Generated\",\"uss\":\"resources\"}");

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var d = SusPackageDescriptor.Load(jsonPath, "com.test.pkg", pkgRoot);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsNull(d, "\"uss\": \"resources\" with no \"resources\" dir has nowhere to write");
        }

        [Test]
        public void Descriptor_UssUnknownValue_ReturnsNull()
        {
            var pkgRoot = Path.Combine(_root, "pkg-unknown");
            Directory.CreateDirectory(Path.Combine(pkgRoot, "Components"));
            var jsonPath = Path.Combine(pkgRoot, "sharq.gen.json");
            File.WriteAllText(jsonPath,
                "{\"sources\":[\"Components\"],\"generated\":\"Runtime/Generated\",\"uss\":\"bogus\"}");

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var d = SusPackageDescriptor.Load(jsonPath, "com.test.pkg", pkgRoot);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsNull(d);
        }
    }
}
