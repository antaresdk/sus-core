using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Zone C of card T-3038: the state matrix, the cell budget, the live-measurement line and
    /// the stage's own overlay host.
    ///
    /// The fixtures are the ones T-3031 / T-3033 already ship — <c>CoreSwatchDemo</c> carries a
    /// closed axis (<c>Tone</c>) and <c>CoreCounterDemo</c> carries none, which is exactly the two
    /// shapes the matrix has to tell apart. The axis the matrix reads is a property, so a test can
    /// aim it at <c>Tone</c> instead of shipping a second demo component whose only job would be
    /// to be called <c>Variant</c>.
    /// </summary>
    public class SusStoryMatrixTests
    {
        const string Swatch = "core/primitives/swatch";
        const string Counter = "core/primitives/counter";
        const string Floating = "core/overlay/floating";

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

        /// <summary>
        /// A component that DOES carry the boolean props the matrix prefers. Deliberately not a
        /// story: the shared fixtures are asserted story-by-story by the registry tests, and the
        /// prop lever needs a component, not an entry.
        /// </summary>
        sealed class StatePropDemo : SusComponent
        {
            public Prop<bool> Disabled = new(false);
            public Prop<bool> Error = new(false);

            protected override void Build()
            {
            }
        }

        static SusStoryEntry Entry(string id) => SusStoryRegistry.Find(id);

        static int CellsInTree(VisualElement root) =>
            root.Query<VisualElement>(className: "sus-sb-matrix__cell").ToList().Count;

        /// <summary>No twins anywhere — the corpus state before the codemod of T-3039.</summary>
        static void NoTwins() => SusStateTwins.Resolver = (_, __) => false;

        /// <summary>Every component has both twins — the corpus state after T-3039.</summary>
        static void AllTwins() => SusStateTwins.Resolver = (_, __) => true;

        // ── rows come from the introspection of T-3031 ───────────────────────

        [Test]
        public void Rows_are_the_allowed_values_of_the_axis_prop()
        {
            NoTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            var allowed = new CoreSwatchDemo().DescribeAllowed()["Tone"].Values;
            Assert.That(matrix.AxisProp, Is.EqualTo("Tone"));
            Assert.That(matrix.Rows, Is.EqualTo(allowed),
                "the rows are DescribeAllowed(), not a list retyped in the shell");
        }

        [Test]
        public void A_component_without_that_axis_gets_one_default_row()
        {
            NoTwins();
            SusStoryMatrix.AxisPropName = "Variant";
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Counter));

            Assert.That(matrix.AxisProp, Is.Null);
            Assert.That(matrix.Rows, Is.EqualTo(new[] { SusStoryMatrix.DefaultRow }));
            Assert.That(matrix.MetaText, Does.StartWith("no axis × state"),
                "the caption says there is no axis instead of naming one the component never had");
        }

        // ── columns: the pseudo-state half of the matrix ─────────────────────

        [Test]
        public void Without_twin_classes_the_pseudo_state_columns_are_not_drawn()
        {
            NoTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.Columns, Is.EqualTo(new[]
            {
                SusStoryStates.Rest, SusStoryStates.Focus,
                SusStoryStates.Disabled, SusStoryStates.Error,
            }));
            Assert.That(matrix.SkippedStates, Is.EqualTo(new[]
            {
                SusStoryStates.Hover, SusStoryStates.Active,
            }));
            Assert.That(matrix.NoteText, Does.StartWith("hover, active"),
                "the missing columns are named, not silently dropped");
        }

        [Test]
        public void With_twin_classes_all_six_columns_come_back()
        {
            AllTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.Columns, Is.EqualTo(SusStoryStates.All));
            Assert.That(matrix.SkippedStates, Is.Empty);
            Assert.That(matrix.NoteText, Is.Empty);
        }

        [Test]
        public void A_hover_cell_carries_the_twin_class_and_nothing_else()
        {
            AllTwins();
            var item = new CoreSwatchDemo();

            var how = SusStoryStates.Force(item, SusStoryStates.Hover);

            Assert.That(how, Is.EqualTo(SusStateForcing.TwinClass));
            Assert.That(item.ClassListContains(SusStateTwins.HoverClass), Is.True);
            Assert.That(item.ClassListContains(SusStateTwins.ActiveClass), Is.False);
        }

        [Test]
        public void Without_a_twin_the_pseudo_state_is_refused_rather_than_faked()
        {
            NoTwins();
            var item = new CoreSwatchDemo();

            var how = SusStoryStates.Force(item, SusStoryStates.Active);

            Assert.That(how, Is.EqualTo(SusStateForcing.Unsupported));
            Assert.That(item.ClassListContains(SusStateTwins.ActiveClass), Is.False,
                "a class nothing styles would leave the cell showing rest under an active label");
        }

        [Test]
        public void Disabled_and_error_go_through_the_component_own_props_when_it_has_them()
        {
            NoTwins();

            var disabled = new StatePropDemo();
            Assert.That(SusStoryStates.Force(disabled, SusStoryStates.Disabled),
                Is.EqualTo(SusStateForcing.Prop));
            Assert.That(disabled.Disabled.Value, Is.True,
                "the component's own binding puts the class on, exactly as in a real app");

            var error = new StatePropDemo();
            Assert.That(SusStoryStates.Force(error, SusStoryStates.Error),
                Is.EqualTo(SusStateForcing.Prop));
            Assert.That(error.Error.Value, Is.True);
        }

        [Test]
        public void Without_a_prop_the_state_falls_back_to_the_visual_state_class()
        {
            NoTwins();

            var disabled = new CoreSwatchDemo();       // no Disabled prop of its own
            Assert.That(SusStoryStates.Force(disabled, SusStoryStates.Disabled),
                Is.EqualTo(SusStateForcing.StateClass));
            Assert.That(disabled.VisualState, Is.EqualTo("disabled"));

            var focus = new CoreSwatchDemo();
            Assert.That(SusStoryStates.Force(focus, SusStoryStates.Focus),
                Is.EqualTo(SusStateForcing.StateClass));
            Assert.That(focus.ClassListContains("sus-vs--focused"), Is.True,
                "the corpus has zero :focus selectors; focus is a class (plan §2.6)");
        }

        // ── the budget ───────────────────────────────────────────────────────

        [Test]
        public void Inside_the_budget_the_matrix_opens_and_builds_every_cell()
        {
            NoTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.CellCount, Is.EqualTo(matrix.Rows.Count * matrix.Columns.Count));
            Assert.That(matrix.CellCount, Is.EqualTo(20), "5 tones × 4 forceable states");
            Assert.That(matrix.CellCount, Is.LessThanOrEqualTo(SusStoryMatrix.CellBudget));
            Assert.That(matrix.CollapseReason, Is.Null);
            Assert.That(matrix.Open, Is.True);
            Assert.That(CellsInTree(matrix), Is.EqualTo(20), "one live instance per cell");
        }

        [Test]
        public void Over_the_budget_the_matrix_starts_folded_and_costs_nothing()
        {
            AllTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.CellCount, Is.EqualTo(30), "5 tones × 6 states");
            Assert.That(matrix.OverBudget, Is.True);
            Assert.That(matrix.Open, Is.False);
            Assert.That(matrix.BuiltCellCount, Is.Zero);
            Assert.That(CellsInTree(matrix), Is.Zero, "no instance is built until it is asked for");
            Assert.That(matrix.CollapseReason, Is.EqualTo("cell budget"));
            Assert.That(matrix.ToggleText, Does.StartWith("▸ matrix · Tone × state · 5 × 6"));

            matrix.Toggle();

            Assert.That(matrix.Open, Is.True);
            Assert.That(CellsInTree(matrix), Is.EqualTo(30));
            Assert.That(matrix.ToggleText, Does.StartWith("▾ matrix"));
        }

        [Test]
        public void A_heavy_story_stays_folded_even_inside_the_budget()
        {
            NoTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            var entry = Entry(Swatch);
            entry.Weight = SusStoryWeight.Heavy;
            var matrix = new SusStoryMatrix();

            try
            {
                matrix.Show(entry);

                Assert.That(matrix.CellCount, Is.EqualTo(20));
                Assert.That(matrix.OverBudget, Is.False, "the budget alone would have opened it");
                Assert.That(matrix.Open, Is.False);
                Assert.That(matrix.CollapseReason, Is.EqualTo("heavy story"));
            }
            finally
            {
                entry.Weight = SusStoryWeight.Normal;
            }
        }

        [Test]
        public void Weight_is_carried_from_the_attribute_to_the_entry()
        {
            Assert.That(Entry(Counter).Weight, Is.EqualTo(SusStoryWeight.Normal),
                "a story that declares nothing is normal");
            Assert.That(new SusStoryAttribute("core/x/y").Weight, Is.EqualTo(SusStoryWeight.Normal));
            Assert.That(new SusStoryDefinition().Weight, Is.EqualTo(SusStoryWeight.Normal));
        }

        // ── the live-measurement line ────────────────────────────────────────

        [Test]
        public void The_size_line_reads_resolvedStyle_of_the_mounted_instance()
        {
            var sizes = new SusStorySizes();
            var element = new VisualElement();
            element.style.fontSize = 14;
            element.style.paddingLeft = 12;
            element.style.borderTopLeftRadius = 4;

            sizes.Track(element);

            // Computed values (font size, radius) resolve at once; height and padding are LAYOUT
            // results and stay unmeasured until the element is laid out — which is exactly why the
            // line re-reads on every GeometryChangedEvent of the instance instead of once.
            Assert.That(sizes.Tracked, Is.SameAs(element));
            Assert.That(sizes.SizesText, Is.EqualTo(
                "h " + SusStorySizes.Unmeasured + " · fs 14 · pad 0 · radius 4  (resolvedStyle)"));
            Assert.That(sizes.SizesText, Does.Contain("(resolvedStyle)"),
                "the line names its source, so nobody reads it as the token table");
        }

        [Test]
        public void The_size_line_is_empty_with_nothing_mounted()
        {
            var sizes = new SusStorySizes();
            sizes.Track(new VisualElement());

            sizes.Track(null);

            Assert.That(sizes.SizesText, Is.Empty);
            Assert.That(sizes.OverlayNoteVisible, Is.False);
        }

        // ── the stage and its own overlay host ───────────────────────────────

        [Test]
        public void The_stage_shows_crumbs_a_name_a_purpose_and_a_matrix()
        {
            NoTwins();
            using var host = new SusStorybookHost();

            host.ShowStoryById(Swatch);

            var entry = Entry(Swatch);
            var crumbs = host.Query<Label>(className: "sus-sb-stage__crumbs").ToList();
            var titles = host.Query<Label>(className: "sus-sb-stage__title").ToList();
            var purposes = host.Query<Label>(className: "sus-sb-stage__purpose").ToList();

            Assert.That(crumbs.Single().text,
                Is.EqualTo(entry.Package + " / " + entry.Group + " / " + entry.Name));
            Assert.That(titles.Single().text, Is.EqualTo(entry.Name));
            Assert.That(purposes.Single().text, Is.EqualTo(entry.Purpose));
            Assert.That(purposes.Single().text, Is.Not.Empty, "a story says what it is for");
            Assert.That(host.Matrix.Rows, Is.Not.Empty);
            Assert.That(host.Sizes.Tracked, Is.Not.Null, "the size line follows the live instance");
        }

        [Test]
        public void A_story_mounted_on_the_stage_resolves_to_the_stage_overlay_host()
        {
            using var host = new SusStorybookHost();

            host.ShowStoryById(Floating);
            var story = host.QaCanvas.Children().OfType<SusComponent>().First();

            Assert.That(host.CanvasOverlay, Is.Not.Null);
            Assert.That(SusBootstrap.ResolveOverlayHost(story), Is.SameAs(host.CanvasOverlay),
                "a popup must land in the stage canvas, not at the panel root (T-3032)");
        }

        [Test]
        public void An_open_overlay_grows_the_canvas_and_says_where_the_popup_went()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById(Floating);

            host.CanvasOverlay.AddToOverlay(new Label("popup"), OverlayCategory.Dropdown);
            host.SyncOverlay();

            Assert.That(host.QaCanvas.ClassListContains("sus-sb-stage__canvas--overlay"), Is.True,
                "the canvas grows so the popup stays inside the frame");
            Assert.That(host.Sizes.OverlayNoteVisible, Is.True);

            host.CanvasOverlay.ClearAll();
            host.SyncOverlay();

            Assert.That(host.QaCanvas.ClassListContains("sus-sb-stage__canvas--overlay"), Is.False);
            Assert.That(host.Sizes.OverlayNoteVisible, Is.False);
        }

        [Test]
        public void An_empty_stage_carries_no_matrix()
        {
            NoTwins();
            using var host = new SusStorybookHost();
            host.ShowStoryById(Swatch);
            Assert.That(host.Matrix.Rows, Is.Not.Empty);

            host.Url.HandleExternal("#/core/primitives/gone");

            Assert.That(host.Matrix.Rows, Is.Empty);
            Assert.That(CellsInTree(host), Is.Zero);
        }
    }
}
