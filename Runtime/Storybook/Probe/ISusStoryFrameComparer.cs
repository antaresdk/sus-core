using System;
using System.Globalization;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.Probe
{
    // Zone E of card T-3040 (plan ARCH-20260907-STORYBOOK-ENGINE.md §4.7): the engine declares
    // the frame contract, the screenshot conveyor implements it (card T-3045).

    /// <summary>How the live stage compares with the story's canonical frame.</summary>
    public enum SusStoryFrameVerdict
    {
        /// <summary>Nobody can answer here: no comparer wired, or no canon for this story.</summary>
        Unavailable = 0,

        /// <summary>Live render equals the canon within the threshold.</summary>
        Match = 1,

        /// <summary>Live render differs from the canon beyond the threshold.</summary>
        Mismatch = 2,
    }

    /// <summary>One frame verdict: what it is, and by how much when it is a mismatch.</summary>
    public readonly struct SusStoryFrameResult
    {
        /// <summary>Shown while nothing can compare frames (mock-up: "frame: —").</summary>
        public const string UnavailableText = "frame: —";

        /// <summary>Shown when the live render matches the canon.</summary>
        public const string MatchText = "frame: canon = live";

        SusStoryFrameResult(SusStoryFrameVerdict verdict, float difference, float threshold)
        {
            Verdict = verdict;
            DifferencePercent = difference;
            ThresholdPercent = threshold;
        }

        public SusStoryFrameVerdict Verdict { get; }

        /// <summary>Share of pixels that differ, in percent (mock-up: 4.1 %).</summary>
        public float DifferencePercent { get; }

        /// <summary>Threshold the comparison used, in percent (mock-up: 0.5 %).</summary>
        public float ThresholdPercent { get; }

        /// <summary>A mismatch is an anomaly; "cannot tell" is not.</summary>
        public bool IsAnomaly => Verdict == SusStoryFrameVerdict.Mismatch;

        public static SusStoryFrameResult Unavailable =>
            new SusStoryFrameResult(SusStoryFrameVerdict.Unavailable, 0f, 0f);

        public static SusStoryFrameResult Match(float threshold = 0f) =>
            new SusStoryFrameResult(SusStoryFrameVerdict.Match, 0f, threshold);

        public static SusStoryFrameResult Mismatch(float difference, float threshold = 0f) =>
            new SusStoryFrameResult(SusStoryFrameVerdict.Mismatch, difference, threshold);

        /// <summary>The one line zone E prints for this verdict.</summary>
        public string Describe()
        {
            switch (Verdict)
            {
                case SusStoryFrameVerdict.Match: return MatchText;
                case SusStoryFrameVerdict.Mismatch:
                    return "frame ≠ canon · " +
                           DifferencePercent.ToString("0.#", CultureInfo.InvariantCulture) + " %";
                default: return UnavailableText;
            }
        }

        /// <summary>The anomaly line a mismatch contributes to the list under the strip.</summary>
        public string DescribeAnomaly()
        {
            if (!IsAnomaly) return null;
            string line = "frame ≠ canon: " +
                          DifferencePercent.ToString("0.#", CultureInfo.InvariantCulture) + " %";
            if (ThresholdPercent > 0f)
                line += " against a " + ThresholdPercent.ToString("0.#", CultureInfo.InvariantCulture) +
                        " % threshold";
            return line;
        }
    }

    /// <summary>
    /// Compares the live stage with the story's canonical frame (plan §4.7: "canon = live is
    /// compared in place"). The ENGINE only declares this contract: taking a picture needs
    /// a render target, a canon store and a diff, all of which live in the screenshot conveyor —
    /// card T-3045 implements it there and registers the implementation in
    /// <see cref="SusStoryFrame.Comparer"/>.
    ///
    /// Keeping the implementation out of core is not tidiness: the engine must build into a
    /// player without dragging a screenshot pipeline in, and the shell must stay honest when
    /// nobody registered a comparer — it says "frame: —" instead of inventing a verdict.
    /// </summary>
    public interface ISusStoryFrameComparer
    {
        /// <summary>
        /// Compares <paramref name="canvas"/> (the stage's live canvas) with the canon recorded
        /// for <paramref name="storyId"/>. Return <see cref="SusStoryFrameResult.Unavailable"/>
        /// when there is no canon for that story — that is an answer, not a failure.
        /// </summary>
        SusStoryFrameResult Compare(string storyId, VisualElement canvas);
    }

    /// <summary>The comparer core ships with: it never has a canon, so it never has a verdict.</summary>
    public sealed class SusStoryFrameUnavailable : ISusStoryFrameComparer
    {
        public SusStoryFrameResult Compare(string storyId, VisualElement canvas) =>
            SusStoryFrameResult.Unavailable;
    }

    /// <summary>
    /// Where the shell looks for a frame comparer. One settable slot, defaulted to the stub, so
    /// the conveyor (T-3045) registers itself in one line and a test restores the default in one
    /// line.
    /// </summary>
    public static class SusStoryFrame
    {
        static ISusStoryFrameComparer _comparer = new SusStoryFrameUnavailable();

        /// <summary>Never null: assigning null puts the "no canon here" stub back.</summary>
        public static ISusStoryFrameComparer Comparer
        {
            get => _comparer;
            set => _comparer = value ?? new SusStoryFrameUnavailable();
        }

        /// <summary>True while nothing but the core stub is registered.</summary>
        public static bool IsStub => _comparer is SusStoryFrameUnavailable;

        /// <summary>Puts the core stub back — the teardown of any test that registered one.</summary>
        public static void Reset() => _comparer = new SusStoryFrameUnavailable();

        /// <summary>
        /// Asks the registered comparer, and treats a throwing comparer as "cannot tell": a
        /// broken screenshot pipeline must not blank the shell.
        /// </summary>
        public static SusStoryFrameResult Compare(string storyId, VisualElement canvas)
        {
            if (string.IsNullOrEmpty(storyId) || canvas == null) return SusStoryFrameResult.Unavailable;
            try
            {
                return _comparer.Compare(storyId, canvas);
            }
            catch (Exception e)
            {
                SusLog.Error("[storybook] frame comparer failed for '" + storyId + "': " + e);
                return SusStoryFrameResult.Unavailable;
            }
        }
    }
}
