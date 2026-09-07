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
        /// Which widget the built-in table gives this prop. Order matters: a closed set beats the
        /// underlying type (an <c>int</c> with an allowed set is a picker, not a slider), and
        /// read-only beats everything (a control that cannot write must not look writable).
        /// </summary>
        public static SusControlKind KindOf(SusPropInfo prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            if (prop.ReadOnly) return SusControlKind.ReadOnly;

            var t = prop.ValueType;

            if (prop.Allowed != null && prop.Allowed.Values != null && prop.Allowed.Values.Count > 0)
                return prop.Allowed.Values.Count <= SegmentLimit ? SusControlKind.Segment : SusControlKind.Dropdown;

            if (t != null && t.IsEnum)
                return Enum.GetNames(t).Length <= SegmentLimit ? SusControlKind.Segment : SusControlKind.Dropdown;

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
        /// not a closed set.
        /// </summary>
        public static IReadOnlyList<string> OptionsOf(SusPropInfo prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            if (prop.Allowed?.Values != null && prop.Allowed.Values.Count > 0)
                return prop.Allowed.Values;
            if (prop.ValueType != null && prop.ValueType.IsEnum)
                return Enum.GetNames(prop.ValueType);
            return Array.Empty<string>();
        }

        /// <summary>
        /// Builds the control for one prop. Returns null only when a registered provider claims
        /// the prop and then declines to build it - the panel counts that as uncovered.
        /// </summary>
        public static SusControl Build(SusPropInfo prop, SusControlContext context)
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

            return BuildBuiltIn(prop, context);
        }

        /// <summary>The built-in table alone, with no provider asked.</summary>
        public static SusControl BuildBuiltIn(SusPropInfo prop, SusControlContext context)
        {
            switch (KindOf(prop))
            {
                case SusControlKind.Toggle:
                    return new SusToggleControl(prop, context);
                case SusControlKind.Segment:
                    return new SusSegmentControl(prop, context, OptionsOf(prop));
                case SusControlKind.Dropdown:
                    return new SusDropdownControl(prop, context, OptionsOf(prop));
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
}
