using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sharq.Core.Editor.Diagnostics;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for the pure classification logic behind SUS Set Doctor (ARCH-PACK-CLASSIC.md §2.2,
    /// T-368): the three silent-breakage states a classic .unitypackage install can end up in.
    /// A neutral fixture module name ("widgets"/"Widgets") is used since this file lives in the
    /// free/MIT sus-core repo. Everything here is exercised through public static entry points
    /// with plain data/temp-dir fixtures — no live AssetDatabase/PackageManager state, matching
    /// the pattern already used by <see cref="SusStarterAssetsTests"/>/<see cref="SusPackageRegistryTests"/>.
    /// </summary>
    public class SusSetDoctorTests
    {
        private static SusSetManifest MakeManifest(params (string id, string dir, string version)[] modules)
        {
            var m = new SusSetManifest { set = "widgets-set", displayName = "Widgets Set", version = "1.0.0", root = "Sharq" };
            var list = new List<SusSetManifestModule>();
            foreach (var (id, dir, version) in modules)
                list.Add(new SusSetManifestModule { id = id, dir = dir, version = version, sha = "deadbeef" });
            m.modules = list.ToArray();
            m.paths = new string[0];
            return m;
        }

        // ─── DetectUpmCollisions ───────────────────────────────────────────

        [Test]
        public void DetectUpmCollisions_BothPresent_ReportsError()
        {
            var manifest = MakeManifest(("core", "Core", "1.0.14"));
            var upm = new HashSet<string> { "com.sharq-it.sus.core" };
            var assets = new HashSet<string> { "Core" };

            var issues = SusSetDoctor.DetectUpmCollisions(manifest, upm, assets);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(SusValidationSeverity.Error, issues[0].Severity);
            StringAssert.Contains("com.sharq-it.sus.core", issues[0].Message);
            StringAssert.Contains("Sharq/Core", issues[0].Message);
        }

        [Test]
        public void DetectUpmCollisions_OnlyUpm_NoFinding()
        {
            var manifest = MakeManifest(("core", "Core", "1.0.14"));
            var upm = new HashSet<string> { "com.sharq-it.sus.core" };
            var assets = new HashSet<string>();

            Assert.IsEmpty(SusSetDoctor.DetectUpmCollisions(manifest, upm, assets));
        }

        [Test]
        public void DetectUpmCollisions_OnlyAsset_NoFinding()
        {
            var manifest = MakeManifest(("core", "Core", "1.0.14"));
            var upm = new HashSet<string>();
            var assets = new HashSet<string> { "Core" };

            Assert.IsEmpty(SusSetDoctor.DetectUpmCollisions(manifest, upm, assets));
        }

        [Test]
        public void DetectUpmCollisions_Neither_NoFinding()
        {
            var manifest = MakeManifest(("core", "Core", "1.0.14"));

            Assert.IsEmpty(SusSetDoctor.DetectUpmCollisions(manifest, new HashSet<string>(), new HashSet<string>()));
        }

        [Test]
        public void DetectUpmCollisions_OnlyCollidingModuleReported_OthersClean()
        {
            var manifest = MakeManifest(("core", "Core", "1.0.14"), ("widgets", "Widgets", "2.0.0"));
            var upm = new HashSet<string> { "com.sharq-it.sus.widgets" };
            var assets = new HashSet<string> { "Core", "Widgets" }; // Core only present as asset, not UPM

            var issues = SusSetDoctor.DetectUpmCollisions(manifest, upm, assets);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("com.sharq-it.sus.widgets", issues[0].Message);
        }

        [Test]
        public void DetectUpmCollisions_PackageNameMatchIsCaseInsensitive()
        {
            // Uses the same comparer SusSetDoctor.RunAll() builds its real HashSet with
            // (StringComparer.OrdinalIgnoreCase) — a plain default-comparer set would make this
            // test pass or fail on the TEST's own case sensitivity, not the production code's.
            var manifest = MakeManifest(("core", "Core", "1.0.14"));
            var upm = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "COM.SHARQ-IT.SUS.CORE" };
            var assets = new HashSet<string> { "Core" };

            Assert.AreEqual(1, SusSetDoctor.DetectUpmCollisions(manifest, upm, assets).Count);
        }

        // ─── DetectResidualPaths ────────────────────────────────────────────

        [Test]
        public void DetectResidualPaths_NoExtra_Empty()
        {
            var manifest = MakeManifest(("core", "Core", "1.0.14"));
            manifest.paths = new[] { "Sharq", "Sharq/Core", "Sharq/Core/README.md" };

            var actual = new[] { "Sharq", "Sharq/Core", "Sharq/Core/README.md" };

            Assert.IsEmpty(SusSetDoctor.DetectResidualPaths(manifest, actual));
        }

        [Test]
        public void DetectResidualPaths_ExtraFile_Reported()
        {
            var manifest = MakeManifest(("core", "Core", "1.0.14"));
            manifest.paths = new[] { "Sharq", "Sharq/Core" };

            var actual = new[] { "Sharq", "Sharq/Core", "Sharq/Core/OldFile.cs" };

            var residual = SusSetDoctor.DetectResidualPaths(manifest, actual);

            CollectionAssert.AreEqual(new[] { "Sharq/Core/OldFile.cs" }, residual);
        }

        [Test]
        public void DetectResidualPaths_WholeStaleFolder_CollapsedToFolderOnly()
        {
            var manifest = MakeManifest(("core", "Core", "1.0.14"));
            manifest.paths = new[] { "Sharq", "Sharq/Core" };

            // A whole folder tree not in the manifest — must report the folder once, not every file.
            var actual = new[]
            {
                "Sharq", "Sharq/Core",
                "Sharq/Core/OldStuff", "Sharq/Core/OldStuff/A.cs", "Sharq/Core/OldStuff/B.cs",
            };

            var residual = SusSetDoctor.DetectResidualPaths(manifest, actual);

            CollectionAssert.AreEqual(new[] { "Sharq/Core/OldStuff" }, residual);
        }

        [Test]
        public void DetectResidualPaths_RealCaseSusIconRenamed_Reported()
        {
            // Real audit case (§2.4): SusIcon.g.cs -> SusIconElement.g.cs. The new manifest no
            // longer lists the old generated file; it must show up as residual.
            var manifest = MakeManifest(("kit", "Kit", "1.0.16"));
            manifest.paths = new[]
            {
                "Sharq", "Sharq/Kit", "Sharq/Kit/Runtime", "Sharq/Kit/Runtime/Generated",
                "Sharq/Kit/Runtime/Generated/SusIconElement.g.cs",
            };
            var actual = new[]
            {
                "Sharq", "Sharq/Kit", "Sharq/Kit/Runtime", "Sharq/Kit/Runtime/Generated",
                "Sharq/Kit/Runtime/Generated/SusIconElement.g.cs",
                "Sharq/Kit/Runtime/Generated/SusIcon.g.cs", // stale
            };

            var residual = SusSetDoctor.DetectResidualPaths(manifest, actual);

            CollectionAssert.AreEqual(new[] { "Sharq/Kit/Runtime/Generated/SusIcon.g.cs" }, residual);
        }

        // ─── ExtractLatestChangelogVersion ─────────────────────────────────

        [Test]
        public void ExtractLatestChangelogVersion_SkipsUnreleased_ReturnsFirstReal()
        {
            var text = "# Changelog\n\n## [Unreleased]\n\nsome notes\n\n## [1.0.14] - 2026-08-12\n\nstuff\n";
            Assert.AreEqual("1.0.14", SusSetDoctor.ExtractLatestChangelogVersion(text));
        }

        [Test]
        public void ExtractLatestChangelogVersion_NoUnreleased_ReturnsFirstHeading()
        {
            var text = "# Changelog\n\n## [1.0.7] - 2026-08-12\n\nstuff\n\n## [1.0.6] - 2026-08-11\n\nolder\n";
            Assert.AreEqual("1.0.7", SusSetDoctor.ExtractLatestChangelogVersion(text));
        }

        [Test]
        public void ExtractLatestChangelogVersion_NoHeadings_ReturnsNull()
        {
            Assert.IsNull(SusSetDoctor.ExtractLatestChangelogVersion("# Changelog\n\nnothing here\n"));
        }

        [Test]
        public void ExtractLatestChangelogVersion_Empty_ReturnsNull()
        {
            Assert.IsNull(SusSetDoctor.ExtractLatestChangelogVersion(""));
            Assert.IsNull(SusSetDoctor.ExtractLatestChangelogVersion(null));
        }

        // ─── DetectVersionMismatches ────────────────────────────────────────

        [Test]
        public void DetectVersionMismatches_Mismatch_ReportsWarning()
        {
            var manifest = MakeManifest(("kit", "Kit", "1.0.16"));
            var actual = new Dictionary<string, string> { ["kit"] = "1.0.14" };

            var issues = SusSetDoctor.DetectVersionMismatches(manifest, actual);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(SusValidationSeverity.Warning, issues[0].Severity);
            StringAssert.Contains("1.0.14", issues[0].Message);
            StringAssert.Contains("1.0.16", issues[0].Message);
        }

        [Test]
        public void DetectVersionMismatches_Match_NoFinding()
        {
            var manifest = MakeManifest(("kit", "Kit", "1.0.16"));
            var actual = new Dictionary<string, string> { ["kit"] = "1.0.16" };

            Assert.IsEmpty(SusSetDoctor.DetectVersionMismatches(manifest, actual));
        }

        [Test]
        public void DetectVersionMismatches_NoActualSignal_SilentlySkipped()
        {
            // No CHANGELOG.md readable for this module — must not false-positive.
            var manifest = MakeManifest(("kit", "Kit", "1.0.16"));

            Assert.IsEmpty(SusSetDoctor.DetectVersionMismatches(manifest, new Dictionary<string, string>()));
        }

        // ─── CollectActualPaths (temp-dir fixture, same pattern as SusStarterAssetsTests) ───

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sus-set-doctor-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Test]
        public void CollectActualPaths_IncludesRootAndNestedEntries_ExcludesMeta()
        {
            var sharq = Path.Combine(_root, "Sharq");
            var core = Path.Combine(sharq, "Core");
            Directory.CreateDirectory(core);
            File.WriteAllText(Path.Combine(core, "README.md"), "hi");
            File.WriteAllText(Path.Combine(core, "README.md.meta"), "guid: x");

            var paths = SusSetDoctor.CollectActualPaths(_root, "Sharq");

            CollectionAssert.Contains(paths, "Sharq");
            CollectionAssert.Contains(paths, "Sharq/Core");
            CollectionAssert.Contains(paths, "Sharq/Core/README.md");
            CollectionAssert.DoesNotContain(paths, "Sharq/Core/README.md.meta");
        }

        [Test]
        public void CollectActualPaths_RootMissing_ReturnsEmpty()
        {
            Assert.IsEmpty(SusSetDoctor.CollectActualPaths(_root, "Sharq"));
        }
    }
}
