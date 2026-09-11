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
        const string Swatch = "enginetests/primitives/swatch";
        const string Counter = "enginetests/primitives/counter";
        const string Floating = "enginetests/overlay/floating";

        string _axisBefore;

        [SetUp]
        public void SetUp()
        {
            _axisBefore = SusStoryMatrix.AxisPropName;
            SusStateTwins.Reset();
            SusStateRoles.Reset();
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
        }

        [TearDown]
        public void TearDown()
        {
            SusStoryMatrix.AxisPropName = _axisBefore;
            SusStateTwins.Reset();
            SusStateRoles.Reset();
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
            root.Query<VisualElement>(className: "sb-matrix__cell").ToList().Count;

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

        }

        /// <summary>
        /// Card T-3427, plan ARCH-20260911-KIT-STATE-CONTRACT.md §2.3 and D4 <c>d:da0868</c>.
        /// The focus column used to call <c>SetVisualState("focused")</c>, which drives the
        /// <c>&lt;prefix&gt;--focused</c> group — styled by four kit components out of eighty,
        /// and re-derived from a binding on the next render in three of those. The ring the
        /// corpus actually draws hangs off <c>.keyboard-focus</c>. The intersection was empty,
        /// and that — not eighty unstyled components — is why every focus column was a copy of
        /// rest.
        /// </summary>
        [Test]
        public void The_focus_column_wears_the_class_the_corpus_paints_the_ring_with()
        {
            NoTwins();
            var focus = new CoreSwatchDemo();

            var how = SusStoryStates.Force(focus, SusStoryStates.Focus);

            Assert.That(how, Is.EqualTo(SusStateForcing.FocusClass));
            Assert.That(focus.ClassListContains(SusStateRoles.KeyboardFocusClass), Is.True,
                "the ring lives on .keyboard-focus (SusKeyboardFocus), and nothing else paints it");
            Assert.That(focus.ClassListContains("sus-vs--focused"), Is.False,
                "the old lever drove a class group the corpus does not style");
            Assert.That(focus.VisualState, Is.EqualTo("normal"),
                "and it no longer moves the component's mutually-exclusive visual state");
        }

        /// <summary>
        /// D5 <c>d:2fead6</c>: for role <c>group</c> the ring rides a CHILD. Dressing the root
        /// would repeat T-3427 with a better class — the column would still be a copy of rest.
        /// </summary>
        [Test]
        public void For_a_group_the_focus_class_lands_on_the_declared_child()
        {
            NoTwins();
            SusStateRoles.Declare(nameof(CoreSwatchDemo), SusStateRoles.Group, "swatch__item");
            var group = new CoreSwatchDemo();
            var item = new VisualElement();
            item.AddToClassList("swatch__item");
            group.Add(item);

            var how = SusStoryStates.Force(group, SusStoryStates.Focus);

            Assert.That(how, Is.EqualTo(SusStateForcing.FocusClass));
            Assert.That(item.ClassListContains(SusStateRoles.KeyboardFocusClass), Is.True);
            Assert.That(group.ClassListContains(SusStateRoles.KeyboardFocusClass), Is.False,
                "the group box has no ring of its own; the focused child does");
        }

        /// <summary>
        /// A group builds its rows on its FIRST LAYOUT, and a matrix cell forces its state the
        /// instant the instance exists — so the declared child is genuinely absent at that
        /// moment. Measured in Play: without the late pass, all 14 components that owed a ring
        /// and showed none were groups. The class waits on the root and moves when the child
        /// arrives; a target that never arrives leaves the root wearing it, because a focus
        /// column with the class nowhere would be the copy of rest all over again.
        /// </summary>
        [Test]
        public void A_declared_target_that_arrives_late_still_gets_the_ring()
        {
            NoTwins();
            SusStateRoles.Declare(nameof(CoreSwatchDemo), SusStateRoles.Group, "swatch__item");
            var group = new CoreSwatchDemo();

            SusStoryStates.Force(group, SusStoryStates.Focus);

            Assert.That(group.ClassListContains(SusStateRoles.KeyboardFocusClass), Is.True,
                "parked on the root while the child is missing");

            var item = new VisualElement();
            item.AddToClassList("swatch__item");
            group.Add(item);
            // What the geometry hook of Force() calls. An element outside a panel gets no
            // geometry events at all, so the move is asserted through the same method the hook
            // uses rather than through an event this fixture cannot deliver.
            Assert.That(SusStoryStates.SettleFocusRing(group), Is.True);

            Assert.That(item.ClassListContains(SusStateRoles.KeyboardFocusClass), Is.True,
                "and moved onto the child the moment the group had one");
            Assert.That(group.ClassListContains(SusStateRoles.KeyboardFocusClass), Is.False,
                "two rings would be worse than none");
        }

        // ── columns: the role half of the matrix (card T-3431) ───────────────

        /// <summary>
        /// Plan §4.6 and D14 <c>d:5ea31f</c>. A column promising a state the role does not have
        /// lies exactly as much as a state the role owes and nobody drew.
        /// </summary>
        [Test]
        public void A_role_that_has_no_state_gets_no_matrix_at_all()
        {
            AllTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            SusStateRoles.Declare(nameof(CoreSwatchDemo), SusStateRoles.Display);
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.Role, Is.EqualTo(SusStateRoles.Display));
            Assert.That(matrix.SilentReason, Is.Not.Null);
            Assert.That(matrix.Columns, Is.Empty, "no column of state survives the role");
            Assert.That(matrix.Rows, Is.Empty);
            Assert.That(matrix.CellCount, Is.Zero);
            Assert.That(matrix.ClassListContains("sb-hidden"), Is.True,
                "not a folded matrix — an absent one; the variant axis below already says the rest");
        }

        [Test]
        public void The_columns_of_a_control_stop_at_the_states_the_role_has()
        {
            AllTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            SusStateRoles.Declare(nameof(CoreSwatchDemo), SusStateRoles.Control);
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.Columns, Is.EqualTo(new[]
            {
                SusStoryStates.Rest, SusStoryStates.Hover, SusStoryStates.Focus,
                SusStoryStates.Active, SusStoryStates.Disabled,
            }));
            Assert.That(matrix.SilentReason, Is.Null);
            Assert.That(matrix.Columns, Does.Not.Contain(SusStoryStates.Error),
                "a control carries no value, so it has nothing that can be invalid (D13)");
            Assert.That(matrix.SkippedStates, Is.Empty,
                "a column the role does not have is not 'skipped' — it was never a column");
        }

        [Test]
        public void An_input_keeps_the_error_column_and_a_component_may_override_its_role()
        {
            AllTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            SusStateRoles.Declare(nameof(CoreSwatchDemo), SusStateRoles.Input);
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.Columns, Is.EqualTo(SusStoryStates.All),
                "a value that can be invalid is a state, not a colour variant");
            Assert.That(SusStateRoles.DutyOfName("SusFormField", SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.Required),
                "the field wrapper departs from its surface role, and the departure is DATA");
            Assert.That(SusStateRoles.RoleDuty(SusStateRoles.Surface, SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.No));
        }

        /// <summary>
        /// Witness from the first Play measurement of T-3431: two kit stories
        /// (<c>kit/world/floating-damage</c>, <c>kit/devtools/diagnostics</c>) build a story-local
        /// scene WRAPPER, so the probe's type is in no registry — and "unknown" reads as "no
        /// opinion", which put four columns of state over a <c>display</c> component. The story
        /// names its SUBJECT; the probe is only evidence about what can be forced.
        /// </summary>
        [Test]
        public void The_role_follows_the_story_subject_and_not_the_instance_it_built()
        {
            SusStateRoles.Declare(nameof(CoreSwatchDemo), SusStateRoles.Display);

            Assert.That(SusStoryStates.Declares(typeof(CoreSwatchDemo), SusStoryStates.Error), Is.False);
            Assert.That(SusStoryStates.Declares(typeof(CoreSwatchDemo), SusStoryStates.Rest), Is.True,
                "rest is the absence of a state, not one a role can refuse");
            Assert.That(SusStoryStates.Declares((System.Type)null, SusStoryStates.Error), Is.True,
                "an unnamed component is still no opinion");

            SusStoryMatrix.AxisPropName = "Tone";
            var matrix = new SusStoryMatrix();
            matrix.Show(Entry(Swatch));

            Assert.That(Entry(Swatch).ComponentType, Is.EqualTo(typeof(CoreSwatchDemo)),
                "the story declares its subject, and that is what the role is read from");
            Assert.That(matrix.Role, Is.EqualTo(SusStateRoles.Display));
            Assert.That(matrix.SilentReason, Is.Not.Null);
        }

        [Test]
        public void A_component_outside_the_registry_keeps_every_column_it_had()
        {
            AllTwins();
            SusStoryMatrix.AxisPropName = "Tone";
            var matrix = new SusStoryMatrix();

            matrix.Show(Entry(Swatch));

            Assert.That(matrix.Role, Is.Null);
            Assert.That(matrix.Columns, Is.EqualTo(SusStoryStates.All),
                "silence of the registry is not a statement about a component: wave 6 adds the rest");
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
            Assert.That(new SusStoryAttribute("enginetests/x/y").Weight, Is.EqualTo(SusStoryWeight.Normal));
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
            var crumbs = host.Query<Label>(className: "sb-stage__crumbs").ToList();
            var titles = host.Query<Label>(className: "sb-stage__title").ToList();
            var purposes = host.Query<Label>(className: "sb-stage__purpose").ToList();

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

        /// <summary>
        /// Card T-3362, plan ARCH-20260911-STORYBOOK-SHELL.md §4.7 and D18. Opening a popup used
        /// to put <c>sb-stage__canvas--overlay</c> on the canvas, and the shell sheet answers
        /// that class with <c>min-height: 350px</c> against a base of 120 — so every popover,
        /// tooltip and menu moved the canvas and everything below it by 230 px, and closing moved
        /// it back. The fork was decided the other way: the canvas keeps ONE declared height and
        /// the popup stays in the frame because the overlay host lives INSIDE the canvas
        /// (T-3032, asserted by the test above). The note still says where the popup went.
        /// </summary>
        [Test]
        public void An_open_overlay_says_where_the_popup_went_and_does_not_move_the_canvas()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById(Floating);

            host.CanvasOverlay.AddToOverlay(new Label("popup"), OverlayCategory.Dropdown);
            host.SyncOverlay();

            Assert.That(host.QaCanvas.ClassListContains("sb-stage__canvas--overlay"), Is.False,
                "the canvas has one declared height: opening a popup must not grow it by 230px");
            Assert.That(host.Sizes.OverlayNoteVisible, Is.True);

            host.CanvasOverlay.ClearAll();
            host.SyncOverlay();

            Assert.That(host.QaCanvas.ClassListContains("sb-stage__canvas--overlay"), Is.False);
            Assert.That(host.Sizes.OverlayNoteVisible, Is.False);
        }

        [Test]
        public void An_empty_stage_carries_no_matrix()
        {
            NoTwins();
            using var host = new SusStorybookHost();
            host.ShowStoryById(Swatch);
            Assert.That(host.Matrix.Rows, Is.Not.Empty);

            host.Url.HandleExternal("#/enginetests/primitives/gone");

            Assert.That(host.Matrix.Rows, Is.Empty);
            Assert.That(CellsInTree(host), Is.Zero);
        }
    }
}
