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
            SusStoryFrameResult frame)
        {
            StoryId = storyId;
            DeclaredEvents = declaredEvents ?? Array.Empty<string>();
            FiredCalls = firedCalls ?? Array.Empty<string>();
            UnfiredEvents = unfiredEvents ?? Array.Empty<string>();
            Anomalies = anomalies ?? Array.Empty<string>();
            Frame = frame;
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
