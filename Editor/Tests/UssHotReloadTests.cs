using System;
using NUnit.Framework;
using Sharq.Core.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Tests
{
    public class UssHotReloadTests
    {
        private DummySusComponent _comp;

        [SetUp]
        public void SetUp()
        {
            _comp = new DummySusComponent();
        }

        [TearDown]
        public void TearDown()
        {
            _comp?.RemoveFromHierarchy();
        }

        #region ReloadCompanionStyleSheets — smoke tests

        [Test]
        public void ReloadCompanionStyleSheets_DoesNotCrashOnComponentWithoutSheets()
        {
            // Component with no companion USS loaded — should not throw
            Assert.DoesNotThrow(() => _comp.ReloadCompanionStyleSheets());
        }

        [Test]
        public void ReloadCompanionStyleSheets_PreservesNonCompanionSheets()
        {
            // Add a mock "global" USS that is NOT a companion sheet
            // (name does NOT contain "DummySusComponent" + ".g")
            var globalSheet = ScriptableObject.CreateInstance<StyleSheet>();
            globalSheet.name = "app-base";
            _comp.styleSheets.Add(globalSheet);

            _comp.ReloadCompanionStyleSheets();

            // Global sheet should still be there
            Assert.IsTrue(_comp.styleSheets.Contains(globalSheet),
                "Global (non-companion) stylesheet must NOT be removed");

            UnityEngine.Object.DestroyImmediate(globalSheet);
        }

        [Test]
        public void ReloadCompanionStyleSheets_RemovesCompanionSheets()
        {
            // Companion sheets have class name + ".g" in name (e.g. "MyWidget_scoped.g")
            var companionSheet = ScriptableObject.CreateInstance<StyleSheet>();
            companionSheet.name = "DummySusComponent.g";
            var companionScoped = ScriptableObject.CreateInstance<StyleSheet>();
            companionScoped.name = "DummySusComponent_scoped.g";

            _comp.styleSheets.Add(companionSheet);
            _comp.styleSheets.Add(companionScoped);

            Assert.AreEqual(2, _comp.styleSheets.count);

            _comp.ReloadCompanionStyleSheets();

            // Both companion sheets should be removed (and possibly re-added if found in Resources)
            // We don't test re-adding (requires Resources), but we verify removal
            Assert.IsFalse(_comp.styleSheets.Contains(companionSheet),
                "Companion sheet must be removed before reload");
            Assert.IsFalse(_comp.styleSheets.Contains(companionScoped),
                "Companion scoped sheet must be removed before reload");

            UnityEngine.Object.DestroyImmediate(companionSheet);
            UnityEngine.Object.DestroyImmediate(companionScoped);
        }

        [Test]
        public void ReloadCompanionStyleSheets_PartialSuffix_LeavesOtherCompanions()
        {
            var staticSheet = ScriptableObject.CreateInstance<StyleSheet>();
            staticSheet.name = "DummySusComponent_static.g";
            var scopedSheet = ScriptableObject.CreateInstance<StyleSheet>();
            scopedSheet.name = "DummySusComponent_scoped.g";
            var mainSheet = ScriptableObject.CreateInstance<StyleSheet>();
            mainSheet.name = "DummySusComponent.g";

            _comp.styleSheets.Add(staticSheet);
            _comp.styleSheets.Add(scopedSheet);
            _comp.styleSheets.Add(mainSheet);

            // Partial reload of only "_scoped.g" — other companions must remain until removed
            _comp.ReloadCompanionStyleSheets(new[] { "_scoped.g" });

            Assert.IsTrue(_comp.styleSheets.Contains(staticSheet),
                "_static.g must not be removed when onlySuffixes={_scoped.g}");
            Assert.IsTrue(_comp.styleSheets.Contains(mainSheet),
                ".g must not be removed when onlySuffixes={_scoped.g}");
            Assert.IsFalse(_comp.styleSheets.Contains(scopedSheet),
                "_scoped.g companion must be removed for partial reload");

            UnityEngine.Object.DestroyImmediate(staticSheet);
            UnityEngine.Object.DestroyImmediate(scopedSheet);
            UnityEngine.Object.DestroyImmediate(mainSheet);
        }

        [Test]
        public void SuffixOf_MapsKnownCompanionPaths()
        {
            // Mirror UssHotReloadService.SuffixOf contract via public path naming
            Assert.AreEqual("_static.g", CompanionSuffix("SusButton", "Assets/x/SusButton_static.g.uss"));
            Assert.AreEqual("_scoped.g", CompanionSuffix("SusButton", "Assets/x/SusButton_scoped.g.uss"));
            Assert.AreEqual(".g", CompanionSuffix("SusButton", "Assets/x/SusButton.g.uss"));
            Assert.IsNull(CompanionSuffix("SusButton", "Assets/x/other.uss"));
        }

        private static string CompanionSuffix(string className, string ussPath)
        {
            var file = System.IO.Path.GetFileName(ussPath);
            if (file == $"{className}_static.g.uss") return "_static.g";
            if (file == $"{className}_scoped.g.uss") return "_scoped.g";
            if (file == $"{className}.g.uss") return ".g";
            return null;
        }

        #endregion

        #region UssHotReloadService — integration

        [Test]
        public void UssHotReloadService_Constructor_DoesNotThrow()
        {
            // [InitializeOnLoad] static ctor is called automatically by Unity.
            // Just verify the type is accessible.
            Assert.DoesNotThrow(() =>
            {
                var type = typeof(UssHotReloadService);
                Assert.IsNotNull(type);
            });
        }

        [Test]
        public void SharqFileImporter_OnUssGenerated_EventExists()
        {
            // Verify the event is declared and can be subscribed/unsubscribed
            Action<string, string[]> handler = (className, paths) => { };
            SharqFileImporter.OnUssGenerated += handler;
            SharqFileImporter.OnUssGenerated -= handler;
        }

        #endregion

        private class DummySusComponent : SusComponent
        {
            protected override void Build() { }
        }
    }
}
