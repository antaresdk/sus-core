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
    /// T-4159 (skin sheet layer, step S1) — the effect of <see cref="SusComponent.SetTrailingSheets"/>
    /// on a live panel, read from <c>resolvedStyle</c>: an override sheet registered on the ROOT
    /// with the SAME specificity as the component's own sheet wins once it is delivered as a
    /// trailing sheet (the same rule attached to the root alone loses — S0, case b), clearing the
    /// scope restores the component's own look, and a component attached after the scope was set
    /// is covered too. Sheet order is pinned in EditMode by <c>SusComponentTrailingSheetsTests</c>.
    /// </summary>
    public class TrailingSheetsCascadePlaymodeTests : UIDocumentTestHelper
    {
        private const string CompClass = "t4159-comp";

        private static readonly Color Own = new Color(1f, 0f, 0f, 1f);
        private static readonly Color Skin = new Color(0f, 0f, 1f, 1f);

        private readonly List<StyleSheet> _sheets = new List<StyleSheet>();

        /// <summary>Component whose own sheet (stand-in for its companion .g) paints it red at (0,1,0).</summary>
        private sealed class Comp : SusComponent
        {
            internal static StyleSheet OwnSheet;

            protected override void Build()
            {
                AddToClassList(CompClass);
                if (OwnSheet != null) styleSheets.Add(OwnSheet);
            }
        }

        protected override PanelSettings GetPanelSettings() => CreateTestPanelSettings();

        public override void TearDown()
        {
            SusComponent.ClearTrailingSheets(Root);
            Comp.OwnSheet = null;
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

        private static string Rule(Color c) => "." + CompClass + " { background-color: " + Rgb(c) + "; }";

        private Comp NewComp()
        {
            Comp.OwnSheet = Sheet("T4159Comp.g", Rule(Own));
            var c = new Comp();
            Comp.OwnSheet = null;
            return c;
        }

        private static void AssertColor(Color expected, VisualElement el, string what)
        {
            var got = el.resolvedStyle.backgroundColor;
            TestContext.Out.WriteLine($"[T-4159] {what}: resolved {Rgb(got)}, expected {Rgb(expected)}");
            Assert.IsTrue(
                Mathf.Abs(got.r - expected.r) < 0.01f && Mathf.Abs(got.g - expected.g) < 0.01f &&
                Mathf.Abs(got.b - expected.b) < 0.01f && Mathf.Abs(got.a - expected.a) < 0.01f,
                $"{what}: resolved {Rgb(got)} a={got.a}, expected {Rgb(expected)}");
        }

        [UnityTest]
        public IEnumerator TrailingSheet_EqualSpecificity_BeatsOwnSheet_ClearRestores()
        {
            var comp = NewComp();
            Root.Add(comp);
            yield return WaitFrame();
            AssertColor(Own, comp, "before Set");

            SusComponent.SetTrailingSheets(Root, new[] { Sheet("T4159Skin.component", Rule(Skin)) });
            yield return WaitFrame();
            AssertColor(Skin, comp, "trailing sheet (0,1,0) vs own (0,1,0)");

            SusComponent.ClearTrailingSheets(Root);
            yield return WaitFrame();
            AssertColor(Own, comp, "after Clear");
        }

        [UnityTest]
        public IEnumerator TrailingSheet_NestedAndLateComponents_AllOverridden()
        {
            SusComponent.SetTrailingSheets(Root, new[] { Sheet("T4159Skin.component", Rule(Skin)) });
            var outer = NewComp();
            Root.Add(outer);
            yield return WaitFrame();

            var inner = NewComp();
            outer.Add(inner);
            yield return WaitFrame();

            AssertColor(Skin, outer, "outer, attached after Set");
            AssertColor(Skin, inner, "nested, attached a frame later");
        }
    }
}
#endif
