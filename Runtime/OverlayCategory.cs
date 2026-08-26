namespace Sharq.Core
{
    /// <summary>
    /// Overlay category — defines z-order via DOM position.
    /// Lower enum value = rendered first (behind). Higher = rendered last (on top).
    /// Categories are ordered: transition → modal → tooltip → dropdown → toast → drag → console.
    ///
    /// Tooltips and dropdowns are ABOVE modals because modals contain
    /// interactive elements (Select, Dropdown, tooltips on buttons) that
    /// must not be clipped by the modal container.
    /// </summary>
    public enum OverlayCategory
    {
        /// <summary>
        /// LEGACY / internal only. World markers (health bars, nameplates) do NOT live in this
        /// OverlayHost — the host is the TOP-most layer (popups/modals), so markers here would
        /// paint OVER screens. World UI renders UNDER screens instead:
        ///   Variant A (default, SusApp / EnsureWorldSpacePanel): a SEPARATE world-space panel
        ///   (SusWorldSpacePanel, PanelSettings.renderMode = WorldSpace) behind all screen UI.
        ///   Variant B (fallback): flat screen-space markers in a dedicated WorldMarkerLayer
        ///   inserted as the FIRST child of the root (below screens), repositioned per frame.
        /// This value survives only for the deprecated WorldSpaceService.OverlayHost fallback,
        /// where it keeps markers at the bottom of the overlay stack. Prefer WorldMarkerLayer.
        /// </summary>
        World = 0,

        /// <summary>Transition effects (fade, slide). Below modals.</summary>
        Transition = 10,

        /// <summary>Dialogs, drawers, full-screen modals. Above transition, below tooltips/dropdowns.</summary>
        Modal = 20,

        /// <summary>Tooltips and hover popups. Above modals, below dropdowns.</summary>
        Tooltip = 30,

        /// <summary>Dropdowns, context menus, autocomplete. Above tooltips and modals.</summary>
        Dropdown = 40,

        /// <summary>
        /// Toasts / snackbars — transient notifications. Above dropdowns/menus so a
        /// notification is never hidden by an open popup, below the debug console.
        /// </summary>
        Toast = 45,

        /// <summary>
        /// Cross-component drag ghosts. Above toasts so a notification mid-drag
        /// cannot bury the ghost; below the debug console.
        /// </summary>
        Drag = 48,

        /// <summary>Debug console, error overlays. Absolute top.</summary>
        Console = 50
    }
}
