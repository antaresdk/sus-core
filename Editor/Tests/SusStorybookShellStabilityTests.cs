using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Probe;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// How OFTEN the shell writes, and whether writing moves anything — cards T-3358 and T-3362,
    /// plan ARCH-20260911-STORYBOOK-SHELL.md §4.6, §4.8 and decisions D15, D16, D17, D18.
    ///
    /// The measurement these replace, taken on 2026-09-11 from
    /// <c>planning/reports/2026-09/2026-09-11-ux-reviewer-1.md</c>:
    /// <list type="bullet">
    /// <item>zone E was refreshed on the 120 ms overlay tick, unconditionally — <b>8,3 times a
    /// second</b>: a walk of the whole canvas subtree, three text rewrites and a rebuilt anomaly
    /// list, per tick, whether anything had changed or not;</item>
    /// <item>the live-size line was subscribed to the geometry of the element it measures, and it
    /// wrote its measurement into a label that participates in the same layout — a loop with no
    /// stopping condition;</item>
    /// <item>opening any popup on the stage grew the canvas from 120 to 350 px, so the canvas and
    /// everything under it jumped <b>230 px</b> on every open and again on every close.</item>
    /// </list>
    ///
    /// Every assertion below is about a COUNTER rather than a picture, and on purpose: a
    /// screenshot cannot tell an identical rewrite from no rewrite, while the layout engine can.
    /// </summary>
    public class SusStorybookShellStabilityTests
    {
        const string Counter = "core/primitives/counter";
        const string Floating = "core/overlay/floating";

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

        static SusStoryEntry Entry(string id) => SusStoryRegistry.Find(id);

        /// <summary>A health source that remembers how often the strip asked it.</summary>
        sealed class CountingHealth
        {
            public int Calls;
            public IReadOnlyList<string> Read(VisualElement canvas)
            {
                Calls++;
                return Array.Empty<string>();
            }
        }

        sealed class EventDemo : SusComponent
        {
            public Action OnOpen;
            public Action OnClose;

            protected override void Build()
            {
            }
        }

        // ── T-3358: the declared interval ────────────────────────────────────

        [Test]
        public void Zone_E_declares_a_five_hundred_millisecond_floor_not_the_overlay_tick()
        {
            Assert.That(SusStoryProbe.RefreshIntervalMs, Is.EqualTo(500),
                "D17: at most 2 refreshes a second, where the 120 ms tick gave 8,3");
            Assert.That(SusStorybookHost.OverlayWatchMs, Is.EqualTo(120),
                "the overlay poll keeps its own fast tick — it has no event to replace it");
            Assert.That(SusStoryProbe.RefreshIntervalMs,
                Is.GreaterThan(SusStorybookHost.OverlayWatchMs * 4),
                "the two ticks are separate numbers, so the probe cannot inherit the fast one");
        }

        [Test]
        public void The_overlay_poll_no_longer_drags_zone_E_along()
        {
            var health = new CountingHealth();
            using var host = new SusStorybookHost();
            host.Probe.HealthSource = health.Read;

            host.ShowStoryById(Counter);
            host.RefreshProbe();
            int afterMount = health.Calls;

            for (int i = 0; i < 10; i++) host.SyncOverlay();

            Assert.That(health.Calls, Is.EqualTo(afterMount),
                "ten overlay polls used to be ten canvas walks and thirty text rewrites");
        }

        [Test]
        public void The_throttled_tick_does_nothing_while_nothing_asked_for_it()
        {
            var health = new CountingHealth();
            using var host = new SusStorybookHost();
            host.Probe.HealthSource = health.Read;

            host.ShowStoryById(Counter);
            host.RefreshProbe();
            int calls = health.Calls;

            Assert.That(host.Probe.Dirty, Is.False, "the refresh consumed the flag");
            Assert.That(host.RefreshProbe(), Is.False, "an idle tick is a boolean read");
            Assert.That(host.RefreshProbe(), Is.False);
            Assert.That(health.Calls, Is.EqualTo(calls));

            host.Probe.MarkDirty();

            Assert.That(host.RefreshProbe(), Is.True, "a named occasion is honoured on the next tick");
            Assert.That(health.Calls, Is.EqualTo(calls + 1));
        }

        [Test]
        public void A_refresh_that_found_nothing_new_writes_nothing()
        {
            var demo = new EventDemo();
            var stage = new VisualElement();
            stage.Add(demo);

            using var probe = new SusStoryProbe { HealthSource = _ => Array.Empty<string>() };
            probe.Attach(Entry(Counter), demo, stage);

            int writes = probe.Writes;
            for (int i = 0; i < 20; i++) probe.Refresh();

            Assert.That(probe.Writes, Is.EqualTo(writes),
                "D17 acceptance: writes without a change == 0");
        }

        [Test]
        public void A_real_change_still_reaches_the_strip()
        {
            var demo = new EventDemo();
            var stage = new VisualElement();
            stage.Add(demo);
            var anomalies = Array.Empty<string>();

            using var probe = new SusStoryProbe { HealthSource = _ => anomalies };
            probe.Attach(Entry(Counter), demo, stage);

            int writes = probe.Writes;
            anomalies = new[] { "EventDemo: visible but zero-size" };
            probe.Refresh();

            Assert.That(probe.Writes, Is.GreaterThan(writes), "the dirty check is not a mute button");
            Assert.That(probe.HealthText, Is.EqualTo("1 anomaly"));
            Assert.That(probe.AnomalyCount, Is.EqualTo(1));
        }

        // ── T-3358: what the strip says, and what it stopped saying ──────────

        [Test]
        public void Repeated_events_collapse_instead_of_widening_the_row()
        {
            var demo = new EventDemo();
            var stage = new VisualElement();
            stage.Add(demo);

            using var probe = new SusStoryProbe { HealthSource = _ => Array.Empty<string>() };
            probe.Attach(Entry(Counter), demo, stage);

            demo.OnOpen?.Invoke();
            demo.OnClose?.Invoke();
            demo.OnOpen?.Invoke();
            demo.OnClose?.Invoke();

            Assert.That(probe.Chips, Is.EqualTo(new[] { "OnOpen() x2", "OnClose() x2" }),
                "the kadr of T-3358 showed OnOpen() OnClose() OnOpen() OnClose() — four chips " +
                "saying two things, and four chips are wider than two");
            Assert.That(probe.Events.Calls.Count, Is.EqualTo(4),
                "the session report still keeps every call: the strip collapses, the ledger does not");
        }

        [Test]
        public void The_frame_field_is_absent_while_nothing_can_answer_and_returns_when_one_can()
        {
            var demo = new EventDemo();
            var stage = new VisualElement();
            stage.Add(demo);

            using var probe = new SusStoryProbe { HealthSource = _ => Array.Empty<string>() };
            probe.Attach(Entry(Counter), demo, stage);

            Assert.That(SusStoryFrame.IsStub, Is.True);
            Assert.That(probe.FrameText, Is.Empty,
                "a dash that never becomes anything else is not knowledge, it is width");

            SusStoryFrame.Comparer = new MatchComparer();
            probe.Refresh();

            Assert.That(probe.FrameText, Is.EqualTo(SusStoryFrameResult.MatchText),
                "the field is hidden because nobody can answer, not because it was deleted");
        }

        sealed class MatchComparer : ISusStoryFrameComparer
        {
            public SusStoryFrameResult Compare(string storyId, VisualElement canvas) =>
                SusStoryFrameResult.Match();
        }

        // ── T-3362: the canvas keeps one height ──────────────────────────────

        [Test]
        public void Opening_a_popup_does_not_resize_the_canvas()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById(Floating);

            host.CanvasOverlay.AddToOverlay(new Label("popup"), OverlayCategory.Dropdown);
            host.SyncOverlay();

            Assert.That(host.QaCanvas.ClassListContains("sus-sb-stage__canvas--overlay"), Is.False,
                "the 230px jump of the verdict came from this class; D18 keeps one declared height");
            Assert.That(host.Sizes.OverlayNoteVisible, Is.True,
                "the popup is still accounted for — in words, not in geometry");
        }

        // ── T-3362: the size line is no longer its own layout trigger ────────

        [Test]
        public void The_size_line_does_not_measure_itself_on_every_layout_pass()
        {
            var tracked = new VisualElement();
            var sizes = new SusStorySizes();

            sizes.Track(tracked);
            int writes = sizes.Writes;

            for (int i = 0; i < 20; i++) sizes.Refresh();

            Assert.That(sizes.Writes, Is.EqualTo(writes),
                "D16: the text it writes is what used to trigger the pass that made it write");
            Assert.That(sizes.Stale, Is.False);
            Assert.That(sizes.RefreshIfStale(), Is.False,
                "the line waits to be asked; it no longer asks itself");
        }

        /// <summary>
        /// The loop of D16 must stay open under ANY text metric, not only the one the package
        /// shipped with. The owner put the mock-up's own faces on disk on 2026-09-11
        /// (<c>Runtime/Resources/SusRuntime/Fonts/Instrument_Sans</c> and <c>Geist_Mono</c>) and
        /// the parallel wave is wiring them up; a different face measures the same string to a
        /// different width, which is exactly what the old subscription turned into another layout
        /// pass. So the assertion is written about the MECHANISM and not about a number: a write
        /// that really changed the text still leaves the line clean. Nothing here mentions a font,
        /// and that is the point — no font can reopen the loop.
        /// </summary>
        [Test]
        public void A_write_that_really_changed_the_text_does_not_ask_for_another_read()
        {
            var tracked = new VisualElement();
            var sizes = new SusStorySizes();
            sizes.Track(tracked);

            int writes = sizes.Writes;
            var before = sizes.SizesText;

            // Stands in for "the font arrived and the metrics moved": whatever produced the new
            // numbers, the line is asked once and writes once.
            tracked.style.height = 44;
            tracked.style.fontSize = 17;
            tracked.style.paddingLeft = 13;
            sizes.Refresh();

            Assert.That(sizes.SizesText, Is.Not.EqualTo(before), "the metrics really did move");
            Assert.That(sizes.Writes, Is.EqualTo(writes + 1), "one changed text, one write");
            Assert.That(sizes.Stale, Is.False,
                "D16: writing the measurement must not schedule the next measurement");
            Assert.That(sizes.RefreshIfStale(), Is.False,
                "a metric change cannot restart the loop, whatever face produced it");
        }

        [Test]
        public void A_canvas_layout_pass_is_what_asks_the_size_line_to_catch_up()
        {
            var sizes = new SusStorySizes();
            sizes.Track(new VisualElement());

            Assert.That(sizes.RefreshIfStale(), Is.False);

            sizes.MarkStale();

            Assert.That(sizes.Stale, Is.True);
            Assert.That(sizes.RefreshIfStale(), Is.True);
            Assert.That(sizes.Stale, Is.False);
        }

        [Test]
        public void Two_layout_passes_over_an_unchanged_story_change_nothing()
        {
            var health = new CountingHealth();
            using var host = new SusStorybookHost();
            host.Probe.HealthSource = health.Read;

            host.ShowStoryById(Counter);
            host.RefreshProbe();

            var probeWrites = host.Probe.Writes;
            var sizeWrites = host.Sizes.Writes;
            var meta = host.Matrix.MetaText;

            host.Probe.MarkDirty();
            host.Sizes.MarkStale();
            host.RefreshProbe();
            host.Probe.MarkDirty();
            host.Sizes.MarkStale();
            host.RefreshProbe();

            Assert.That(host.Probe.Writes, Is.EqualTo(probeWrites),
                "the judge of D15: a live text that did not change does not move a zone");
            Assert.That(host.Sizes.Writes, Is.EqualTo(sizeWrites));
            Assert.That(host.Matrix.MetaText, Is.EqualTo(meta));
        }
    }
}
