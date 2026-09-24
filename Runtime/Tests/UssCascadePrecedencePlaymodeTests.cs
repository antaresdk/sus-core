#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// T-4158 (skin sheet layer, step S0) — pins the UI Toolkit cascade rule the
    /// skin contract relies on, on a live panel, by reading <c>resolvedStyle</c>:
    /// <list type="bullet">
    /// <item>(a) a sheet attached to an ANCESTOR with a MORE specific selector (0,2,0) beats the
    /// element's own companion sheet (0,1,0) — specificity is compared before sheet distance;</item>
    /// <item>(b) at EQUAL specificity (0,1,0) the ancestor sheet loses to the element's own sheet —
    /// the sheet closer to the element wins the tie;</item>
    /// <item>(c) a sheet added to the element's own <c>styleSheets</c> AFTER the companion sheet
    /// wins at equal specificity (source order on the same element), and adding it BEFORE loses.</item>
    /// </list>
    /// The "component" here mirrors how <see cref="SusComponent"/> delivers its companion
    /// <c>.g</c> sheet: <c>styleSheets.Add</c> on the component element itself. The kit fixture
    /// <c>SusMenuButtonSkinCascadeTests</c> repeats (a)–(c) on a real generated sheet.
    /// If (a) ever turns red, the "prefix overrides with the skin class" skin contract is void.
    /// </summary>
    public class UssCascadePrecedencePlaymodeTests : UIDocumentTestHelper
    {
        private const string SkinClass = "t4158-skin";
        private const string CompClass = "t4158-comp";

        private static readonly Color Own = new Color(1f, 0f, 0f, 1f);
        private static readonly Color Ancestor = new Color(0f, 1f, 0f, 1f);
        private static readonly Color Tail = new Color(0f, 0f, 1f, 1f);

        private readonly List<StyleSheet> _sheets = new List<StyleSheet>();

        protected override PanelSettings GetPanelSettings() => CreateTestPanelSettings();

        public override void TearDown()
        {
            base.TearDown();
            foreach (var s in _sheets)
                if (s != null) Object.DestroyImmediate(s);
            _sheets.Clear();
        }

        private StyleSheet Sheet(string name, string uss)
        {
            if (SusRuntimeHotReload.StyleSheetFromUss == null)
                Assert.Ignore("No USS-from-text factory registered (SusUssFromString is Editor-only).");
            var sheet = SusRuntimeHotReload.StyleSheetFromUss(uss, name);
            Assert.IsNotNull(sheet, $"factory returned no sheet for {name}");
            _sheets.Add(sheet);
            return sheet;
        }

        private static string Rgb(Color c) =>
            $"rgb({Mathf.RoundToInt(c.r * 255)}, {Mathf.RoundToInt(c.g * 255)}, {Mathf.RoundToInt(c.b * 255)})";

        private static string Rule(string selector, Color c) =>
            selector + " { background-color: " + Rgb(c) + "; }";

        /// <summary>Component element with its own companion-style sheet: <c>.t4158-comp</c> (0,1,0).</summary>
        private VisualElement MountComponent()
        {
            Root.AddToClassList(SkinClass);
            var comp = new VisualElement { name = "t4158-comp" };
            comp.AddToClassList(CompClass);
            comp.styleSheets.Add(Sheet("T4158Comp.g", Rule("." + CompClass, Own)));
            Root.Add(comp);
            return comp;
        }

        private static void AssertColor(Color expected, VisualElement el, string what)
        {
            var got = el.resolvedStyle.backgroundColor;
            TestContext.Out.WriteLine($"[T-4158] {what}: resolved {Rgb(got)} a={got.a:0.##}, expected {Rgb(expected)}");
            Assert.IsTrue(
                Mathf.Abs(got.r - expected.r) < 0.01f && Mathf.Abs(got.g - expected.g) < 0.01f &&
                Mathf.Abs(got.b - expected.b) < 0.01f && Mathf.Abs(got.a - expected.a) < 0.01f,
                $"{what}: resolved {Rgb(got)} a={got.a}, expected {Rgb(expected)}");
        }

        [UnityTest]
        public IEnumerator Baseline_OwnCompanionSheetApplies()
        {
            var comp = MountComponent();
            yield return WaitFrame();
            AssertColor(Own, comp, "baseline (own sheet only)");
        }

        [UnityTest]
        public IEnumerator A_AncestorSheet_HigherSpecificity_BeatsOwnSheet()
        {
            var comp = MountComponent();
            Root.styleSheets.Add(Sheet("T4158Skin.a", Rule($".{SkinClass} .{CompClass}", Ancestor)));
            yield return WaitFrame();
            AssertColor(Ancestor, comp, "(a) ancestor (0,2,0) vs own (0,1,0)");
        }

        [UnityTest]
        public IEnumerator B_AncestorSheet_EqualSpecificity_LosesToOwnSheet()
        {
            var comp = MountComponent();
            Root.styleSheets.Add(Sheet("T4158Skin.b", Rule("." + CompClass, Ancestor)));
            yield return WaitFrame();
            AssertColor(Own, comp, "(b) ancestor (0,1,0) vs own (0,1,0)");
        }

        [UnityTest]
        public IEnumerator C_TailSheetOnElement_AfterCompanion_WinsAtEqualSpecificity()
        {
            var comp = MountComponent();
            comp.styleSheets.Add(Sheet("T4158Tail.c", Rule("." + CompClass, Tail)));
            yield return WaitFrame();
            AssertColor(Tail, comp, "(c) tail after own, both (0,1,0)");
        }

        [UnityTest]
        public IEnumerator C_SheetOnElement_BeforeCompanion_LosesAtEqualSpecificity()
        {
            Root.AddToClassList(SkinClass);
            var comp = new VisualElement { name = "t4158-comp" };
            comp.AddToClassList(CompClass);
            comp.styleSheets.Add(Sheet("T4158Head.c", Rule("." + CompClass, Tail)));
            comp.styleSheets.Add(Sheet("T4158Comp.g", Rule("." + CompClass, Own)));
            Root.Add(comp);
            yield return WaitFrame();
            AssertColor(Own, comp, "(c-control) head before own, both (0,1,0)");
        }
    }
}
#endif
