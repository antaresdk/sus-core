using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Controls;
using Sharq.Core.Storybook.Nav;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Zone D as a whole (card T-3034): grouping, the coverage number that makes a missing control
    /// impossible to hide, the dependency and "nobody reads it" observations, the empty panel, and
    /// the deep link the panel produces.
    /// </summary>
    public class SusControlPanelTests
    {
        readonly List<SusControlPanel> _panels = new();

        [SetUp]
        public void SetUp() => SusControlFactory.ClearProviders();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _panels.Count; i++) _panels[i].Dispose();
            _panels.Clear();
            SusControlFactory.ClearProviders();
        }

        SusControlPanel Panel(
            SusComponent component,
            SusStoryContext story = null,
            SusStoryRoute route = null)
        {
            var panel = new SusControlPanel(component, "Fixture", story, route);
            _panels.Add(panel);
            return panel;
        }

        static SusStoryContext Story(SusComponent component, SusStoryRoute route = null) =>
            new SusStoryContext(null, component, route);

        // ── the number ──────────────────────────────────────────────────

        [Test]
        public void EveryPropGetsAControl_AndTheFooterSaysTheNumbersOutLoud()
        {
            var component = new SusIntrospectionFixture();
            int declared = component.DescribeProps().Count;

            var panel = Panel(component);

            Assert.AreEqual(declared, panel.PropCount);
            Assert.AreEqual(declared, panel.ControlCount, "the table covers every prop shape");
            Assert.AreEqual("props " + declared + " · controls " + declared, panel.CountText);
            Assert.AreEqual(SusControlPanel.CoverageOkBadge, panel.BadgeText);
            Assert.IsTrue(panel.IsCovered);
            CollectionAssert.IsEmpty(panel.Uncovered);
        }

        [Test]
        public void GroupsAppearInTheOrderOfTheMockUp_AxesStateContentBehaviour()
        {
            var panel = Panel(new SusIntrospectionFixture());

            var groups = panel.Query<VisualElement>(className: "sus-sb-ctlpanel__group").ToList();
            var names = new List<string>();
            for (int i = 0; i < groups.Count; i++) names.Add(groups[i].name);

            CollectionAssert.AreEqual(
                new[]
                {
                    "sus-storybook-group-axis",
                    "sus-storybook-group-state",
                    "sus-storybook-group-content",
                    "sus-storybook-group-behavior",
                    "sus-storybook-group-data",
                },
                names);
        }

        [Test]
        public void APropWithNoControlAndNoReason_IsADefect_NotAShorterPanel()
        {
            SusControlFactory.Register(new SilentProvider("Text"));

            var panel = Panel(new SusIntrospectionFixture());

            CollectionAssert.Contains(panel.Uncovered, "Text");
            Assert.IsFalse(panel.IsCovered);
            Assert.AreEqual(SusControlPanel.CoverageDefectBadge, panel.BadgeText);
            Assert.AreEqual(panel.PropCount - 1, panel.ControlCount);
        }

        [Test]
        public void APropExcludedWithAReason_LowersTheControlCountWithoutBeingADefect()
        {
            var component = new SusIntrospectionFixture();
            var story = Story(component);
            story.Exclude("DeadProp", "kept for the API, driven by nothing");

            var panel = Panel(component, story);

            Assert.AreEqual(panel.PropCount - 1, panel.ControlCount);
            Assert.IsTrue(panel.IsCovered, "props == controls + exclusions");
            Assert.AreEqual(SusControlPanel.CoverageOkBadge, panel.BadgeText);
            Assert.AreEqual(1, panel.Excluded.Count);
            StringAssert.Contains("DeadProp", panel.Excluded[0]);
        }

        [Test]
        public void AHandWrittenControlIsMarkedManual_SoTheReaderKnowsWhoWiredIt()
        {
            var component = new SusIntrospectionFixture();
            var story = Story(component);
            story.AddManualControl("Variant", "story drives it with its own picker");

            var panel = Panel(component, story);

            Assert.IsTrue(panel.Find("Variant").IsManual);
            Assert.IsFalse(panel.Find("Text").IsManual);
        }

        // ── observations ────────────────────────────────────────────────

        [Test]
        public void APropNobodyReads_IsListedAsDead_AndOneWithAWatcherIsNot()
        {
            var panel = Panel(new SusIntrospectionFixture());

            CollectionAssert.Contains(panel.DeadProps, "DeadProp");
            CollectionAssert.DoesNotContain(panel.DeadProps, "Size",
                "UseAllowed watches Size, so somebody reads it");
            Assert.IsTrue(panel.Find("DeadProp").IsDead);
        }

        [Test]
        public void ADependentControl_SaysWhatItDependsOnAndDimsUntilTheConditionHolds()
        {
            var component = new SusIntrospectionFixture();
            var panel = Panel(component);

            var icon = panel.Find("Icon");
            Assert.IsFalse(icon.IsActive, "ContentMode is 'text', so Icon does nothing");
            StringAssert.Contains("ContentMode = icon", icon.DependencyNote);
            Assert.IsTrue(icon.ClassListContains("sus-sb-ctl--inert"), "dimming is a class");

            // Driving the prop it depends on revives it - through the panel, as a user would.
            Assert.IsTrue(panel.Find("ContentMode").SetFromString("icon"));

            Assert.IsTrue(icon.IsActive);
            Assert.IsFalse(icon.ClassListContains("sus-sb-ctl--inert"));
        }

        // ── empty panel ─────────────────────────────────────────────────

        [Test]
        public void AComponentWithoutProps_ShowsTheEmptyStateAndNoDefect()
        {
            var panel = Panel(new SusIntrospectionEmptyFixture());

            Assert.IsTrue(panel.IsEmpty);
            Assert.AreEqual(0, panel.PropCount);
            Assert.AreEqual(0, panel.ControlCount);
            Assert.IsTrue(panel.IsCovered, "no props is not a hole");

            var empty = panel.Q<VisualElement>(className: "sus-sb-ctlpanel__empty");
            Assert.IsNotNull(empty);
            Assert.IsFalse(empty.ClassListContains(SusControl.HiddenClass), "the empty state is shown");
        }

        // ── deep link ───────────────────────────────────────────────────

        [Test]
        public void AnUntouchedPanel_ProducesALinkWithNoQuery()
        {
            var panel = Panel(new SusIntrospectionFixture());

            var route = panel.BuildRoute("enginetests/fixtures/introspection");

            Assert.AreEqual(0, route.Query.Count, "only differences travel");
            Assert.AreEqual("#/enginetests/fixtures/introspection", route.ToHash());
        }

        [Test]
        public void ChangingOneControl_PutsOnlyThatPropIntoTheLink()
        {
            var component = new SusIntrospectionFixture();
            var panel = Panel(component);
            SusControl raised = null;
            panel.ValueChanged += c => raised = c;

            Assert.IsTrue(panel.Find("Size").SetFromString("lg"));

            Assert.AreSame(panel.Find("Size"), raised, "the panel reports which control wrote");
            var route = panel.BuildRoute("enginetests/fixtures/introspection");
            Assert.AreEqual(1, route.Query.Count);
            Assert.AreEqual("lg", route.Query["Size"]);
            StringAssert.Contains("?Size=lg", route.ToHash());
        }

        [Test]
        public void ALinkWithAQuery_IsAppliedToThePropsWhenThePanelIsBuilt()
        {
            var component = new SusIntrospectionFixture();
            var route = new SusStoryRoute(
                "enginetests/fixtures/introspection",
                new Dictionary<string, string> { ["Size"] = "lg", ["Disabled"] = "true" });

            var panel = Panel(component, Story(component, route), route);

            Assert.AreEqual("lg", component.Size.Value);
            Assert.IsTrue(component.Disabled.Value);
            Assert.AreEqual("lg", panel.Find("Size").StringValue);
        }

        /// <summary>Claims a prop and then builds nothing - the hole the coverage gate hunts.</summary>
        sealed class SilentProvider : ISusControlProvider
        {
            readonly string _prop;

            public SilentProvider(string prop) => _prop = prop;

            public bool CanBuild(SusPropInfo prop) => prop.Name == _prop;

            public SusControl Build(SusPropInfo prop, SusControlContext context) => null;
        }
    }
}
