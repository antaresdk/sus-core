using System;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Base for overlay-pinned content components (framework primitive, sus-core).
    ///
    /// Part of the C2 two-tier model: it IS a <see cref="SusComponent"/> (so the whole
    /// hierarchy stays uniform), but it fixes the <see cref="OverlayCategory"/> the
    /// component lives in. The layer is declared centrally per subclass and cannot be
    /// changed per-instance, so a modal can never end up in the tooltip layer, etc.
    ///
    /// Two mount mechanisms are provided:
    ///   - <see cref="MountSelfInOverlay"/> / <see cref="UnmountSelfFromOverlay"/> —
    ///     teleport THIS element into the overlay (used by modal, toast).
    ///   - child-teleport via <see cref="SusOverlayService"/> (used by tooltip, popup),
    ///     which keeps the activator inline and floats only a child card/popup.
    /// </summary>
    public abstract class SusOverlayComponent : SusComponent
    {
        /// <summary>
        /// The overlay layer this primitive is pinned to. Sealed by each concrete base
        /// (<see cref="SusModalBase"/> → Modal, etc.) so it cannot be overridden.
        /// </summary>
        protected abstract OverlayCategory Layer { get; }

        private OverlayHost _selfHost;
        private OverlayEntry _selfEntry;
        private VisualElement _selfOriginalParent;

        /// <summary>
        /// Bumped on every mount/unmount state change (see <see cref="MountSelfInOverlay"/> /
        /// <see cref="UnmountSelfFromOverlay"/>). A deferred restore scheduled by an unmount
        /// captures the token at schedule time and only performs the reparent-back if the
        /// token is still current when the callback fires — see T-2174: a same-frame
        /// Cancel()+Start() (SusTutorialModal on step change) unmounts then immediately
        /// remounts into the overlay; the unmount's deferred restore must not run a frame
        /// later and rip the freshly-remounted element back out (that produced the
        /// "attached 6 times in 1s" RemountLoopAudit flood).
        /// </summary>
        private int _overlayMountToken;

        /// <summary>The OverlayHost resolved by the last self-mount, if any.</summary>
        protected OverlayHost ResolvedHost => _selfHost;

        /// <summary>True while this element is mounted into the overlay (self-mount).</summary>
        protected bool IsMountedInOverlay => _selfEntry != null;

        /// <summary>
        /// True while <see cref="MountSelfInOverlay"/>/<see cref="UnmountSelfFromOverlay"/> is
        /// in the middle of reparenting this element to/from its overlay host. UI Toolkit
        /// fires a real <c>DetachFromPanelEvent</c> (and therefore this component's own
        /// <c>Unmounted()</c>) as a side effect of the internal <c>RemoveFromHierarchy()</c>
        /// call that any reparent requires — subclasses (SusModal, ...) whose
        /// <c>Unmounted()</c> override runs real "closing" logic (e.g. <c>CloseOverlay()</c>)
        /// MUST check this flag first and return early when it's true. Otherwise the
        /// detach-for-relocation is misread as a genuine user-facing close: the very first
        /// <c>MountSelfInOverlay</c> call (element still parented inline) detaches from the
        /// inline parent to move into the host, which re-triggers the "closing" path
        /// mid-relocation and undoes the open before it ever finishes — reproduced
        /// empirically as silent display:None / no reparent, or the UI Toolkit "already
        /// being modified" error depending on internal timing (case 8, qa-4 2026-08-16).
        /// No amount of deferring the CALLER's open (schedule delay, GeometryChangedEvent)
        /// fixes this — the reentrancy happens one level down, inside AddToOverlay's own
        /// RemoveFromHierarchy(), regardless of when MountSelfInOverlay itself runs.
        /// </summary>
        protected bool IsRelocatingToOverlay { get; private set; }

        /// <summary>
        /// Tells <see cref="SusComponent.OnDetachFromPanelHandler"/> to skip
        /// <c>DisposeAllBindings()</c> while relocating — see <see cref="SusComponent.IsRelocating"/>
        /// for the full rationale.
        /// </summary>
        protected override bool IsRelocating => IsRelocatingToOverlay;

        /// <summary>
        /// Teleports THIS element into its pinned overlay layer, remembering the original
        /// parent for restore. Returns false if no OverlayHost was found (caller may fall
        /// back to inline display).
        /// </summary>
        protected bool MountSelfInOverlay(bool dismissOnClickOutside = false, Action onDismiss = null)
        {
            // Capture restore parent whenever we leave a non-host ancestor.
            // Previously only set when _selfHost was null — second open lost restore.
            if (parent != null && parent is not OverlayHost)
                _selfOriginalParent = parent;

            if (_selfHost == null)
            {
                var p = parent;
                while (p != null) { if (p is OverlayHost oh) { _selfHost = oh; break; } p = p.parent; }
                if (_selfHost == null && panel?.visualTree != null)
                    _selfHost = panel.visualTree.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            }

            if (_selfHost == null)
                return false;

            // Already on host — treat as mounted; do not reparent (AttachToPanel-safe).
            if (parent == _selfHost)
                return true;

            // Apply theme + tokens BEFORE reparenting so var() resolves in the overlay.
            SusThemeService.ApplyThemeClasses(this);
            IsRelocatingToOverlay = true;
            _overlayMountToken++;
            try
            {
                _selfEntry = _selfHost.AddToOverlay(this, Layer, dismissOnClickOutside, onDismiss);
            }
            finally
            {
                IsRelocatingToOverlay = false;
            }
            return true;
        }

        /// <summary>Removes this element from the overlay and restores it to its original parent.</summary>
        protected void UnmountSelfFromOverlay()
        {
            if (_selfEntry != null && _selfHost != null)
            {
                var entry = _selfEntry;
                _selfEntry = null;
                _overlayMountToken++;
                IsRelocatingToOverlay = true;
                try
                {
                    _selfHost.RemoveFromOverlay(entry);
                }
                finally
                {
                    IsRelocatingToOverlay = false;
                }
                if (_selfOriginalParent != null)
                {
                    // this branch also runs when the HOST initiated the removal
                    // (OverlayHost.RemoveFromOverlay/ClearAll called directly, not through
                    // this component's own Close()/Model=false path) — that path never sets
                    // IsRelocatingToOverlay before detaching, so the DetachFromPanelEvent
                    // this element's own RemoveFromHierarchy() fires reaches this method
                    // REENTRANT, synchronously, from inside UIR's own render-tree traversal
                    // (RepaintPanels -> ProcessChanges -> ... -> Unmounted() -> here). Adding
                    // this element back to its original parent SYNCHRONOUSLY in that window
                    // mutates the visual tree while UIR is still walking it and corrupts its
                    // internal bookkeeping (UpdateLocalFlipsWinding NullRef + repeated
                    // RepaintPanels assertion failures — reproduced live via ClearAll() on a
                    // still-open SusModal). Defer exactly like the "stale DOM" branch
                    // below already does for the identical underlying reason — safe
                    // because the next real frame is guaranteed to be outside any active
                    // traversal. A plain Close() click (IsRelocatingToOverlay never involved,
                    // no reentrancy) is delayed by one frame too, which every existing test
                    // already tolerates (asserts run after `yield return WaitFrame()`).
                    // Capture the mount token now — if MountSelfInOverlay runs again
                    // (same-frame Cancel()+Start()) before this callback fires, the token
                    // will have moved on and the stale restore below must no-op instead of
                    // ripping the freshly-remounted element back out (T-2174).
                    var restore = _selfOriginalParent;
                    _selfOriginalParent = null;
                    var restoreToken = _overlayMountToken;
                    restore.schedule.Execute(() =>
                    {
                        if (restoreToken == _overlayMountToken && parent != restore)
                            restore.Add(this);
                    }).ExecuteLater(0);
                }
            }
            else if (parent is OverlayHost && _selfOriginalParent != null)
            {
                // Stale DOM on host without stack entry. Defer restore — Unmounted
                // may run inside DetachFromPanel where hierarchy mutation is illegal.
                _overlayMountToken++;
                var restore = _selfOriginalParent;
                _selfOriginalParent = null;
                var restoreToken = _overlayMountToken;
                restore.schedule.Execute(() =>
                {
                    if (restoreToken == _overlayMountToken && parent != restore)
                        restore.Add(this);
                }).ExecuteLater(0);
            }
        }
    }

    /// <summary>
    /// Base for modal dialogs / drawers. Pinned to <see cref="OverlayCategory.Modal"/>.
    /// Self-teleports into the overlay and installs a focus trap. Router's
    /// <c>SusRouterModal</c> and downstream modal components both derive from this so every
    /// modal is a real <see cref="SusComponent"/> living in exactly the modal layer.
    /// </summary>
    public abstract class SusModalBase : SusOverlayComponent
    {
        protected sealed override OverlayCategory Layer => OverlayCategory.Modal;

        /// <summary>Opens the modal in the overlay (with focus trap). Falls back to inline display.</summary>
        protected bool OpenInOverlay(bool dismissOnClickOutside, Action onDismiss)
        {
            var mounted = MountSelfInOverlay(dismissOnClickOutside, onDismiss);
            if (mounted)
                ResolvedHost?.InstallFocusTrap(this);
            return mounted;
        }

        /// <summary>Closes the modal, restoring it to its original parent.</summary>
        protected void CloseFromOverlay() => UnmountSelfFromOverlay();
    }

    /// <summary>
    /// Base for tooltips. Pinned to <see cref="OverlayCategory.Tooltip"/>. The activator
    /// stays inline; only a child card is floated via <see cref="SusOverlayService"/>.
    /// </summary>
    public abstract class SusTooltipBase : SusOverlayComponent
    {
        protected sealed override OverlayCategory Layer => OverlayCategory.Tooltip;

        /// <summary>Floats a child card into the tooltip layer next to an anchor.</summary>
        protected void ShowOverlayCard(VisualElement card, VisualElement anchor)
            => SusOverlayService.ShowTooltip(card, anchor);

        /// <summary>Removes the floated card, optionally restoring it to a parent.</summary>
        protected void HideOverlayCard(VisualElement card, VisualElement originalParent = null)
            => SusOverlayService.HideTooltip(card, originalParent);
    }

    /// <summary>
    /// Base for dropdown/select/menu popups. Pinned to <see cref="OverlayCategory.Dropdown"/>.
    /// The trigger stays inline; only a child popup is floated via <see cref="SusOverlayService"/>
    /// with full tracking (click-outside, scroll reposition, anchor detach).
    /// </summary>
    public abstract class SusPopupBase : SusOverlayComponent
    {
        protected sealed override OverlayCategory Layer => OverlayCategory.Dropdown;

        /// <summary>Floats a child popup below an anchor (dropdown layer), with tracking.</summary>
        protected void ShowPopup(VisualElement popup, VisualElement anchor, Action onClose = null)
            => SusOverlayService.Show(popup, anchor, onClose);

        /// <summary>Removes the floated popup, restoring it to its original parent.</summary>
        protected void HidePopup(VisualElement popup, VisualElement originalParent = null)
            => SusOverlayService.Hide(popup, originalParent);

        /// <summary>Repositions the popup relative to its anchor (with viewport clamping).</summary>
        protected void RepositionPopup(VisualElement popup, VisualElement anchor)
            => SusOverlayService.RepositionFloating(popup, anchor);
    }

    /// <summary>
    /// Base for toasts / snackbars. Pinned to <see cref="OverlayCategory.Toast"/> (above
    /// dropdowns, below console), so a transient notification is never hidden by an open
    /// popup. Self-teleports into the overlay.
    /// </summary>
    public abstract class SusToastBase : SusOverlayComponent
    {
        protected sealed override OverlayCategory Layer => OverlayCategory.Toast;

        /// <summary>Shows the toast in the overlay toast layer. Falls back to inline display.</summary>
        protected bool ShowToast(bool dismissOnClickOutside = false, Action onDismiss = null)
            => MountSelfInOverlay(dismissOnClickOutside, onDismiss);

        /// <summary>Hides the toast, restoring it to its original parent.</summary>
        protected void HideToast() => UnmountSelfFromOverlay();
    }
}
