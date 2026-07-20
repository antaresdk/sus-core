using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Portal-style overlay container. Renders children above normal UI via
    /// DOM order (last sibling = topmost, no z-index).
    ///
    /// Elements are stacked by category:
    /// transition &lt; modal &lt; tooltip &lt; dropdown &lt; toast &lt; console.
    /// Tooltips/dropdowns sit above modals so popups from inside a dialog stay visible.
    /// World (0) is legacy/internal — world markers render UNDER screens (in a separate
    /// <see cref="SusWorldSpacePanel"/> or the <see cref="WorldMarkerLayer"/>), never in this host.
    /// Within a category, last-added renders on top.
    ///
    /// In the <see cref="SusApp"/> scaffold this is the topmost of three fixed layers
    /// (<see cref="WorldMarkerLayer"/> → <see cref="ScreenHost"/> → OverlayHost).
    ///
    /// Usage:
    /// <code>
    /// var overlay = SusBootstrap.GetOrCreateOverlay(root);
    /// overlay.AddToOverlay(myPopup, OverlayCategory.Dropdown, dismissOnClickOutside: true);
    /// overlay.RemoveFromOverlay(myPopup);
    /// </code>
    /// </summary>
    public class OverlayHost : SusLayer
    {
        public const string OverlayHostName = "overlay-host";

        private readonly List<OverlayEntry> _stack = new();
        private bool _clickGuardInstalled;

        /// <summary>
        /// Creates a full-screen, click-transparent overlay host. Absolute
        /// positioning stretches it over the whole panel so modal/tooltip
        /// children (which use absolute insets) get a non-zero rect. Picking is
        /// ignored while empty so route content underneath stays interactive;
        /// modal overlays set their own picking to capture clicks.
        ///
        /// Auto-BringToFront on layout changes: in USS, last sibling renders
        /// on top (no z-index). When content is added to the parent container
        /// AFTER this overlay, it would render behind content. GeometryChanged
        /// guarantees we're always the topmost child.
        /// </summary>
        public OverlayHost()
        {
            style.position = Position.Absolute;
            style.top = 0;
            style.left = 0;
            style.right = 0;
            style.bottom = 0;
            pickingMode = PickingMode.Ignore;

            // USS layering: last sibling = top. Auto-repair when layout changes
            // (new siblings push us down in the render order).
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (parent != null && parent.childCount > 1
                && parent.ElementAt(parent.childCount - 1) != this)
            {
                BringToFront();
            }
        }

        /// <summary>
        /// Adds an element to the overlay stack. Elements are inserted in
        /// category+z-order so higher categories render on top.
        /// Returns the entry for later removal.
        /// </summary>
        public OverlayEntry AddToOverlay(VisualElement element, OverlayCategory category,
            bool dismissOnClickOutside = false, System.Action onDismiss = null)
        {
            if (element == null) return null;

            // Copy companion USS from nearest SusComponent ancestor BEFORE
            // RemoveFromHierarchy breaks the ancestor chain. Without this,
            // elements teleported to OverlayHost lose scoped/companion styles
            // (LoadCompanionStyleSheets loads on the component, not on root).
            CopyAncestorStyleSheets(element);

            // Re-show path (e.g. SusTooltip ShowCard while card is already in
            // OverlayHost): RemoveFromHierarchy detaches the DOM child but used
            // to leave a stale _stack entry. Then insertAt = stack.Count >
            // childCount → ArgumentOutOfRangeException, and because _stack.Insert
            // ran first each failure grew the index (1, 2, 3…).
            RemoveStackEntriesFor(element);
            element.RemoveFromHierarchy();
            PruneDetachedStackEntries();

            var entry = new OverlayEntry(element, category, dismissOnClickOutside, onDismiss);

            // Insert in correct position: sorted by category, then by insertion order
            int insertAt = 0;
            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Category <= category)
                    insertAt = i + 1;
                else
                    break;
            }

            // VisualElement.Hierarchy.Insert requires index in [0, childCount].
            int childCount = hierarchy.childCount;
            if (insertAt > childCount)
                insertAt = childCount;
            if (insertAt < 0)
                insertAt = 0;

            _stack.Insert(insertAt, entry);
            hierarchy.Insert(insertAt, element);

            InstallClickGuard();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // OverlayAudit: full-screen / blocking overlays with accidental pickable chrome.
            // Dropdown/Tooltip intentionally host interactive children (list rows, links) —
            // do not warn for those categories (false positive for Select/Menu).
            if (category != OverlayCategory.Modal
                && category != OverlayCategory.Dropdown
                && category != OverlayCategory.Tooltip)
            {
                var pickableCount = 0;
                element.Query<VisualElement>().ForEach(e =>
                {
                    if (e.pickingMode == PickingMode.Position && e.visible)
                        pickableCount++;
                });
                if (pickableCount > 0)
                {
                    Debug.LogWarning($"[OverlayAudit] '{element.GetType().Name}' added to overlay " +
                        $"in {category} category with {pickableCount} pickable children. " +
                        $"It may block clicks to underlying UI. " +
                        $"Consider setting pickingMode=Ignore on children.");
                }
            }

            // ModalStackDepthAudit: warn if too many modals are layered
            var modalCount = _stack.Count(e => e.Category == OverlayCategory.Modal);
            if (modalCount > 5)
            {
                Debug.LogWarning($"[ModalStackAudit] {modalCount} modals on screen. " +
                    $"Deep modal stacking may indicate a flow bug or unclosed modals.");
            }
#endif

            return entry;
        }

        /// <summary>
        /// Drops stack entries that track <paramref name="element"/> without
        /// touching the DOM (caller handles reparent / RemoveFromHierarchy).
        /// </summary>
        private void RemoveStackEntriesFor(VisualElement element)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Element == element)
                    _stack.RemoveAt(i);
            }
        }

        /// <summary>
        /// Removes stack entries whose elements are no longer children of this
        /// host (detached elsewhere without RemoveFromOverlay).
        /// </summary>
        private void PruneDetachedStackEntries()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var el = _stack[i].Element;
                if (el == null || el.parent != this)
                    _stack.RemoveAt(i);
            }
        }

        private void InstallClickGuard()
        {
            if (_clickGuardInstalled) return;

            // Walk stack — if any entry has dismissOnClickOutside, install guard
            bool needsGuard = false;
            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].DismissOnClickOutside)
                {
                    needsGuard = true;
                    break;
                }
            }
            if (!needsGuard) return;

            _clickGuardInstalled = true;
            RegisterCallback<ClickEvent>(OnOverlayClick);
        }

        private void UninstallClickGuard()
        {
            bool stillNeeded = false;
            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].DismissOnClickOutside)
                {
                    stillNeeded = true;
                    break;
                }
            }
            if (stillNeeded) return;

            _clickGuardInstalled = false;
            UnregisterCallback<ClickEvent>(OnOverlayClick);
        }

        private void OnOverlayClick(ClickEvent evt)
        {
            // Collect dismissable entries to remove (avoid mutation during iteration)
            var toRemove = new List<OverlayEntry>();
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var entry = _stack[i];
                if (!entry.DismissOnClickOutside) continue;

                // Check if click target is inside this overlay element
                if (IsInside(evt.target as VisualElement, entry.Element))
                    break; // clicked on the overlay itself — stop, don't dismiss

                toRemove.Add(entry);
            }

            foreach (var entry in toRemove)
            {
                RemoveFromOverlay(entry);
                UninstallClickGuard();
            }

            evt.StopPropagation();
        }

        private static bool IsInside(VisualElement target, VisualElement container)
        {
            if (target == null || container == null) return false;
            VisualElement cur = target;
            while (cur != null)
            {
                if (cur == container) return true;
                cur = cur.parent;
            }
            return false;
        }

        public void RemoveFromOverlay(VisualElement element)
        {
            if (element == null) return;

            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Element == element)
                {
                    var entry = _stack[i];
                    _stack.RemoveAt(i);
                    element.RemoveFromHierarchy();
                    entry.OnDismiss?.Invoke();
                    UninstallClickGuard();
                    return;
                }
            }
        }

        public void RemoveFromOverlay(OverlayEntry entry)
        {
            if (entry == null) return;
            RemoveFromOverlay(entry.Element);
        }

        public void ClearCategory(OverlayCategory category)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Category == category)
                {
                    var entry = _stack[i];
                    _stack.RemoveAt(i);
                    entry.Element.RemoveFromHierarchy();
                    entry.OnDismiss?.Invoke();
                }
            }
            UninstallClickGuard();
        }

        public void ClearAll()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var entry = _stack[i];
                entry.Element.RemoveFromHierarchy();
                entry.OnDismiss?.Invoke();
            }
            _stack.Clear();
            UninstallClickGuard();
        }

        /// <summary>
        /// Returns the number of active overlays.
        /// </summary>
        public int Count => _stack.Count;

        /// <summary>
        /// Returns the overlay stack (read-only snapshot).
        /// </summary>
        public IReadOnlyList<OverlayEntry> Stack => _stack;

        /// <summary>
        /// Validates that this OverlayHost is the last child of its parent.
        /// Logs a warning if the invariant is broken (overlays may render behind content).
        /// </summary>
        public bool ValidateIsLastChild()
        {
            if (parent == null || parent.childCount == 0)
                return true;

            var lastChild = parent.ElementAt(parent.childCount - 1);
            if (lastChild != this)
            {
                Debug.LogWarning(
                    $"[OverlayHost] NOT last child of '{parent.name}'! " +
                    $"Overlays may render behind content. Call BringToFront() to fix.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Logs the full overlay stack: index, category, element name, and size.
        /// Useful for debugging "why is my layer not visible?".
        /// </summary>
        public void DumpStack()
        {
            Debug.Log($"[OverlayHost] Stack ({_stack.Count} entries):");
            for (int i = 0; i < _stack.Count; i++)
            {
                var e = _stack[i];
                var size = e.Element?.resolvedStyle;
                var w = size?.width ?? 0;
                var h = size?.height ?? 0;
                Debug.Log($"  [{i}] cat={e.Category} name='{e.Element?.name}' " +
                    $"size={w:F0}x{h:F0} dismissOnClick={e.DismissOnClickOutside}");
            }
            if (_stack.Count == 0)
                Debug.Log("  (empty)");
        }

        // ── Convenience methods for common overlay patterns ──

        /// <summary>
        /// Shows a tooltip positioned near an anchor element.
        /// The tooltip is added to the Tooltip category and auto-positions.
        /// </summary>
        public OverlayEntry ShowTooltip(VisualElement anchor, VisualElement content, string position = "top")
        {
            if (anchor == null || content == null) return null;

            content.style.position = Position.Absolute;

            anchor.schedule.Execute(() =>
            {
                var anchorWorld = anchor.worldBound;
                var tooltipWorld = content.worldBound;

                switch (position)
                {
                    case "bottom":
                        content.style.top = anchorWorld.yMax + 8;
                        content.style.left = anchorWorld.x + (anchorWorld.width - tooltipWorld.width) / 2;
                        break;
                    case "left":
                        content.style.top = anchorWorld.y + (anchorWorld.height - tooltipWorld.height) / 2;
                        content.style.left = anchorWorld.x - tooltipWorld.width - 8;
                        break;
                    case "right":
                        content.style.top = anchorWorld.y + (anchorWorld.height - tooltipWorld.height) / 2;
                        content.style.left = anchorWorld.xMax + 8;
                        break;
                    default: // "top"
                        content.style.top = anchorWorld.y - tooltipWorld.height - 8;
                        content.style.left = anchorWorld.x + (anchorWorld.width - tooltipWorld.width) / 2;
                        break;
                }
            }).ExecuteLater(0);

            return AddToOverlay(content, OverlayCategory.Tooltip,
                dismissOnClickOutside: true);
        }

        /// <summary>
        /// Shows a dropdown menu positioned below an anchor element.
        /// Auto-flips above if there's not enough space below.
        /// </summary>
        public OverlayEntry ShowDropdown(VisualElement anchor, VisualElement content)
        {
            if (anchor == null || content == null) return null;

            content.style.position = Position.Absolute;
            content.style.minWidth = anchor.worldBound.width;

            anchor.schedule.Execute(() =>
            {
                var anchorWorld = anchor.worldBound;
                var contentWorld = content.worldBound;

                content.style.top = anchorWorld.yMax;
                content.style.left = anchorWorld.x;

                // Flip above if not enough space below
                var rootHeight = panel?.visualTree?.worldBound.height ?? Screen.height;
                if (anchorWorld.yMax + contentWorld.height > rootHeight && anchorWorld.y > contentWorld.height)
                {
                    content.style.top = anchorWorld.y - contentWorld.height;
                }
            }).ExecuteLater(0);

            return AddToOverlay(content, OverlayCategory.Dropdown,
                dismissOnClickOutside: true);
        }

        private bool _escapeHandlerInstalled;

        /// <summary>
        /// Installs keyboard Escape handler. Dismisses the topmost dismissable overlay.
        /// Call once during app initialization. Idempotent.
        /// </summary>
        public void InstallEscapeHandler()
        {
            if (_escapeHandlerInstalled) return;
            if (panel == null) return;

            _escapeHandlerInstalled = true;
            panel.visualTree.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;

                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    if (_stack[i].DismissOnClickOutside)
                    {
                        RemoveFromOverlay(_stack[i]);
                        evt.StopPropagation();
                        return;
                    }
                }
            }, TrickleDown.NoTrickleDown);
        }

        /// <summary>
        /// Traps focus within a given overlay (for modals). Tab/Shift+Tab wrap.
        /// </summary>
        public void InstallFocusTrap(VisualElement overlayElement)
        {
            if (overlayElement == null) return;

            overlayElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Tab) return;

                var focusables = overlayElement.Query<VisualElement>()
                    .Where(e => e.focusable && e.enabledInHierarchy)
                    .ToList();

                if (focusables.Count == 0) return;

                var current = overlayElement.focusController?.focusedElement as VisualElement;
                var currentIndex = focusables.IndexOf(current);

                if (evt.shiftKey)
                {
                    var target = currentIndex <= 0 ? focusables[focusables.Count - 1] : focusables[currentIndex - 1];
                    target.Focus();
                }
                else
                {
                    var target = currentIndex >= focusables.Count - 1 ? focusables[0] : focusables[currentIndex + 1];
                    target.Focus();
                }

                evt.StopPropagation();
            }, TrickleDown.NoTrickleDown);
        }

        // ── Companion USS inheritance ──────────────────────────────────────

        /// <summary>
        /// Copies companion USS from the nearest SusComponent ancestor to the
        /// element before it's removed from the hierarchy and added to overlay.
        /// Without this, elements lose their scoped/companion styles because
        /// LoadCompanionStyleSheets attaches USS to the component, not to root.
        /// </summary>
        private static void CopyAncestorStyleSheets(VisualElement element)
        {
            var comp = FindSusComponentFromParent(element);
            if (comp == null) return;

            for (int i = 0; i < comp.styleSheets.count; i++)
            {
                var s = comp.styleSheets[i];
                if (!element.styleSheets.Contains(s))
                    element.styleSheets.Add(s);
            }
        }

        /// <summary>
        /// Walks element.parent upwards to find the nearest SusComponent.
        /// </summary>
        private static SusComponent FindSusComponentFromParent(VisualElement el)
        {
            var p = el?.parent;
            while (p != null)
            {
                if (p is SusComponent sc) return sc;
                p = p.parent;
            }
            return null;
        }
    }
}
