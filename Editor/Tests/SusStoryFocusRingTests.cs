using NUnit.Framework;
using Sharq.Core;
using Sharq.Core.Storybook;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Survival of the focus ring across a component's own re-render (card T-3450, plan
    /// <c>docs-canon/plans/impl/ARCH-20260911-KIT-STATE-CONTRACT.md</c> §2.3).
    ///
    /// What these tests are FOR: the ring of a group rides a CHILD, and that child is the output
    /// of a reactive render — <c>BindListFor</c> and every hand-rolled row builder begin with
    /// <c>container.Clear()</c>. So the element wearing <c>.keyboard-focus</c> is not modified, it
    /// is DESTROYED, and its replacement is born bare. The matrix then shows a focus column that
    /// is pixel-for-pixel the rest column — measured on <c>SusListGroup</c> and
    /// <c>SusDataTable</c>, which both lit their ring and lost it before the first frame.
    ///
    /// The failure is silent in the worst way: nothing throws, nothing logs, and the column is
    /// still drawn. Only a pixel comparison notices, and that comparison is what T-3427 spent a
    /// wave building. Hence a unit test at the seam instead: the request for a ring is remembered
    /// per instance, and the ring is put back after the render that ate it.
    ///
    /// Two things this rig cannot deliver, and how each is handled honestly:
    /// <list type="bullet">
    /// <item>A reactive flush. <c>ScheduleBindUpdate</c> returns early while <c>panel</c> is null,
    ///   so a prop change on a detached instance never re-renders. The fixture therefore exposes
    ///   its render body as a method and the test calls THAT — the same code the effect runs, not
    ///   an imitation of it.</item>
    /// <item>A geometry event. An element outside a panel gets none, so the restore is asked for
    ///   through <see cref="SusStoryStates.SettleFocusRing"/> — exactly what the geometry keeper
    ///   registered by <c>Force</c> calls.</item>
    /// </list>
    /// </summary>
    public class SusStoryFocusRingTests
    {
        const string RowClass = "fx-rerender__row";

        /// <summary>
        /// A group whose rows ARE a render product: every render clears the container and builds
        /// fresh children, which is the shape of <c>SusListGroup</c> and of the body of
        /// <c>SusDataTable</c>.
        /// </summary>
        sealed class RerenderingGroupDemo : SusComponent
        {
            public Prop<int> Rows = new(2);

            VisualElement _container;

            protected override void Build()
            {
                _container = new VisualElement();
                _container.AddToClassList("fx-rerender");
                Add(_container);

                WatchEffect(() => Rerender(Rows.Value));
            }

            /// <summary>
            /// The render body, reachable by name. It is what the effect above runs; a test calls
            /// it directly because a detached instance never gets a flush.
            /// </summary>
            public void Rerender(int rows)
            {
                _container.Clear();
                for (int i = 0; i < rows; i++)
                {
                    var row = new VisualElement();
                    row.AddToClassList(RowClass);
                    _container.Add(row);
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            SusStateRoles.Reset();
            SusStateRoles.Declare(nameof(RerenderingGroupDemo), SusStateRoles.Group, RowClass);
        }

        [TearDown]
        public void TearDown() => SusStateRoles.Reset();

        static VisualElement FirstRow(VisualElement group) => group.Q(className: RowClass);

        static int RingCount(VisualElement root)
        {
            int n = root.ClassListContains(SusStateRoles.KeyboardFocusClass) ? 1 : 0;
            foreach (var e in root.Query<VisualElement>().ToList())
            {
                if (e.ClassListContains(SusStateRoles.KeyboardFocusClass)) n++;
            }
            return n;
        }

        [Test]
        public void A_re_render_really_does_strip_the_ring_off_the_child()
        {
            // The premise, asserted rather than assumed: without it the restore below would be
            // guarding a defect the fixture no longer reproduces.
            var group = new RerenderingGroupDemo();
            var before = FirstRow(group);
            SusStoryStates.Force(group, SusStoryStates.Focus);
            Assert.That(before.ClassListContains(SusStateRoles.KeyboardFocusClass), Is.True,
                "the ring lands on the declared child");

            group.Rerender(3);

            Assert.That(before.parent, Is.Null, "the element that wore the ring left the tree");
            Assert.That(FirstRow(group), Is.Not.SameAs(before), "its replacement is a different element");
            Assert.That(FirstRow(group).ClassListContains(SusStateRoles.KeyboardFocusClass), Is.False,
                "and is born bare — this is the defect T-3450 names");
        }

        [Test]
        public void The_ring_is_put_back_on_the_child_the_re_render_produced()
        {
            var group = new RerenderingGroupDemo();
            SusStoryStates.Force(group, SusStoryStates.Focus);

            group.Rerender(3);
            var restored = SusStoryStates.SettleFocusRing(group);

            Assert.That(restored, Is.True, "the keeper had work to do and says so");
            Assert.That(FirstRow(group).ClassListContains(SusStateRoles.KeyboardFocusClass), Is.True,
                "the new child wears the ring the old one took with it");
            Assert.That(RingCount(group), Is.EqualTo(1),
                "exactly one ring: a leftover copy would read as two focused rows");
        }

        [Test]
        public void Repeated_renders_do_not_wear_the_ring_out()
        {
            var group = new RerenderingGroupDemo();
            SusStoryStates.Force(group, SusStoryStates.Focus);

            for (int rows = 3; rows <= 6; rows++)
            {
                group.Rerender(rows);
                SusStoryStates.SettleFocusRing(group);
            }

            Assert.That(FirstRow(group).ClassListContains(SusStateRoles.KeyboardFocusClass), Is.True);
            Assert.That(RingCount(group), Is.EqualTo(1));
        }

        [Test]
        public void The_keeper_is_idle_when_the_ring_is_already_where_it_belongs()
        {
            var group = new RerenderingGroupDemo();
            SusStoryStates.Force(group, SusStoryStates.Focus);

            Assert.That(SusStoryStates.SettleFocusRing(group), Is.False,
                "nothing to do — and saying otherwise is what would make the geometry keeper loop");
        }

        [Test]
        public void An_instance_that_never_asked_for_a_ring_is_never_dressed_with_one()
        {
            // The request cannot be read back off the tree — a stripped ring leaves no trace
            // anywhere in it — so it is remembered per instance. The other half of that: an
            // instance the matrix shows at rest stays at rest no matter who calls the keeper.
            var group = new RerenderingGroupDemo();

            Assert.That(SusStoryStates.SettleFocusRing(group), Is.False);
            Assert.That(RingCount(group), Is.EqualTo(0));
        }
    }
}
