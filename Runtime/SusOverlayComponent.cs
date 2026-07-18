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

        /// <summary>The OverlayHost resolved by the last self-mount, if any.</summary>
        protected OverlayHost ResolvedHost => _selfHost;

        /// <summary>True while this element is mounted into the overlay (self-mount).</summary>
        protected bool IsMountedInOverlay => _selfEntry != null;

        /// <summary>
        /// Teleports THIS element into its pinned overlay layer, remembering the original
        /// parent for restore. Returns false if no OverlayHost was found (caller may fall
        /// back to inline display).
        /// </summary>
        protected bool MountSelfInOverlay(bool dismissOnClickOutside = false, Action onDismiss = null)
        {
            if (_selfHost == null)
            {
                _selfOriginalParent = parent;
                var p = parent;
                while (p != null) { if (p is OverlayHost oh) { _selfHost = oh; break; } p = p.parent; }
                if (_selfHost == null && panel?.visualTree != null)
                    _selfHost = panel.visualTree.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            }

            if (_selfHost != null && parent != _selfHost)
            {
                // Apply theme + tokens BEFORE reparenting so var() resolves in the overlay.
                SusThemeService.ApplyThemeClasses(this);
                _selfEntry = _selfHost.AddToOverlay(this, Layer, dismissOnClickOutside, onDismiss);
                return true;
            }
            return false;
        }

        /// <summary>Removes this element from the overlay and restores it to its original parent.</summary>
        protected void UnmountSelfFromOverlay()
        {
            if (_selfEntry != null && _selfHost != null)
            {
                _selfHost.RemoveFromOverlay(_selfEntry);
                _selfEntry = null;
                if (_selfOriginalParent != null) { _selfOriginalParent.Add(this); _selfOriginalParent = null; }
            }
        }
    }

    /// <summary>
    /// Base for modal dialogs / drawers. Pinned to <see cref="OverlayCategory.Modal"/>.
    /// Self-teleports into the overlay and installs a focus trap. Router's
    /// <c>SusRouterModal</c> and kit's <c>SusModal</c> both derive from this so every
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
