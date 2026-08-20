using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for SusComponent.LoadCompanionStyleSheets()'s base-type-chain fallback (T-1273).
    ///
    /// Tier-B C# subclasses of a Sharq visual-root component (e.g. lumenfall's LfScoreboard :
    /// SusTable, LfNavButton : SusButton) have no .sharq of their own, so no companion USS file
    /// exists under their exact type name. Before this fix, LoadCompanionStyleSheets() resolved
    /// the companion filename from the polymorphic GetType().Name only — for such a subclass that
    /// silently found nothing, and the base's ENTIRE companion stylesheet never attached (found
    /// live for LfScoreboard/LfNavButton while fixing T-1243). The fix walks up the base-type
    /// chain (stopping at SusComponent) when the exact-name lookup finds nothing, so the subclass
    /// inherits its nearest styled ancestor's companion sheet(s) automatically.
    ///
    /// Repro strategy: two throwaway component types (a "base" with its own companion .uss on
    /// disk, and a "Tier-B" subclass with none) resolved through the same editor-only
    /// AssetDatabase fallback path production code already uses when Resources/ doesn't have a
    /// copy (see SusComponent.RegisterEditorGeneratedDir / s_editorGeneratedDirs) — a real,
    /// isolated on-disk .uss asset under a temp Assets/ dir registered just for this test.
    /// </summary>
    public class SusComponentCompanionFallbackTests
    {
        private const string TestDir = "Assets/_T1273CompanionFallbackTestTemp";

        // Must match DummyBaseWithSheet's Type.Name exactly (nested-class Type.Name has no
        // enclosing-type prefix) — LoadCompanionStyleSheetsForType resolves the filename from it.
        private const string BaseUssFileName = "T1273DummyBaseWithSheet.g";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestDir))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(TestDir));

            var ussPath = $"{TestDir}/{BaseUssFileName}.uss";
            File.WriteAllText(ussPath, ".t1273-fallback-marker { color: rgb(1, 2, 3); }");
            AssetDatabase.ImportAsset(ussPath, ImportAssetOptions.ForceSynchronousImport);

            // Register the temp dir as an editor-fallback source (mirrors what a package's own
            // Generated dir registration looks like — see SusComponent.RegisterEditorGeneratedDir).
            SusComponent.RegisterEditorGeneratedDir(TestDir);

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath),
                "Setup sanity: the fixture .uss must import as a StyleSheet asset before the fallback test runs");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TestDir))
                AssetDatabase.DeleteAsset(TestDir);
        }

        [Test]
        public void TierBSubclass_WithNoOwnCompanionSheet_InheritsBaseTypesCompanionSheet()
        {
            var subclass = new T1273DummyTierBSubclass();
            try
            {
                var found = false;
                for (var i = 0; i < subclass.styleSheets.count; i++)
                {
                    var sheet = subclass.styleSheets[i];
                    if (sheet != null && sheet.name == BaseUssFileName) { found = true; break; }
                }

                Assert.IsTrue(found,
                    $"T1273DummyTierBSubclass (no .sharq of its own) must inherit its base type's " +
                    $"companion stylesheet ({BaseUssFileName}.uss) via the base-type-chain fallback; " +
                    $"styleSheets had {subclass.styleSheets.count} sheet(s), none named '{BaseUssFileName}'.");
            }
            finally
            {
                subclass.RemoveFromHierarchy();
            }
        }

        [Test]
        public void BaseType_WithOwnCompanionSheet_StillResolvesByExactName_Unchanged()
        {
            // Baseline: a type that DOES have its own companion sheet must keep resolving it by
            // its own exact name — the fallback must not change behavior for the common case.
            var baseComp = new T1273DummyBaseWithSheet();
            try
            {
                var found = false;
                for (var i = 0; i < baseComp.styleSheets.count; i++)
                {
                    var sheet = baseComp.styleSheets[i];
                    if (sheet != null && sheet.name == BaseUssFileName) { found = true; break; }
                }

                Assert.IsTrue(found,
                    $"T1273DummyBaseWithSheet must resolve its own companion sheet by exact type name.");
            }
            finally
            {
                baseComp.RemoveFromHierarchy();
            }
        }

        private class T1273DummyBaseWithSheet : SusComponent
        {
            protected override void Build() { }
        }

        private class T1273DummyTierBSubclass : T1273DummyBaseWithSheet
        {
        }
    }
}
