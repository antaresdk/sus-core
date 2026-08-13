using NUnit.Framework;
using Sharq.Core.Editor.Diagnostics;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for <see cref="SusSetDoctorAutoRun.IsManifestPath"/> — the filesystem-only half of
    /// the trigger that decides whether an asset-import batch is worth re-running Set Doctor for
    /// (T-368). The other half (a UPM package-name diff via <c>PackageInfo</c>) isn't unit-tested
    /// here — it needs live PackageManager state, same boundary already documented for
    /// <c>SusPackageRegistry</c>'s AssetDatabase-scanning half.
    /// </summary>
    public class SusSetDoctorAutoRunTests
    {
        [Test]
        public void IsManifestPath_ManifestFile_True()
        {
            Assert.IsTrue(SusSetDoctorAutoRun.IsManifestPath("Assets/Sharq/sus-set.json"));
        }

        [Test]
        public void IsManifestPath_ManifestFileCaseInsensitive_True()
        {
            Assert.IsTrue(SusSetDoctorAutoRun.IsManifestPath("Assets/Sharq/SUS-SET.JSON"));
        }

        [Test]
        public void IsManifestPath_NestedUnderModule_StillMatchesByFileName()
        {
            // The manifest always lives at the set root, but the check is filename-only —
            // cheap and doesn't need to know the root path in advance.
            Assert.IsTrue(SusSetDoctorAutoRun.IsManifestPath("Assets/Sharq/Kit/sus-set.json"));
        }

        [TestCase("Assets/Sharq/Core/Runtime/SusApp.cs")]
        [TestCase("Assets/SomeOtherAsset.png")]
        [TestCase("Assets/Sharq/sus-set.json.meta")]
        [TestCase("")]
        [TestCase(null)]
        public void IsManifestPath_UnrelatedPath_False(string path)
        {
            Assert.IsFalse(SusSetDoctorAutoRun.IsManifestPath(path));
        }
    }
}
