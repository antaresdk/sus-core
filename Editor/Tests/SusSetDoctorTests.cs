using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sharq.Core.Editor.Diagnostics;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for the pure classification logic behind SUS Set Doctor v2 (ARCH-PACK-CLASSIC.md
    /// §2.3 D7 / §5.5, T-556/T-557) — the "правило атрибуции" that replaced "everything not in
    /// the one shared sus-set.json is a residual". A neutral fixture module name
    /// ("widgets"/"Widgets") is used alongside "core"/"kit"/"game" where a DoD scenario names
    /// them explicitly, since this file lives in the free/MIT sus-core repo. Everything here is
    /// exercised through public/internal static entry points with plain data/temp-dir fixtures —
    /// no live AssetDatabase/PackageManager state, matching the pattern already used by
    /// <see cref="SusStarterAssetsTests"/>/<see cref="SusPackageRegistryTests"/>.
    /// </summary>
    public class SusSetDoctorTests
    {
        private const string Root = "Sharq";

        private static SusModuleManifest MakeModule(string id, string dir, string version, params string[] extraPaths)
        {
            var paths = new List<string> { $"{Root}/{dir}", $"{Root}/{dir}/sus-module.json" };
            paths.AddRange(extraPaths);
            paths.Sort(System.StringComparer.Ordinal);
            return new SusModuleManifest
            {
                schema = SusModuleManifest.Schema, id = id, dir = dir, root = Root,
                package = "com.sharq-it.sus." + id, version = version, sha = "deadbeef",
                paths = paths.ToArray(),
            };
        }

        private static SusSetManifest MakeDescriptor(string setId, string lead, params string[] moduleIds)
        {
            return new SusSetManifest
            {
                schema = SusSetManifest.Schema, set = setId, displayName = setId + " display", version = "1.0.0",
                lead = lead, root = Root, modules = moduleIds,
                sharedPaths = new[]
                {
                    Root, $"{Root}/README.txt", $"{Root}/LICENSE.txt", $"{Root}/Third-Party Notices.txt",
                    $"{Root}/Samples", $"{Root}/sus-set.{setId}.json",
                },
            };
        }

        // ─── DetectUpmCollisions ───────────────────────────────────────────

        [Test]
        public void DetectUpmCollisions_ModulePresentAndUpmInstalled_ReportsError()
        {
            var modules = new[] { MakeModule("core", "Core", "1.0.14") };
            var upm = new HashSet<string> { "com.sharq-it.sus.core" };

            var issues = SusSetDoctor.DetectUpmCollisions(modules, upm);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(SusValidationSeverity.Error, issues[0].Severity);
            StringAssert.Contains("com.sharq-it.sus.core", issues[0].Message);
            StringAssert.Contains("Sharq/Core", issues[0].Message);
        }

        [Test]
        public void DetectUpmCollisions_ModuleNotPresent_NoFinding_EvenIfUpmInstalled()
        {
            // T-557 DoD (д)-adjacent: since D7, a module's own manifest surviving on disk is what
            // makes it "present" — an empty presentModules list (module truly absent) must never
            // false-positive just because the UPM package happens to be registered.
            var upm = new HashSet<string> { "com.sharq-it.sus.core" };

            Assert.IsEmpty(SusSetDoctor.DetectUpmCollisions(new SusModuleManifest[0], upm));
        }

        [Test]
        public void DetectUpmCollisions_ModulePresent_UpmNotInstalled_NoFinding()
        {
            var modules = new[] { MakeModule("core", "Core", "1.0.14") };

            Assert.IsEmpty(SusSetDoctor.DetectUpmCollisions(modules, new HashSet<string>()));
        }

        [Test]
        public void DetectUpmCollisions_OnlyCollidingModuleReported_OthersClean()
        {
            var modules = new[] { MakeModule("core", "Core", "1.0.14"), MakeModule("widgets", "Widgets", "2.0.0") };
            var upm = new HashSet<string> { "com.sharq-it.sus.widgets" };

            var issues = SusSetDoctor.DetectUpmCollisions(modules, upm);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("com.sharq-it.sus.widgets", issues[0].Message);
        }

        [Test]
        public void DetectUpmCollisions_PackageNameMatchIsCaseInsensitive()
        {
            var modules = new[] { MakeModule("core", "Core", "1.0.14") };
            var upm = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "COM.SHARQ-IT.SUS.CORE" };

            Assert.AreEqual(1, SusSetDoctor.DetectUpmCollisions(modules, upm).Count);
        }

        [Test]
        public void DetectUpmCollisions_GameSurvivesKitOnTopOfGameImport()
        {
            // T-550/T-557 DoD (д): the exact repro that motivated D7 — kit-set imported on top of
            // game-set must NOT make the "game" UPM collision undetectable. Since Game/sus-module.json
            // is never touched by kit-set's packer, Game stays in presentModules regardless of which
            // set descriptor(s) are also present — DetectUpmCollisions doesn't even need to know
            // about descriptors to get this right.
            var modules = new[] { MakeModule("core", "Core", "1.0.16"), MakeModule("game", "Game", "1.0.24") };
            var upm = new HashSet<string> { "com.sharq-it.sus.game" };

            var issues = SusSetDoctor.DetectUpmCollisions(modules, upm);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("com.sharq-it.sus.game", issues[0].Message);
        }

        // ─── ClassifyStrayPaths: Residual (т.т.4 §5.5) — T-557 DoD (б) ─────────

        [Test]
        public void ClassifyStrayPaths_NoExtra_Empty()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var actual = kit.paths;

            Assert.IsEmpty(SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, new SusSetManifest[0], actual));
        }

        [Test]
        public void ClassifyStrayPaths_FileUnderPresentModuleNotInItsManifest_ResidualWithDeleteHint()
        {
            // T-557 DoD (б): "файл под Kit/, которого нет в Kit/sus-module.json -> Residual с хинтом".
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var actual = kit.paths.Append($"{Root}/Kit/OldFile.cs").ToArray();

            var issues = SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, new SusSetManifest[0], actual);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.Residual", issues[0].Category);
            Assert.AreEqual(SusValidationSeverity.Warning, issues[0].Severity);
            StringAssert.Contains("Sharq/Kit/OldFile.cs", issues[0].Message);
            StringAssert.Contains("Delete", issues[0].FixHint);
        }

        [Test]
        public void ClassifyStrayPaths_ResidualWholeStaleFolder_CollapsedToFolderOnly()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var actual = kit.paths.Concat(new[]
            {
                $"{Root}/Kit/OldStuff", $"{Root}/Kit/OldStuff/A.cs", $"{Root}/Kit/OldStuff/B.cs",
            }).ToArray();

            var issues = SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, new SusSetManifest[0], actual);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("Sharq/Kit/OldStuff", issues[0].Message);
            StringAssert.DoesNotContain("A.cs", issues[0].Message);
        }

        [Test]
        public void ClassifyStrayPaths_RealCaseSusIconRenamed_Residual()
        {
            // Real audit case (§2.4): SusIcon.g.cs -> SusIconElement.g.cs. The module's own new
            // manifest no longer lists the old generated file; it must show up as residual.
            var kit = MakeModule("kit", "Kit", "1.0.16",
                $"{Root}/Kit/Runtime", $"{Root}/Kit/Runtime/Generated", $"{Root}/Kit/Runtime/Generated/SusIconElement.g.cs");
            var actual = kit.paths.Append($"{Root}/Kit/Runtime/Generated/SusIcon.g.cs").ToArray();

            var issues = SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, new SusSetManifest[0], actual);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.Residual", issues[0].Category);
            StringAssert.Contains("Sharq/Kit/Runtime/Generated/SusIcon.g.cs", issues[0].Message);
        }

        [Test]
        public void ClassifyStrayPaths_FileUnderModuleSamplesSubtreeNotListed_Residual()
        {
            // §5.5 "владение": a module owns TWO subtrees — <root>/<dir>/** AND
            // <root>/Samples/<dir>/** (T-534) — a stray file in the samples subtree is residual too.
            var kit = MakeModule("kit", "Kit", "1.0.16", $"{Root}/Samples/Kit", $"{Root}/Samples/Kit/Storybook.uss");
            var actual = kit.paths.Append($"{Root}/Samples/Kit/Old.uss").ToArray();

            var issues = SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, new SusSetManifest[0], actual);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.Residual", issues[0].Category);
            StringAssert.Contains("Sharq/Samples/Kit/Old.uss", issues[0].Message);
        }

        [Test]
        public void ClassifyStrayPaths_Residual_MentionsDeleteWholeFolderOnlyWithSingleDescriptor()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var actual = kit.paths.Append($"{Root}/Kit/Old.cs").ToArray();
            var oneDescriptor = new[] { MakeDescriptor("kit-set", "kit", "core", "kit") };
            var twoDescriptors = new[] { MakeDescriptor("kit-set", "kit", "core", "kit"), MakeDescriptor("game-set", "game", "core", "kit", "game") };

            var withOne = SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, oneDescriptor, actual.Concat(oneDescriptor[0].sharedPaths));
            var withTwo = SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, twoDescriptors, actual.Concat(twoDescriptors.SelectMany(d => d.sharedPaths)));

            StringAssert.Contains("delete the whole", withOne.Single(i => i.Category == "SetDoctor.Residual").FixHint);
            StringAssert.DoesNotContain("delete the whole", withTwo.Single(i => i.Category == "SetDoctor.Residual").FixHint);
        }

        // ─── ClassifyStrayPaths: Unattributed (т.5 §5.5) — T-557 DoD (в) ───────

        [Test]
        public void ClassifyStrayPaths_FolderNotOwnedByAnyPresentModule_UnattributedWithoutDeleteHint()
        {
            // T-557 DoD (в): "папка Sharq/MyOwnStuff -> Unattributed без 'delete'".
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var actual = kit.paths.Append($"{Root}/MyOwnStuff").ToArray();

            var issues = SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, new SusSetManifest[0], actual);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.Unattributed", issues[0].Category);
            Assert.AreEqual(SusValidationSeverity.Warning, issues[0].Severity);
            StringAssert.Contains("Sharq/MyOwnStuff", issues[0].Message);
            StringAssert.DoesNotContain("Delete", issues[0].FixHint);
            StringAssert.DoesNotContain("delete", issues[0].FixHint);
        }

        [Test]
        public void ClassifyStrayPaths_ModuleRemovedButFilesRemain_UnattributedNotResidual()
        {
            // The other T-550 half: a module whose OWN manifest is gone entirely (not just this
            // module — a sibling's presence must not attribute an absent module's leftovers to it).
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var actual = kit.paths.Concat(new[] { $"{Root}/Game", $"{Root}/Game/Foo.cs" }).ToArray();

            var issues = SusSetDoctor.ClassifyStrayPaths(Root, new[] { kit }, new SusSetManifest[0], actual);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.Unattributed", issues[0].Category);
            StringAssert.Contains("Sharq/Game", issues[0].Message);
        }

        // ─── DetectModuleManifestMissing — T-557 DoD (г) ───────────────────────

        [Test]
        public void DetectModuleManifestMissing_FolderPresentManifestGone_Reported()
        {
            // T-557 DoD (г): "Sharq/Game без sus-module.json при дескрипторе game-set -> ModuleManifestMissing".
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var gameSet = MakeDescriptor("game-set", "game", "core", "kit", "game");
            var actual = kit.paths.Concat(new[] { $"{Root}/Game", $"{Root}/Game/Runtime" }).ToArray();

            var issues = SusSetDoctor.DetectModuleManifestMissing(Root, new[] { gameSet }, new[] { kit }, actual);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.ModuleManifestMissing", issues[0].Category);
            StringAssert.Contains("Sharq/Game", issues[0].Message);
            StringAssert.DoesNotContain("Delete", issues[0].FixHint);
        }

        [Test]
        public void DetectModuleManifestMissing_FolderAbsentEntirely_NotReported()
        {
            // Nowhere on disk at all -> DetectIncompleteSets' job, not this one.
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var gameSet = MakeDescriptor("game-set", "game", "core", "kit", "game");

            Assert.IsEmpty(SusSetDoctor.DetectModuleManifestMissing(Root, new[] { gameSet }, new[] { kit }, kit.paths));
        }

        [Test]
        public void DetectModuleManifestMissing_ManifestPresent_NoFinding()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var game = MakeModule("game", "Game", "1.0.24");
            var gameSet = MakeDescriptor("game-set", "game", "core", "kit", "game");

            Assert.IsEmpty(SusSetDoctor.DetectModuleManifestMissing(Root, new[] { gameSet }, new[] { kit, game }, kit.paths.Concat(game.paths)));
        }

        // ─── DetectIncompleteSets ───────────────────────────────────────────

        [Test]
        public void DetectIncompleteSets_ModuleMissingFilesAndManifest_Reported()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var gameSet = MakeDescriptor("game-set", "game", "kit", "game"); // "game" alone is incomplete here

            var issues = SusSetDoctor.DetectIncompleteSets(Root, new[] { gameSet }, new[] { kit }, kit.paths);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.IncompleteSet", issues[0].Category);
            StringAssert.Contains("game", issues[0].Message);
        }

        [Test]
        public void DetectIncompleteSets_ModuleFolderPresentManifestMissing_NotReported()
        {
            // That combination is ModuleManifestMissing's scenario, not this one.
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var gameSet = MakeDescriptor("game-set", "game", "kit", "game");
            var actual = kit.paths.Append($"{Root}/Game").ToArray();

            Assert.IsEmpty(SusSetDoctor.DetectIncompleteSets(Root, new[] { gameSet }, new[] { kit }, actual));
        }

        [Test]
        public void DetectIncompleteSets_AllModulesPresent_NoFinding()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var kitSet = MakeDescriptor("kit-set", "kit", "kit");

            Assert.IsEmpty(SusSetDoctor.DetectIncompleteSets(Root, new[] { kitSet }, new[] { kit }, kit.paths));
        }

        // ─── DetectVersionMismatches ────────────────────────────────────────

        [Test]
        public void DetectVersionMismatches_Mismatch_ReportsWarning()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var actual = new Dictionary<string, string> { ["kit"] = "1.0.14" };

            var issues = SusSetDoctor.DetectVersionMismatches(new[] { kit }, actual);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(SusValidationSeverity.Warning, issues[0].Severity);
            StringAssert.Contains("1.0.14", issues[0].Message);
            StringAssert.Contains("1.0.16", issues[0].Message);
        }

        [Test]
        public void DetectVersionMismatches_Match_NoFinding()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");
            var actual = new Dictionary<string, string> { ["kit"] = "1.0.16" };

            Assert.IsEmpty(SusSetDoctor.DetectVersionMismatches(new[] { kit }, actual));
        }

        [Test]
        public void DetectVersionMismatches_NoActualSignal_SilentlySkipped()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");

            Assert.IsEmpty(SusSetDoctor.DetectVersionMismatches(new[] { kit }, new Dictionary<string, string>()));
        }

        // ─── DetectRelocatedModules / DetectRelocatedDescriptors ───────────────

        [Test]
        public void DetectRelocatedModules_DeclaredMatchesActual_NoFinding()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");

            Assert.IsEmpty(SusSetDoctor.DetectRelocatedModules(new[] { (kit, Root, "Kit") }));
        }

        [Test]
        public void DetectRelocatedModules_ActualDirDiffers_ReportsWarningWithoutDelete()
        {
            var kit = MakeModule("kit", "Kit", "1.0.16");

            var issues = SusSetDoctor.DetectRelocatedModules(new[] { (kit, Root, "KitRenamed") });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.Relocated", issues[0].Category);
            Assert.AreEqual(SusValidationSeverity.Warning, issues[0].Severity);
            StringAssert.DoesNotContain("Delete", issues[0].FixHint ?? "");
        }

        [Test]
        public void DetectRelocatedDescriptors_DeclaredMatchesActual_NoFinding()
        {
            var kitSet = MakeDescriptor("kit-set", "kit", "kit");

            Assert.IsEmpty(SusSetDoctor.DetectRelocatedDescriptors(new[] { (kitSet, Root) }));
        }

        [Test]
        public void DetectRelocatedDescriptors_ActualRootDiffers_ReportsWarning()
        {
            var kitSet = MakeDescriptor("kit-set", "kit", "kit");

            var issues = SusSetDoctor.DetectRelocatedDescriptors(new[] { (kitSet, "SharqRenamed") });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("SetDoctor.Relocated", issues[0].Category);
        }

        // ─── T-557 DoD (а): kit + game manifests together -> 0 findings ───────

        [Test]
        public void FullPipeline_KitAndGameManifestsPresent_ZeroFindings()
        {
            var core = MakeModule("core", "Core", "1.0.16");
            var router = MakeModule("router", "Router", "1.0.8");
            var kit = MakeModule("kit", "Kit", "1.0.17");
            var game = MakeModule("game", "Game", "1.0.24");
            var modules = new[] { core, router, kit, game };
            var kitSet = MakeDescriptor("kit-set", "kit", "core", "router", "kit");
            var gameSet = MakeDescriptor("game-set", "game", "core", "router", "kit", "game");
            var descriptors = new[] { kitSet, gameSet };

            var actual = modules.SelectMany(m => m.paths)
                .Concat(descriptors.SelectMany(d => d.sharedPaths))
                .Distinct()
                .ToList();

            var issues = new List<SusValidationIssue>();
            issues.AddRange(SusSetDoctor.DetectUpmCollisions(modules, new HashSet<string>()));
            issues.AddRange(SusSetDoctor.ClassifyStrayPaths(Root, modules, descriptors, actual));
            issues.AddRange(SusSetDoctor.DetectModuleManifestMissing(Root, descriptors, modules, actual));
            issues.AddRange(SusSetDoctor.DetectIncompleteSets(Root, descriptors, modules, actual));
            issues.AddRange(SusSetDoctor.DetectVersionMismatches(modules, new Dictionary<string, string>()));
            issues.AddRange(SusSetDoctor.DetectRelocatedModules(modules.Select(m => (m, Root, m.dir)).ToList()));
            issues.AddRange(SusSetDoctor.DetectRelocatedDescriptors(descriptors.Select(d => (d, Root)).ToList()));

            CollectionAssert.IsEmpty(issues);
        }

        [Test]
        public void FullPipeline_KitOnTopOfGame_GameNeverAskedToBeDeleted()
        {
            // T-550's exact repro, at the classification level: kit-set re-imported over an
            // existing game-set install. Game/sus-module.json is untouched by kit's packer
            // (D7) so it stays present — no finding may suggest deleting anything under Game/.
            var core = MakeModule("core", "Core", "1.0.17");
            var router = MakeModule("router", "Router", "1.0.9");
            var kit = MakeModule("kit", "Kit", "1.0.18");
            var game = MakeModule("game", "Game", "1.0.24"); // stale kit descriptor doesn't know it — still present as manifest
            var modules = new[] { core, router, kit, game };
            var kitSet = MakeDescriptor("kit-set", "kit", "core", "router", "kit"); // only descriptor now present
            var descriptors = new[] { kitSet };

            var actual = modules.SelectMany(m => m.paths).Concat(kitSet.sharedPaths).Distinct().ToList();

            var issues = new List<SusValidationIssue>();
            issues.AddRange(SusSetDoctor.ClassifyStrayPaths(Root, modules, descriptors, actual));
            issues.AddRange(SusSetDoctor.DetectModuleManifestMissing(Root, descriptors, modules, actual));
            issues.AddRange(SusSetDoctor.DetectIncompleteSets(Root, descriptors, modules, actual));

            foreach (var issue in issues)
            {
                Assert.AreNotEqual("SetDoctor.Residual", issue.Category, "no finding may attribute Game/ as residual of kit-set");
                if (issue.FixHint != null)
                    StringAssert.DoesNotContain("Sharq/Game", issue.FixHint.Replace("delete the whole 'Assets/Sharq'", ""));
            }
        }

        // ─── IsSetDescriptorFileName (T-556 naming: sus-set.<set>.json, not bare sus-set.json) ──

        [TestCase("sus-set.kit-set.json", true)]
        [TestCase("sus-set.game-set.json", true)]
        [TestCase("SUS-SET.KIT-SET.JSON", true)]
        [TestCase("sus-set.json", false)] // pre-T-556 legacy name — never matches again (§2.3 D7 п.3)
        [TestCase("sus-module.json", false)]
        [TestCase("sus-set..json", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsSetDescriptorFileName_Cases(string name, bool expected) =>
            Assert.AreEqual(expected, InvokeIsSetDescriptorFileName(name));

        // SusSetDoctor.IsSetDescriptorFileName is internal (InternalsVisibleTo covers this test
        // assembly) — called directly, not through SusSetDoctorAutoRun.IsManifestPath (which also
        // matches sus-module.json and is tested separately in SusSetDoctorAutoRunTests).
        private static bool InvokeIsSetDescriptorFileName(string name) => SusSetDoctor.IsSetDescriptorFileName(name);

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
