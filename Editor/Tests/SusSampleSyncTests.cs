using System.IO;
using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-507: whole-tree sample sync replaces the hard-coded file-name lists of the
    /// "Refresh … From Package" menus. Contract = what the workspace sample-sync gate (R39) judges.
    /// </summary>
    public class SusSampleSyncTests
    {
        string _root, _src, _dst;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sus-sample-sync-" + Path.GetRandomFileName());
            _src = Path.Combine(_root, "src");
            _dst = Path.Combine(_root, "dst");
            Directory.CreateDirectory(Path.Combine(_src, "Tests"));
            Directory.CreateDirectory(Path.Combine(_dst, "Tests"));
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, true); } catch { /* temp */ }
        }

        void W(string root, string rel, string text)
        {
            var p = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, text);
        }

        string R(string root, string rel) => File.ReadAllText(Path.Combine(root, rel));
        bool E(string root, string rel) => File.Exists(Path.Combine(root, rel));

        [Test]
        public void SyncTree_CopiesNewAndStale_DeletesGone_KeepsLocalDrivers()
        {
            W(_src, "Stories.cs", "v2");               W(_src, "Stories.cs.meta", "guid-src");
            W(_src, "NewStory.cs", "new");             W(_src, "NewStory.cs.meta", "guid-new");
            W(_src, "Tests/LiveTests.cs", "t2");
            W(_src, "Storybook.uss", "uss");
            W(_dst, "Stories.cs", "v1");               W(_dst, "Stories.cs.meta", "guid-dst");
            W(_dst, "Tests/LiveTests.cs", "t1");
            W(_dst, "Gone.cs", "old");                 W(_dst, "Gone.cs.meta", "guid-gone");
            W(_dst, "StorybookShotAll.cs", "local");   W(_dst, "StorybookShotAll.cs.meta", "guid-local");

            var r = SusSampleSync.SyncTree(_src, _dst);

            Assert.AreEqual("v2", R(_dst, "Stories.cs"), "stale code is overwritten");
            Assert.AreEqual("guid-dst", R(_dst, "Stories.cs.meta"), "existing .meta (GUID) is left alone");
            Assert.AreEqual("new", R(_dst, "NewStory.cs"), "new file arrives (was never in any hard-coded list)");
            Assert.AreEqual("guid-new", R(_dst, "NewStory.cs.meta"), "new file brings the package GUID");
            Assert.AreEqual("t2", R(_dst, "Tests/LiveTests.cs"), "subfolders are recursive");
            Assert.AreEqual("uss", R(_dst, "Storybook.uss"));
            Assert.IsFalse(E(_dst, "Gone.cs"), "file removed from the package is removed from the copy");
            Assert.IsFalse(E(_dst, "Gone.cs.meta"), "…with its .meta");
            Assert.IsTrue(E(_dst, "StorybookShotAll.cs"), "*ShotAll.cs local driver survives (T-070)");
            Assert.IsTrue(E(_dst, "StorybookShotAll.cs.meta"));
            CollectionAssert.AreEquivalent(new[] { "Stories.cs", "NewStory.cs", "Tests/LiveTests.cs", "Storybook.uss" }, r.Copied);
            CollectionAssert.AreEquivalent(new[] { "Gone.cs" }, r.Deleted);
            CollectionAssert.AreEquivalent(new[] { "StorybookShotAll.cs" }, r.KeptLocal);
            Assert.AreEqual(0, r.Skipped);
        }

        [Test]
        public void SyncTree_SoftAssets_CopiedOnlyWhenAbsent_EolDifferenceIsUnchanged()
        {
            W(_src, "Storybook.unity", "scene-src");
            W(_src, "Panel.asset", "panel-src");
            W(_dst, "Panel.asset", "panel-editor-rewritten");
            W(_src, "Same.cs", "a\nb\n");
            W(_dst, "Same.cs", "a\r\nb\r\n");

            var r = SusSampleSync.SyncTree(_src, _dst);

            Assert.AreEqual("scene-src", R(_dst, "Storybook.unity"), "missing scene is seeded");
            Assert.AreEqual("panel-editor-rewritten", R(_dst, "Panel.asset"), "existing serialized asset is the editor's (R39 S3 soft)");
            Assert.AreEqual("a\r\nb\r\n", R(_dst, "Same.cs"), "CRLF vs LF is the same text (T-782) — not rewritten");
            CollectionAssert.Contains(r.Unchanged, "Same.cs");
            CollectionAssert.Contains(r.SoftKept, "Panel.asset");
            CollectionAssert.Contains(r.Copied, "Storybook.unity");
        }

        [Test]
        public void SyncTree_UnityScenes_ForceSyncedWhenStale()
        {
            // T-948: soft-kept Storybook.unity left UIDocument disabled / stylesheet null in the
            // sus-dev copy; Refresh must overwrite scenes from Samples~.
            W(_src, "Storybook.unity", "scene-src-enabled");
            W(_dst, "Storybook.unity", "scene-copy-disabled");
            W(_src, "Panel.asset", "panel-src");
            W(_dst, "Panel.asset", "panel-editor");

            var r = SusSampleSync.SyncTree(_src, _dst);

            Assert.AreEqual("scene-src-enabled", R(_dst, "Storybook.unity"), ".unity is force-synced (T-948)");
            Assert.AreEqual("panel-editor", R(_dst, "Panel.asset"), ".asset stays soft-kept");
            CollectionAssert.Contains(r.Copied, "Storybook.unity");
            CollectionAssert.Contains(r.SoftKept, "Panel.asset");
        }

        [Test]
        public void SyncTree_LockedDestination_ReportedNotThrown()
        {
            W(_src, "A.cs", "1");
            W(_src, "B.cs", "2");
            var r = SusSampleSync.SyncTree(_src, _dst, copyFile: (s, d) =>
            {
                if (s.EndsWith("A.cs")) return false;
                File.Copy(s, d, true);
                return true;
            });
            CollectionAssert.AreEquivalent(new[] { "A.cs" }, r.SkippedLocked);
            Assert.AreEqual("2", R(_dst, "B.cs"));
        }

        [Test]
        public void Verify_ReportsStaleAndAbsent_IgnoresSoftMetaAndLocal()
        {
            W(_src, "Stories.cs", "v2");
            W(_src, "NewStory.cs", "new");
            W(_src, "Panel.asset", "p1");
            W(_src, "Stories.cs.meta", "m1");
            W(_src, "Storybook.unity", "scene-src");
            W(_dst, "Stories.cs", "v1");
            W(_dst, "Panel.asset", "p2");
            W(_dst, "Storybook.unity", "scene-stale");
            W(_dst, "StorybookShotAll.cs", "local");

            var drift = SusSampleSync.Verify(_src, _dst);
            CollectionAssert.AreEquivalent(
                new[] { "S1 stale: Stories.cs", "S2 absent: NewStory.cs", "S1 stale: Storybook.unity" },
                drift);

            SusSampleSync.SyncTree(_src, _dst);
            Assert.IsEmpty(SusSampleSync.Verify(_src, _dst), "after a sync the copy is fresh");
            Assert.AreEqual("scene-src", R(_dst, "Storybook.unity"));
            Assert.AreEqual("p2", R(_dst, "Panel.asset"), ".asset still soft-kept through SyncTree");
        }

        [Test]
        public void MatchesAny_Glob()
        {
            Assert.IsTrue(SusSampleSync.MatchesAny("StorybookShotAll.cs", SusSampleSync.DefaultSkipLocal));
            Assert.IsTrue(SusSampleSync.MatchesAny("GameStorybookShotAll.cs", SusSampleSync.DefaultSkipLocal));
            Assert.IsFalse(SusSampleSync.MatchesAny("StorybookShotAll.cs.meta", SusSampleSync.DefaultSkipLocal));
            Assert.IsFalse(SusSampleSync.MatchesAny("StorybookStories.cs", SusSampleSync.DefaultSkipLocal));
        }
    }
}
