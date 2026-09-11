using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Controls;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The CLOSED AXIS of variants — card T-3379, plan ARCH-20260911-STORYBOOK-SHELL.md §4.9 and
    /// decision D26.
    ///
    /// The measurement this fixes, taken on 2026-09-11: 17 kit components declare
    /// <c>Prop&lt;string&gt; Variant</c>, 8 of them clamp it with <c>UseAllowed</c> and 9 do not.
    /// On those 9 the engine had no way to learn the enumeration the component's own template
    /// branches on, so the matrix drew one row, its caption said "no axis × state · 1 × 4", and
    /// zone D handed the buyer a text field to guess the spelling in
    /// (<c>SusAlert.sharq:60</c> against <c>:14-17</c>). Stories that declared the axis: 0.
    ///
    /// Three things are asserted here and nowhere else: the engine takes the set from a STORY,
    /// the COMPONENT still wins when both speak, and both consumers (matrix rows and zone D's
    /// control) read the same resolution.
    /// </summary>
    public class SusStoryAxisTests
    {
        const string StoryAxis = "core/showcase/variant";
        const string StoryClash = "core/showcase/variant-clash";
        const string Counter = "core/primitives/counter";
        const string Swatch = "core/primitives/swatch";

        string _axisBefore;

        [SetUp]
        public void SetUp()
        {
            _axisBefore = SusStoryMatrix.AxisPropName;
            SusStateTwins.Reset();
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
        }

        [TearDown]
        public void TearDown()
        {
            SusStoryMatrix.AxisPropName = _axisBefore;
            SusStateTwins.Reset();
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        static SusStoryEntry Entry(string id) => SusStoryRegistry.Find(id);

        /// <summary>No state twins anywhere — keeps the column count at the four forceable ones.</summary>
        static void NoTwins() => SusStateTwins.Resolver = (_, __) => false;

        // ── the declaration ──────────────────────────────────────────────────

        [Test]
        public void A_story_that_lists_its_variants_carries_them_into_the_registry()
        {
            var entry = Entry(StoryAxis);

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.DeclaresAxis, Is.True);
            Assert.That(entry.AxisProp, Is.EqualTo("Variant"),
                "a story that names no Axis means the default prop, not 'no axis'");
            Assert.That(entry.AxisValues, Is.EqualTo(new[] { "tonal", "outlined", "text" }),
                "declaration ORDER is the row order of the matrix, so it is part of the declaration");
        }

        [Test]
        public void A_story_that_lists_nothing_declares_no_axis()
        {
            var entry = Entry(Counter);

            Assert.That(entry.DeclaresAxis, Is.False);
            Assert.That(entry.AxisValues, Is.Empty);
            Assert.That(entry.AxisProp, Is.EqualTo(SusStoryAxis.DefaultPropName));
        }

        [Test]
        public void Normalize_trims_drops_blanks_and_keeps_the_declared_order()
        {
            var clean = SusStoryAxis.Normalize(new[] { " tonal ", "", "outlined", null, "TONAL", "text" });

            Assert.That(clean, Is.EqualTo(new[] { "tonal", "outlined", "text" }),
                "a repeat would be a duplicated matrix row; a blank would be an unnameable one");
            Assert.That(SusStoryAxis.Normalize(null), Is.Empty);
            Assert.That(SusStoryAxis.Normalize(new[] { "  ", "" }), Is.Empty);
        }

        // ── resolution: who wins ─────────────────────────────────────────────

        [Test]
        public void The_story_fills_the_hole_when_the_component_clamped_nothing()
        {
            var axis = SusStoryAxis.Resolve(Entry(StoryAxis), new CoreVariantDemo());

            Assert.That(axis.IsClosed, Is.True);
            Assert.That(axis.Source, Is.EqualTo(SusStoryAxisSource.Story));
            Assert.That(axis.PropName, Is.EqualTo("Variant"));
            Assert.That(axis.Values, Is.EqualTo(new[] { "tonal", "outlined", "text" }));
        }

        [Test]
        public void The_component_wins_over_the_story_and_the_disagreement_is_logged()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "declares Variant as .* but the component clamps it"));

            var axis = SusStoryAxis.Resolve(Entry(StoryClash), new CoreClampedVariantDemo());

            Assert.That(axis.Source, Is.EqualTo(SusStoryAxisSource.Component),
                "the component's set is the one the runtime coerces to, so nothing overrides it");
            Assert.That(axis.Values, Is.EqualTo(new[] { "tonal", "outlined" }),
                "'invented' is not reachable, so it is not a row");
        }

        [Test]
        public void A_component_with_no_such_prop_and_a_story_with_no_list_is_no_axis()
        {
            var axis = SusStoryAxis.Resolve(Entry(Counter), new CoreCounterDemo());

            Assert.That(axis.IsClosed, Is.False);
            Assert.That(axis.Source, Is.EqualTo(SusStoryAxisSource.None));
            Assert.That(axis.PropName, Is.Null);
            Assert.That(axis.Values, Is.Empty);
        }

        [Test]
        public void A_story_axis_on_another_prop_does_not_answer_for_this_one()
        {
            var axis = SusStoryAxis.FromStory(Entry(StoryAxis), "Size");

            Assert.That(axis.IsClosed, Is.False,
                "a story that enumerated Variant said nothing about Size");
        }

        // ── consumer 1: the matrix rows ──────────────────────────────────────

        [Test]
        public void The_matrix_builds_one_row_per_declared_value_and_names_the_axis()
        {
            NoTwins();
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(StoryAxis));

            Assert.That(matrix.AxisProp, Is.EqualTo("Variant"));
            Assert.That(matrix.AxisSource, Is.EqualTo(SusStoryAxisSource.Story));
            Assert.That(matrix.Rows, Is.EqualTo(new[] { "tonal", "outlined", "text" }));
            Assert.That(matrix.Rows.Count, Is.EqualTo(3),
                "three rows where the pre-T-3379 engine drew one");
            Assert.That(matrix.MetaText, Does.StartWith("Variant × state"));
            Assert.That(matrix.MetaText, Does.Not.Contain("no axis"),
                "the caption 'no axis' was the visible face of the missing producer");
        }

        [Test]
        public void A_component_that_clamps_the_prop_itself_still_feeds_the_rows()
        {
            NoTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.AxisSource, Is.EqualTo(SusStoryAxisSource.Component),
                "T-3379 ADDED a producer; it must not have replaced the one that worked");
            Assert.That(matrix.Rows, Is.EqualTo(new CoreSwatchDemo().DescribeAllowed()["Tone"].Values));
        }

        [Test]
        public void A_story_without_an_axis_still_gets_exactly_one_default_row()
        {
            NoTwins();
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Counter));

            Assert.That(matrix.Rows, Is.EqualTo(new[] { SusStoryMatrix.DefaultRow }));
            Assert.That(matrix.AxisSource, Is.EqualTo(SusStoryAxisSource.None));
            Assert.That(matrix.MetaText, Does.StartWith("no axis"),
                "an undeclared axis still says so out loud instead of inventing rows");
        }

        // ── consumer 2: zone D's control ─────────────────────────────────────

        [Test]
        public void Zone_D_turns_the_story_declared_axis_into_a_picker()
        {
            var entry = Entry(StoryAxis);
            var story = entry.Instantiate(null, out var component);

            using var panel = new SusControlPanel(component, entry.Name, story);
            var control = panel.Find("Variant");

            Assert.That(control, Is.Not.Null);
            Assert.That(control, Is.InstanceOf<SusSegmentControl>(),
                "3 values is inside SegmentLimit, so the buyer gets buttons, not a text field");
            Assert.That(control, Is.Not.InstanceOf<SusTextControl>(),
                "a text field is how the buyer had to guess the spelling before T-3379");

            var options = ((SusSegmentControl)control).Options;
            Assert.That(options, Is.EqualTo(new[] { "tonal", "outlined", "text" }));
        }

        /// <summary>
        /// D26's "a value outside the set cannot be entered" is structural, not validated: the
        /// widget offers one button per legal value and no place to type. Asserting the widget's
        /// SHAPE is the honest form of that claim — a validator could be bypassed, a missing text
        /// field cannot.
        /// </summary>
        [Test]
        public void The_picker_offers_the_declared_values_and_no_way_to_type_another()
        {
            var entry = Entry(StoryAxis);
            var story = entry.Instantiate(null, out var component);

            using var panel = new SusControlPanel(component, entry.Name, story);
            var control = (SusSegmentControl)panel.Find("Variant");

            Assert.That(control.Buttons.Count, Is.EqualTo(3));
            Assert.That(control.Buttons.Select(b => b.text),
                Is.EqualTo(new[] { "tonal", "outlined", "text" }));
            Assert.That(control.Query<TextField>().ToList(), Is.Empty,
                "no free-text entry: the spelling is offered, not guessed");
        }

        [Test]
        public void A_prop_the_story_said_nothing_about_keeps_its_ordinary_control()
        {
            var entry = Entry(Counter);
            var story = entry.Instantiate(null, out var component);

            using var panel = new SusControlPanel(component, entry.Name, story);

            Assert.That(panel.Find("Label"), Is.InstanceOf<SusTextControl>(),
                "the axis declaration must not turn every string prop into a picker");
        }
    }
}
