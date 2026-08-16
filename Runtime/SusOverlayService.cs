using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Unified floating-overlay engine (framework primitive, sus-core).
    ///
    /// This is the SINGLE mount path for transient floating UI — popups, dropdowns,
    /// selects, menus and tooltips. Every floating gets the same treatment:
    ///   - cross-close: opening a new one closes all others (opt-in per call)
    ///   - click-outside: pointer-down outside the topmost floating closes it
    ///   - scroll tracking: anchor scroll → reposition
    ///   - anchor detach: anchor removed from panel → floating cleaned up
    ///   - owner grouping: floatings can share an <see cref="object"/> owner so a
    ///     group (e.g. a menu + its submenus) can be closed together
    ///
    /// It is CONTENT-AGNOSTIC: the <c>owner</c> is a plain <see cref="object"/>, so
    /// core needs no knowledge of concrete component types. Higher-level packages pass
    /// their component instance as the owner and layer the semantics on top.
    ///
    /// Category stacking is handled by <see cref="OverlayHost"/>:
    ///   World=0 &lt; Transition=10 &lt; Modal=20 &lt; Tooltip=30 &lt; Dropdown=40 &lt; Toast=45 &lt; Console=50
    /// </summary>
    public static class SusOverlayService
    {
        // ═══════════════════════════════════════════════════════
        //  Floating state
        // ═══════════════════════════════════════════════════════

        private class FloatingState
        {
            public VisualElement Overlay;
            public VisualElement Anchor;
            public OverlayEntry Entry;
            public Action OnClose;
            public ScrollView TrackedScroll;
            public Action<float> VScrollCb;
            public Action<float> HScrollCb;
            public VisualElement OriginalParent;
            public object Owner;                  // non-null = grouped floating (e.g. a menu)
            public List<FloatingState> Children;  // linked floatings that track this parent
            public bool MatchAnchorWidth;         // popup API (Select/Dropdown) — clone anchor width every resync
            public EventCallback<GeometryChangedEvent> AnchorGeometryCb;  // T-407: anchor moved/resized → resync
            public EventCallback<GeometryChangedEvent> PanelGeometryCb;   // T-407: panel resized → resync
        }

        private static readonly List<FloatingState> _floatings = new();

        // ── Tooltip state (separate — doesn't cross-close) ──
        private static OverlayEntry _activeTooltipEntry;
        private static bool _pointerDownRegistered;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _floatings.Clear();
            _activeTooltipEntry = null;
            _pointerDownRegistered = false;
        }
#endif

        // ═══════════════════════════════════════════════════════
        //  OverlayHost access
        // ═══════════════════════════════════════════════════════

        private static OverlayHost GetOverlayHost(VisualElement requester)
        {
            if (requester?.panel == null) return null;
            return SusBootstrap.GetOrCreateOverlay(requester.panel.visualTree);
        }

        // ═══════════════════════════════════════════════════════
        //  CORE — ShowFloating / HideFloating
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Adds an element to the overlay at the given category AND sets up tracking
        /// (click-outside, scroll reposition, anchor detach). Returns the OverlayEntry.
        /// If <paramref name="closeOthers"/> is true, closes all active floatings first.
        /// </summary>
        public static OverlayEntry ShowFloating(VisualElement overlay, VisualElement anchor,
            OverlayCategory category, Action onClose = null,
            bool closeOthers = true, VisualElement originalParent = null,
            object owner = null, bool matchAnchorWidth = false)
        {
            if (overlay == null || anchor == null) return null;

            var host = GetOverlayHost(anchor);
            if (host == null) return null;

            if (closeOthers)
                CloseAllFloatings();

            overlay.pickingMode = PickingMode.Position;
            SusThemeService.ApplyThemeClasses(overlay);

            var entry = host.AddToOverlay(overlay, category, dismissOnClickOutside: false);

            var state = new FloatingState
            {
                Overlay = overlay,
                Anchor = anchor,
                Entry = entry,
                OnClose = onClose,
                OriginalParent = originalParent,
                Owner = owner,
                MatchAnchorWidth = matchAnchorWidth,
            };

            // Scroll tracking — keep the delegate refs so we can unsubscribe exactly.
            state.TrackedScroll = FindScrollViewAncestor(anchor);
            if (state.TrackedScroll != null)
            {
                state.VScrollCb = _ => RepositionSingle(state);
                state.HScrollCb = _ => RepositionSingle(state);
                state.TrackedScroll.verticalScroller.valueChanged += state.VScrollCb;
                state.TrackedScroll.horizontalScroller.valueChanged += state.HScrollCb;
            }

            anchor.RegisterCallback<DetachFromPanelEvent>(OnFloatingAnchorDetached);

            // T-407: the very first Show() call happens synchronously (often before UI Toolkit
            // has resolved a layout pass on a just-mounted anchor/popup — worldBound/resolvedStyle
            // are stale or zero at that instant). GeometryChangedEvent fires whenever the anchor's
            // OWN resolved rect changes — including the first real layout pass and every later
            // reflow (e.g. Storybook panel resize) — so re-syncing width+position there (instead
            // of once at Show-time) keeps the popup glued to its trigger for the component's
            // entire open lifetime, not just its first frame.
            state.AnchorGeometryCb = _ => RepositionSingle(state);
            anchor.RegisterCallback<GeometryChangedEvent>(state.AnchorGeometryCb);

            // Panel-level reflow (breakpoint/density/window resize) can move the anchor without
            // necessarily firing its OWN GeometryChangedEvent first (parent reflow settles before
            // the anchor's leaf rect is touched in some layouts) — track the root too, belt&braces.
            if (host.panel?.visualTree != null)
            {
                state.PanelGeometryCb = _ => RepositionSingle(state);
                host.panel.visualTree.RegisterCallback<GeometryChangedEvent>(state.PanelGeometryCb);
            }

            _floatings.Add(state);
            EnsurePointerDown(host);

            return entry;
        }

        /// <summary>Hides a floating and cleans up all tracking (and its linked children).</summary>
        public static void HideFloating(VisualElement overlay)
        {
            if (overlay == null) return;

            for (int i = _floatings.Count - 1; i >= 0; i--)
            {
                var s = _floatings[i];
                if (s.Overlay != overlay) continue;

                _floatings.RemoveAt(i);

                // Hide linked children first (e.g. submenus)
                if (s.Children != null)
                {
                    for (int j = s.Children.Count - 1; j >= 0; j--)
                        HideFloating(s.Children[j].Overlay);
                }

                var host = s.Overlay.parent as OverlayHost;
                host?.RemoveFromOverlay(s.Entry);

                if (s.Anchor != null)
                {
                    s.Anchor.UnregisterCallback<DetachFromPanelEvent>(OnFloatingAnchorDetached);
                    if (s.AnchorGeometryCb != null)
                        s.Anchor.UnregisterCallback<GeometryChangedEvent>(s.AnchorGeometryCb);
                }
                if (s.PanelGeometryCb != null)
                {
                    var root = host?.panel?.visualTree ?? s.Overlay.panel?.visualTree;
                    root?.UnregisterCallback<GeometryChangedEvent>(s.PanelGeometryCb);
                }

                if (s.TrackedScroll != null)
                {
                    if (s.VScrollCb != null) s.TrackedScroll.verticalScroller.valueChanged -= s.VScrollCb;
                    if (s.HScrollCb != null) s.TrackedScroll.horizontalScroller.valueChanged -= s.HScrollCb;
                }

                if (s.OriginalParent != null && s.Overlay.parent != s.OriginalParent)
                {
                    s.Overlay.RemoveFromHierarchy();
                    s.OriginalParent.Add(s.Overlay);
                    s.Overlay.style.minHeight = StyleKeyword.None;
                }

                s.OnClose?.Invoke();
                break;
            }

            if (_floatings.Count == 0)
                UnregisterPointerDown();
        }

        /// <summary>Closes ALL active floatings.</summary>
        public static void CloseAllFloatings()
        {
            for (int i = _floatings.Count - 1; i >= 0; i--)
                HideFloating(_floatings[i].Overlay);
        }

        /// <summary>
        /// Hides every floating whose <c>Owner</c> equals <paramref name="owner"/>.
        /// Used to close a grouped set (e.g. a menu together with its submenus)
        /// without core knowing anything about the concrete owner type.
        /// </summary>
        public static void HideFloatingsByOwner(object owner)
        {
            if (owner == null) return;
            for (int i = _floatings.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_floatings[i].Owner, owner))
                    HideFloating(_floatings[i].Overlay);
            }
        }

        /// <summary>
        /// Links a child floating to a parent so the child repositions when the parent
        /// does (scroll) and is closed when the parent closes. Generic replacement for
        /// the old menu-specific LinkSubScroll.
        /// </summary>
        public static void LinkChild(OverlayEntry parentEntry, OverlayEntry childEntry)
        {
            FloatingState parent = null;
            FloatingState child = null;
            foreach (var s in _floatings)
            {
                if (s.Entry == parentEntry) parent = s;
                if (s.Entry == childEntry) child = s;
            }
            if (parent == null || child == null) return;

            parent.Children ??= new List<FloatingState>();
            if (!parent.Children.Contains(child))
                parent.Children.Add(child);
        }

        // ═══════════════════════════════════════════════════════
        //  Anchor detach
        // ═══════════════════════════════════════════════════════

        private static void OnFloatingAnchorDetached(DetachFromPanelEvent evt)
        {
            var detached = evt.target as VisualElement;
            for (int i = _floatings.Count - 1; i >= 0; i--)
            {
                if (_floatings[i].Anchor == detached)
                {
                    HideFloating(_floatings[i].Overlay);
                    return;
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        //  Scroll reposition
        // ═══════════════════════════════════════════════════════

        private static void RepositionSingle(FloatingState state)
        {
            if (state.Overlay == null || state.Anchor == null) return;

            // Popup API (Select/Dropdown): re-clone the anchor width on every resync, not just
            // at Show()-time — a GeometryChangedEvent means the anchor's rect (and therefore its
            // width) may have just changed (T-407: panel resize left the popup at a stale width).
            if (state.MatchAnchorWidth)
            {
                var w = state.Anchor.resolvedStyle.width;
                if (w > 0f) state.Overlay.style.width = w;
            }

            RepositionFloating(state.Overlay, state.Anchor);

            if (state.Children != null)
            {
                foreach (var child in state.Children)
                    RepositionSingle(child);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  Click-outside
        // ═══════════════════════════════════════════════════════

        private static void EnsurePointerDown(OverlayHost host)
        {
            if (_pointerDownRegistered) return;
            if (host?.panel == null) return;

            host.panel.visualTree.RegisterCallback<PointerDownEvent>(
                OnPointerDown, TrickleDown.TrickleDown);
            _pointerDownRegistered = true;
        }

        private static void UnregisterPointerDown()
        {
            if (!_pointerDownRegistered) return;
            VisualElement root = null;
            for (int i = _floatings.Count - 1; i >= 0; i--)
            {
                if (_floatings[i].Overlay?.panel != null)
                { root = _floatings[i].Overlay.panel.visualTree; break; }
            }
            root?.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            _pointerDownRegistered = false;
        }

        private static void OnPointerDown(PointerDownEvent evt)
        {
            var target = evt.target as VisualElement;
            if (target == null) return;

            // Topmost floating that doesn't contain the click → close it
            for (int i = _floatings.Count - 1; i >= 0; i--)
            {
                var s = _floatings[i];
                if (s.Overlay.Contains(target)) continue;
                if (s.Anchor != null && s.Anchor.Contains(target)) continue;

                var owner = s.Owner;
                HideFloating(s.Overlay);

                // Grouped floating (e.g. a menu) → also close the rest of the group.
                if (owner != null)
                    HideFloatingsByOwner(owner);

                evt.StopPropagation();
                return;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  POPUP API — Select, Dropdown
        // ═══════════════════════════════════════════════════════

        public static void Show(VisualElement popup, VisualElement anchor)
            => Show(popup, anchor, null);

        public static void Show(VisualElement popup, VisualElement anchor, Action onClose)
        {
            if (popup == null || anchor == null) return;

            foreach (var cls in anchor.GetClasses())
            {
                if (cls.StartsWith("sus-select--") || cls.StartsWith("sus-dropdown--"))
                    popup.AddToClassList(cls);
            }

            // Clone width from anchor — select/dropdown popups must match source.
            var w = anchor.resolvedStyle.width;
            if (w > 0f) popup.style.width = w;

            RepositionFloating(popup, anchor);

            // originalParent: null — do not reparent back to the field on Hide.
            // Reparenting caused SusScrollView (inside popup) to Attach/Detach on every
            // open/close and tripped RemountLoopAudit during rapid toggles.
            // matchAnchorWidth: true — Select/Dropdown popups must track the trigger's width
            // for the whole open lifetime (T-407), not just the first Show() call.
            ShowFloating(popup, anchor, OverlayCategory.Dropdown,
                onClose: onClose,
                closeOthers: true,
                originalParent: null,
                matchAnchorWidth: true);
        }

        public static void Hide(VisualElement popup, VisualElement originalParent)
        {
            if (popup == null) return;
            // Only restore a parent when caller explicitly opts in AND Show left it unset.
            // Select/Dropdown pass null to park the popup detached until the next Show.
            if (originalParent != null)
            {
                for (int i = _floatings.Count - 1; i >= 0; i--)
                {
                    if (_floatings[i].Overlay == popup && _floatings[i].OriginalParent == null)
                        _floatings[i].OriginalParent = originalParent;
                }
            }
            HideFloating(popup);
        }

        // ═══════════════════════════════════════════════════════
        //  Positioning helpers
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Positions a floating element absolutely below its anchor, with viewport clamping.
        /// Callers can override left/top afterwards for custom logic.
        /// </summary>
        public static void RepositionFloating(VisualElement floating, VisualElement anchor)
        {
            if (floating == null || anchor == null) return;

            floating.style.position = Position.Absolute;

            var anchorWorld = anchor.worldBound;
            float top = anchorWorld.yMax;
            float left = anchorWorld.x;

            var panel = floating.panel ?? anchor.panel;
            if (panel != null)
            {
                var pw = panel.visualTree.worldBound.width;
                var ph = panel.visualTree.worldBound.height;
                var fw = floating.resolvedStyle.width > 0 ? floating.resolvedStyle.width : 200f;
                var fh = floating.resolvedStyle.height > 0 ? floating.resolvedStyle.height : 48f;

                // T-407: flip ABOVE the anchor only when there's genuinely no room below AND
                // there IS room above — never just clamp downward (that used to slide the popup
                // up over the anchor/header without actually flipping past it, T-407 case
                // kit-dropdown.png: popup ended up overlapping the Storybook header).
                if (top + fh > ph)
                {
                    var aboveTop = anchorWorld.y - fh;
                    top = aboveTop >= 0f ? aboveTop : Mathf.Max(8f, ph - fh - 8f);
                }
                if (left + fw > pw) left = pw - fw - 8f;
                if (left < 0) left = 8f;
                if (top < 0) top = 8f;
            }

            floating.style.top = top;
            floating.style.left = left;
        }

        // ═══════════════════════════════════════════════════════
        //  TOOLTIP
        // ═══════════════════════════════════════════════════════

        public static void ShowTooltip(VisualElement tooltipCard, VisualElement anchor)
        {
            if (tooltipCard == null || anchor == null) return;

            // Already floating this card — avoid re-AddToOverlay churn on hover ticks.
            if (_activeTooltipEntry != null
                && _activeTooltipEntry.Element == tooltipCard
                && tooltipCard.parent is OverlayHost)
                return;

            if (_activeTooltipEntry != null && _activeTooltipEntry.Element != tooltipCard)
                HideInternalTooltip(_activeTooltipEntry.Element, null);

            var host = GetOverlayHost(anchor);
            if (host == null) return;

            tooltipCard.pickingMode = PickingMode.Ignore;
            SusThemeService.ApplyThemeClasses(tooltipCard);
            _activeTooltipEntry = host.AddToOverlay(tooltipCard, OverlayCategory.Tooltip,
                dismissOnClickOutside: false);
        }

        public static void HideTooltip(VisualElement tooltipCard, VisualElement originalParent)
            => HideInternalTooltip(tooltipCard, originalParent);

        private static void HideInternalTooltip(VisualElement tooltipCard, VisualElement originalParent)
        {
            if (tooltipCard == null) return;

            if (_activeTooltipEntry != null && _activeTooltipEntry.Element == tooltipCard)
            {
                var host = tooltipCard.parent as OverlayHost;
                host?.RemoveFromOverlay(_activeTooltipEntry);
                _activeTooltipEntry = null;
            }

            if (originalParent != null && tooltipCard.parent != originalParent)
            {
                tooltipCard.RemoveFromHierarchy();
                originalParent.Add(tooltipCard);
            }
        }

        public static void PositionTooltip(VisualElement tooltipCard, Vector2 topLeft)
        {
            if (tooltipCard == null) return;
            tooltipCard.style.position = Position.Absolute;
            tooltipCard.style.left = topLeft.x;
            tooltipCard.style.top = topLeft.y;
        }

        // ═══════════════════════════════════════════════════════
        //  Scroll view detection
        // ═══════════════════════════════════════════════════════

        private static ScrollView FindScrollViewAncestor(VisualElement el)
        {
            var p = el.parent;
            while (p != null)
            {
                if (p is ScrollView sv) return sv;
                p = p.parent;
            }
            return null;
        }
    }
}
