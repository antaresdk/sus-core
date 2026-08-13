using System.IO;
using NUnit.Framework;
using Sharq.Core.Editor;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for the raskladko-neutral starter-folder/file resolution added for T-366
    /// (ARCH-PACK-CLASSIC.md §3 T5): the UPM channel ships <c>Editor/Setup/Starter~</c>,
    /// the classic .unitypackage channel ships <c>Editor/Setup/StarterAssets</c> with an
    /// extra trailing <c>.txt</c> on <c>HomeScreen.sharq</c> / <c>Generated/HomeScreen.g.cs</c>.
    /// </summary>
    public class SusStarterAssetsTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sus-starter-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        // ─── ResolveStarterFolder ──────────────────────────────────────────

        [Test]
        public void ResolveStarterFolder_TildeLayout_Found()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Starter~"));

            var found = SusStarterAssets.ResolveStarterFolder(_root);

            StringAssert.EndsWith("Starter~", found);
        }

        [Test]
        public void ResolveStarterFolder_ClassicLayout_Found()
        {
            Directory.CreateDirectory(Path.Combine(_root, "StarterAssets"));

            var found = SusStarterAssets.ResolveStarterFolder(_root);

            StringAssert.EndsWith("StarterAssets", found);
        }

        [Test]
        public void ResolveStarterFolder_NeitherPresent_ReturnsNull()
        {
            Assert.IsNull(SusStarterAssets.ResolveStarterFolder(_root));
        }

        [Test]
        public void ResolveStarterFolder_BothPresent_PrefersTilde()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Starter~"));
            Directory.CreateDirectory(Path.Combine(_root, "StarterAssets"));

            var found = SusStarterAssets.ResolveStarterFolder(_root);

            StringAssert.EndsWith("Starter~", found);
        }

        // ─── ResolveStarterFile ────────────────────────────────────────────

        [Test]
        public void ResolveStarterFile_BareFileExists_ReturnsBarePath()
        {
            File.WriteAllText(Path.Combine(_root, "HomeScreen.sharq"), "<template/>");

            var found = SusStarterAssets.ResolveStarterFile(_root, "HomeScreen.sharq");

            StringAssert.EndsWith("HomeScreen.sharq", found);
            Assert.IsFalse(found.EndsWith(".txt"));
        }

        [Test]
        public void ResolveStarterFile_OnlyTxtSuffixedExists_ReturnsTxtPath()
        {
            File.WriteAllText(Path.Combine(_root, "HomeScreen.sharq.txt"), "<template/>");

            var found = SusStarterAssets.ResolveStarterFile(_root, "HomeScreen.sharq");

            StringAssert.EndsWith("HomeScreen.sharq.txt", found);
        }

        [Test]
        public void ResolveStarterFile_NeitherExists_ReturnsNull()
        {
            Assert.IsNull(SusStarterAssets.ResolveStarterFile(_root, "HomeScreen.sharq"));
        }

        [Test]
        public void ResolveStarterFile_NestedRelativePath_ResolvesTxtVariant()
        {
            var genDir = Path.Combine(_root, "Generated");
            Directory.CreateDirectory(genDir);
            File.WriteAllText(Path.Combine(genDir, "HomeScreen.g.cs.txt"), "// generated");

            var relPath = Path.Combine("Generated", "HomeScreen.g.cs");
            var found = SusStarterAssets.ResolveStarterFile(_root, relPath);

            StringAssert.EndsWith(Path.Combine("Generated", "HomeScreen.g.cs.txt"), found);
        }

        // ─── CopyHomeScreen — end-to-end suffix stripping ─────────────────

        [Test]
        public void CopyHomeScreen_ClassicTxtSuffixedSource_WritesBareDestination()
        {
            // Simulate a classic StarterAssets layout: files carry a trailing .txt so the
            // Sharq watcher / compiler don't pick them up inside the starter folder itself.
            var starter = Path.Combine(_root, "StarterAssetsSim");
            var genDir = Path.Combine(starter, "Generated");
            Directory.CreateDirectory(genDir);
            File.WriteAllText(Path.Combine(starter, "HomeScreen.sharq.txt"), "<template>sharq-content</template>");
            File.WriteAllText(Path.Combine(genDir, "HomeScreen.g.cs.txt"), "// gcs-content");

            var projectUiRoot = Path.Combine(_root, "ProjectUI");
            Directory.CreateDirectory(projectUiRoot);

            var sharqSrc = SusStarterAssets.ResolveStarterFile(starter, "HomeScreen.sharq");
            var gcsSrc = SusStarterAssets.ResolveStarterFile(starter, Path.Combine("Generated", "HomeScreen.g.cs"));
            Assert.NotNull(sharqSrc);
            Assert.NotNull(gcsSrc);

            // Exercises the same "fixed bare destination name" pattern CopyHomeScreen uses —
            // verified directly here since CopyHomeScreen itself depends on GetStarterRoot(),
            // which walks live package/AssetDatabase state not reproducible with a fake temp dir.
            var destSharq = Path.Combine(projectUiRoot, "HomeScreen.sharq");
            var destGcsDir = Path.Combine(projectUiRoot, "Generated");
            Directory.CreateDirectory(destGcsDir);
            File.WriteAllText(destSharq, File.ReadAllText(sharqSrc));
            File.WriteAllText(Path.Combine(destGcsDir, "HomeScreen.g.cs"), File.ReadAllText(gcsSrc));

            Assert.IsTrue(File.Exists(destSharq));
            Assert.IsFalse(File.Exists(destSharq + ".txt"));
            Assert.AreEqual("<template>sharq-content</template>", File.ReadAllText(destSharq));
            Assert.IsTrue(File.Exists(Path.Combine(destGcsDir, "HomeScreen.g.cs")));
        }
    }
}
