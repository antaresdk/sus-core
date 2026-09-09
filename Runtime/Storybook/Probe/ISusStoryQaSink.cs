using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.Probe
{
    /// <summary>
    /// What zone E knows about one story after it has been shown: the input of the session
    /// report of plan §4.7 (<c>stories</c>, <c>events fired</c>, <c>health</c>) and of layer 4 of
    /// the judging rule §5 ("event-unwired"). Plain data on purpose — writing it as JSON is a
    /// step of the conveyor, not of the shell.
    /// </summary>
    public sealed class SusStoryProbeReport
    {
        public SusStoryProbeReport(
            string storyId,
            IReadOnlyList<string> declaredEvents,
            IReadOnlyList<string> firedCalls,
            IReadOnlyList<string> unfiredEvents,
            IReadOnlyList<string> anomalies,
            SusStoryFrameResult frame,
            IReadOnlyList<string> props = null,
            IReadOnlyList<string> controls = null,
            IReadOnlyList<string> uncovered = null,
            IReadOnlyList<string> excluded = null,
            IReadOnlyList<string> manualControls = null)
        {
            StoryId = storyId;
            DeclaredEvents = declaredEvents ?? Array.Empty<string>();
            FiredCalls = firedCalls ?? Array.Empty<string>();
            UnfiredEvents = unfiredEvents ?? Array.Empty<string>();
            Anomalies = anomalies ?? Array.Empty<string>();
            Frame = frame;
            Props = props ?? Array.Empty<string>();
            Controls = controls ?? Array.Empty<string>();
            Uncovered = uncovered ?? Array.Empty<string>();
            Excluded = excluded ?? Array.Empty<string>();
            ManualControls = manualControls ?? Array.Empty<string>();
        }

        /// <summary>Story address the report is about.</summary>
        public string StoryId { get; }

        /// <summary>Every <c>On*</c> the instance declared (DescribeEvents).</summary>
        public IReadOnlyList<string> DeclaredEvents { get; }

        /// <summary>Every call recorded this session, oldest first, with arguments.</summary>
        public IReadOnlyList<string> FiredCalls { get; }

        /// <summary>Declared events that never fired — "promise without a deed".</summary>
        public IReadOnlyList<string> UnfiredEvents { get; }

        /// <summary>Health anomalies of the stage canvas at the moment of the report.</summary>
        public IReadOnlyList<string> Anomalies { get; }

        /// <summary>Frame verdict at the moment of the report.</summary>
        public SusStoryFrameResult Frame { get; }

        /// <summary>
        /// Every <c>[CreateProperty]</c> prop the mounted instance declares
        /// (<c>SusComponent.DescribeProps()</c>) — the N of "props N · controls M" (card T-3034)
        /// and the numerator R134 layer 1 (control-gap) reads from the session report
        /// (<c>sus-story-session/v1</c>, plan §4.7). Empty when the panel was never built
        /// (<see cref="SusStoryProbe.Attach"/> got no control panel).
        /// </summary>
        public IReadOnlyList<string> Props { get; }

        /// <summary>
        /// Props for which zone D actually built a control
        /// (<c>SusControlPanel.ControlledProps</c> — the build-time snapshot, not the live
        /// <c>Controls</c> list, which teardown empties before this report is handed out; card
        /// T-3184).
        /// </summary>
        public IReadOnlyList<string> Controls { get; }

        /// <summary>
        /// Props with neither a control nor a declared exclusion — a hole in the story
        /// (<c>SusControlPanel.Uncovered</c>). Should read empty on a healthy story; kept
        /// explicit rather than inferred so the session JSON can say so plainly.
        /// </summary>
        public IReadOnlyList<string> Uncovered { get; }

        /// <summary>
        /// Props the story deliberately left without a control (<c>ctx.Exclusions.Keys</c>) —
        /// plain prop names, the reason lives in the ledger, not in this list. R134 L1 treats
        /// membership here as covered, same as a real control.
        /// </summary>
        public IReadOnlyList<string> Excluded { get; }

        /// <summary>
        /// Props the story drives with a hand-written control (<c>ctx.ManualControls.Keys</c>).
        /// Also counts as covered for R134 L1 — a manual control is still a control.
        /// </summary>
        public IReadOnlyList<string> ManualControls { get; }
    }

    /// <summary>
    /// The extension point QA drivers hang off (plan §6.1 step 6). It exists so that the checks
    /// which live today as hand-kept files next to the kit shell — a layout/stress fixture set
    /// and a per-story "live verify" catalogue — attach to the ENGINE instead of to a copy of
    /// the shell, and so that no driver ever has to keep its own list of stories again: the
    /// engine calls the sink for whatever story it just mounted.
    ///
    /// A sink must not throw. <see cref="SusStoryQa"/> catches and logs anyway, because a QA
    /// hook that can blank the shell is worse than the defect it was looking for.
    /// </summary>
    public interface ISusStoryQaSink
    {
        /// <summary>A story has just been mounted into <paramref name="canvas"/>.</summary>
        void StoryMounted(SusStoryEntry entry, SusComponent instance, VisualElement canvas);

        /// <summary>The live instance raised an event; <paramref name="call"/> carries arguments.</summary>
        void EventFired(SusStoryEntry entry, string eventName, string call);

        /// <summary>The story is going away; here is everything zone E saw of it.</summary>
        void StoryFinished(SusStoryProbeReport report);
    }

    /// <summary>Registry of <see cref="ISusStoryQaSink"/>. Empty in a buyer's build.</summary>
    public static class SusStoryQa
    {
        static readonly List<ISusStoryQaSink> _sinks = new();

        /// <summary>Registered sinks, in registration order.</summary>
        public static IReadOnlyList<ISusStoryQaSink> Sinks => _sinks;

        public static void Register(ISusStoryQaSink sink)
        {
            if (sink == null || _sinks.Contains(sink)) return;
            _sinks.Add(sink);
        }

        public static void Unregister(ISusStoryQaSink sink)
        {
            if (sink == null) return;
            _sinks.Remove(sink);
        }

        /// <summary>Drops every sink — the teardown of a test that registered one.</summary>
        public static void Clear() => _sinks.Clear();

        internal static void NotifyMounted(SusStoryEntry entry, SusComponent instance, VisualElement canvas)
        {
            for (int i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                Safe(() => sink.StoryMounted(entry, instance, canvas));
            }
        }

        internal static void NotifyEvent(SusStoryEntry entry, string eventName, string call)
        {
            for (int i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                Safe(() => sink.EventFired(entry, eventName, call));
            }
        }

        internal static void NotifyFinished(SusStoryProbeReport report)
        {
            if (report == null) return;
            for (int i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                Safe(() => sink.StoryFinished(report));
            }
        }

        static void Safe(Action call)
        {
            try { call(); }
            catch (Exception e) { SusLog.Error("[storybook] QA sink failed: " + e); }
        }
    }
}
