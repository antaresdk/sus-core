using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Sharq.Core.Storybook.Controls
{
    /// <summary>
    /// The control table of ARCH-20260907-STORYBOOK-ENGINE §4.3 as CODE: given one
    /// <see cref="SusPropInfo"/>, it answers which widget drives it and builds that widget.
    ///
    /// This is what replaces the hand-written control lists of the pre-engine storybook (965
    /// calls across the paid packages, card T-3034): a panel is now DERIVED from the component's
    /// public API, so a prop added tomorrow gets its control for free, and a prop nobody wired up
    /// can no longer hide - the panel counts props against controls and says so out loud.
    ///
    /// A type the table does not know is taken by a registered <see cref="ISusControlProvider"/>;
    /// see <see cref="Register"/>.
    /// </summary>
    public static class SusControlFactory
    {
        /// <summary>
        /// Largest closed set still shown as a segmented picker; anything wider becomes a
        /// dropdown (mock-up "Controls Panel", card T-3034).
        /// </summary>
        public const int SegmentLimit = 5;

        /// <summary>
        /// Widest total of the value texts of a closed set still shown as a segmented picker
        /// (characters). The zone D column is narrow: five long values would be cut off, so a set
        /// over the budget is a dropdown however few values it has
        /// (ARCH-20261014-SB-PROP-CONTROLS D6, d:56c403).
        /// </summary>
        public const int SegmentLabelBudget = 32;

        static readonly List<ISusControlProvider> Extra = new();

        /// <summary>Providers registered on top of the built-in table, oldest first.</summary>
        public static IReadOnlyList<ISusControlProvider> Providers => Extra;

        /// <summary>
        /// Adds a provider. The LAST registered provider is asked FIRST, so a package can
        /// override both the built-in table and an earlier provider.
        /// </summary>
        public static void Register(ISusControlProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (!Extra.Contains(provider)) Extra.Add(provider);
        }

        /// <summary>Removes a provider; false when it was not registered.</summary>
        public static bool Unregister(ISusControlProvider provider) => Extra.Remove(provider);

        /// <summary>Drops every registered provider (test isolation).</summary>
        public static void ClearProviders() => Extra.Clear();

        /// <summary>
        /// Registers the providers the ENGINE itself ships on top of the built-in table - today
        /// the glyph picker of icon props (<see cref="SusIconControlProvider"/>, card T-3035).
        /// Idempotent (the providers are singletons), and called by every
        /// <see cref="SusControlPanel"/> it builds, so a panel is never the stub by accident.
        ///
        /// Deliberately NOT a static constructor: <see cref="ClearProviders"/> must be able to
        /// leave the table bare, or the built-in rows could not be tested at all.
        /// </summary>
        public static void RegisterDefaults() => Register(SusIconControlProvider.Default);

        /// <summary>
        /// Which widget the built-in table gives this prop (ARCH-20261014-SB-PROP-CONTROLS §4.1,
        /// rows in the order they are tried). A closed set beats the underlying type (an
        /// <c>int</c> with an allowed set is a picker, not a slider), and read-only beats
        /// everything (a control that cannot write must not look writable). The kind is a
        /// function of the prop's DECLARATION - type, <c>UseAllowed</c>, attributes - so the same
        /// prop is the same widget in every story (card T-4521, U-033).
        /// </summary>
        /// <param name="prop">The prop to dress.</param>
        /// <param name="declaredSet">
        /// A closed set the CALLER knows and the prop does not - today the variant axis a story
        /// enumerated for a component that never clamped the prop (<see cref="SusStoryAxis"/>,
        /// card T-3379). It fills a hole; it never overrides the component's own set, because
        /// that set is the one the runtime enforces.
        /// </param>
        public static SusControlKind KindOf(SusPropInfo prop, IReadOnlyList<string> declaredSet = null)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            if (prop.ReadOnly) return SusControlKind.ReadOnly;

            var t = prop.ValueType;

            var options = OptionsOf(prop, declaredSet);
            if (options.Count > 0)
            {
                // A colour NAME is always a list: Button has six, Checkbox five, and a threshold
                // that turns one of them into a segment would make the same prop look different
                // per component (U-033). Swatches are not an option - see the plan, section 2.3.
                if (IsColorName(prop)) return SusControlKind.Dropdown;
                return options.Count <= SegmentLimit && LabelLength(options) <= SegmentLabelBudget
                    ? SusControlKind.Segment
                    : SusControlKind.Dropdown;
            }

            if (t == typeof(bool)) return SusControlKind.Toggle;
            if (t == typeof(Color) || t == typeof(Color32)) return SusControlKind.Color;
            if (prop.LooksLikeIcon) return SusControlKind.Icon;
            if (t == typeof(string)) return SusControlKind.Text;
            if (SusPropGrouping.IsNumeric(t)) return SusControlKind.Number;
            if (t != null && typeof(IList).IsAssignableFrom(t)) return SusControlKind.List;

            // Models and anything else the panel cannot legitimately edit: shown, not faked.
            return SusControlKind.ReadOnly;
        }

        /// <summary>
        /// Legal values of a closed-set prop in display order, or an empty list when the prop is
        /// not a closed set. Case variants collapse to their FIRST spelling and the empty string
        /// is never an option of its own (see <see cref="SusClosedSet"/>).
        /// </summary>
        public static IReadOnlyList<string> OptionsOf(SusPropInfo prop, IReadOnlyList<string> declaredSet = null) =>
            ClosedSetOf(prop, declaredSet).Values;

        /// <summary>
        /// The closed set of a prop, resolved once: canonical values (case duplicates collapsed to
        /// the first spelling, the empty string removed), where the set came from, and what an
        /// empty prop value stands for. Order of sources: the component's own
        /// <c>UseAllowed</c>, then a C# enum, then the axis the STORY declared (card T-3379) - the
        /// story fills a hole and never overrides the component, because the component's set is
        /// the one the runtime enforces (ARCH-20261014-SB-PROP-CONTROLS D1, d:f09527).
        /// </summary>
        internal static SusClosedSet ClosedSetOf(SusPropInfo prop, IReadOnlyList<string> declaredSet = null)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            if (prop.Allowed?.Values != null && prop.Allowed.Values.Count > 0)
                return SusClosedSet.From(prop.Allowed.Values, fromStory: false, prop.Allowed);
            if (prop.ValueType != null && prop.ValueType.IsEnum)
                return SusClosedSet.From(Enum.GetNames(prop.ValueType), fromStory: false, allowed: null);
            if (declaredSet != null && declaredSet.Count > 0 && prop.ValueType == typeof(string))
                return SusClosedSet.From(declaredSet, fromStory: true, allowed: null);
            return SusClosedSet.Empty;
        }

        /// <summary>
        /// True for a prop that carries a colour NAME: <c>Color</c> or <c>*Color</c>. Such a set
        /// is always a dropdown (<see cref="SegmentLabelBudget"/> never applies).
        /// </summary>
        public static bool IsColorName(SusPropInfo prop) =>
            prop != null &&
            (string.Equals(prop.Name, "Color", StringComparison.Ordinal) ||
             prop.Name.EndsWith("Color", StringComparison.Ordinal));

        static int LabelLength(IReadOnlyList<string> options)
        {
            int total = 0;
            for (int i = 0; i < options.Count; i++) total += options[i].Length;
            return total;
        }

        /// <summary>
        /// Builds the control for one prop. Returns null only when a registered provider claims
        /// the prop and then declines to build it - the panel counts that as uncovered.
        /// </summary>
        public static SusControl Build(
            SusPropInfo prop, SusControlContext context, IReadOnlyList<string> declaredSet = null)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));

            for (int i = Extra.Count - 1; i >= 0; i--)
            {
                var provider = Extra[i];
                bool claims;
                try { claims = provider.CanBuild(prop); }
                catch (Exception e)
                {
                    SusLog.Warn("[storybook] control provider " + provider.GetType().Name +
                                " threw on '" + prop.Name + "': " + e.Message);
                    continue;
                }
                if (!claims) continue;

                try { return provider.Build(prop, context); }
                catch (Exception e)
                {
                    SusLog.Error("[storybook] control provider " + provider.GetType().Name +
                                 " failed to build '" + prop.Name + "': " + e);
                    return null;
                }
            }

            return BuildBuiltIn(prop, context, declaredSet);
        }

        /// <summary>The built-in table alone, with no provider asked.</summary>
        public static SusControl BuildBuiltIn(
            SusPropInfo prop, SusControlContext context, IReadOnlyList<string> declaredSet = null)
        {
            switch (KindOf(prop, declaredSet))
            {
                case SusControlKind.Toggle:
                    return new SusToggleControl(prop, context);
                case SusControlKind.Segment:
                    return new SusSegmentControl(prop, context, ClosedSetOf(prop, declaredSet));
                case SusControlKind.Dropdown:
                    return new SusDropdownControl(prop, context, ClosedSetOf(prop, declaredSet));
                case SusControlKind.Text:
                    return new SusTextControl(prop, context);
                case SusControlKind.Icon:
                    return new SusIconControl(prop, context);
                case SusControlKind.Number:
                    return new SusNumberControl(prop, context);
                case SusControlKind.Color:
                    return new SusColorControl(prop, context);
                case SusControlKind.List:
                    return new SusListControl(prop, context);
                default:
                    return new SusReadOnlyControl(prop, context);
            }
        }

        /// <summary>
        /// Canonical string form of a value: what the deep link carries and what a segmented
        /// control compares against. Invariant culture on purpose - a link written on a machine
        /// with a comma decimal separator must open on one without it.
        /// </summary>
        public static string ToQueryValue(object value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case string s:
                    return s;
                case bool b:
                    return b ? "true" : "false";
                case Color c:
                    return "#" + ColorUtility.ToHtmlStringRGBA(c);
                case Color32 c32:
                    return "#" + ColorUtility.ToHtmlStringRGBA(c32);
                case Enum e:
                    return e.ToString();
                case IFormattable f:
                    return f.ToString(null, CultureInfo.InvariantCulture);
                case IList list:
                    return Join(list);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        static string Join(IList list)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(ToQueryValue(list[i]));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// A closed set as the panel shows it (ARCH-20261014-SB-PROP-CONTROLS section 4.3): canonical
    /// values without case duplicates, plus the facts the dev captions need.
    /// </summary>
    internal sealed class SusClosedSet
    {
        public static readonly SusClosedSet Empty = new(Array.Empty<string>(), false, false, null);

        SusClosedSet(IReadOnlyList<string> values, bool fromStory, bool hadDuplicates, string emptyAs)
        {
            Values = values;
            FromStory = fromStory;
            HadDuplicates = hadDuplicates;
            EmptyAs = emptyAs;
        }

        /// <summary>Canonical values in declaration order.</summary>
        public IReadOnlyList<string> Values { get; }

        /// <summary>The set was declared by a story, not by the component (caption "set from story").</summary>
        public bool FromStory { get; }

        /// <summary>The source listed the same value twice, ignoring case (caption "duplicate values").</summary>
        public bool HadDuplicates { get; }

        /// <summary>
        /// The option the empty string is shown as, or null when the component does not allow it.
        /// A legal empty value is not an option of its own: it reads as the fallback the component
        /// treats it as, so the picker never grows a blank button.
        /// </summary>
        public string EmptyAs { get; }

        /// <summary>The option that is active for a prop currently holding <paramref name="current"/>.</summary>
        public string Display(string current) =>
            string.IsNullOrEmpty(current) && EmptyAs != null ? EmptyAs : current;

        /// <summary>True when <paramref name="text"/> is one of the options (case-insensitive) or the legal empty value.</summary>
        public bool Accepts(string text)
        {
            if (string.IsNullOrEmpty(text)) return EmptyAs != null;
            for (int i = 0; i < Values.Count; i++)
                if (string.Equals(Values[i], text, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static SusClosedSet From(IReadOnlyList<string> source, bool fromStory, SusAllowedInfo allowed)
        {
            var values = new List<string>(source.Count);
            bool hadDuplicates = false;
            bool hadEmpty = false;
            for (int i = 0; i < source.Count; i++)
            {
                var v = source[i];
                if (string.IsNullOrEmpty(v))
                {
                    hadEmpty = true;
                    continue;
                }
                bool seen = false;
                for (int j = 0; j < values.Count && !seen; j++)
                    seen = string.Equals(values[j], v, StringComparison.OrdinalIgnoreCase);
                if (seen) hadDuplicates = true;
                else values.Add(v);
            }

            string emptyAs = null;
            if (values.Count > 0 && allowed != null && (allowed.AllowEmpty || hadEmpty))
                emptyAs = string.IsNullOrEmpty(allowed.Fallback) ? values[0] : allowed.Fallback;
            return new SusClosedSet(values, fromStory, hadDuplicates, emptyAs);
        }
    }
}
