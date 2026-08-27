using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// T-2216 — SusFontAsset promises six slots (Regular/Medium/Bold/Light/Heading/Mono) but
    /// SusFontService.ApplyFonts only ever applied Regular; the other five and the three
    /// Resolve* helper methods were dead code (never read, never called). EditMode.
    ///
    /// Contract: ApplyFonts must dispatch every non-Regular slot to elements that opt in via a
    /// marker USS class (SusFontService.&lt;Role&gt;ClassName), and must warn once when a filled
    /// slot has no marked element to apply to (a silent half-applied font set is the defect this
    /// card exists to close).
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

        [TearDown]
        public void TearDown()
        {
            SusLog.ResetForTests(SusLogLevel.Warn, defineFloor: false);
        }

        [Test]
        public void ApplyFonts_AppliesRegularToRoot()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            var root = new VisualElement();

            SusFontService.ApplyFonts(root, asset);

            Assert.AreEqual(asset.Regular.font, root.style.unityFontDefinition.value.font);
        }

        [Test]
        public void ApplyFonts_AppliesHeadingToMarkedElement()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Heading = FontDefinition.FromFont(MakeFont("Heading"));
            var root = new VisualElement();
            var title = new VisualElement();
            title.AddToClassList(SusFontService.HeadingClassName);
            root.Add(title);

            SusFontService.ApplyFonts(root, asset);

            Assert.AreEqual(asset.Heading.font, title.style.unityFontDefinition.value.font);
        }

        [Test]
        public void ApplyFonts_HeadingUnset_MarkedElementFallsBackToBold()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Bold = FontDefinition.FromFont(MakeFont("Bold"));
            // Heading left unset on purpose — ResolveHeading() must fall back to Bold.
            var root = new VisualElement();
            var title = new VisualElement();
            title.AddToClassList(SusFontService.HeadingClassName);
            root.Add(title);

            SusFontService.ApplyFonts(root, asset);

            Assert.AreEqual(asset.Bold.font, title.style.unityFontDefinition.value.font);
        }

        [Test]
        public void ApplyFonts_AppliesMonoToMarkedElement()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Mono = FontDefinition.FromFont(MakeFont("Mono"));
            var root = new VisualElement();
            var stat = new VisualElement();
            stat.AddToClassList(SusFontService.MonoClassName);
            root.Add(stat);

            SusFontService.ApplyFonts(root, asset);

            Assert.AreEqual(asset.Mono.font, stat.style.unityFontDefinition.value.font);
        }

        [Test]
        public void ApplyFonts_AppliesBoldMediumLightToMarkedElements()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Bold = FontDefinition.FromFont(MakeFont("Bold"));
            asset.Medium = FontDefinition.FromFont(MakeFont("Medium"));
            asset.Light = FontDefinition.FromFont(MakeFont("Light"));
            var root = new VisualElement();
            var bold = new VisualElement(); bold.AddToClassList(SusFontService.BoldClassName); root.Add(bold);
            var medium = new VisualElement(); medium.AddToClassList(SusFontService.MediumClassName); root.Add(medium);
            var light = new VisualElement(); light.AddToClassList(SusFontService.LightClassName); root.Add(light);

            SusFontService.ApplyFonts(root, asset);

            Assert.AreEqual(asset.Bold.font, bold.style.unityFontDefinition.value.font);
            Assert.AreEqual(asset.Medium.font, medium.style.unityFontDefinition.value.font);
            Assert.AreEqual(asset.Light.font, light.style.unityFontDefinition.value.font);
        }

        [Test]
        public void ApplyFonts_AppliesCondensedToMarkedElement()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Condensed = FontDefinition.FromFont(MakeFont("Condensed"));
            var root = new VisualElement();
            var heroTitle = new VisualElement();
            heroTitle.AddToClassList(SusFontService.CondensedClassName);
            root.Add(heroTitle);

            SusFontService.ApplyFonts(root, asset);

            Assert.AreEqual(asset.Condensed.font, heroTitle.style.unityFontDefinition.value.font);
        }

        [Test]
        public void ApplyFonts_CondensedUnset_FallsBackThroughHeadingChain()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Bold = FontDefinition.FromFont(MakeFont("Bold"));
            // Condensed and Heading both unset -> ResolveCondensed() must reach Bold via ResolveHeading().
            var root = new VisualElement();
            var heroTitle = new VisualElement();
            heroTitle.AddToClassList(SusFontService.CondensedClassName);
            root.Add(heroTitle);

            SusFontService.ApplyFonts(root, asset);

            Assert.AreEqual(asset.Bold.font, heroTitle.style.unityFontDefinition.value.font);
        }

        [Test]
        public void ApplyFonts_FilledSlotWithoutMarkedElement_WarnsOnce()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Mono = FontDefinition.FromFont(MakeFont("Mono")); // filled, but no element carries the marker class
            var root = new VisualElement();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Mono"));
            SusFontService.ApplyFonts(root, asset);
        }

        [Test]
        public void ApplyFonts_NoFilledExtraSlots_NoWarning()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            var root = new VisualElement();

            SusFontService.ApplyFonts(root, asset);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ApplyFonts_FilledSlotWithMarkedElement_NoWarning()
        {
            var asset = MakeAsset();
            asset.Regular = FontDefinition.FromFont(MakeFont("Regular"));
            asset.Mono = FontDefinition.FromFont(MakeFont("Mono"));
            var root = new VisualElement();
            var stat = new VisualElement();
            stat.AddToClassList(SusFontService.MonoClassName);
            root.Add(stat);

            SusFontService.ApplyFonts(root, asset);

            LogAssert.NoUnexpectedReceived();
        }

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
