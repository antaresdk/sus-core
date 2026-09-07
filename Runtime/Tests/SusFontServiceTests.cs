using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// T-2767 (D-069 / R120) — SusFontService used to write the typeface as an INLINE
    /// -unity-font-definition, which outranks every USS rule and left the buyer unable to restyle
    /// it. The service is now a pure USS-class switch: the typeface lives in _font.uss and this
    /// type only adds/removes the marker classes. EditMode.
    ///
    /// Contract asserted here: (1) no entry point writes an inline appearance style any more,
    /// (2) the role classes switch exclusively, (3) a font set handed to ApplyFonts is REPORTED
    /// with the Editor route that makes it reach USS rather than silently ignored. What the
    /// classes actually resolve to needs a live panel — see SusFontUssPlaymodeTests.
    /// </summary>
    public class SusFontServiceTests
    {
        private static Font MakeFont(string name) => new Font(name);

        private static SusFontAsset MakeAsset()
        {
            var asset = ScriptableObject.CreateInstance<SusFontAsset>();
            asset.name = "TestFontSet";
            return asset;
        }

        private static void AssertNoInlineFont(VisualElement el, string what)
        {
            var inline = el.style.unityFontDefinition.value;
            Assert.IsNull(inline.font, $"{what}: legacy Font written inline");
            Assert.IsNull(inline.fontAsset, $"{what}: SDF FontAsset written inline");
        }

        [TearDown]
        public void TearDown()
        {
            SusLog.ResetForTests(SusLogLevel.Warn, defineFloor: false);
        }

        // ── The regression this card exists to close ─────────────────────────────────────────

        [Test]
        public void ApplyFonts_FilledSet_WritesNoInlineStyleAnywhere()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Heading = FontDefinition.FromFont(MakeFont("Heading"));
            var root = new VisualElement();
            var title = new VisualElement();
            title.AddToClassList(SusFontService.HeadingClassName);
            root.Add(title);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Export Font Set to USS"));
            SusFontService.ApplyFonts(root, asset);

            AssertNoInlineFont(root, "root");
            AssertNoInlineFont(title, "marked element");
        }

        [Test]
        public void ApplyToOverlayHost_WritesNoInlineStyle()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            var root = new VisualElement();

            SusFontService.ApplyToOverlayHost(root, asset);

            AssertNoInlineFont(root, "overlay host path");
        }

        [Test]
        public void ResetToDefault_ClearsInlineFontBackToCascade()
        {
            var root = new VisualElement();
            root.style.unityFontDefinition = FontDefinition.FromFont(MakeFont("Legacy"));

            SusFontService.ResetToDefault(root);

            AssertNoInlineFont(root, "after reset");
        }

        // ── Class switching ─────────────────────────────────────────────────────────────────

        [Test]
        public void ClassNameFor_MatchesPublishedConstants()
        {
            Assert.AreEqual(SusFontService.RegularClassName, SusFontService.ClassNameFor(SusFontRole.Regular));
            Assert.AreEqual(SusFontService.MediumClassName, SusFontService.ClassNameFor(SusFontRole.Medium));
            Assert.AreEqual(SusFontService.BoldClassName, SusFontService.ClassNameFor(SusFontRole.Bold));
            Assert.AreEqual(SusFontService.LightClassName, SusFontService.ClassNameFor(SusFontRole.Light));
            Assert.AreEqual(SusFontService.HeadingClassName, SusFontService.ClassNameFor(SusFontRole.Heading));
            Assert.AreEqual(SusFontService.MonoClassName, SusFontService.ClassNameFor(SusFontRole.Mono));
            Assert.AreEqual(SusFontService.CondensedClassName, SusFontService.ClassNameFor(SusFontRole.Condensed));
        }

        [Test]
        public void ApplyRoleClass_IsExclusive()
        {
            var el = new VisualElement();

            SusFontService.ApplyRoleClass(el, SusFontRole.Heading);
            Assert.IsTrue(el.ClassListContains(SusFontService.HeadingClassName));

            SusFontService.ApplyRoleClass(el, SusFontRole.Mono);
            Assert.IsTrue(el.ClassListContains(SusFontService.MonoClassName), "new role applied");
            Assert.IsFalse(el.ClassListContains(SusFontService.HeadingClassName), "previous role removed");
        }

        [Test]
        public void ApplyRoleClass_WritesNoInlineStyle()
        {
            var el = new VisualElement();

            SusFontService.ApplyRoleClass(el, SusFontRole.Bold);

            AssertNoInlineFont(el, "role class");
        }

        [Test]
        public void ClearRoleClasses_RemovesEveryRoleMarker()
        {
            var el = new VisualElement();
            foreach (var cls in SusFontService.RoleClassNames) el.AddToClassList(cls);

            SusFontService.ClearRoleClasses(el);

            foreach (var cls in SusFontService.RoleClassNames)
                Assert.IsFalse(el.ClassListContains(cls), cls);
        }

        // ── Reporting: a font set must never be silently ignored ────────────────────────────

        [Test]
        public void ApplyFonts_FilledSet_NamesTheUssRoute()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Mono = FontDefinition.FromFont(MakeFont("Mono"));
            var root = new VisualElement();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Regular, Mono"));
            SusFontService.ApplyFonts(root, asset);
        }

        [Test]
        public void ApplyFonts_EmptySet_Silent()
        {
            var asset = MakeAsset();
            var root = new VisualElement();

            SusFontService.ApplyFonts(root, asset);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ApplyFonts_NullArguments_Silent()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));

            SusFontService.ApplyFonts(null, asset);
            SusFontService.ApplyFonts(new VisualElement(), null);

            LogAssert.NoUnexpectedReceived();
        }

        // ── Slot fallback chains (pure data, unchanged by T-2767) ───────────────────────────

        [Test]
        public void ResolveCondensed_ChainMatchesDocumentedFallback()
        {
            var asset = MakeAsset();
            var regular = FontDefinition.FromFont(MakeFont("Regular"));
            var bold = FontDefinition.FromFont(MakeFont("Bold"));
            var heading = FontDefinition.FromFont(MakeFont("Heading"));
            var condensed = FontDefinition.FromFont(MakeFont("Condensed"));

            asset.Regular = regular;
            Assert.AreEqual(regular.font, asset.ResolveCondensed().font, "all unset -> Regular");

            asset.Bold = bold;
            Assert.AreEqual(bold.font, asset.ResolveCondensed().font, "Bold set -> Bold (via Heading chain)");

            asset.Heading = heading;
            Assert.AreEqual(heading.font, asset.ResolveCondensed().font, "Heading set -> Heading beats Bold");

            asset.Condensed = condensed;
            Assert.AreEqual(condensed.font, asset.ResolveCondensed().font, "Condensed set -> Condensed wins outright");
        }
    }
}
