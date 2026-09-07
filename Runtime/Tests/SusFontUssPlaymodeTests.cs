using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// T-2767 (D-069 / R120) — the marker classes SusFontService manages must carry a real
    /// typeface FROM USS, on a live panel. Before this card they were inert on the USS side: the
    /// class existed only as a C# query marker and the font arrived as an inline style. These
    /// tests read resolvedStyle (what the cascade actually produced), not the inline style, so
    /// they fail if the declaration ever leaves _font.uss again.
    /// </summary>
    public class SusFontUssPlaymodeTests : UIDocumentTestHelper
    {
        // Always the packaged SUS theme sheet — the project may ship its own test PanelSettings
        // without the SusDefault TSS, and then there is no _font.uss in the panel to judge.
        protected override PanelSettings GetPanelSettings() => CreateTestPanelSettings();

        private Label Mark(string className)
        {
            var label = new Label("Ag");
            if (className != null) label.AddToClassList(className);
            Root.Add(label);
            return label;
        }

        private static FontDefinition Resolved(VisualElement el) => el.resolvedStyle.unityFontDefinition;

        private static string Name(FontDefinition fd) =>
            fd.fontAsset != null ? fd.fontAsset.name : fd.font != null ? fd.font.name : "<none>";

        private static void AssertHasFont(VisualElement el, string what)
        {
            var fd = Resolved(el);
            Assert.IsTrue(fd.fontAsset != null || fd.font != null,
                $"{what}: the cascade produced no typeface at all — _font.uss is not in the panel");
        }

        [UnityTest]
        public IEnumerator BodyText_ResolvesFromUss_WithNoServiceCall()
        {
            var plain = Mark(null);
            yield return WaitFrame();

            AssertHasFont(plain, "untagged label");
        }

        [UnityTest]
        public IEnumerator BoldMarkerClass_ResolvesToADifferentTypefaceThanBody()
        {
            var plain = Mark(null);
            var bold = Mark(SusFontService.BoldClassName);
            yield return WaitFrame();

            AssertHasFont(bold, "sus-font-bold");
            Assert.AreNotEqual(Name(Resolved(plain)), Name(Resolved(bold)),
                "sus-font-bold resolved to the body typeface — the marker class carries no font");
        }

        [UnityTest]
        public IEnumerator HeadingMarkerClass_FollowsTheTokenChainToTheBoldFamily()
        {
            var bold = Mark(SusFontService.BoldClassName);
            var heading = Mark(SusFontService.HeadingClassName);
            yield return WaitFrame();

            // --font-family-heading falls back to --font-family-bold when no project/skin
            // override is loaded (same chain SusFontAsset.ResolveHeading() documents).
            Assert.AreEqual(Name(Resolved(bold)), Name(Resolved(heading)),
                "sus-font-heading did not follow --font-family-heading -> --font-family-bold");
        }

        [UnityTest]
        public IEnumerator EveryRoleMarkerClass_ResolvesToSomeTypeface()
        {
            var labels = new Label[SusFontService.RoleClassNames.Length];
            for (var i = 0; i < labels.Length; i++)
                labels[i] = Mark(SusFontService.RoleClassNames[i]);
            yield return WaitFrame();

            for (var i = 0; i < labels.Length; i++)
                AssertHasFont(labels[i], SusFontService.RoleClassNames[i]);
        }

        [UnityTest]
        public IEnumerator ApplyRoleClass_SwitchesTheResolvedTypeface_AndClearReturnsIt()
        {
            var plain = Mark(null);
            var el = Mark(null);
            yield return WaitFrame();
            var body = Name(Resolved(plain));
            Assert.AreEqual(body, Name(Resolved(el)), "precondition: untagged element uses body font");

            SusFontService.ApplyRoleClass(el, SusFontRole.Bold);
            yield return WaitFrame();
            Assert.AreNotEqual(body, Name(Resolved(el)), "ApplyRoleClass did not change the resolved font");

            SusFontService.ClearRoleClasses(el);
            yield return WaitFrame();
            Assert.AreEqual(body, Name(Resolved(el)), "ClearRoleClasses did not hand the element back to the cascade");
        }

        [UnityTest]
        public IEnumerator MarkerClass_OutranksAComponentRuleOnTheSameElement()
        {
            // The marker is deliberate markup, so it must win over a component's own single-class
            // -unity-font-definition rule that loads later in the cascade. `.sus-label` is such a
            // rule (packaged _text.uss); tagging it must still switch the typeface.
            var label = new Label("Ag");
            label.AddToClassList("sus-label");
            Root.Add(label);
            var tagged = new Label("Ag");
            tagged.AddToClassList("sus-label");
            tagged.AddToClassList(SusFontService.BoldClassName);
            Root.Add(tagged);
            yield return WaitFrame();

            Assert.AreNotEqual(Name(Resolved(label)), Name(Resolved(tagged)),
                "a component class beat the deliberate sus-font-bold marker");
        }
    }
}
