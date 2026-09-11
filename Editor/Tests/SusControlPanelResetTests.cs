using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Controls;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Reset to the story's seed — card T-3406, contract of zone D (R142 <c>reset</c>, plan
    /// ARCH-20260911-STORYBOOK-SHELL §4.1: "возвращает засев, а не дефолты типа"; owner's
    /// question 1.3).
    ///
    /// What was measured on 2026-09-11: zone D could take a story apart and had no way of putting
    /// it back. Three turns of a control and the state the author considered worth showing was
    /// gone — and by D21 p. 1 that seeded state IS the point of a story, so what the buyer was
    /// left with is the empty shell the plan warns about. The way back existed only as "reload the
    /// page".
    ///
    /// The distinction every test here defends is seed vs type default: the panel snapshots its
    /// values AFTER the story ran <c>Configure</c> and BEFORE a deep link is applied, so a story
    /// pinned to <c>Tone = "error"</c> must come back to "error" and never to the component's own
    /// "primary". The second is the closed set: a seed outside the values zone D offers is refused
    /// by name instead of leaving a picker with no active button (plan D26, cards T-3379/T-3395).
    /// </summary>
    public class SusControlPanelResetTests
    {
        const string SwatchPreset = "enginetests/primitives/swatch-error";   // story seeds Tone = "error"
        const string StoryAxis = "enginetests/showcase/variant";             // closed axis from the STORY

        SusLogLevel _levelBefore;

        [SetUp]
        public void SetUp()
        {
            _levelBefore = SusLog.Level;
            SusLog.Level = SusLogLevel.Warn;   // the refusal must be sayable out loud
            SusControlFactory.ClearProviders();
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
        }

        [TearDown]
        public void TearDown()
        {
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
            SusControlFactory.ClearProviders();
            SusLog.Level = _levelBefore;
        }

        static SusStoryEntry Entry(string id) => SusStoryRegistry.Find(id);

        // ── the button ───────────────────────────────────────────────────────

        [Test]
        public void Zone_D_offers_the_reset_beside_the_numbers()
        {
            var component = new SusIntrospectionFixture();
            using var panel = new SusControlPanel(component, "Fixture");

            var counters = panel.Q<VisualElement>(className: "sus-sb-ctlpanel__counters");
            Assert.That(counters, Is.Not.Null);

            var reset = counters.Q<Button>(className: "sus-sb-ctlpanel__reset");
            Assert.That(reset, Is.Not.Null, "no reset button: the seed is unreachable once changed");
            Assert.That(reset, Is.SameAs(panel.ResetButton));
            Assert.That(reset.text, Is.EqualTo(SusControlPanel.ResetLabel));
            Assert.That(reset.tooltip, Does.Contain("STORY"), "the caption must say seed, not default");
        }

        [Test]
        public void The_button_is_dimmed_while_there_is_nothing_to_put_back()
        {
            var component = new SusIntrospectionFixture();
            using var panel = new SusControlPanel(component, "Fixture");

            Assert.That(panel.CanResetToStory, Is.False);
            Assert.That(panel.ResetButton.enabledSelf, Is.False,
                "a button that promises a change and produces none is worse than a dimmed one");

            panel.Find("Text").SetFromString("typed by the reader");
            Assert.That(panel.CanResetToStory, Is.True);
            Assert.That(panel.ResetButton.enabledSelf, Is.True);

            panel.ResetToStory();
            Assert.That(panel.ResetButton.enabledSelf, Is.False, "back at the seed: nothing left to undo");
        }

        // ── seed, not type default ───────────────────────────────────────────

        [Test]
        public void Reset_returns_the_value_the_story_seeded_and_not_the_default_of_the_type()
        {
            var component = new SusIntrospectionFixture();
            // What Configure(ctx) does: the author pins the state he considers showable. The panel
            // is built after it, which is what makes the snapshot a SEED.
            component.Text.Value = "seeded by the story";
            using var panel = new SusControlPanel(component, "Fixture");

            panel.Find("Text").SetFromString("typed by the reader");
            Assert.That(component.Text.Value, Is.EqualTo("typed by the reader"));

            Assert.That(panel.ResetToStory(), Is.EqualTo(1), "one prop moved");

            Assert.That(component.Text.Value, Is.EqualTo("seeded by the story"));
            Assert.That(component.Text.Value, Is.Not.EqualTo("hello"),
                "\"hello\" is the default of the prop — the seed is what the story chose");
            Assert.That(panel.ResetRefused, Is.Empty);
        }

        [Test]
        public void Reset_moves_every_changed_prop_and_leaves_the_untouched_ones_alone()
        {
            var component = new SusIntrospectionFixture();
            component.Text.Value = "seeded";
            component.Progress.Value = 40;
            using var panel = new SusControlPanel(component, "Fixture");

            panel.Find("Text").SetFromString("changed");
            panel.Find("Progress").SetFromString("90");
            panel.Find("Disabled").SetFromString("true");

            Assert.That(panel.ResetToStory(), Is.EqualTo(3));
            Assert.That(component.Text.Value, Is.EqualTo("seeded"));
            Assert.That(component.Progress.Value, Is.EqualTo(40));
            Assert.That(component.Disabled.Value, Is.False);

            // Idempotent: a second click has nothing to move and says so with zero.
            Assert.That(panel.ResetToStory(), Is.EqualTo(0));
        }

        [Test]
        public void A_story_pinned_by_its_provider_comes_back_to_that_pin()
        {
            // The data-born story of the fixtures: Configure sets Tone = "error" while the
            // component's own default is "primary" — the two answers a reset could give.
            var entry = Entry(SwatchPreset);
            Assert.That(entry, Is.Not.Null);
            var story = entry.Instantiate(null, out var component);
            using var panel = new SusControlPanel(component, entry.Name, story);

            Assert.That(panel.Defaults["Tone"], Is.EqualTo("error"), "the snapshot is taken after Configure");

            var tone = panel.Find("Tone");
            Assert.That(tone.SetFromString("success"), Is.True);

            Assert.That(panel.ResetToStory(), Is.EqualTo(1));
            Assert.That(tone.StringValue, Is.EqualTo("error"));
            Assert.That(tone.StringValue, Is.Not.EqualTo("primary"), "the type default is not the seed");
        }

        // ── the closed axis (T-3379 / T-3395, plan D26) ──────────────────────

        [Test]
        public void Reset_on_a_closed_axis_lands_inside_the_set_and_the_picker_shows_it()
        {
            var entry = Entry(StoryAxis);
            var story = entry.Instantiate(null, out var component);
            using var panel = new SusControlPanel(component, entry.Name, story);

            var control = (SusSegmentControl)panel.Find("Variant");
            Assert.That(control.SetFromString("text"), Is.True);

            Assert.That(panel.ResetToStory(), Is.EqualTo(1));

            Assert.That(control.StringValue, Is.EqualTo("tonal"), "the value the story was built with");
            Assert.That(control.Options, Contains.Item(control.StringValue),
                "a reset that left the axis would leave the picker with no active button");
            var active = control.Buttons.Where(b => b.ClassListContains("sus-sb-ctl__seg--active")).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "exactly one button reads as chosen");
            Assert.That(active[0].text, Is.EqualTo("tonal"), "and it is the one holding the seed");
        }

        [Test]
        public void A_seed_outside_the_closed_set_is_refused_by_name_instead_of_written()
        {
            // The story declares {tonal, outlined, text} and the component does not clamp Variant
            // at all (the 9 of 17 kit components of T-3379), so a Configure CAN leave a value the
            // axis does not list. Zone D offers three buttons and no way to type: writing that
            // value back would show three buttons with none of them active.
            var component = new CoreVariantDemo();
            component.Variant.Value = "invented";
            var story = new SusStoryContext(Entry(StoryAxis), component, null);
            using var panel = new SusControlPanel(component, "Variant axis", story);

            var control = (SusSegmentControl)panel.Find("Variant");
            Assert.That(control, Is.Not.Null);
            Assert.That(panel.Defaults["Variant"], Is.EqualTo("invented"));
            Assert.That(control.Accepts("invented"), Is.False, "not one of the offered values");

            Assert.That(control.SetFromString("tonal"), Is.True);
            Assert.That(panel.CanResetToStory, Is.False,
                "the only difference is one the reset would refuse — so the button is dimmed");

            LogAssert.Expect(LogType.Warning, new Regex("Variant.*invented.*outside the closed set"));
            Assert.That(panel.ResetToStory(), Is.EqualTo(0));

            Assert.That(panel.ResetRefused, Contains.Item("Variant"));
            Assert.That(control.StringValue, Is.EqualTo("tonal"),
                "the control keeps a legal value instead of an unofferable one");
        }

        [Test]
        public void A_component_that_clamps_the_prop_itself_still_resets_to_the_seed()
        {
            // T-3395: the component is the source of the allowed values whenever it speaks. The
            // reset reads the SAME resolution zone D built the control from, so a clamped prop and
            // a story-declared axis cannot disagree about what is legal.
            var component = new SusIntrospectionFixture();
            component.Size.Value = "lg";
            using var panel = new SusControlPanel(component, "Fixture");

            var size = (SusSegmentControl)panel.Find("Size");
            Assert.That(size.Options, Is.EqualTo(new[] { "sm", "md", "lg" }));
            Assert.That(size.SetFromString("sm"), Is.True);

            Assert.That(panel.ResetToStory(), Is.EqualTo(1));
            Assert.That(size.StringValue, Is.EqualTo("lg"));
            Assert.That(panel.ResetRefused, Is.Empty);
        }
    }
}
