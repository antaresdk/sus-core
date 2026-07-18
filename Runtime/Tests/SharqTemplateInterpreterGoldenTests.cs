using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// E3 golden: interpreter TryApply on toy + real template fixtures.
    /// </summary>
    public class SharqTemplateInterpreterGoldenTests : UIDocumentTestHelper
    {
        const string SimpleTemplate = @"
<ui:VisualElement class=""golden-root"">
    <ui:Label name=""title"" class=""golden-title"" text=""Hello"" />
    <ui:VisualElement name=""body"" class=""golden-body"">
        <ui:Label name=""hint"" text=""world"" />
    </ui:VisualElement>
</ui:VisualElement>";

        // Extracted from SusDivider.sharq (full template)
        const string FixtureDivider = @"
<ui:VisualElement $MainElement
                  class=""sus-divider""
                  :class='{
    ""sus-divider--vertical"": Direction.Value == ""vertical"",
    ""sus-divider--inset"": Inset.Value
}' />";

        // Extracted from SusBadge.sharq (full template)
        const string FixtureBadge = @"
<ui:VisualElement $MainElement class=""sus-badge""
                  :class='{
    ""sus-badge--primary"": Color.Value == ""primary"",
    ""sus-badge--danger"": Color.Value == ""danger""
}'>
    <slot name=""icon"" />
    <ui:Label v-if=""Text.Value != ''"" :text=""Text.Value"" class=""sus-badge__text"" />
</ui:VisualElement>";

        // Simplified SusAlert body (no string.IsNullOrEmpty / nested SusIcon)
        const string FixtureAlertSimple = @"
<ui:VisualElement $MainElement class=""sus-alert""
                  :class='{
    ""sus-alert--success"": Type.Value == ""success"",
    ""sus-alert--info"": Type.Value == ""info"",
    ""sus-alert--warning"": Type.Value == ""warning"",
    ""sus-alert--error"": Type.Value == ""error""
}'>
    <ui:VisualElement class=""sus-alert__body"">
        <ui:Label v-if=""Title.Value != ''"" :text=""Title.Value"" class=""sus-alert__title"" />
        <ui:Label v-if=""Text.Value != ''"" :text=""Text.Value"" class=""sus-alert__text"" />
    </ui:VisualElement>
</ui:VisualElement>";

        // Simplified SusChip (variants without || empty defaults)
        const string FixtureChipSimple = @"
<ui:VisualElement $MainElement class=""sus-chip""
                  :class='{
    ""sus-chip--selected"": Selected.Value,
    ""sus-chip--outlined"": Variant.Value == ""outlined"",
    ""sus-chip--tonal"": Variant.Value == ""tonal""
}'>
    <ui:Label v-if=""Label.Value != ''"" :text=""Label.Value"" class=""sus-chip__text"" />
</ui:VisualElement>";

        // SusButton-like root with @click — must apply (event skipped, not hard-fail)
        const string FixtureWithClick = @"
<ui:VisualElement $MainElement class=""sus-btn-like"">
    <ui:Label name=""lbl"" text=""Go"" @click=""OnClick"" />
</ui:VisualElement>";

        [UnityTest]
        public IEnumerator TryApply_BuildsExpectedStructure()
        {
            var comp = new GoldenSusComponent();
            Root.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(SharqTemplateInterpreter.TryApply(comp, SimpleTemplate),
                "Simple template must apply without fallback");
            yield return WaitFrame();

            Assert.IsTrue(comp.ClassListContains("golden-root"),
                "$MainElement semantics: root class applies to host");
            Assert.IsNotNull(comp.Q<Label>("title"));
            Assert.AreEqual("Hello", comp.Q<Label>("title").text);
            Assert.IsNotNull(comp.Q("body"));
            Assert.IsNotNull(comp.Q<Label>("hint"));
            Assert.AreEqual("world", comp.Q<Label>("hint").text);
        }

        [UnityTest]
        public IEnumerator TryApply_PreservesProps_ViaSnapshot()
        {
            var comp = new GoldenSusComponent();
            Root.Add(comp);
            yield return WaitFrame();

            comp.Title.Value = "Captured";
            Assert.IsTrue(SharqTemplateInterpreter.TryApply(comp, SimpleTemplate));
            yield return WaitFrame();

            Assert.AreEqual("Captured", comp.Title.Value,
                "Prop values must survive template hot-reload");
        }

        [UnityTest]
        public IEnumerator TryApply_Fallback_OnUnsupportedVFor()
        {
            var comp = new GoldenSusComponent();
            Root.Add(comp);
            yield return WaitFrame();

            const string bad = @"
<ui:VisualElement>
    <ui:Label v-for=""Items.Value"" :text=""item"" />
</ui:VisualElement>";

            Assert.IsFalse(SharqTemplateInterpreter.TryApply(comp, bad),
                "v-for must fall back to full recompile");
        }

        [UnityTest]
        public IEnumerator TryApply_KitFixture_Divider()
        {
            var comp = new KitDividerHost();
            Root.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(SharqTemplateInterpreter.TryApply(comp, FixtureDivider),
                "SusDivider template fixture must TryApply");
            yield return WaitFrame();
            Assert.IsTrue(comp.ClassListContains("sus-divider"));
        }

        [UnityTest]
        public IEnumerator TryApply_KitFixture_Badge()
        {
            var comp = new KitBadgeHost();
            Root.Add(comp);
            yield return WaitFrame();

            comp.Text.Value = "3";
            comp.Color.Value = "danger";
            Assert.IsTrue(SharqTemplateInterpreter.TryApply(comp, FixtureBadge),
                "SusBadge template fixture must TryApply");
            yield return WaitFrame();
            Assert.IsTrue(comp.ClassListContains("sus-badge"));
            Assert.IsTrue(comp.ClassListContains("sus-badge--danger"));
            var lbl = comp.Q<Label>(className: "sus-badge__text");
            Assert.IsNotNull(lbl);
            Assert.AreEqual("3", lbl.text);
        }

        [UnityTest]
        public IEnumerator TryApply_KitFixture_AlertSimple()
        {
            var comp = new KitAlertHost();
            Root.Add(comp);
            yield return WaitFrame();

            comp.Type.Value = "success";
            comp.Title.Value = "Saved";
            Assert.IsTrue(SharqTemplateInterpreter.TryApply(comp, FixtureAlertSimple));
            yield return WaitFrame();
            Assert.IsTrue(comp.ClassListContains("sus-alert--success"));
        }

        [UnityTest]
        public IEnumerator TryApply_KitFixture_ChipSimple()
        {
            var comp = new KitChipHost();
            Root.Add(comp);
            yield return WaitFrame();

            comp.Label.Value = "Filter";
            comp.Selected.Value = true;
            comp.Variant.Value = "outlined";
            Assert.IsTrue(SharqTemplateInterpreter.TryApply(comp, FixtureChipSimple));
            yield return WaitFrame();
            Assert.IsTrue(comp.ClassListContains("sus-chip--selected"));
            Assert.IsTrue(comp.ClassListContains("sus-chip--outlined"));
        }

        [UnityTest]
        public IEnumerator TryApply_SkipsAtClick_WithoutHardFail()
        {
            var comp = new GoldenSusComponent();
            Root.Add(comp);
            yield return WaitFrame();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Skipping unsupported event"));
            Assert.IsTrue(SharqTemplateInterpreter.TryApply(comp, FixtureWithClick),
                "@click must be skipped with warning, not hard-fail");
            yield return WaitFrame();
            Assert.IsNotNull(comp.Q<Label>("lbl"));
        }

        [UnityTest]
        public IEnumerator TryApply_ComplexExpr_ExpectedFallback()
        {
            var comp = new KitAlertHost();
            Root.Add(comp);
            yield return WaitFrame();

            const string complex = @"
<ui:VisualElement $MainElement class=""sus-alert"">
    <ui:Label v-if=""!string.IsNullOrEmpty(Title.Value)"" :text=""Title.Value"" />
</ui:VisualElement>";

            Assert.IsFalse(SharqTemplateInterpreter.TryApply(comp, complex),
                "string.IsNullOrEmpty must expected-fallback");
        }
    }

    /// <summary>Minimal SusComponent for interpreter golden tests.</summary>
    public class GoldenSusComponent : SusComponent
    {
        public Prop<string> Title = new("");

        protected override void Build()
        {
            // Empty — tree comes from TryApply in tests.
        }
    }

    public class KitDividerHost : SusComponent
    {
        public Prop<string> Direction = new("horizontal");
        public Prop<bool> Inset = new(false);
        protected override void Build() { }
    }

    public class KitBadgeHost : SusComponent
    {
        public Prop<string> Text = new("");
        public Prop<string> Color = new("primary");
        protected override void Build() { }
    }

    public class KitAlertHost : SusComponent
    {
        public Prop<string> Type = new("info");
        public Prop<string> Title = new("");
        public Prop<string> Text = new("");
        protected override void Build() { }
    }

    public class KitChipHost : SusComponent
    {
        public Prop<string> Label = new("");
        public Prop<bool> Selected = new(false);
        public Prop<string> Variant = new("tonal");
        protected override void Build() { }
    }

    public enum GoldenTone { Quiet, Loud }

    /// <summary>E2a round-trip for SusComponentSnapshot serialize/restore.</summary>
    public class SusComponentSnapshotTests : UIDocumentTestHelper
    {
        [UnityTest]
        public IEnumerator Capture_Restore_RoundTrip_PrimitiveProps()
        {
            var a = new GoldenSusComponent();
            a.Title.Value = "Alpha";
            Root.Add(a);
            yield return WaitFrame();

            var snap = SusComponentSnapshot.Capture(Root);
            Assert.Greater(snap.Count, 0);

            var json = SusComponentSnapshot.SerializeEntries(snap);
            var restored = SusComponentSnapshot.DeserializeEntries(json);
            Assert.AreEqual(snap.Count, restored.Count);

            a.Title.Value = "Mutated";
            SusComponentSnapshot.Restore(Root, restored);
            yield return WaitFrame();

            Assert.AreEqual("Alpha", a.Title.Value);
        }

        [UnityTest]
        public IEnumerator Capture_Restore_RoundTrip_EnumProp()
        {
            var a = new EnumHostComponent();
            a.Tone.Value = GoldenTone.Loud;
            Root.Add(a);
            yield return WaitFrame();

            var snap = SusComponentSnapshot.Capture(Root);
            var json = SusComponentSnapshot.SerializeEntries(snap);
            var restored = SusComponentSnapshot.DeserializeEntries(json);

            a.Tone.Value = GoldenTone.Quiet;
            SusComponentSnapshot.Restore(Root, restored);
            yield return WaitFrame();

            Assert.AreEqual(GoldenTone.Loud, a.Tone.Value);
        }

        [Test]
        public void SerializeEntries_Empty_IsEmptyArray()
        {
            Assert.AreEqual("[]", SusComponentSnapshot.SerializeEntries(new List<SusComponentSnapshot.Entry>()));
            Assert.AreEqual(0, SusComponentSnapshot.DeserializeEntries("[]").Count);
            Assert.AreEqual(0, SusComponentSnapshot.DeserializeEntries("").Count);
        }
    }

    public class EnumHostComponent : SusComponent
    {
        public Prop<GoldenTone> Tone = new(GoldenTone.Quiet);
        protected override void Build() { }
    }
}
