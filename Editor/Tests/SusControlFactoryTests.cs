using System.Collections.Generic;
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

        [SetUp]
        public void SetUp()
        {
            SusControlFactory.ClearProviders();
            _component = new SusIntrospectionFixture();
            _context = new SusControlContext(_component);
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

        [Test]
        public void Numeric_WithoutRange_FallsBackToZeroToHundred_AndSaysSo()
        {
            var control = Build<SusNumberControl>("DebounceMs");

            Assert.IsFalse(control.HasDeclaredRange);
            Assert.AreEqual(SusControl.FallbackMin, control.Minimum);
            Assert.AreEqual(SusControl.FallbackMax, control.Maximum);
            Assert.AreEqual(SusControl.NoRangeNote, control.Note);

            Assert.IsTrue(control.SetFromString("42"));
            Assert.AreEqual(42, _component.DebounceMs.Value, "an int prop keeps whole numbers");
        }

        [Test]
        public void Numeric_WithUnit_PutsTheUnitInTheCaption()
        {
            var control = Build<SusNumberControl>("Progress");

            Assert.AreEqual("%", control.Unit);
            StringAssert.Contains("%", control.Note);
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
}
