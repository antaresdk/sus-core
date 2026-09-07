using UnityEngine.UIElements;

namespace Sharq.Core.Editor.TestSupport
{
    /// <summary>
    /// EditMode geometry test hosts build a <c>UIDocument</c> by hand instead of going through
    /// <c>SusApp</c>/<see cref="SusBootstrap.Mount{T}(VisualElement)"/>, so nothing loads the
    /// design-token cascade for them. Before the size lestnica (ARCH-20260907-DIMENSION-TOKENS)
    /// this was harmless — a hand-built host that skipped the cascade still got the right pixels
    /// because every rule was a bare literal. After tokenization a component's own USS reads
    /// <c>var(--sk-box-lg, 72px)</c>: the literal is only a FALLBACK, used when the custom
    /// property is undefined anywhere in the ancestor chain. A host that never loads a
    /// downstream package's size-ladder sheet (registered as an "extra cascade sheet", only
    /// attached to a container by <see cref="SusBootstrap.LoadTokenCascade"/> /
    /// <see cref="SusBootstrap.EnsureTokenCascade"/>) always falls back to that literal — the
    /// BASE ("lg") step — no matter which <c>.breakpoint-*</c> class is on the element, because
    /// the block carrying `sm`/`xl`/... overrides for that property never got onto the tree at
    /// all (D-23/D-24, plan §4.2.1, T-3083). A host measuring a compact (`sm`) layout then
    /// silently measures the DESKTOP number instead and calls it green.
    ///
    /// <see cref="EnsureCascade(VisualElement)"/> is the fix: load the cascade sheets (palette /
    /// font / theme / design-tokens + every extra sheet registered by downstream packages, size
    /// ladders included) onto the host's root, the same way a real app does. It calls only
    /// <see cref="SusBootstrap.EnsureTokenCascade"/> (sheets only) — NOT
    /// <see cref="SusBootstrap.LoadTokenCascade"/> — because several existing hosts pin a FIXED
    /// `.breakpoint-*` class on the root to reproduce one exact viewport regardless of the
    /// panel's real pixel size (e.g. `SusShopScreenContentGeometryTests` forces `breakpoint-sm`
    /// on hosts built at 692×605 and 375×812 alike, T-1199); `LoadTokenCascade` would additionally
    /// attach <c>SusBreakpointService</c>, which recomputes the class from the root's own
    /// measured width and would silently override that forced class. Loading sheets only leaves
    /// class assignment exactly where each host already puts it (a literal
    /// <c>AddToClassList("breakpoint-xl")</c>, or nothing — some components self-tag from their
    /// OWN width, e.g. <c>SusHudRoot.SyncContainerBreakpoint</c>, and only need the sheet present
    /// on an ancestor to have their custom-property overrides take effect).
    ///
    /// No dependency on any downstream package: this lives in core and calls only the core
    /// cascade. Extra sheets registered by downstream packages arrive because each one already
    /// registers itself into
    /// <see cref="SusBootstrap.RegisterCascadeStyleSheet"/> from its own
    /// <c>InitializeOnLoadMethod</c>/<c>RuntimeInitializeOnLoadMethod</c> before any editor test
    /// code runs — the same registration a shipped app relies on (ARCH-20260907-DIMENSION-TOKENS
    /// §4.2.1, D-24).
    /// </summary>
    public static class SusCascadeTestHost
    {
        /// <summary>
        /// Loads the design-token cascade sheets onto <paramref name="root"/> (idempotent —
        /// safe to call more than once on the same element). Does not touch breakpoint/density
        /// classes; does not attach SusBreakpointService/SusDensityService/OverlayHost. Use this
        /// on the element every downstream component in the host resolves its cascade root to
        /// (usually the <c>UIDocument</c>'s <c>rootVisualElement</c>).
        /// </summary>
        public static void EnsureCascade(VisualElement root) => SusBootstrap.EnsureTokenCascade(root);

        /// <summary>
        /// <see cref="EnsureCascade(VisualElement)"/> plus an explicit, test-controlled
        /// `.breakpoint-*` (and optional `.density-*`) class on <paramref name="root"/> — the
        /// common case for a fixed-viewport repro host that wants a specific breakpoint
        /// regardless of the panel's measured pixel size. Pass the bare class name
        /// (e.g. <c>"breakpoint-sm"</c>, <c>"density-compact"</c>), not a selector.
        /// </summary>
        public static void EnsureCascade(VisualElement root, string breakpointClass, string densityClass = null)
        {
            EnsureCascade(root);
            if (root == null) return;
            if (!string.IsNullOrEmpty(breakpointClass)) root.AddToClassList(breakpointClass);
            if (!string.IsNullOrEmpty(densityClass)) root.AddToClassList(densityClass);
        }
    }
}
