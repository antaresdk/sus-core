using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Sharq.Core.Storybook.Controls;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The control table of ARCH-20260907-STORYBOOK-ENGINE §4.3, one test per row (card T-3034):
    /// which widget a prop shape gets, what its bounds and captions say, and that driving the
    /// widget really writes the prop. Built on the introspection fixtures of T-3031, so the two
    /// contracts are checked against the same component.
    ///
    /// EditMode: a component's Build() runs in its constructor and controls need no panel, so
    /// nothing here waits for a frame.
    /// </summary>
    public class SusControlFactoryTests
    {
        readonly List<SusControl> _built = new();
        SusIntrospectionFixture _component;
        SusControlContext _context;
        SusControlSpecFixture _spec;

        [SetUp]
        public void SetUp()
        {
            SusControlFactory.ClearProviders();
            _component = new SusIntrospectionFixture();
            _context = new SusControlContext(_component);
            _spec = new SusControlSpecFixture();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _built.Count; i++) _built[i].Dispose();
            _built.Clear();
            SusControlFactory.ClearProviders();
        }

        SusPropInfo Prop(string name)
        {
            var props = _component.DescribeProps();
            for (int i = 0; i < props.Count; i++)
                if (props[i].Name == name) return props[i];
            Assert.Fail("prop not described: " + name);
            return null;
        }

        SusPropInfo SpecProp(string name)
        {
            var props = _spec.DescribeProps();
            for (int i = 0; i < props.Count; i++)
                if (props[i].Name == name) return props[i];
            Assert.Fail("spec prop not described: " + name);
            return null;
        }

        /// <summary>Builds the control of a prop of the plan-4.5 fixture (card T-4521).</summary>
        T BuildSpec<T>(string propName, SusControlContext context = null) where T : SusControl
        {
            var control = SusControlFactory.Build(SpecProp(propName), context ?? new SusControlContext(_spec));
            Assert.IsNotNull(control, "no control for " + propName);
            _built.Add(control);
            Assert.IsInstanceOf<T>(control, propName + " control type");
            return (T)control;
        }

        T Build<T>(string propName) where T : SusControl
        {
            var control = SusControlFactory.Build(Prop(propName), _context);
            Assert.IsNotNull(control, "no control for " + propName);
            _built.Add(control);
            Assert.IsInstanceOf<T>(control, propName + " control type");
            return (T)control;
        }

        // ── bool ────────────────────────────────────────────────────────

        [Test]
        public void Bool_IsSwitch_AndFlippingItWritesTheProp()
        {
            var control = Build<SusToggleControl>("Disabled");

            Assert.AreEqual(SusControlKind.Toggle, control.Kind);
            Assert.IsFalse(control.Current);

            control.Flip();

            Assert.IsTrue(_component.Disabled.Value, "prop written through TrySetValue");
            Assert.IsTrue(control.Switch.ClassListContains("sb-ctl__switch--on"), "state is a class");
        }

        // ── closed sets ─────────────────────────────────────────────────

        [Test]
        public void AllowedSet_OfThree_IsSegment_WithTheDeclaredValues()
        {
            var control = Build<SusSegmentControl>("Size");

            Assert.AreEqual(SusControlKind.Segment, control.Kind);
            CollectionAssert.AreEqual(new[] { "sm", "md", "lg" }, control.Options);
            Assert.IsTrue(control.Buttons[1].ClassListContains("sb-ctl__seg--active"), "md is current");

            Assert.IsTrue(control.SetFromString("lg"));

            Assert.AreEqual("lg", _component.Size.Value);
            Assert.IsTrue(control.Buttons[2].ClassListContains("sb-ctl__seg--active"));
            Assert.IsFalse(control.Buttons[1].ClassListContains("sb-ctl__seg--active"));
        }

        [Test]
        public void Enum_IsSegment_WithEveryMemberName()
        {
            var control = Build<SusSegmentControl>("Mode");

            CollectionAssert.AreEqual(new[] { "Compact", "Cozy", "Roomy" }, control.Options);

            Assert.IsTrue(control.SetFromString("Roomy"));
            Assert.AreEqual(IntrospectionMode.Roomy, _component.Mode.Value);
        }

        [Test]
        public void AllowedSet_WiderThanFive_IsDropdown()
        {
            // The set is resolver-backed, so widening the backing list widens the control.
            _component.PageSizes = new List<int> { 10, 25, 50, 100, 200, 500 };

            var control = Build<SusDropdownControl>("PageSize");

            Assert.AreEqual(SusControlKind.Dropdown, control.Kind);
            Assert.AreEqual(6, control.Options.Count);
            Assert.Greater(control.Options.Count, SusControlFactory.SegmentLimit);

            Assert.IsTrue(control.SetFromString("200"));
            Assert.AreEqual(200, _component.PageSize.Value);
        }

        // ── text and icons ──────────────────────────────────────────────

        [Test]
        public void String_IsTextField_AndTheEmptyStringIsALegalValueWithACaption()
        {
            var control = Build<SusTextControl>("Text");

            Assert.AreEqual("hello", control.Field.value);
            Assert.IsEmpty(control.Note, "a non-empty value needs no caption");

            // What the field's change callback calls; a detached BaseField raises no event of
            // its own, so an EditMode test enters through the same door the widget uses.
            Assert.IsTrue(control.SetFromString(string.Empty));

            Assert.AreEqual(string.Empty, _component.Text.Value);
            Assert.AreEqual(string.Empty, control.Field.value, "the widget mirrors the prop back");
            Assert.AreEqual(SusControl.EmptyStringNote, control.Note);
        }

        [Test]
        public void IconNamedString_IsAGlyphField_NotAPlainTextField()
        {
            var control = Build<SusIconControl>("Icon");

            Assert.AreEqual(SusControlKind.Icon, control.Kind);
            Assert.AreEqual("star", control.Field.value);

            Assert.IsTrue(control.SetFromString("gear"));

            Assert.AreEqual("gear", _component.Icon.Value);
            Assert.AreEqual("gear", control.Field.value);
            Assert.AreEqual("gear", control.Glyph.Name.Value, "the preview follows the name");
        }

        // ── numbers ─────────────────────────────────────────────────────

        [Test]
        public void Numeric_WithDeclaredRange_TakesItsBoundsAndUnitFromTheAttribute()
        {
            var control = Build<SusNumberControl>("Value");

            Assert.AreEqual(SusControlKind.Number, control.Kind);
            Assert.IsTrue(control.HasDeclaredRange);
            Assert.AreEqual(0d, control.Minimum);
            Assert.AreEqual(10d, control.Maximum);
            StringAssert.Contains("0", control.Note);
            StringAssert.Contains("10", control.Note);

            Assert.IsTrue(control.SetFromString("7"));

            Assert.AreEqual(7f, _component.Value.Value, 0.001f);
            Assert.AreEqual(7f, control.Slider.value, 0.001f, "the slider follows the prop");
        }

        // POOL-01 U-034, plan ARCH-20261014-SB-PROP-CONTROLS D3 (d:d05b9d): the old 0..100 scale is gone.
        [Test]
        public void Numeric_WithoutRange_IsFieldOnly_NoSlider_NoClamp()
        {
            var control = Build<SusNumberControl>("DebounceMs");

            Assert.IsFalse(control.HasDeclaredRange);
            Assert.AreEqual(SusRangeStatus.Undeclared, control.Status);
            Assert.IsFalse(control.HasSlider, "no declared range, no slider");
            Assert.IsTrue(control.Slider.ClassListContains(SusControl.HiddenClass));
            Assert.IsTrue(control.Field.ClassListContains("sb-ctl__number--solo"), "the field takes the row");
            Assert.AreEqual(SusControl.NoRangeNote, control.Note, "dev caption, and no invented scale in it");
            StringAssert.DoesNotContain("100", control.Note);

            Assert.IsTrue(control.SetFromString("1500"));
            Assert.AreEqual(1500, _component.DebounceMs.Value, "1500 ms is not clamped to 100");

            Assert.IsTrue(control.SetFromString("-1"));
            Assert.AreEqual(-1, _component.DebounceMs.Value, "a sentinel survives the field");

            Assert.IsTrue(control.SetFromString("2.6"));
            Assert.AreEqual(3, _component.DebounceMs.Value, "an int prop keeps whole numbers (away from zero)");

            Assert.IsFalse(control.SetFromString("NaN"), "NaN is refused");
            Assert.IsFalse(control.SetFromString("Infinity"), "an infinity is refused");
            Assert.AreEqual(3, _component.DebounceMs.Value);
        }

        [Test]
        public void Numeric_WithUnit_PutsTheUnitInTheCaption()
        {
            var control = Build<SusNumberControl>("Progress");

            Assert.AreEqual("%", control.Unit);
            StringAssert.Contains("%", control.Note);
        }

        // ── numbers: plan ARCH-20261014-SB-PROP-CONTROLS 4.2 / 4.5 (card T-4521, U-034) ──

        [Test]
        public void Numeric_WithRange_SliderSnapsToStep()
        {
            var control = BuildSpec<SusNumberControl>("Rating");

            Assert.IsTrue(control.HasSlider);
            Assert.AreEqual(0.5d, control.Step);

            Assert.IsTrue(control.Drag(3.3));
            Assert.AreEqual(3.5f, _spec.Rating.Value, 0.0001f, "3.3 snaps to the nearest half");
            Assert.AreEqual(3.5f, control.Slider.value, 0.0001f, "the slider follows the snapped value");

            Assert.IsTrue(control.Drag(3.2));
            Assert.AreEqual(3.0f, _spec.Rating.Value, 0.0001f);
        }

        [Test]
        public void Numeric_WithRange_FieldIsExact_AndClamps()
        {
            var control = BuildSpec<SusNumberControl>("Rating");

            Assert.IsTrue(control.SetFromString("3.3"));
            Assert.AreEqual(3.3f, _spec.Rating.Value, 0.0001f, "the field is exact, it does not snap");

            Assert.IsTrue(control.SetFromString("9"));
            Assert.AreEqual(5f, _spec.Rating.Value, 0.0001f, "the field clamps into the declared range");
        }

        [Test]
        public void Numeric_IntWithoutStep_StepsByOne()
        {
            var control = BuildSpec<SusNumberControl>("Count");

            Assert.AreEqual(1d, control.Step);
            Assert.IsTrue(control.Drag(4.4));
            Assert.AreEqual(4, _spec.Count.Value);
            Assert.IsTrue(control.Drag(4.6));
            Assert.AreEqual(5, _spec.Count.Value);
        }

        [Test]
        public void Numeric_FloatWithoutStep_StepsByHundredthOfSpan()
        {
            var control = BuildSpec<SusNumberControl>("Gain");

            Assert.AreEqual(0.02d, control.Step, 1e-9);
            Assert.IsTrue(control.Drag(0.337));
            Assert.AreEqual(0.34f, _spec.Gain.Value, 0.0001f);
        }

        [Test]
        public void Numeric_Special_ChipWritesSentinel_AndDims()
        {
            var control = BuildSpec<SusNumberControl>("Width");
            Assert.AreEqual(1, control.Chips.Count);
            Assert.AreEqual("auto", control.Chips[0].text);

            Assert.IsTrue(control.SetFromString("120"));
            Assert.AreEqual(-1, control.ActiveSpecial, "120 is an ordinary value");
            Assert.IsFalse(control.IsDimmed);
            Assert.IsFalse(control.Chips[0].ClassListContains(SusNumberControl.SpecialOnClass));

            control.PickSpecial(0);

            Assert.AreEqual(-1f, _spec.Width.Value, "the chip writes the sentinel");
            Assert.AreEqual(0, control.ActiveSpecial);
            Assert.IsTrue(control.IsDimmed);
            Assert.IsTrue(control.Chips[0].ClassListContains(SusNumberControl.SpecialOnClass), "state is a class");
            Assert.IsTrue(control.Slider.ClassListContains(SusNumberControl.DimClass), "slider muted by class");
            Assert.IsTrue(control.Field.ClassListContains(SusNumberControl.DimClass), "field muted by class");
            Assert.IsTrue(control.Slider.enabledSelf && control.Field.enabledSelf, "muted, not disabled");
            Assert.AreEqual(string.Empty, control.Field.value, "the field is empty while a special value is active");
            Assert.AreEqual("auto", control.Field.textEdition.placeholder, "its placeholder names the meaning");
        }

        [Test]
        public void Numeric_Special_OutsideRange_IsNotClamped()
        {
            var control = BuildSpec<SusNumberControl>("Width");

            Assert.AreEqual(-1f, _spec.Width.Value, "-1 lies outside 0..600 and stays");
            StringAssert.DoesNotContain(SusControl.OutOfRangeNote, control.Note, "a special value is not out of range");

            control.SetFromString("50");
            Assert.IsTrue(control.SetFromString("-1"), "typing the sentinel writes it as declared");
            Assert.AreEqual(-1f, _spec.Width.Value);

            Assert.IsTrue(control.SetFromString("-5"));
            Assert.AreEqual(0f, _spec.Width.Value, "an ordinary value below the range still clamps");
        }

        [Test]
        public void Numeric_Special_LeavingBySlider()
        {
            var control = BuildSpec<SusNumberControl>("Width");
            control.PickSpecial(0);
            Assert.IsTrue(control.IsDimmed);

            Assert.IsTrue(control.Drag(100));

            Assert.AreEqual(100f, _spec.Width.Value, "an ordinary value, snapped to the step");
            Assert.AreEqual(-1, control.ActiveSpecial);
            Assert.IsFalse(control.IsDimmed);
            Assert.IsFalse(control.Chips[0].ClassListContains(SusNumberControl.SpecialOnClass), "the chip went out");
            Assert.AreEqual("100", control.Field.value);
        }

        [Test]
        public void Numeric_RangeFrom_FollowsSiblingProps()
        {
            var control = BuildSpec<SusNumberControl>("Scaled");
            Assert.AreEqual(5d, control.Maximum);
            Assert.AreEqual(0.5d, control.Step);

            _spec.Max.Value = 10;

            Assert.AreEqual(10d, control.Maximum, "the maximum follows Max");
            Assert.AreEqual(10f, control.Slider.highValue, "the slider was re-bounded");
            Assert.AreEqual(2f, _spec.Scaled.Value, "the value itself is not touched");
            StringAssert.Contains("0…10", control.Note);

            _spec.Max.Value = 0;
            Assert.AreEqual(SusRangeStatus.Invalid, control.Status, "a neighbour that empties the range invalidates it");
            Assert.IsFalse(control.HasSlider);

            _spec.Max.Value = 8;
            Assert.AreEqual(SusRangeStatus.Valid, control.Status, "and it recovers when the neighbour does");
            Assert.AreEqual(8d, control.Maximum);
        }

        [Test]
        public void Numeric_RangeFrom_WithoutRange_OrWithMissingNeighbour_IsInvalid()
        {
            Assert.AreEqual(SusRangeStatus.Invalid, BuildSpec<SusNumberControl>("NoBase").Status,
                "[SusRangeFrom] needs the static [SusRange] under it");
            Assert.AreEqual(SusRangeStatus.Invalid, BuildSpec<SusNumberControl>("Ghost").Status,
                "a neighbour that does not exist is a wrong declaration");
        }

        [Test]
        public void Numeric_SeedOutsideRange_ShowsTrueValue()
        {
            var control = BuildSpec<SusNumberControl>("Seeded");

            Assert.AreEqual(140, _spec.Seeded.Value, "the story's seed is not rewritten");
            Assert.AreEqual("140", control.Field.value, "the field shows the true value");
            Assert.AreEqual(100f, control.Slider.value, "the thumb sits at the edge");
            StringAssert.Contains(SusControl.OutOfRangeNote, control.Note);
        }

        [Test]
        public void Numeric_InvalidRange_FallsBackToField()
        {
            var control = BuildSpec<SusNumberControl>("Broken");

            Assert.IsTrue(control.HasDeclaredRange);
            Assert.AreEqual(SusRangeStatus.Invalid, control.Status);
            Assert.IsFalse(control.HasSlider, "a contradictory range is treated like no range");
            Assert.AreEqual(SusControl.RangeInvalidNote, control.Note);

            Assert.IsTrue(control.SetFromString("77"));
            Assert.AreEqual(77, _spec.Broken.Value, "no clamp either");
        }

        // ── closed sets: plan 4.3 / 4.5 (card T-4521, U-033, U-039) ──

        [Test]
        public void ClosedSet_CaseDuplicates_ShownOnce()
        {
            var control = BuildSpec<SusSegmentControl>("Shape");

            CollectionAssert.AreEqual(new[] { "Radial", "Linear" }, control.Options, "the first spelling wins");
            Assert.AreEqual(2, control.Buttons.Count);
            StringAssert.Contains(SusControl.DuplicateValuesNote, control.Note);

            // The prop itself may hold a lower-case spelling; the canonical option is the active one.
            _spec.Shape.Value = "radial";
            Assert.IsTrue(control.Buttons[0].ClassListContains("sb-ctl__seg--active"));
        }

        [Test]
        public void ClosedSet_FiveShortLabels_IsSegment()
        {
            var control = BuildSpec<SusSegmentControl>("Four");

            Assert.AreEqual(SusControlKind.Segment, control.Kind);
            Assert.AreEqual(4, control.Options.Count);
            Assert.AreEqual(23, control.Options.Sum(o => o.Length), "inside the " +
                SusControlFactory.SegmentLabelBudget + "-character budget");
        }

        [Test]
        public void ClosedSet_LabelBudgetExceeded_IsDropdown()
        {
            var control = BuildSpec<SusDropdownControl>("Wide");

            Assert.AreEqual(SusControlKind.Dropdown, control.Kind);
            Assert.AreEqual(3, control.Options.Count, "three values are few enough for a segment...");
            Assert.Greater(control.Options.Sum(o => o.Length), SusControlFactory.SegmentLabelBudget,
                "...but their texts are over the budget");
        }

        [Test]
        public void ClosedSet_ColorName_IsAlwaysDropdown()
        {
            var control = BuildSpec<SusDropdownControl>("Color");

            Assert.AreEqual(SusControlKind.Dropdown, control.Kind);
            Assert.AreEqual(2, control.Options.Count, "two short values would otherwise be a segment");

            Assert.AreEqual(SusControlKind.Dropdown, SusControlFactory.KindOf(SpecProp("AccentColor")),
                "the rule is the name, *Color too");
        }

        [Test]
        public void ClosedSet_EmptyValue_SelectsFallback()
        {
            var control = BuildSpec<SusSegmentControl>("Style");

            Assert.AreEqual(string.Empty, _spec.Style.Value, "the empty string is a legal value here");
            CollectionAssert.AreEqual(new[] { "tonal", "elevated", "outlined" }, control.Options,
                "and it is not an option of its own");
            Assert.IsTrue(control.Buttons[0].ClassListContains("sb-ctl__seg--active"), "it reads as the fallback");
            Assert.IsTrue(control.Accepts(string.Empty), "a story seeded with the empty string can be restored");
        }

        [Test]
        public void ClosedSet_ComponentSetBeatsStorySet()
        {
            var story = new[] { "x", "y" };

            var own = SusControlFactory.Build(Prop("Size"), _context, story);
            _built.Add(own);
            Assert.IsInstanceOf<SusSegmentControl>(own);
            CollectionAssert.AreEqual(new[] { "sm", "md", "lg" }, ((SusSegmentControl)own).Options,
                "the component's own set wins");
            Assert.IsEmpty(own.Note, "no 'set from story' caption: the story set was not used");

            // The story set fills a hole only where the component declared nothing - and says so (dev).
            var filled = SusControlFactory.Build(Prop("Variant"), _context, story);
            _built.Add(filled);
            CollectionAssert.AreEqual(story, ((SusSegmentControl)filled).Options);
            StringAssert.Contains(SusControl.SetFromStoryNote, filled.Note);
        }

        // ── captions: plan 4.4 (D8, d:b763b1) ──

        [Test]
        public void Captions_DiagnosticsHiddenWhenOff()
        {
            var quiet = new SusControlContext(_spec) { ShowDiagnostics = false };

            Assert.AreEqual("0…5 · step 0.5", BuildSpec<SusNumberControl>("Rating", quiet).Note,
                "the range caption explains the number: it stays");
            Assert.AreEqual("0…600 px · step 10", BuildSpec<SusNumberControl>("Width", quiet).Note,
                "unit and step stay too");
            Assert.IsEmpty(BuildSpec<SusNumberControl>("Delay", quiet).Note, "'range not declared' is dev-only");
            Assert.AreEqual("0…100", BuildSpec<SusNumberControl>("Seeded", quiet).Note, "so is 'out of range'");
            Assert.IsEmpty(BuildSpec<SusNumberControl>("Broken", quiet).Note, "and 'range invalid'");
            Assert.IsEmpty(BuildSpec<SusSegmentControl>("Shape", quiet).Note, "and 'duplicate values'");

            // The same prop with diagnostics on says it.
            StringAssert.Contains(SusControl.NoRangeNote, BuildSpec<SusNumberControl>("Delay").Note);
        }

        // ── colour ──────────────────────────────────────────────────────

        [Test]
        public void Color_OffersOneSwatchPerSkinToken_AndAFreeValue()
        {
            var control = Build<SusColorControl>("Accent");

            Assert.AreEqual(SusControlKind.Color, control.Kind);
            Assert.AreEqual(SusControlContext.StandardColorTokens.Count, control.Swatches.Count);
            // The colour is the skin's, so the swatch carries a class and no colour literal.
            Assert.IsTrue(control.Swatches[0].ClassListContains("sb-ctl__swatch--primary"));

            Assert.IsTrue(control.SetFromString("#00FF00FF"));

            Assert.AreEqual(Color.green, _component.Accent.Value);
            Assert.AreEqual("#00FF00FF", control.Custom.value);
            Assert.IsNull(control.PickedToken, "a free value is not a token");
        }

        // ── list ────────────────────────────────────────────────────────

        [Test]
        public void List_ShowsItsRowCount_AndAddingARowWritesANewList()
        {
            var control = Build<SusListControl>("Items");

            Assert.AreEqual(SusControlKind.List, control.Kind);
            Assert.AreEqual(2, control.Count);
            Assert.IsFalse(control.IsOpen, "the rows start folded");
            StringAssert.Contains("2", control.Summary.text);
            Assert.IsFalse(control.Restorable, "a list cannot be restored from a link");

            control.AddRow();

            Assert.AreEqual(3, _component.Items.Value.Count);
            Assert.AreEqual(3, control.Count);

            control.Toggle();
            Assert.IsTrue(control.IsOpen);
        }

        // ── model / read-only ───────────────────────────────────────────

        [Test]
        public void Model_IsReadOnly_AndFollowsTheValueThroughItsChangeEvent()
        {
            var control = Build<SusReadOnlyControl>("Model");

            Assert.AreEqual(SusControlKind.ReadOnly, control.Kind);
            Assert.AreEqual(SusControl.ReadOnlyNote, control.Note);
            Assert.IsFalse(control.SetFromString("anything"), "a display row refuses writes");

            _component.Model.Value = new IntrospectionModel { Id = "hero", Count = 3 };

            Assert.AreEqual("hero#3", control.Display, "the row re-read the prop from SubscribeChanged");
        }

        // ── extension point ─────────────────────────────────────────────

        [Test]
        public void RegisteredProvider_WinsOverTheBuiltInTable()
        {
            SusControlFactory.Register(new StubProvider("Text", replace: true));

            var control = SusControlFactory.Build(Prop("Text"), _context);
            _built.Add(control);

            Assert.IsInstanceOf<StubControl>(control,
                "the provider decided, not the type of the prop");
        }

        [Test]
        public void ProviderThatDeclinesToBuild_LeavesThePropWithoutAControl()
        {
            SusControlFactory.Register(new StubProvider("Text", replace: false));

            Assert.IsNull(SusControlFactory.Build(Prop("Text"), _context),
                "null is the honest answer, and the panel counts it as uncovered");
        }

        [Test]
        public void QueryValues_AreInvariant_SoALinkTravelsBetweenLocales()
        {
            Assert.AreEqual("true", SusControlFactory.ToQueryValue(true));
            Assert.AreEqual("1.5", SusControlFactory.ToQueryValue(1.5f));
            Assert.AreEqual("Cozy", SusControlFactory.ToQueryValue(IntrospectionMode.Cozy));
            Assert.AreEqual("#FF0000FF", SusControlFactory.ToQueryValue(Color.red));
            Assert.AreEqual(string.Empty, SusControlFactory.ToQueryValue(null));
        }

        /// <summary>What a package outside core builds: its own row, through the public base.</summary>
        sealed class StubControl : SusControl
        {
            public StubControl(SusPropInfo prop, SusControlContext context)
                : base(prop, SusControlKind.ReadOnly, context) { }
        }

        /// <summary>Provider that claims one prop by name; builds its own row, or nothing.</summary>
        sealed class StubProvider : ISusControlProvider
        {
            readonly string _prop;
            readonly bool _replace;

            public StubProvider(string prop, bool replace)
            {
                _prop = prop;
                _replace = replace;
            }

            public bool CanBuild(SusPropInfo prop) => prop.Name == _prop;

            public SusControl Build(SusPropInfo prop, SusControlContext context) =>
                _replace ? new StubControl(prop, context) : null;
        }
    }

    /// <summary>
    /// The prop shapes of plan ARCH-20261014-SB-PROP-CONTROLS 4.5 (card T-4521): ranges with and
    /// without a step, a sentinel, a range fed by a neighbour, a broken range, and the closed sets
    /// that need a duplicate, a label budget, a colour name and a legal empty value. Its own
    /// fixture, so the introspection fixtures (and the prop counts their tests pin) stay as they were.
    /// </summary>
    public sealed class SusControlSpecFixture : SusComponent
    {
        [SusRange(0, 5, 0.5)] public Prop<float> Rating = new(0f);
        [SusRange(0, 10)] public Prop<int> Count = new(3);
        [SusRange(0, 2)] public Prop<float> Gain = new(1f);

        [SusRange(0, 600, 10, Unit = "px")]
        [SusSpecial(-1, "auto")]
        public Prop<float> Width = new(-1f);

        [SusRange(0, 5, 0.5)]
        [SusRangeFrom(MaxProp = nameof(Max))]
        public Prop<float> Scaled = new(2f);

        public Prop<int> Max = new(5);

        [SusRange(0, 100)] public Prop<int> Seeded = new(140);
        [SusRange(5, 1)] public Prop<int> Broken = new(3);
        public Prop<int> Delay = new(120);

        [SusRangeFrom(MaxProp = nameof(Max))] public Prop<float> NoBase = new(1f);

        [SusRange(0, 5)]
        [SusRangeFrom(MaxProp = "NoSuchProp")]
        public Prop<float> Ghost = new(1f);

        public Prop<string> Shape = new("Radial");
        public Prop<string> Four = new("filled");
        public Prop<string> Wide = new("clockwise-sweep");
        public Prop<string> Color = new("primary");
        public Prop<string> AccentColor = new("primary");
        public Prop<string> Style = new("");

        protected override void Build()
        {
            const string who = "SusControlSpecFixture.";
            UseAllowed(Shape, new[] { "Radial", "Linear", "radial", "linear" }, "Radial", propName: who + "Shape");
            UseAllowed(Four, new[] { "filled", "tonal", "text", "outlined" }, "filled", propName: who + "Four");
            UseAllowed(Wide, new[] { "clockwise-sweep", "counter-sweep", "ping-pong-xyz" }, "clockwise-sweep",
                propName: who + "Wide");
            UseAllowed(Color, new[] { "primary", "danger" }, "primary", propName: who + "Color");
            UseAllowed(AccentColor, new[] { "primary", "danger" }, "primary", propName: who + "AccentColor");
            UseAllowed(Style, new[] { "tonal", "elevated", "outlined" }, "tonal", allowEmpty: true,
                propName: who + "Style");
        }
    }
}
