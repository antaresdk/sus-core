using NUnit.Framework;
using Sharq.Core.Editor;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for the pure path/name logic behind <see cref="SusPackageRegistry"/>'s
    /// classic-layout discovery (ARCH-PACK-CLASSIC.md §3 T6, T-367): finding
    /// <c>sharq.gen.json</c> descriptors that live directly under <c>Assets/</c> with no
    /// UPM registration at all. Fixture paths below use a neutral module name ("Widgets") —
    /// this file lives in the free/MIT sus-core repo, so it must not name any paid module.
    /// The AssetDatabase-scanning half (<c>FindAssetsDescriptorRoots</c>) is exercised live
    /// against the real project (see 2026-08-13-builder-4 report) — it isn't re-tested here
    /// because it needs a real, imported asset on disk.
    /// </summary>
    public class SusPackageRegistryTests
    {
        [Test]
        public void IsDescriptorAssetPath_ExactDescriptorFileName_True()
        {
            Assert.IsTrue(SusPackageRegistry.IsDescriptorAssetPath("Assets/Sharq/Widgets/sharq.gen.json"));
        }

        [Test]
        public void IsDescriptorAssetPath_CaseInsensitive_True()
        {
            Assert.IsTrue(SusPackageRegistry.IsDescriptorAssetPath("Assets/Sharq/Widgets/SHARQ.GEN.JSON"));
        }

        [TestCase("Assets/Sharq/Widgets/sharq.gen.json.meta")]
        [TestCase("Assets/Sharq/Widgets/other-sharq.gen.json")]
        [TestCase("Assets/Sharq/Widgets/sharq.gen.notjson")]
        [TestCase("Assets/Sharq/Widgets/README.md")]
        [TestCase("")]
        [TestCase(null)]
        public void IsDescriptorAssetPath_NotExactMatch_False(string assetPath)
        {
            Assert.IsFalse(SusPackageRegistry.IsDescriptorAssetPath(assetPath));
        }

        [Test]
        public void ModuleRootFromDescriptorPath_ReturnsContainingFolder()
        {
            var root = SusPackageRegistry.ModuleRootFromDescriptorPath(
                @"C:\proj\Assets\Sharq\Widgets\sharq.gen.json");

            StringAssert.EndsWith("Widgets", root);
        }

        [Test]
        public void NormalizeRoot_BackslashesAndTrailingSlash_Normalized()
        {
            Assert.AreEqual("C:/proj/Assets/Sharq/Widgets",
                SusPackageRegistry.NormalizeRoot(@"C:\proj\Assets\Sharq\Widgets\"));
        }

        [Test]
        public void NormalizeRoot_DifferentSeparatorsSamePath_ProduceEqualKeys()
        {
            var a = SusPackageRegistry.NormalizeRoot(@"C:\proj\Assets\Sharq\Widgets");
            var b = SusPackageRegistry.NormalizeRoot("C:/proj/Assets/Sharq/Widgets/");

            Assert.AreEqual(a, b, "Assets-scan roots must dedup against UPM resolvedPath roots regardless of separator style.");
        }

        [Test]
        public void Find_UnknownName_ReturnsNull()
        {
            // Whatever real descriptors this dev project happens to have registered, an
            // unmatchable name must still resolve to null, not throw.
            Assert.IsNull(SusPackageRegistry.Find("com.sharq-it.this-package-does-not-exist"));
        }
    }
}
