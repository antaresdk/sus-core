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
            SelectorLayout = "unknown";
            _warned = false;
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


        // -- the reflective part ---------------------------------------------
        // UI Toolkit exposes NO public way to enumerate the selectors of a StyleSheet: every
        // handle on the way -- StyleComplexSelector, StyleSelector, StyleSelectorPart -- is
        // internal, and the public surface of StyleSheet is limited to its asset identity. So the
        // access is reflective, and the whole of it is isolated in this one region: the next time
        // the editor moves the storage, one place changes.
        //
        // The storage HAS moved, and both layouts are read (card T-3448):
        //   * Unity 6.2 and earlier -- one flat array field `m_ComplexSelectors`;
        //   * Unity 6.3             -- three hash tables `m_Tables`
        //                              (Dictionary of string to StyleComplexSelector, keyed by
        //                              the type / id / class of the RIGHTMOST simple selector)
        //                              plus the two chain heads `firstRootSelector` and
        //                              `firstWildCardSelector`; selectors sharing a bucket are
        //                              chained through `nextInTable`.
        // Verified live on 6000.3.17f1 (project sus-dev): the m_Tables walk of SusButton.g.uss
        // yields 186 complex selectors and 38 class names -- exactly the set of class tokens in
        // the file -- while `m_ComplexSelectors` does not exist on that version at all. That
        // missing field is what made the scan answer "no twin" for EVERY class, which in turn hid
        // the hover/active columns of the matrix no matter what the skin declared.
        //
        // Reflection goes over SERIALIZED fields rather than internal properties because a
        // serialized field name is part of the asset format and therefore the most stable handle
        // Unity offers here. Any failure degrades to "no twin", says so once, and leaves the
        // reason readable in SelectorLayout instead of only in a log nobody reads.

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// Which storage layout the last scan actually read: <c>m_ComplexSelectors</c> (Unity 6.2
        /// and earlier), <c>m_Tables</c> (Unity 6.3), <c>none</c> when neither could be reached.
        /// That last value is the BLIND scan, and it means every answer of <see cref="Has"/> is a
        /// default <c>false</c> rather than a reading of the skin; <c>unknown</c> until the first
        /// scan. A test asserts this is never <c>none</c>, because a blind scan is invisible in
        /// the matrix -- it looks exactly like a skin that declares no twins.
        /// </summary>
        public static string SelectorLayout { get; private set; } = "unknown";

        static bool _warned;

        static HashSet<string> CollectSelectorParts(StyleSheet sheet)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            var layout = "none";
            try
            {
                foreach (var complex in ComplexSelectors(sheet, ref layout))
                foreach (var selector in Enumerate(complex, "m_Selectors", "selectors"))
                foreach (var part in Enumerate(selector, "m_Parts", "parts"))
                {
                    if (!IsClassPart(part)) continue;
                    var value = Member(part, "m_Value", "value") as string;
                    if (!string.IsNullOrEmpty(value)) found.Add(value);
                }
            }
            catch (Exception e)
            {
                layout = "none";
                Warn("[storybook] cannot read USS selectors of '" + sheet.name + "' (" +
                     e.Message + "); hover/active columns stay hidden. " +
                     "Set SusStateTwins.Resolver to answer another way.");
            }
            SelectorLayout = layout;
            if (layout == "none")
            {
                Warn("[storybook] no known USS selector storage on the StyleSheet of this editor " +
                     "(neither m_ComplexSelectors nor m_Tables); hover/active columns stay " +
                     "hidden. Card T-3448 names the two layouts that are read.");
            }
            return found;
        }

        static void Warn(string message)
        {
            if (_warned) return;
            _warned = true;
            SusLog.Warn(message);
        }

        /// <summary>
        /// Every complex selector of the sheet, whichever layout holds them; <paramref name="layout"/>
        /// comes back naming the one that answered.
        /// </summary>
        static List<object> ComplexSelectors(StyleSheet sheet, ref string layout)
        {
            var selectors = new List<object>();

            // Layout of Unity 6.2 and earlier: one array with everything in it.
            if (Member(sheet, "m_ComplexSelectors", "complexSelectors") is System.Collections.IEnumerable flat)
            {
                layout = "m_ComplexSelectors";
                foreach (var item in flat)
                {
                    if (item != null) selectors.Add(item);
                }
                return selectors;
            }

            // Layout of Unity 6.3: bucket tables plus the two chain heads. `nextInTable` is a
            // forward chain, but it is walked with a visited set anyway -- a selector reachable
            // from two buckets must not be walked twice, and a malformed chain must not hang the
            // editor.
            var seen = new HashSet<object>();
            var heads = new List<object>();
            if (Member(sheet, "m_Tables", "tables") is System.Collections.IEnumerable tables)
            {
                // The layout is named by the PRESENCE of the storage, not by the number of
                // selectors read out of it: an empty sheet is not a blind scan, and calling it
                // one would raise a false alarm on every sheet that happens to hold no rules.
                layout = "m_Tables";
                foreach (var table in tables)
                {
                    if (table is not System.Collections.IDictionary bucket) continue;
                    foreach (var head in bucket.Values)
                    {
                        if (head != null) heads.Add(head);
                    }
                }
            }
            var root = Member(sheet, "firstRootSelector");
            if (root != null) heads.Add(root);
            var wildcard = Member(sheet, "firstWildCardSelector");
            if (wildcard != null) heads.Add(wildcard);

            foreach (var head in heads)
            {
                for (var cursor = head; cursor != null; cursor = Member(cursor, "nextInTable"))
                {
                    if (!seen.Add(cursor)) break;
                    selectors.Add(cursor);
                }
            }
            return selectors;
        }

        /// <summary>
        /// True when the part names a CLASS. The part type is an internal enum, so it is read by
        /// NAME rather than by ordinal -- a renumbering must not turn ids into classes. When the
        /// type cannot be read at all the part is accepted, which is the older, laxer reading.
        /// </summary>
        static bool IsClassPart(object part)
        {
            var type = Member(part, "m_Type", "type");
            return type == null || string.Equals(type.ToString(), "Class", StringComparison.Ordinal);
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
