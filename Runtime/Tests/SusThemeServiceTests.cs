using NUnit.Framework;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// R-D8 / T-1123 — SusThemeService (SetTheme / ApplyThemeClasses / ResolveCascadeRoot). EditMode.
    /// </summary>
    public class SusThemeServiceTests
    {
        [TearDown]
        public void TearDown()
        {
            // Restore default after each test (editor domain-reload-off safe via ResetStatics on enter Play).
            SusThemeService.Current.Value = SusTheme.Dark;
        }

        [Test]
        public void SetTheme_AddsThemeClass_AndUpdatesCurrent()
        {
            var root = new VisualElement();
            SusThemeService.Instance.SetTheme(root, SusTheme.Light);

            Assert.IsTrue(root.ClassListContains("theme-light"));
            Assert.IsFalse(root.ClassListContains("theme-dark"));
            Assert.AreEqual(SusTheme.Light, SusThemeService.Current.Value);
        }

        [Test]
        public void SetTheme_ReplacesPreviousThemeClass()
        {
            var root = new VisualElement();
            SusThemeService.Instance.SetTheme(root, SusTheme.Light);
            SusThemeService.Instance.SetTheme(root, SusTheme.Dark);

            Assert.IsTrue(root.ClassListContains("theme-dark"));
            Assert.IsFalse(root.ClassListContains("theme-light"));
            Assert.AreEqual(SusTheme.Dark, SusThemeService.Current.Value);
        }

        [Test]
        public void SetTheme_CustomName_UsesCssClass()
        {
            var root = new VisualElement();
            var midnight = new SusTheme("midnight");
            SusThemeService.Instance.SetTheme(root, midnight);

            Assert.IsTrue(root.ClassListContains("theme-midnight"));
            Assert.AreEqual("midnight", SusThemeService.Current.Value.Name);
        }

        [Test]
        public void SetTheme_NullRoot_WithoutCascade_IsNoOp()
        {
            var before = SusThemeService.Current.Value;
            Assert.DoesNotThrow(() => SusThemeService.Instance.SetTheme(null, SusTheme.Light));
            Assert.AreEqual(before, SusThemeService.Current.Value);
        }

        [Test]
        public void ApplyThemeClasses_UsesCurrent()
        {
            SusThemeService.Current.Value = SusTheme.Light;
            var el = new VisualElement();
            SusThemeService.ApplyThemeClasses(el);

            Assert.IsTrue(el.ClassListContains("theme-light"));
        }

        [Test]
        public void ResolveCascadeRoot_PrefersThemeClassAncestor()
        {
            var cascade = new VisualElement();
            cascade.AddToClassList("theme-dark");
            var child = new VisualElement();
            cascade.Add(child);

            var resolved = SusThemeService.ResolveCascadeRoot(child);
            Assert.AreSame(cascade, resolved);
        }

        [Test]
        public void ResolveCascadeRoot_NullHint_WithoutBootstrap_ReturnsNull()
        {
            // No TokenCascadeRoot in bare EditMode fixture → null hint → null.
            Assert.IsNull(SusThemeService.ResolveCascadeRoot(null));
        }

        [Test]
        public void CopyStyleSheets_CopiesMissingSheetsOnly()
        {
            var from = new VisualElement();
            var to = new VisualElement();
            // Without real StyleSheet assets, count stays 0 — API must not throw.
            Assert.DoesNotThrow(() => SusThemeService.CopyStyleSheets(from, to));
            Assert.DoesNotThrow(() => SusThemeService.CopyStyleSheets(null, to));
            Assert.DoesNotThrow(() => SusThemeService.CopyStyleSheets(from, null));
        }
    }
}
