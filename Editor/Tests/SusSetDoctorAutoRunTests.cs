using NUnit.Framework;
using Sharq.Core.Editor.Diagnostics;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for <see cref="SusSetDoctorAutoRun.IsManifestPath"/> — the filesystem-only half of
    /// the trigger that decides whether an asset-import batch is worth re-running Set Doctor for
    /// (T-368, updated for T-556/T-557's per-module manifest + per-set descriptor split). The
    /// other half (a UPM package-name diff via <c>PackageInfo</c>) isn't unit-tested here — it
    /// needs live PackageManager state, same boundary already documented for
    /// <c>SusPackageRegistry</c>'s AssetDatabase-scanning half.
    /// </summary>
    public class SusSetDoctorAutoRunTests
    {
        [Test]
        public void IsManifestPath_ModuleManifestFile_True()
        {
            Assert.IsTrue(SusSetDoctorAutoRun.IsManifestPath("Assets/Sharq/Kit/sus-module.json"));
        }

        [Test]
        public void IsManifestPath_ModuleManifestFileCaseInsensitive_True()
        {
            Assert.IsTrue(SusSetDoctorAutoRun.IsManifestPath("Assets/Sharq/Kit/SUS-MODULE.JSON"));
        }

        [Test]
        public void IsManifestPath_SetDescriptorFile_True()
        {
            Assert.IsTrue(SusSetDoctorAutoRun.IsManifestPath("Assets/Sharq/sus-set.kit-set.json"));
        }

        [Test]
        public void IsManifestPath_SetDescriptorFileCaseInsensitive_True()
        {
            Assert.IsTrue(SusSetDoctorAutoRun.IsManifestPath("Assets/Sharq/SUS-SET.GAME-SET.JSON"));
        }

        [Test]
        public void IsManifestPath_LegacyBareSetJson_False()
        {
            // Pre-T-556 name — never written again (§2.3 D7 п.3 / инвариант I15(4)); a stray file
            // with this exact name is not a manifest this trigger needs to react to.
            Assert.IsFalse(SusSetDoctorAutoRun.IsManifestPath("Assets/Sharq/sus-set.json"));
        }

        [TestCase("Assets/Sharq/Core/Runtime/SusApp.cs")]
        [TestCase("Assets/SomeOtherAsset.png")]
        [TestCase("Assets/Sharq/Kit/sus-module.json.meta")]
        [TestCase("Assets/Sharq/sus-set.kit-set.json.meta")]
        [TestCase("")]
        [TestCase(null)]
        public void IsManifestPath_UnrelatedPath_False(string path)
        {
            Assert.IsFalse(SusSetDoctorAutoRun.IsManifestPath(path));
        }
    }
}
