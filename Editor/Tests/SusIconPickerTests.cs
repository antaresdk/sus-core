using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core;
using Sharq.Core.Storybook.Controls;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The glyph picker of card T-3035 (plan ARCH-20260907-STORYBOOK-ENGINE §4.3.1): the engine's
    /// control for icon props. What is checked here is what the pre-engine storybook could not
    /// promise — the list is the REGISTRY (every registered provider), not a constant of 16 names;
    /// the popup opens in the STAGE's overlay host, not on the panel root; a page is a page and
    /// not a filter; and picking a glyph writes the prop.
    ///
    /// EditMode: controls need no panel, so nothing waits for a frame. The one thing that needs a
    /// tree is the host resolution, and the test builds that tree itself.
    /// </summary>
    public class SusIconPickerTests
    {
        /// <summary>
        /// A registered icon source with names but no assets — the only honest way to test the
        /// 400-cell page, since the built-in core set is far smaller than one page.
        /// </summary>
        sealed class FakeIconProvider : ISusIconProvider
        {
            readonly List<string> _names = new();

            public FakeIconProvider(string prefix, int count)
            {
                for (int i = 0; i < count; i++)
                    _names.Add(prefix + i.ToString("D4"));
            }

            public UnityEngine.UIElements.VectorImage Load(string name, SusIconWeight weight) => null;
            public IEnumerable<string> KnownNames => _names;
            public void Invalidate() { }
        }

        readonly List<SusControl> _built = new();
        SusIntrospectionFixture _component;
        SusControlContext _context;
        VisualElement _stage;
        OverlayHost _stageHost;
        FakeIconProvider _fake;

        [SetUp]
        public void SetUp()
        {
            SusControlFactory.ClearProviders();
            SusControlFactory.RegisterDefaults();

            _component = new SusIntrospectionFixture();
            _context = new SusControlContext(_component);

            // The shape the storybook builds (T-3032): a stage that owns its own overlay host,
            // with the control panel inside it. A popup must land in THAT host.
            var root = new VisualElement();
            _stage = new VisualElement();
            _stageHost = new OverlayHost();
            root.Add(_stage);
            _stage.Add(_stageHost);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _built.Count; i++) _built[i].Dispose();
            _built.Clear();
            SusControlFactory.ClearProviders();
            if (_fake != null)
            {
                SusIconRegistry.UnregisterProvider(_fake);
                _fake = null;
            }
        }

        SusPropInfo Prop(string name)
        {
            var props = _component.DescribeProps();
            for (int i = 0; i < props.Count; i++)
                if (props[i].Name == name) return props[i];
            Assert.Fail("prop not described: " + name);
            return null;
        }

        SusIconPickerControl Picker(string propName = "Icon")
        {
            var control = SusControlFactory.Build(Prop(propName), _context);
            Assert.IsNotNull(control, "no control for " + propName);
            _built.Add(control);
            Assert.IsInstanceOf<SusIconPickerControl>(control, propName + " control type");
            var picker = (SusIconPickerControl)control;
            _stage.Insert(0, picker);
            return picker;
        }

        void RegisterFake(int count)
        {
            _fake = new FakeIconProvider("fake-glyph-", count);
            SusIconRegistry.RegisterProvider(_fake);
        }

        // -- the provider ----------------------------------------------------------

        [Test]
        public void Engine_RegistersThePicker_AndItTakesIconPropsOnly()
        {
            CollectionAssert.Contains(SusControlFactory.Providers, SusIconControlProvider.Default,
                "RegisterDefaults puts the picker on the table");

            Assert.IsTrue(SusIconControlProvider.Default.CanBuild(Prop("Icon")), "*Icon string prop");
            Assert.IsFalse(SusIconControlProvider.Default.CanBuild(Prop("Text")), "plain string prop");
            Assert.IsFalse(SusIconControlProvider.Default.CanBuild(null), "no prop, no claim");

            var icon = Picker();
            Assert.AreEqual(SusControlKind.Icon, icon.Kind);

            var text = SusControlFactory.Build(Prop("Text"), _context);
            _built.Add(text);
            Assert.IsInstanceOf<SusTextControl>(text, "the picker does not swallow ordinary text props");
        }

        [Test]
        public void RegisterDefaults_IsIdempotent()
        {
            int before = SusControlFactory.Providers.Count;
            SusControlFactory.RegisterDefaults();
            SusControlFactory.RegisterDefaults();
            Assert.AreEqual(before, SusControlFactory.Providers.Count, "one singleton, one entry");
        }

        // -- the grid comes from the registry --------------------------------------

        [Test]
        public void Grid_IsBuiltFromTheRegistry_NotFromAConstantList()
        {
            RegisterFake(12);
            var picker = Picker();
            picker.Open();

            Assert.IsTrue(picker.IsOpen, "the popup is mounted");
            Assert.AreEqual(SusIconRegistry.KnownAliases.Count, picker.AllNames.Count,
                "every name the registry knows, from every provider");
            CollectionAssert.Contains(picker.AllNames, "fake-glyph-0007",
                "a name a newly registered provider supplies is in the grid");
            Assert.AreEqual(picker.Matches.Count, picker.Cells.Count,
                "a set smaller than one page is shown whole");
            Assert.IsFalse(picker.ShowsEmptyState);
        }

        [Test]
        public void Page_IsAPage_NotAFilter_AndScrollingExtendsIt()
        {
            RegisterFake(1000);
            var picker = Picker();
            picker.Open();

            Assert.GreaterOrEqual(picker.Matches.Count, 1000, "the whole registry matches");
            Assert.AreEqual(SusIconPickerControl.PageSize, picker.Cells.Count, "first page is capped");

            picker.ShowMore();
            Assert.AreEqual(SusIconPickerControl.PageSize * 2, picker.Cells.Count, "second page");

            // The cap is a page over the WHOLE registry: a query re-pages, it does not search
            // inside the 400 already built.
            picker.Search("fake-glyph-09");
            Assert.AreEqual(100, picker.Matches.Count, "fake-glyph-0900…0999");
            Assert.AreEqual(100, picker.Cells.Count);
        }

        [Test]
        public void Search_FiltersByName_AndTheFooterCountsTheMatch()
        {
            RegisterFake(20);
            var picker = Picker();
            picker.Open();
            int total = picker.AllNames.Count;

            picker.Search("fake-glyph-001");
            Assert.AreEqual(10, picker.Matches.Count, "0010…0019");
            foreach (var name in picker.Matches)
                StringAssert.Contains("fake-glyph-001", name);
            Assert.AreEqual("10 of " + total + " for \"fake-glyph-001\"", picker.CountText);

            picker.Search("");
            Assert.AreEqual(total, picker.Matches.Count, "clearing the query restores the set");
            StringAssert.EndsWith("glyphs", picker.CountText);
        }

        [Test]
        public void Search_WithNoMatch_ShowsTheEmptyStateInsteadOfAnEmptyGrid()
        {
            var picker = Picker();
            picker.Open();

            picker.Search("no-such-glyph-anywhere");

            Assert.AreEqual(0, picker.Matches.Count);
            Assert.AreEqual(0, picker.Cells.Count);
            Assert.IsTrue(picker.ShowsEmptyState, "the popup says why the grid is empty");
        }

        // -- writing ---------------------------------------------------------------

        [Test]
        public void PickingAGlyph_WritesTheProp_AndClosesThePopup()
        {
            var picker = Picker();
            Assert.AreEqual("star", picker.ValueLabel, "the field shows the current name");

            picker.Open();
            picker.Search("gear");
            Assert.Greater(picker.Cells.Count, 0, "the core set has a gear");

            var picked = picker.Matches[0];
            Assert.IsTrue(picker.PickAt(0), "the cell's own entry point");

            Assert.AreEqual(picked, _component.Icon.Value, "prop written through TrySetValue");
            Assert.AreEqual(picked, picker.ValueLabel);
            Assert.AreEqual(picked, picker.Glyph.Name.Value, "the field glyph follows the value");
            Assert.IsFalse(picker.IsOpen, "picking closes the popup");
        }

        [Test]
        public void NoneOption_ClearsTheProp_AndTheFieldSaysSo()
        {
            var picker = Picker();
            picker.Open();

            Assert.IsTrue(picker.Select(SusIconPickerControl.NoneOption));

            Assert.AreEqual(string.Empty, _component.Icon.Value, "the empty string is a value");
            Assert.AreEqual(SusIconPickerControl.EmptyValueLabel, picker.ValueLabel);
            Assert.IsFalse(picker.IsOpen);
        }

        [Test]
        public void SelectedGlyph_IsMarkedInTheGrid()
        {
            var picker = Picker();
            picker.Select("gear");
            picker.Open();

            int marked = 0;
            for (int i = 0; i < picker.Cells.Count; i++)
                if (picker.Cells[i].ClassListContains("sus-sb-iconpicker__cell--selected")) marked++;

            Assert.AreEqual(1, marked, "exactly the current value is highlighted");
        }

        // -- where the popup lives -------------------------------------------------

        [Test]
        public void Popup_OpensInTheStageHost_NotOnThePanelRoot()
        {
            var picker = Picker();
            picker.Open();

            Assert.AreSame(_stageHost, picker.Host, "ancestor-first resolution, T-3032");
            Assert.AreSame(_stageHost, picker.Popup.hierarchy.parent);

            picker.Close();

            Assert.IsFalse(picker.IsOpen);
            Assert.IsNull(picker.Popup.hierarchy.parent, "closing takes the popup out of the host");
            Assert.AreEqual(0, _stageHost.Count, "and out of the stack, not just out of the tree");
        }

        [Test]
        public void WithoutAHostAndWithoutAPanel_OpeningIsANoOp_NotAThrow()
        {
            var control = SusControlFactory.Build(Prop("Icon"), _context);
            _built.Add(control);
            var picker = (SusIconPickerControl)control;   // never parented: no host, no panel

            Assert.DoesNotThrow(() => picker.Open());
            Assert.IsFalse(picker.IsOpen);
        }

        // -- the footer ------------------------------------------------------------

        [Test]
        public void Footer_NamesTheSources_AndTheRegisteredSetThatIsNotImported()
        {
            RegisterFake(7);
            var picker = Picker();
            picker.Open();

            var sources = SusIconPickerControl.Sources();
            Assert.Greater(sources.Count, 0, "at least the core provider is registered");

            int declared = 0;
            bool coreSeen = false;
            for (int i = 0; i < sources.Count; i++)
            {
                declared += sources[i].Count;
                if (sources[i].Label == "core") coreSeen = true;
            }
            Assert.IsTrue(coreSeen, "the built-in set names itself 'core'");
            Assert.LessOrEqual(picker.AllNames.Count, declared,
                "the union cannot exceed the sum of the sources");
            StringAssert.Contains("core ", picker.CountText);
            StringAssert.EndsWith("glyphs", picker.CountText);

            // Phosphor is ALWAYS registered and supplies nothing until its sample is imported —
            // the hint has to say that instead of leaving an empty grid unexplained.
            Assert.AreEqual(SusIconPickerControl.NotImportedHint(), picker.HintText);
        }

        [Test]
        public void PhosphorSetSize_IsDeclared_NotGuessed()
        {
            Assert.AreEqual(1512 * 6, PhosphorIconProvider.DeclaredSvgCount);
        }

        // -- dependency ------------------------------------------------------------

        [Test]
        public void IconProp_WithDependsOn_IsDimmedAndNamesTheCondition()
        {
            var picker = Picker();

            Assert.IsFalse(picker.IsActive, "ContentMode is 'text', so the icon does nothing");
            StringAssert.Contains("ContentMode", picker.DependencyNote);
            Assert.IsTrue(picker.ClassListContains("sus-sb-ctl--inert"), "the row is dimmed by a class");

            _component.ContentMode.Value = "icon";
            picker.UpdateDependency();

            Assert.IsTrue(picker.IsActive);
            Assert.IsFalse(picker.ClassListContains("sus-sb-ctl--inert"));
        }

        // -- deep link -------------------------------------------------------------

        [Test]
        public void DeepLink_RestoresTheGlyphName()
        {
            var picker = Picker();

            Assert.IsTrue(picker.Restorable);
            Assert.IsTrue(picker.SetFromString("bell"));
            Assert.AreEqual("bell", _component.Icon.Value);
            Assert.AreEqual("bell", picker.StringValue);

            Assert.IsTrue(picker.SetFromString(string.Empty), "an empty deep-link value clears it");
            Assert.AreEqual(string.Empty, _component.Icon.Value);
        }

    }
}
