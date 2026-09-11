using System;
using System.Collections.Generic;

namespace Sharq.Core.Storybook
{
    /// <summary>Where the closed set of a variant axis came from.</summary>
    public enum SusStoryAxisSource
    {
        /// <summary>Nobody enumerated the axis: it stays an open string.</summary>
        None = 0,

        /// <summary>The COMPONENT enumerated it (<c>UseAllowed</c>, read via <c>DescribeAllowed</c>).</summary>
        Component = 1,

        /// <summary>The STORY enumerated it (<see cref="SusStoryAttribute.AxisValues"/>).</summary>
        Story = 2,
    }

    /// <summary>
    /// The CLOSED axis of variants of one story (plan ARCH-20260911-STORYBOOK-SHELL.md §4.9,
    /// decision D26, card T-3379): which prop carries the variants, which values are legal, and
    /// who said so.
    ///
    /// Why this type exists at all. The engine already had the CONSUMER of a closed axis - the
    /// state matrix builds one row per legal value (<c>SusStoryMatrix.ResolveRows</c>) and zone D
    /// turns a closed set into a segmented picker (<c>SusControlFactory.KindOf</c>). It had no
    /// PRODUCER for the half of the corpus where the enumeration is known to the story and not to
    /// the component: of the 17 kit components that declare <c>Prop&lt;string&gt; Variant</c>,
    /// only 8 clamped it with <c>UseAllowed</c>, so on the other 9 the matrix printed
    /// "no axis x state - 1 x 4" and zone D printed a bare text field - the buyer had to GUESS
    /// the spelling of a value that the component's own USS enumerates
    /// (<c>SusAlert.sharq:60</c> against <c>:14-17</c>). One row cannot be compared with another
    /// row, so the showcase judge of D22/D23 had nothing to judge either.
    ///
    /// Those nine have since moved (card T-3395): all 17 clamp Variant now, and the ten stories
    /// that had named the set in the attribute dropped it, so nothing in the kit corpus takes
    /// this axis from a story any more. The story producer stays for the case it was built for -
    /// a component whose enumeration lives only in its template - and for downstream packages
    /// that have not caught up.
    ///
    /// Two producers, in this order of trust:
    /// <list type="number">
    /// <item>the COMPONENT, when it clamped the prop itself - that set is what the runtime
    /// actually enforces, so nothing may override it;</item>
    /// <item>the STORY, when only the story knows the enumeration - the values the component's
    /// template branches on, named once in the attribute instead of guessed by every reader.</item>
    /// </list>
    ///
    /// When BOTH speak and they disagree, the component wins and the disagreement is logged: a
    /// story that lists a value the component will coerce away is a defect of the story, and a
    /// silent override would hide it.
    /// </summary>
    public sealed class SusStoryAxis
    {
        /// <summary>The prop an axis lives on unless a story says otherwise.</summary>
        public const string DefaultPropName = "Variant";

        /// <summary>The answer "this story has no closed axis" - never null.</summary>
        public static readonly SusStoryAxis None =
            new SusStoryAxis(null, Array.Empty<string>(), SusStoryAxisSource.None);

        SusStoryAxis(string propName, IReadOnlyList<string> values, SusStoryAxisSource source)
        {
            PropName = propName;
            Values = values ?? Array.Empty<string>();
            Source = source;
        }

        /// <summary>Prop the variants are written to, or null when there is no axis.</summary>
        public string PropName { get; }

        /// <summary>Legal values in declaration order; empty when there is no axis.</summary>
        public IReadOnlyList<string> Values { get; }

        /// <summary>Who enumerated the values.</summary>
        public SusStoryAxisSource Source { get; }

        /// <summary>True when the axis is a closed set the matrix can build rows from.</summary>
        public bool IsClosed => Source != SusStoryAxisSource.None && Values.Count > 0;

        /// <summary>
        /// Cleans a declared list: trims, drops blanks, drops repeats (ordinal, case-insensitive)
        /// and KEEPS declaration order. Order is the row order of the matrix and the button order
        /// of zone D, so it is part of the declaration and not an implementation detail.
        /// </summary>
        public static IReadOnlyList<string> Normalize(IReadOnlyList<string> declared)
        {
            if (declared == null || declared.Count == 0) return Array.Empty<string>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var clean = new List<string>(declared.Count);
            for (int i = 0; i < declared.Count; i++)
            {
                var value = declared[i];
                if (string.IsNullOrWhiteSpace(value)) continue;
                value = value.Trim();
                if (!seen.Add(value)) continue;
                clean.Add(value);
            }
            return clean.Count == 0 ? Array.Empty<string>() : clean;
        }

        /// <summary>
        /// The axis the COMPONENT enumerated for <paramref name="propName"/>, or
        /// <see cref="None"/>. Source of truth: <c>UseAllowed</c> runs in <c>Created()</c>, so a
        /// probe built and never mounted already carries the set.
        /// </summary>
        public static SusStoryAxis FromComponent(SusComponent probe, string propName)
        {
            if (probe == null) return None;
            var name = Name(propName);

            IReadOnlyList<string> values = null;
            string key = null;
            try
            {
                var allowed = probe.DescribeAllowed();
                if (allowed == null) return None;
                foreach (var pair in allowed)
                {
                    if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
                    key = pair.Key;
                    values = pair.Value?.Values;
                    break;
                }
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] could not read the allowed set of '" + name + "': " + e.Message);
                return None;
            }

            var clean = Normalize(values);
            return clean.Count == 0 ? None : new SusStoryAxis(key, clean, SusStoryAxisSource.Component);
        }

        /// <summary>
        /// The axis the STORY enumerated, or <see cref="None"/>. A story whose declared prop is
        /// not the one being asked about is not an answer to this question.
        /// </summary>
        public static SusStoryAxis FromStory(SusStoryEntry entry, string propName)
        {
            if (entry == null || entry.AxisValues.Count == 0) return None;
            var name = Name(propName);
            if (!string.Equals(entry.AxisProp, name, StringComparison.OrdinalIgnoreCase)) return None;
            return new SusStoryAxis(entry.AxisProp, entry.AxisValues, SusStoryAxisSource.Story);
        }

        /// <summary>
        /// The closed axis of a story: the component's own set when it has one, otherwise the set
        /// the story declared, otherwise <see cref="None"/>.
        /// </summary>
        public static SusStoryAxis Resolve(SusStoryEntry entry, SusComponent probe, string propName = null)
        {
            var name = Name(propName);
            var fromComponent = FromComponent(probe, name);
            var fromStory = FromStory(entry, name);

            if (!fromComponent.IsClosed) return fromStory;
            if (!fromStory.IsClosed) return fromComponent;

            if (!SameValues(fromComponent.Values, fromStory.Values))
                SusLog.Warn(
                    "[storybook] story '" + (entry.Id ?? "?") + "' declares " + name + " as [" +
                    string.Join(", ", fromStory.Values) + "] but the component clamps it to [" +
                    string.Join(", ", fromComponent.Values) + "] - the component wins, because " +
                    "that is the set it actually enforces. Fix the story's AxisValues.");

            return fromComponent;
        }

        static string Name(string propName) =>
            string.IsNullOrWhiteSpace(propName) ? DefaultPropName : propName.Trim();

        static bool SameValues(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public override string ToString() =>
            IsClosed
                ? PropName + " in [" + string.Join(", ", Values) + "] (" + Source + ")"
                : "no axis";
    }
}
