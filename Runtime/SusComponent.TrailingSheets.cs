using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Trailing style sheets: a scope-wide list of <see cref="StyleSheet"/>s that every styled
    /// component under a scope element carries AFTER its own companion sheets.
    ///
    /// <para><b>Why.</b> UI Toolkit resolves equal-specificity conflicts in favor of the sheet
    /// attached LOWER in the hierarchy, then by order on the same element. A component's own
    /// generated sheet sits on the component element itself, so an override sheet attached to an
    /// ancestor (e.g. the app root) loses every tie against it. A sheet appended to the
    /// component's own <c>styleSheets</c> after the generated one wins that tie. This layer
    /// delivers such sheets without the caller walking the tree.</para>
    ///
    /// <para><b>How.</b> <see cref="SetTrailingSheets"/> registers the sheets for a scope element
    /// and updates the live recipients; a component attached later picks them up itself on
    /// <see cref="AttachToPanelEvent"/> by walking its ancestors once (O(depth)).
    /// <see cref="ClearTrailingSheets"/> removes them from the registered recipients only — no
    /// tree query, no polling. Only components that carry at least one style sheet of their own
    /// become recipients (code-only components have nothing to override on themselves).
    /// Recompiling or hot-reloading a component's companion sheets keeps the trailing sheets last.</para>
    ///
    /// <para><b>Rules.</b> The nearest registered scope among a component's ancestors (the component
    /// itself included) wins; nested scopes do not accumulate. Sheets applied here are appended to
    /// every nesting level, so the layer is meant for small override sheets — design tokens and
    /// broad rules belong on the scope root as usual. Elements portaled into an overlay through
    /// <see cref="OverlayHost"/> or <see cref="SusThemeService.CopyStyleSheets"/> receive the
    /// source component's trailing sheets with the copied companion sheets and lose them on clear.
    /// A component mounted directly into an overlay layer that is NOT a descendant of the scope
    /// gets no trailing sheets — register the scope on the application root to cover overlays.
    /// A component detached from its panel stops being tracked; it re-evaluates its trailing
    /// sheets on the next attach.</para>
    ///
    /// <para>This is style sheet delivery only: no style value is set from code. Main thread only.</para>
    /// </summary>
    public abstract partial class SusComponent
    {
        private sealed class TrailingScope
        {
            public StyleSheet[] Sheets;
        }

        // Scope element -> its trailing sheets. Weak keys: a dropped scope element does not
        // keep its sheets alive.
        private static readonly ConditionalWeakTable<VisualElement, TrailingScope> s_trailingScopes =
            new ConditionalWeakTable<VisualElement, TrailingScope>();

        // Number of registered scopes. Zero (the default for every app that never calls
        // SetTrailingSheets) makes the attach path skip the ancestor walk entirely.
        private static int s_trailingScopeCount;

        // Attached components that carry style sheets of their own — the recipient registry
        // Set/Clear walk instead of querying the visual tree.
        private static readonly HashSet<SusComponent> s_trailingRecipients = new HashSet<SusComponent>();
        private static readonly List<SusComponent> s_trailingWork = new List<SusComponent>();

        private VisualElement _trailingScope;
        private StyleSheet[] _trailingApplied;
        private List<VisualElement> _trailingCopies;

        /// <summary>
        /// Trailing style sheets currently appended to this component (empty when none).
        /// </summary>
        public IReadOnlyList<StyleSheet> TrailingSheets =>
            (IReadOnlyList<StyleSheet>)_trailingApplied ?? Array.Empty<StyleSheet>();

        /// <summary>Number of attached components currently tracked as trailing-sheet recipients.</summary>
        internal static int TrailingRecipientCount => s_trailingRecipients.Count;

        /// <summary>
        /// Registers <paramref name="sheets"/> as the trailing sheets of <paramref name="scope"/>
        /// and appends them, in order, after the own sheets of every attached styled component
        /// under <paramref name="scope"/> (the scope itself included). Components attached later
        /// pick them up on attach. Calling again replaces the previous list for this scope;
        /// calling with the same sheets is a no-op. A null or empty list is the same as
        /// <see cref="ClearTrailingSheets"/>. Null entries and duplicates are ignored.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
        public static void SetTrailingSheets(VisualElement scope, IReadOnlyList<StyleSheet> sheets)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            var normalized = NormalizeTrailingSheets(sheets);
            if (normalized == null)
            {
                ClearTrailingSheets(scope);
                return;
            }

            if (s_trailingScopes.TryGetValue(scope, out var entry))
            {
                if (SameTrailingSheets(entry.Sheets, normalized)) return;
                entry.Sheets = normalized;
            }
            else
            {
                s_trailingScopes.Add(scope, new TrailingScope { Sheets = normalized });
                s_trailingScopeCount++;
            }

            ResyncTrailingRecipients(null);
        }

        /// <summary>
        /// Removes the trailing sheets registered for <paramref name="scope"/> from every tracked
        /// recipient (and from their overlay copies). A recipient under an outer registered scope
        /// falls back to that scope's sheets. Unknown or null scope is a no-op.
        /// </summary>
        public static void ClearTrailingSheets(VisualElement scope)
        {
            if (scope == null || !s_trailingScopes.TryGetValue(scope, out _)) return;

            s_trailingScopes.Remove(scope);
            if (s_trailingScopeCount > 0) s_trailingScopeCount--;

            ResyncTrailingRecipients(scope);
        }

        /// <summary>
        /// Returns the trailing sheets registered for exactly <paramref name="scope"/>
        /// (not inherited from an outer scope), or an empty list.
        /// </summary>
        public static IReadOnlyList<StyleSheet> GetTrailingSheets(VisualElement scope)
        {
            if (scope != null && s_trailingScopes.TryGetValue(scope, out var entry))
                return entry.Sheets;
            return Array.Empty<StyleSheet>();
        }

        /// <summary>
        /// Links an element that received copies of <paramref name="source"/>'s style sheets
        /// (overlay portals) so it follows the source's trailing sheets: they are moved to its end
        /// now and replaced or removed together with the source's.
        /// </summary>
        internal static void TrackTrailingCopy(VisualElement source, VisualElement copy)
        {
            if (!(source is SusComponent comp) || copy == null || ReferenceEquals(source, copy)) return;

            comp._trailingCopies ??= new List<VisualElement>();
            if (!comp._trailingCopies.Contains(copy))
                comp._trailingCopies.Add(copy);

            if (comp._trailingApplied != null)
                MoveTrailingToEnd(copy, comp._trailingApplied);

            // A copy that is itself a component keeps its OWN trailing sheets last as well.
            if (copy is SusComponent copyComp && copyComp._trailingApplied != null)
                MoveTrailingToEnd(copyComp, copyComp._trailingApplied);
        }

        // ─── Lifecycle hooks (SusComponent.cs / SusComponent.Styles.cs) ───────────

        /// <summary>Attach: become a recipient if styled, pick up the nearest scope's sheets.</summary>
        private void OnTrailingAttach() => SyncTrailingSheets();

        /// <summary>Detach: stop tracking (sheets stay until the next attach re-evaluates them).</summary>
        private void OnTrailingDetach() => s_trailingRecipients.Remove(this);

        /// <summary>
        /// Own sheets changed (companion load / reload / hot reload): keep trailing sheets last.
        /// Free for a component under construction (not attached, nothing applied).
        /// </summary>
        private void OnOwnStyleSheetsChanged()
        {
            if (panel == null && _trailingApplied == null) return;
            SyncTrailingSheets();
        }

        // ─── Core ─────────────────────────────────────────────────────────────────

        private void SyncTrailingSheets()
        {
            VisualElement scope = null;
            StyleSheet[] want = null;

            var ownCount = styleSheets.count - (_trailingApplied?.Length ?? 0);
            if (panel != null && ownCount > 0)
            {
                s_trailingRecipients.Add(this);
                if (s_trailingScopeCount > 0)
                    scope = FindTrailingScope(this, out want);
            }
            else
            {
                s_trailingRecipients.Remove(this);
            }

            ApplyTrailingSheets(scope, want);
        }

        private static VisualElement FindTrailingScope(VisualElement from, out StyleSheet[] sheets)
        {
            for (var el = from; el != null; el = el.hierarchy.parent)
            {
                if (s_trailingScopes.TryGetValue(el, out var entry))
                {
                    sheets = entry.Sheets;
                    return el;
                }
            }
            sheets = null;
            return null;
        }

        private void ApplyTrailingSheets(VisualElement scope, StyleSheet[] want)
        {
            var had = _trailingApplied;
            _trailingScope = want == null ? null : scope;

            if (ReferenceEquals(had, want) && (want == null || TrailingInPlace(this, want)))
                return;

            ReplaceTrailing(this, had, want);
            _trailingApplied = want;

            if (_trailingCopies == null) return;
            for (int i = 0; i < _trailingCopies.Count; i++)
                ReplaceTrailing(_trailingCopies[i], had, want);
        }

        private static void ResyncTrailingRecipients(VisualElement onlyScope)
        {
            s_trailingWork.Clear();
            foreach (var r in s_trailingRecipients)
                if (onlyScope == null || ReferenceEquals(r._trailingScope, onlyScope))
                    s_trailingWork.Add(r);

            for (int i = 0; i < s_trailingWork.Count; i++)
            {
                var r = s_trailingWork[i];
                if (r.panel == null)
                {
                    // Missed detach (panel torn down): drop from the registry and the scope.
                    s_trailingRecipients.Remove(r);
                    if (onlyScope != null) r.ApplyTrailingSheets(null, null);
                    continue;
                }
                r.SyncTrailingSheets();
            }
            s_trailingWork.Clear();
        }

        private static void ReplaceTrailing(VisualElement el, StyleSheet[] had, StyleSheet[] want)
        {
            if (had != null)
            {
                for (int i = 0; i < had.Length; i++)
                {
                    var s = had[i];
                    // Unity null check: a destroyed sheet cannot be passed to Remove.
                    if (s != null && (want == null || Array.IndexOf(want, s) < 0))
                        el.styleSheets.Remove(s);
                }
            }
            if (want != null)
                MoveTrailingToEnd(el, want);
        }

        private static void MoveTrailingToEnd(VisualElement el, StyleSheet[] sheets)
        {
            if (TrailingInPlace(el, sheets)) return;
            var set = el.styleSheets;
            for (int i = 0; i < sheets.Length; i++)
                if (sheets[i] != null && set.Contains(sheets[i])) set.Remove(sheets[i]);
            for (int i = 0; i < sheets.Length; i++)
                if (sheets[i] != null) set.Add(sheets[i]);
        }

        private static bool TrailingInPlace(VisualElement el, StyleSheet[] sheets)
        {
            var set = el.styleSheets;
            var offset = set.count - sheets.Length;
            if (offset < 0) return false;
            for (int i = 0; i < sheets.Length; i++)
                if (!ReferenceEquals(set[offset + i], sheets[i])) return false;
            return true;
        }

        private static StyleSheet[] NormalizeTrailingSheets(IReadOnlyList<StyleSheet> sheets)
        {
            if (sheets == null || sheets.Count == 0) return null;
            var list = new List<StyleSheet>(sheets.Count);
            for (int i = 0; i < sheets.Count; i++)
            {
                var s = sheets[i];
                if (s != null && !list.Contains(s)) list.Add(s);
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        private static bool SameTrailingSheets(StyleSheet[] a, StyleSheet[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }
    }
}
