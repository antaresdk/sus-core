using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Probe;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Zone E of card T-3040: the event feed wires itself, prints arguments, marks what was
    /// promised and never raised, forgets everything when the story changes, and carries the
    /// health count of the stage canvas into zone A.
    ///
    /// Health is read through <see cref="SusStoryProbe.HealthSource"/>. The default is the real
    /// detector (<c>SusUiProbe.GetAnomalies</c>), but that detector answers only for an ATTACHED
    /// panel with a computed layout — an EditMode test has neither, and the detector itself is
    /// already judged in PlayMode by <c>SusUiProbeHealthPlaymodeTests</c>. What these tests judge
    /// is the wiring on top of it: what the strip counts, what it lists, and what zone A shows.
    /// </summary>
    public class SusStoryProbeTests
    {
        const string Counter = "core/primitives/counter";
        const string Swatch = "core/primitives/swatch";

        [SetUp]
        public void SetUp()
        {
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
            SusStoryFrame.Reset();
            SusStoryQa.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SusStoryFrame.Reset();
            SusStoryQa.Clear();
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        /// <summary>
        /// Three events of the three shapes the feed has to survive: no argument, a string
        /// argument, and one that is never raised. Deliberately not a story — the feed attaches
        /// to an INSTANCE, and the registry fixtures are asserted elsewhere.
        /// </summary>
        sealed class ProbeEventDemo : SusComponent
        {
            public Action OnOpen;
            public Action<string> OnChange;
            public Action OnClose;

            protected override void Build()
            {
            }
        }

        /// <summary>A canvas whose "broken" child is marked, so a test can play the detector.</summary>
        sealed class BrokenDemo : SusComponent
        {
            public const string BrokenClass = "sus-demo-broken";

            protected override void Build()
            {
                var collapsed = new VisualElement();
                collapsed.AddToClassList(BrokenClass);
                Add(collapsed);
            }
        }

        static SusStoryEntry Entry(string id) => SusStoryRegistry.Find(id);

        // Stands in for SusUiProbe where there is no panel: same answer shape ("<what> : <why>"),
        // driven by a marker the fixture puts on the element that is meant to be broken.
        static IReadOnlyList<string> MarkedAnomalies(VisualElement canvas)
        {
            var found = new List<string>();
            void Walk(VisualElement el)
            {
                if (el == null) return;
                if (el.ClassListContains(BrokenDemo.BrokenClass))
                    found.Add(el.GetType().Name + ": visible but zero-size");
                foreach (var child in el.Children()) Walk(child);
            }
            Walk(canvas);
            return found;
        }

        static SusStoryProbe Attached(SusComponent instance, VisualElement canvas = null, string id = Counter)
        {
            var probe = new SusStoryProbe();
            var stage = canvas ?? new VisualElement();
            stage.Add(instance);
            probe.Attach(Entry(id), instance, stage);
            return probe;
        }

        // ── events wire themselves ───────────────────────────────────────────

        [Test]
        public void Every_declared_event_is_subscribed_without_being_named()
        {
            var demo = new ProbeEventDemo();
            using var probe = Attached(demo);

            Assert.That(probe.Events.Declared,
                Is.EquivalentTo(new[] { "OnOpen", "OnChange", "OnClose" }),
                "the feed reads DescribeEvents, nobody registers events by hand");

            demo.OnOpen?.Invoke();

            Assert.That(probe.Chips, Is.EqualTo(new[] { "OnOpen()" }));
        }

        [Test]
        public void A_chip_carries_the_argument_the_event_was_raised_with()
        {
            var demo = new ProbeEventDemo();
            using var probe = Attached(demo);

            demo.OnChange?.Invoke("Uzbekistan");

            Assert.That(probe.Chips.Last(), Is.EqualTo("OnChange(\"Uzbekistan\")"),
                "'it fired' and 'it fired with THIS' are different answers (mock-up zone E)");
        }

        [Test]
        public void The_strip_keeps_only_the_last_four_calls()
        {
            var demo = new ProbeEventDemo();
            using var probe = Attached(demo);

            for (int i = 0; i < 6; i++) demo.OnChange?.Invoke("v" + i);

            Assert.That(probe.Chips.Count, Is.EqualTo(SusStoryEventLog.ChipCount));
            Assert.That(probe.Chips.Last(), Is.EqualTo("OnChange(\"v5\")"));
            Assert.That(probe.Events.Calls.Count, Is.EqualTo(6),
                "the session report keeps every call, the strip only shows the last few");
        }

        [Test]
        public void An_event_that_never_fired_is_named_in_the_warning_line()
        {
            var demo = new ProbeEventDemo();
            using var probe = Attached(demo);

            demo.OnOpen?.Invoke();

            Assert.That(probe.HasUnfired, Is.True);
            Assert.That(probe.UnfiredText,
                Is.EqualTo(SusStoryProbe.UnfiredPrefix + "OnChange, OnClose"));

            demo.OnChange?.Invoke("x");
            demo.OnClose?.Invoke();

            Assert.That(probe.HasUnfired, Is.False, "a promise kept leaves the warning line");
            Assert.That(probe.UnfiredText, Is.Empty);
        }

        [Test]
        public void Switching_the_story_forgets_the_previous_session()
        {
            var first = new ProbeEventDemo();
            using var probe = Attached(first);
            first.OnOpen?.Invoke();
            Assert.That(probe.Chips, Is.Not.Empty);

            var second = new ProbeEventDemo();
            var stage = new VisualElement();
            stage.Add(second);
            probe.Attach(Entry(Swatch), second, stage);

            Assert.That(probe.Chips, Is.Empty, "a chip of the previous story is a lie about this one");
            Assert.That(probe.Events.Calls, Is.Empty);
            Assert.That(probe.UnfiredText,
                Is.EqualTo(SusStoryProbe.UnfiredPrefix + "OnOpen, OnChange, OnClose"));

            first.OnChange?.Invoke("stale");

            Assert.That(probe.Chips, Is.Empty, "the old instance is unsubscribed, not just ignored");
        }

        // ── health ───────────────────────────────────────────────────────────

        [Test]
        public void The_health_count_comes_from_the_probe_over_the_stage_canvas()
        {
            var demo = new BrokenDemo();
            var stage = new VisualElement();
            stage.Add(demo);

            using var probe = new SusStoryProbe { HealthSource = MarkedAnomalies };
            probe.Attach(Entry(Counter), demo, stage);

            Assert.That(probe.AnomalyCount, Is.EqualTo(1));
            Assert.That(probe.HealthText, Is.EqualTo("health 1"));
            Assert.That(probe.Anomalies.Single(), Does.Contain("visible but zero-size"));

            var rows = probe.Query<VisualElement>(className: "sus-sb-probe__anomaly").ToList();
            Assert.That(rows.Count, Is.EqualTo(1), "every counted anomaly is also listed");
            Assert.That(probe.Query<Label>(className: "sus-sb-probe__anomaly-text").First().text,
                Is.EqualTo(probe.Anomalies[0]));
        }

        [Test]
        public void A_healthy_canvas_reads_zero_and_lists_nothing()
        {
            var demo = new ProbeEventDemo();
            using var probe = new SusStoryProbe { HealthSource = MarkedAnomalies };
            var stage = new VisualElement();
            stage.Add(demo);
            probe.Attach(Entry(Counter), demo, stage);

            Assert.That(probe.AnomalyCount, Is.Zero);
            Assert.That(probe.HealthText, Is.EqualTo("health 0"));
            Assert.That(probe.Query<VisualElement>(className: "sus-sb-probe__anomaly").ToList(), Is.Empty);
        }

        [Test]
        public void The_default_health_source_is_the_shared_detector()
        {
            using var probe = new SusStoryProbe();

            Assert.That(probe.HealthSource, Is.EqualTo((Func<VisualElement, IReadOnlyList<string>>)
                SusStoryProbe.DefaultHealthSource),
                "zone E and sus_ui_health must never disagree about one canvas");
        }

        // ── frame ────────────────────────────────────────────────────────────

        [Test]
        public void Core_ships_no_comparer_so_the_frame_line_says_it_cannot_tell()
        {
            var demo = new ProbeEventDemo();
            using var probe = Attached(demo);

            Assert.That(SusStoryFrame.IsStub, Is.True);
            Assert.That(probe.FrameText, Is.EqualTo(SusStoryFrameResult.UnavailableText));
            Assert.That(probe.Frame.IsAnomaly, Is.False, "'cannot tell' is not an anomaly");
        }

        [Test]
        public void A_registered_comparer_decides_the_frame_line()
        {
            SusStoryFrame.Comparer = new FakeComparer(SusStoryFrameResult.Match());
            var demo = new ProbeEventDemo();
            using var probe = Attached(demo);

            Assert.That(probe.FrameText, Is.EqualTo(SusStoryFrameResult.MatchText));

            SusStoryFrame.Comparer = new FakeComparer(SusStoryFrameResult.Mismatch(4.1f, 0.5f));
            probe.Refresh();

            Assert.That(probe.FrameText, Is.EqualTo("frame ≠ canon · 4.1 %"));
            Assert.That(probe.Frame.IsAnomaly, Is.True);
            Assert.That(probe.Query<Label>(className: "sus-sb-probe__frame")
                    .First().ClassListContains("sus-sb-probe__frame--bad"), Is.True);
        }

        [Test]
        public void A_comparer_that_throws_leaves_the_shell_saying_it_cannot_tell()
        {
            SusStoryFrame.Comparer = new ThrowingComparer();
            var demo = new ProbeEventDemo();
            LogAssert.Expect(LogType.Error, new Regex("frame comparer failed"));

            using var probe = Attached(demo);

            Assert.That(probe.FrameText, Is.EqualTo(SusStoryFrameResult.UnavailableText));
        }

        sealed class FakeComparer : ISusStoryFrameComparer
        {
            readonly SusStoryFrameResult _result;
            public FakeComparer(SusStoryFrameResult result) => _result = result;
            public SusStoryFrameResult Compare(string storyId, VisualElement canvas) => _result;
        }

        sealed class ThrowingComparer : ISusStoryFrameComparer
        {
            public SusStoryFrameResult Compare(string storyId, VisualElement canvas) =>
                throw new InvalidOperationException("no canon store");
        }

        // ── the QA extension point ───────────────────────────────────────────

        [Test]
        public void A_qa_sink_sees_the_mount_the_calls_and_the_finished_session()
        {
            var sink = new RecordingSink();
            SusStoryQa.Register(sink);

            var demo = new ProbeEventDemo();
            using var probe = Attached(demo);
            demo.OnChange?.Invoke("Uzbekistan");
            probe.Clear();

            Assert.That(sink.Mounted, Is.EqualTo(new[] { Counter }));
            Assert.That(sink.Calls, Is.EqualTo(new[] { "OnChange(\"Uzbekistan\")" }));
            Assert.That(sink.Reports.Single().UnfiredEvents,
                Is.EquivalentTo(new[] { "OnOpen", "OnClose" }),
                "the session report is what the judging rule of plan §5 reads");
        }

        sealed class RecordingSink : ISusStoryQaSink
        {
            public readonly List<string> Mounted = new();
            public readonly List<string> Calls = new();
            public readonly List<SusStoryProbeReport> Reports = new();

            public void StoryMounted(SusStoryEntry entry, SusComponent instance, VisualElement canvas) =>
                Mounted.Add(entry?.Id);

            public void EventFired(SusStoryEntry entry, string eventName, string call) => Calls.Add(call);

            public void StoryFinished(SusStoryProbeReport report) => Reports.Add(report);
        }

        // ── through the shell ────────────────────────────────────────────────

        [Test]
        public void The_shell_wires_zone_E_to_the_story_it_mounted()
        {
            using var host = new SusStorybookHost();

            host.ShowStoryById(Counter);
            var story = (CoreCounterDemo)host.QaCanvas.Children().OfType<SusComponent>().First();

            Assert.That(host.ZoneProbe.Children().Contains(host.Probe), Is.True, "zone E is filled");
            Assert.That(host.Probe.Events.Declared, Is.EquivalentTo(new[] { "OnCount" }));
            Assert.That(host.Probe.UnfiredText, Is.EqualTo(SusStoryProbe.UnfiredPrefix + "OnCount"));

            story.OnCount?.Invoke(3);

            Assert.That(host.Probe.Chips, Is.EqualTo(new[] { "OnCount(3)" }));
            Assert.That(host.Probe.HasUnfired, Is.False);
        }

        [Test]
        public void The_shell_mirrors_the_anomaly_count_into_zone_A()
        {
            using var host = new SusStorybookHost();
            host.Probe.HealthSource = _ => new[] { "CoreCounterDemo: visible but zero-size" };

            host.ShowStoryById(Counter);
            host.SyncOverlay();   // the stage tick that re-reads health

            Assert.That(host.Probe.AnomalyCount, Is.EqualTo(1));
            Assert.That(host.Nav.AnomalyCount, Is.EqualTo(1),
                "zone A carries the same number, so a broken story is visible from the list");

            host.Probe.HealthSource = _ => Array.Empty<string>();
            host.SyncOverlay();

            Assert.That(host.Nav.AnomalyCount, Is.Zero);
        }

        [Test]
        public void Leaving_a_story_empties_the_strip()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById(Counter);
            var story = (CoreCounterDemo)host.QaCanvas.Children().OfType<SusComponent>().First();
            story.OnCount?.Invoke(1);
            Assert.That(host.Probe.Chips, Is.Not.Empty);

            host.ShowStoryById(Swatch);

            Assert.That(host.Probe.Chips, Is.Empty);
            Assert.That(host.Probe.Events.Declared, Is.Empty, "the swatch declares no events");
            Assert.That(host.Probe.HasUnfired, Is.False);
        }
    }
}
