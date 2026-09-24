using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.UI;
using Sharq.Core.Editor.TestSupport;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Card T-3905 (plan ARCH-20260923-STORY-SUBJECT-HOST.md, step H1): a story declares
    /// <c>ctx.SetHost(host, slot)</c> and the ENGINE — never the story — puts the host where the
    /// instance would otherwise have gone, with the instance parented last inside the slot
    /// (plan D1-D3). Byte-for-byte compatible for every story that never calls it (plan D6).
    ///
    /// Two layers: <see cref="SusStoryContext.SetHost"/> itself (plain <see cref="Test"/>, no
    /// engine needed — D2's validation is a property of the context, not of mounting) and the
    /// engine honouring it on the stage, in a matrix cell, and through teardown.
    /// </summary>
    public class SusStorySubjectHostTests
    {
        const string Counter = "enginetests/primitives/counter";
        const string Hosted = "enginetests/overlay/hosted";
        const string HostedAxis = "enginetests/overlay/hosted-axis";
        const string HostedScenery = "enginetests/overlay/hosted-scenery";
        const string HostedModal = "enginetests/overlay/hosted-modal";

        [SetUp]
        public void SetUp()
        {
            SusStateTwins.Reset();
            SusStateRoles.Reset();
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
        }

        [TearDown]
        public void TearDown()
        {
            SusStateTwins.Reset();
            SusStateRoles.Reset();
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        static SusStoryEntry Entry(string id) => SusStoryRegistry.Find(id);

        /// <summary>The canvas's own viewport (card T-3708, decision D1) — the ScrollView an
        /// element added via <c>_canvasViewport.Add(...)</c> reports as its LOGICAL
        /// <see cref="VisualElement.parent"/> (Unity resolves a container's content redirection
        /// there, so a mounted subject's <c>.parent</c> is the ScrollView itself, never its
        /// <c>contentContainer</c> — <see cref="SusStorybookHost.QaSubjectRoot"/> is one level
        /// too deep for a <c>.parent</c> comparison, same pattern as
        /// <see cref="SusStoryStageScrollTests"/> and <see cref="SusStoryCanvasViewportGeometryTests"/>).</summary>
        static ScrollView ViewportOf(SusStorybookHost host)
        {
            VisualElement e = host.QaSubjectRoot;
            while (e != null && !(e is ScrollView)) e = e.parent;
            return e as ScrollView;
        }

        // ── SetHost itself: D2's contract, no mounting involved ──────────────

        [Test]
        public void SetHost_ignores_a_null_host()
        {
            var ctx = new SusStoryContext(Entry(Counter), new CoreCounterDemo(), null);

            ctx.SetHost(null);

            Assert.That(ctx.Host, Is.Null);
            Assert.That(ctx.Slot, Is.Null);
        }

        [Test]
        public void SetHost_defaults_the_slot_to_the_host()
        {
            var ctx = new SusStoryContext(Entry(Counter), new CoreCounterDemo(), null);
            var host = new VisualElement();

            ctx.SetHost(host);

            Assert.That(ctx.Host, Is.SameAs(host));
            Assert.That(ctx.Slot, Is.SameAs(host));
        }

        [Test]
        public void SetHost_accepts_a_descendant_slot()
        {
            var ctx = new SusStoryContext(Entry(Counter), new CoreCounterDemo(), null);
            var host = new VisualElement();
            var inner = new VisualElement();
            host.Add(inner);

            ctx.SetHost(host, inner);

            Assert.That(ctx.Host, Is.SameAs(host));
            Assert.That(ctx.Slot, Is.SameAs(inner));
        }

        [Test]
        public void SetHost_rejects_a_slot_outside_the_host()
        {
            var ctx = new SusStoryContext(Entry(Counter), new CoreCounterDemo(), null);
            var host = new VisualElement();
            var stray = new VisualElement();   // not a descendant of host

            Assert.Throws<ArgumentException>(() => ctx.SetHost(host, stray));
        }

        [Test]
        public void SetHost_rejects_a_second_call()
        {
            var ctx = new SusStoryContext(Entry(Counter), new CoreCounterDemo(), null);
            ctx.SetHost(new VisualElement());

            Assert.Throws<InvalidOperationException>(() => ctx.SetHost(new VisualElement()));
        }

        // ── the stage: SusStorybookHost.Mount / Unmount ───────────────────────

        [Test]
        public void Stage_mounts_the_host_with_the_instance_last_in_the_slot()
        {
            using var host = new SusStorybookHost();

            Assert.IsTrue(host.ShowStoryById(Hosted));

            // The canvas also carries its own OverlayHost sibling (SusBootstrap.GetOrCreateOverlay,
            // T-3032) — a fixture of the shell, not part of what this card places — so the check is
            // "the host is a DIRECT child of the canvas", not an exact child count.
            Assert.That(host.QaSubjectRoot.Children().Count(c => c.ClassListContains(CoreHostScaffold.HostClass)),
                Is.EqualTo(1), "the HOST takes the instance's place, directly under the canvas");
            var mountedHost = host.QaSubjectRoot.Children().First(c => c.ClassListContains(CoreHostScaffold.HostClass));
            Assert.That(host.QaSubjectHost, Is.SameAs(mountedHost));

            var slot = mountedHost.Q<VisualElement>(className: CoreHostScaffold.SlotClass);
            Assert.That(slot, Is.Not.Null);
            Assert.That(slot.childCount, Is.EqualTo(1));
            Assert.That(slot.ElementAt(0), Is.InstanceOf<CoreCounterDemo>(),
                "the instance is the slot's LAST (and only) child");
        }

        [Test]
        public void Stage_without_SetHost_still_mounts_the_instance_directly_on_the_canvas()
        {
            using var host = new SusStorybookHost();

            Assert.IsTrue(host.ShowStoryById(Counter));

            var instances = host.QaSubjectRoot.Children().OfType<CoreCounterDemo>().ToList();
            Assert.That(instances, Has.Count.EqualTo(1));
            Assert.That(instances[0].parent, Is.SameAs(ViewportOf(host)),
                "regression (plan D6): a story that never calls SetHost mounts byte-for-byte as before " +
                "- a direct child of the canvas's own viewport (card T-3708, decision D1), no host in between");
            Assert.That(host.QaSubjectHost, Is.Null);
        }

        [Test]
        public void Scenery_lands_beside_the_host_not_beside_the_buried_instance()
        {
            using var host = new SusStorybookHost();

            Assert.IsTrue(host.ShowStoryById(HostedScenery));

            // Card T-3168 default (before=false): trigger, then host — never inside the host,
            // where AddSibling's own canvas-index math would never find it (plan D3). The canvas
            // overlay host (T-3032) is a third sibling this card does not place, so the check is
            // relative order between the two markers, not an exact child count.
            var children = host.QaSubjectRoot.Children().ToList();
            int triggerAt = children.FindIndex(c => c.ClassListContains(CoreScenery.MarkerClass));
            int hostAt = children.FindIndex(c => c.ClassListContains(CoreHostScaffold.HostClass));
            Assert.That(triggerAt, Is.GreaterThanOrEqualTo(0));
            Assert.That(hostAt, Is.GreaterThanOrEqualTo(0));
            Assert.That(triggerAt, Is.LessThan(hostAt), "before=false puts the trigger ahead of the host");
        }

        [Test]
        public void Teardown_removes_the_host_and_the_instance_by_reference()
        {
            using var host = new SusStorybookHost();
            Assert.IsTrue(host.ShowStoryById(Hosted));

            Assert.IsTrue(host.ShowStoryById(Counter), "switch away -> Unmount()");

            Assert.That(host.QaSubjectHost, Is.Null);
            Assert.That(host.QaCanvas.Query<VisualElement>(className: CoreHostScaffold.HostClass)
                .ToList(), Is.Empty, "no stray host anywhere under the canvas");
            var instances = host.QaSubjectRoot.Children().OfType<CoreCounterDemo>().ToList();
            Assert.That(instances, Has.Count.EqualTo(1), "the new story did mount");
        }

        // ── the matrix cell: card T-3906's half, wrapper contract only ────────

        [Test]
        public void Matrix_cells_wrap_the_declared_host_around_the_instance()
        {
            SusStateTwins.Resolver = (_, __) => false;
            var before = SusStoryMatrix.AxisPropName;
            SusStoryMatrix.AxisPropName = "Tone";
            try
            {
                var matrix = new SusStoryMatrix();

                matrix.Show(Entry(HostedAxis));

                var boxes = matrix.Query<VisualElement>(className: "sb-matrix__item").ToList();
                Assert.That(boxes.Count, Is.GreaterThan(0));
                foreach (var box in boxes)
                {
                    Assert.That(box.childCount, Is.EqualTo(1));
                    Assert.That(box.ElementAt(0).ClassListContains(CoreHostScaffold.HostClass), Is.True,
                        "the box (sb-matrix__item) contains the host, the host contains the instance (plan D3)");
                    var slot = box.ElementAt(0).Q<VisualElement>(className: CoreHostScaffold.SlotClass);
                    Assert.That(slot?.Children().OfType<CoreSwatchDemo>().Count(), Is.EqualTo(1));
                }
            }
            finally
            {
                SusStoryMatrix.AxisPropName = before;
            }
        }

        // ── self-teleporting instance in a host: the risk named in plan §6 ────

        EditorWindow _window;
        SusStorybookHost _liveHost;

        [UnityTest]
        public IEnumerator Self_teleporting_instance_in_a_host_does_not_restore_after_switching_away()
        {
            Assume.That(!Application.isBatchMode,
                "needs a real graphics device to init an EditorWindow view (T-1731 pattern)");

            // T-4145: shared factory instead of CreateInstance<EditorWindow>() + Show() straight
            // here — off-screen, focus handed back (SusEditorWindowTestHost's doc).
            _window = SusEditorWindowTestHost.CreateAndShow();
            _liveHost = new SusStorybookHost();
            _window.rootVisualElement.Add(_liveHost);
            var appOverlay = SusBootstrap.GetOrCreateOverlay(_window.rootVisualElement);
            try
            {
                Assert.IsTrue(_liveHost.ShowStoryById(HostedModal));
                yield return null;
                yield return null;

                Assert.IsTrue(_liveHost.ShowStoryById(Counter), "switch away -> Unmount()");
                yield return null;
                yield return null;
                yield return null;

                Assert.That(_liveHost.QaCanvas.Query<VisualElement>(className: CoreHostScaffold.HostClass)
                    .ToList(), Is.Empty, "the host does not survive as a second 'original parent'");
                Assert.That(_window.rootVisualElement.panel.visualTree
                    .Query<VisualElement>(className: CoreMatrixModalDemo.MarkerClass).ToList(), Is.Empty,
                    "the modal did not schedule a restore into the torn-down host");
                Assert.That(appOverlay.childCount, Is.Zero);
            }
            finally
            {
                _liveHost?.Dispose();
                _liveHost = null;
                if (_window != null) _window.Close();
                _window = null;
            }
        }
    }
}
