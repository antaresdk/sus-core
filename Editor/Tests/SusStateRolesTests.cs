using System.Linq;
using NUnit.Framework;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The role registry of the state contract (cards T-3427 and T-3431, plan
    /// <c>docs-canon/plans/impl/ARCH-20260911-KIT-STATE-CONTRACT.md</c> §4.1–§4.2 and §4.6).
    ///
    /// What these tests are FOR: the registry is the only thing standing between the matrix and
    /// its old habit of drawing every column it could force. If a row of it goes missing the
    /// matrix does not crash — it quietly starts promising states again, and the only witness is
    /// a frame nobody is looking at. So the shape of the table is asserted here, by name: this
    /// assembly cannot reference the component library, and it does not need to — a registry is data.
    ///
    /// The counts are the plan's own (§4.2) and they are transcribed from
    /// <see cref="SusStateRoles.DataSource"/>, which is what rule <c>R145 state-contract</c>
    /// judges the corpus against. Drift between the two shows up as a failing count here or as an
    /// R145 finding there, and both name the same file.
    /// </summary>
    public class SusStateRolesTests
    {
        [TearDown]
        public void TearDown() => SusStateRoles.Reset();

        [Test]
        public void Every_kit_component_carries_exactly_one_role_and_the_seven_add_up_to_eighty()
        {
            Assert.That(SusStateRoles.NamesOf(SusStateRoles.Control).Count, Is.EqualTo(9));
            Assert.That(SusStateRoles.NamesOf(SusStateRoles.Input).Count, Is.EqualTo(9));
            Assert.That(SusStateRoles.NamesOf(SusStateRoles.Group).Count, Is.EqualTo(18));
            Assert.That(SusStateRoles.NamesOf(SusStateRoles.Surface).Count, Is.EqualTo(8));
            Assert.That(SusStateRoles.NamesOf(SusStateRoles.Overlay).Count, Is.EqualTo(8));
            Assert.That(SusStateRoles.NamesOf(SusStateRoles.Feedback).Count, Is.EqualTo(11));
            Assert.That(SusStateRoles.NamesOf(SusStateRoles.Display).Count, Is.EqualTo(17));

            var all = SusStateRoles.Names.ToList();
            Assert.That(all.Count, Is.EqualTo(80), "the kit corpus of the plan, component by component");
            Assert.That(all.Distinct().Count(), Is.EqualTo(80), "a component has one role, not two");
            Assert.That(SusStateRoles.AllRoles.Sum(r => SusStateRoles.NamesOf(r).Count),
                Is.EqualTo(80), "and no component sits outside the seven roles");
        }

        /// <summary>
        /// §4.6: the matrix stops being drawn for 36 of the 80 — 17 display, 11 feedback, 8
        /// overlay. This is the number card T-3431 is measured by.
        /// </summary>
        [Test]
        public void Thirty_six_components_have_no_state_to_show_and_forty_four_do()
        {
            var silent = SusStateRoles.Names
                .Where(n => !SusStateRoles.States.Any(s =>
                    SusStateRoles.DutyOfName(n, s) != SusStateDuty.No))
                .ToList();

            Assert.That(silent.Count, Is.EqualTo(36),
                "17 display + 11 feedback + 8 overlay: a column over any of them is a forgery (D14)");
            Assert.That(silent.Count(n => SusStateRoles.RoleOfName(n) == SusStateRoles.Display),
                Is.EqualTo(17));
            Assert.That(silent.Count(n => SusStateRoles.RoleOfName(n) == SusStateRoles.Feedback),
                Is.EqualTo(11));
            Assert.That(silent.Count(n => SusStateRoles.RoleOfName(n) == SusStateRoles.Overlay),
                Is.EqualTo(8));
            Assert.That(80 - silent.Count, Is.EqualTo(44),
                "9 control + 9 input + 18 group + 8 surface keep a meaningful matrix");
        }

        /// <summary>
        /// D13 <c>d:f5da73</c>. <c>sus-alert--error</c> is a COLOUR of the alert, not a breakage
        /// of it: an alert does not enter the error state, it reports one. The column would
        /// promise a transition that does not exist — the same forgery as a <c>focus</c> caption
        /// over a copy of rest, from the other side.
        /// </summary>
        [Test]
        public void Error_is_a_state_only_where_there_is_a_value_that_can_be_invalid()
        {
            Assert.That(SusStateRoles.RoleDuty(SusStateRoles.Input, SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.Required));
            Assert.That(SusStateRoles.RoleDuty(SusStateRoles.Feedback, SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.No));
            Assert.That(SusStateRoles.RoleDuty(SusStateRoles.Control, SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.No), "a control carries no value");

            Assert.That(SusStateRoles.DutyOfName("SusAlert", SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.No));
            Assert.That(SusStateRoles.DutyOfName("SusTextfield", SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.Required));
            Assert.That(SusStateRoles.DutyOfName("SusFormField", SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.Required),
                "the field wrapper departs from its surface role, and the departure is a ROW");

            var withError = SusStateRoles.Names
                .Count(n => SusStateRoles.DutyOfName(n, SusStoryStates.Error) != SusStateDuty.No);
            Assert.That(withError, Is.EqualTo(10), "9 input plus SusFormField (§4.2)");
        }

        /// <summary>D5 <c>d:2fead6</c>: where the ring rides a child, the child is NAMED.</summary>
        [Test]
        public void A_group_whose_ring_lives_on_a_child_declares_that_child()
        {
            Assert.That(SusStateRoles.RingTargetClassOfName("SusTabs"), Is.EqualTo("sus-tabs__tab"));
            Assert.That(SusStateRoles.RingTargetClassOfName("SusListGroup"),
                Is.EqualTo("sus-list-group__item"));
            Assert.That(SusStateRoles.RingTargetClassOfName("SusButton"), Is.Null,
                "a control takes the focus itself: the ring rides the root and needs no row");
        }

        /// <summary>
        /// The registry covers the first corpus today; the second is wave 6 of the plan. Until
        /// then an unknown component must keep the columns it had — silence of a registry is not
        /// a statement about a component, and reading it as one would blank 48 matrices at once.
        /// </summary>
        [Test]
        public void An_unknown_component_is_no_opinion_rather_than_no_states()
        {
            Assert.That(SusStateRoles.RoleOfName("SusNotAThing"), Is.Null);
            Assert.That(SusStateRoles.DutyOfName("SusNotAThing", SusStoryStates.Focus),
                Is.EqualTo(SusStateDuty.Variant));
            Assert.That(SusStateRoles.Duty(typeof(CoreSwatchDemo), SusStoryStates.Error),
                Is.EqualTo(SusStateDuty.Variant));
            Assert.That(SusStateRoles.Knows(typeof(CoreSwatchDemo)), Is.False);
        }

        [Test]
        public void A_declaration_wins_over_the_table_and_the_reset_takes_it_back()
        {
            SusStateRoles.Declare(nameof(CoreSwatchDemo), SusStateRoles.Display);
            Assert.That(SusStateRoles.RoleOf(typeof(CoreSwatchDemo)), Is.EqualTo(SusStateRoles.Display));
            Assert.That(SusStateRoles.DeclaresAnyState(typeof(CoreSwatchDemo)), Is.False);

            SusStateRoles.Reset();

            Assert.That(SusStateRoles.RoleOf(typeof(CoreSwatchDemo)), Is.Null);
            Assert.That(SusStateRoles.DeclaresAnyState(typeof(CoreSwatchDemo)), Is.True,
                "back to no opinion, which the matrix reads as every column");
        }
    }
}
