using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// The twin-class contract behind the <c>hover</c> and <c>active</c> columns of the state
    /// matrix (plan ARCH-20260907-STORYBOOK-ENGINE.md §2.6, §4.5 p. 3, card T-3038).
    ///
    /// UI Toolkit has NO API for forcing a pseudo-class — there is no equivalent of the browser
    /// devtools "force :hover". A matrix column labelled <c>hover</c> that simply renders the rest
    /// state would therefore LIE, and a lying column is worse than a missing one. The way out is a
    /// USS contract: every rule <c>X:hover { … }</c> is rewritten to
    /// <c>X:hover, X.sus-state-hover { … }</c>, and the matrix turns the state on by adding the
    /// twin class. The codemod that rewrites the 526 selectors of the corpus is a separate card
    /// (T-3039); THIS file is only the mechanism plus the honest answer for components the codemod
    /// has not reached yet.
    ///
    /// The answer is derived from the stylesheets actually applied to the element — a component
    /// whose skin declares no twin gets no column. When the answer cannot be derived at all the
    /// result is <c>false</c>: "no twin" is the safe reading, because it hides a column rather
    /// than showing a wrong one.
    /// </summary>
    public static class SusStateTwins
    {
        /// <summary>Twin of <c>:hover</c>.</summary>
        public const string HoverClass = "sus-state-hover";

        /// <summary>Twin of <c>:active</c>.</summary>
        public const string ActiveClass = "sus-state-active";

        /// <summary>
        /// Override of the lookup — the seam that lets a test, a screenshot rig or a future
        /// codemod manifest answer instead of the reflective scan. Null means
        /// <see cref="ScanStyleSheets"/>.
        /// </summary>
        public static Func<VisualElement, string, bool> Resolver { get; set; }

        // Per-StyleSheet cache of every class name mentioned by its selectors. Scanning a sheet
        // costs reflection over its whole selector table, so it is done once per asset, not once
        // per matrix cell.
        static readonly Dictionary<StyleSheet, HashSet<string>> Scanned = new();

        /// <summary>
        /// True when the skin of <paramref name="element"/> declares the given twin class, i.e.
        /// when adding that class actually switches the component into the state.
        /// </summary>
        public static bool Has(VisualElement element, string twinClass)
        {
            if (element == null || string.IsNullOrEmpty(twinClass)) return false;
            var resolver = Resolver;
            if (resolver != null)
            {
                try { return resolver(element, twinClass); }
                catch (Exception e)
                {
                    SusLog.Warn("[storybook] state-twin resolver threw: " + e.Message);
                    return false;
                }
            }
            return ScanStyleSheets(element, twinClass);
        }

        /// <summary>Drops the override and the scan cache — the reset a test needs.</summary>
        public static void Reset()
        {
            Resolver = null;
            Scanned.Clear();
        }

        /// <summary>
        /// Default lookup: walks the element and its ancestors, and asks every stylesheet
        /// attached along the way whether it mentions the class.
        /// </summary>
        public static bool ScanStyleSheets(VisualElement element, string twinClass)
        {
            for (var e = element; e != null; e = e.hierarchy.parent)
            {
                var sheets = e.styleSheets;
                for (int i = 0; i < sheets.count; i++)
                {
                    if (Mentions(sheets[i], twinClass)) return true;
                }
            }
            return false;
        }

        /// <summary>True when the sheet has at least one selector part equal to the class.</summary>
        public static bool Mentions(StyleSheet sheet, string className)
        {
            if (sheet == null || string.IsNullOrEmpty(className)) return false;
            if (!Scanned.TryGetValue(sheet, out var names))
            {
                names = CollectSelectorParts(sheet);
                Scanned[sheet] = names;
            }
            return names.Contains(className);
        }

        // ── the reflective part ─────────────────────────────────────────────
        // StyleSheet keeps its selector table in serialized private fields (m_ComplexSelectors →
        // m_Selectors → m_Parts → m_Value); UI Toolkit exposes none of it publicly. Reflection
        // over SERIALIZED fields is used rather than over the internal properties because a
        // serialized field name is part of the asset format and therefore the most stable handle
        // Unity offers here. Any failure degrades to "no twin" and says so once.

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static bool _warned;

        static HashSet<string> CollectSelectorParts(StyleSheet sheet)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var complex in Enumerate(sheet, "m_ComplexSelectors", "complexSelectors"))
                foreach (var selector in Enumerate(complex, "m_Selectors", "selectors"))
                foreach (var part in Enumerate(selector, "m_Parts", "parts"))
                {
                    var value = Member(part, "m_Value", "value") as string;
                    if (!string.IsNullOrEmpty(value)) found.Add(value);
                }
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    SusLog.Warn("[storybook] cannot read USS selectors of '" + sheet.name +
                                "' (" + e.Message + "); hover/active columns stay hidden. " +
                                "Set SusStateTwins.Resolver to answer another way.");
                }
            }
            return found;
        }

        static IEnumerable<object> Enumerate(object owner, params string[] names)
        {
            if (Member(owner, names) is not System.Collections.IEnumerable list) yield break;
            foreach (var item in list)
            {
                if (item != null) yield return item;
            }
        }

        static object Member(object owner, params string[] names)
        {
            if (owner == null) return null;
            var type = owner.GetType();
            foreach (var name in names)
            {
                for (var t = type; t != null; t = t.BaseType)
                {
                    var field = t.GetField(name, Any);
                    if (field != null) return field.GetValue(owner);
                    var prop = t.GetProperty(name, Any);
                    if (prop != null && prop.CanRead) return prop.GetValue(owner);
                }
            }
            return null;
        }
    }
}
