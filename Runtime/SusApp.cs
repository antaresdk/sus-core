using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Unified bootstrap facade for SUS applications — the single documented entry point,
    /// replacing the three ad-hoc patterns that used to coexist:
    ///   A. Single component — <c>ApplyDefaultTSS</c> + <c>Mount&lt;T&gt;</c>;
    ///   B. Router sample — <c>LoadTokenCascade</c> + <c>Router.Mount</c>;
    ///   C. Demo app — <c>ApplyDefaultTSS</c> + custom USS + <c>Router.Mount</c> (no cascade → bug).
    ///
    /// <see cref="SusApp"/> is a thin fluent builder over <see cref="SusBootstrap"/> that
    /// guarantees the correct, hard-to-remember initialization ORDER:
    ///   panel TSS → EventSystem → token cascade → fixed layer scaffold
    ///   (WorldMarkerLayer → ScreenHost → OverlayHost) → world-space panel
    ///   → custom/brand styles → configure hooks (router/manual) → mount → theme (last,
    ///   so the OverlayHost carries the theme class).
    ///
    /// The scaffold is ALWAYS built on the UIDocument root, in exactly this composition and
    /// z-order (see <c>EnsureScaffold</c>): world markers stay under screens, overlays stay on
    /// top, and all app content (Mount&lt;T&gt; / router) mounts into the middle <see cref="ScreenHost"/>.
    ///
    /// After <see cref="Run"/> / <see cref="Mount{T}"/>, world UI mounts via
    /// <see cref="WorldSpaceService.BindToWorld"/> onto the auto-created
    /// <see cref="SusWorldSpacePanel"/> (<see cref="WorldPanel"/>).
    /// Opt out with <see cref="UseWorldSpace"/>(false) for pure 2D apps.
    ///
    /// The core builder is navigation-agnostic. Router wiring is added by sus-router via a
    /// <c>UseRouter(...)</c> extension method (keeps core decoupled from router).
    ///
    /// Usage:
    /// <code>
    /// // Single component
    /// SusApp.Create(uiDocument).Mount&lt;HomeScreen&gt;();
    ///
    /// // Manual UI
    /// SusApp.Create(uiDocument)
    ///       .UseTheme(SusTheme.Dark)
    ///       .Configure(root => BuildUi(root))
    ///       .Run();
    ///
    /// // Router app (needs sus-router)
    /// SusApp.Create(uiDocument)
    ///       .UseTheme(SusTheme.Dark)
    ///       .UseCustomStyles("SusRuntime/demo-tokens")
    ///       .UseRouter(router, r => r.Register("/", typeof(HomeScreen)), initialPath: "/")
    ///       .Run();
    /// </code>
    /// </summary>
    public sealed class SusApp
    {
        private readonly VisualElement _root;
        private readonly UIDocument _document;

        private SusTheme _theme = SusTheme.Dark;
        private SusFontAsset _fontAsset;
        private bool _useTokenCascade = true;
        private bool _useWorldSpace = true;
        private readonly List<string> _customStyles = new();
        private readonly List<ISusIconProvider> _iconProviders = new();
        private readonly List<Action<VisualElement>> _configures = new();
        private bool _finalized;
        private SusWorldSpacePanel _worldPanel;
        private WorldMarkerLayer _markerLayer;
        private ScreenHost _screenHost;
        private OverlayHost _overlayHost;

        private SusApp(VisualElement root, UIDocument document)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _document = document;
        }

        /// <summary>Root element the app mounts into.</summary>
        public VisualElement Root => _root;

        /// <summary>The backing UIDocument, if created from one (else null).</summary>
        public UIDocument Document => _document;

        /// <summary>
        /// The fixed scaffold layers built on <see cref="Root"/> after <see cref="Run"/>/<see cref="Mount{T}"/>,
        /// in guaranteed z-order: <see cref="WorldMarkers"/> (lowest) → <see cref="ScreenHost"/> (app
        /// content) → <see cref="Overlay"/> (topmost popups/modals). Null before finalization.
        /// </summary>
        public ScreenHost ScreenHost => _screenHost;

        /// <summary>Lowest scaffold layer: screen-space world markers (below screens). Null before finalization.</summary>
        public WorldMarkerLayer WorldMarkers => _markerLayer;

        /// <summary>Topmost scaffold layer: portal for popups/tooltips/modals/toasts. Null before finalization.</summary>
        public OverlayHost Overlay => _overlayHost;

        /// <summary>
        /// World-space panel created during finalization (null if
        /// <see cref="UseWorldSpace"/>(false) or not yet finalized / not playing).
        /// Healthbars and other world UI mount here via <see cref="WorldSpaceService"/>.
        /// </summary>
        public SusWorldSpacePanel WorldPanel => _worldPanel;

        // ─── Create ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates an app bound to a <see cref="UIDocument"/>. Applies the SUS default TSS
        /// to the document's PanelSettings (creating one if absent), then targets its
        /// <c>rootVisualElement</c>. Hides the <c>ApplyDefaultTSS(UIDocument)</c> pitfall.
        /// </summary>
        public static SusApp Create(UIDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            SusBootstrap.ApplyDefaultTSS(document);
            var root = document.rootVisualElement;
            root.style.flexGrow = 1f;
            return new SusApp(root, document);
        }

        /// <summary>
        /// Creates an app bound to an arbitrary container (advanced — when you manage
        /// PanelSettings / TSS yourself). No default TSS is applied.
        /// </summary>
        public static SusApp Create(VisualElement root) => new SusApp(root, null);

        // ─── Configuration (fluent) ──────────────────────────────────────

        /// <summary>Sets the theme applied last (default <see cref="SusTheme.Dark"/>).</summary>
        public SusApp UseTheme(SusTheme theme)
        {
            _theme = theme;
            return this;
        }

        /// <summary>
        /// Enables/disables the design-token cascade (L1–L5 + responsive services).
        /// Enabled by default — disabling is only for fully custom setups. Disabling it is
        /// what silently broke pattern C (missing <c>--sk-*</c> variables).
        /// </summary>
        public SusApp UseTokenCascade(bool enabled = true)
        {
            _useTokenCascade = enabled;
            return this;
        }

        /// <summary>
        /// Ensures a <see cref="SusWorldSpacePanel"/> and wires
        /// <see cref="WorldSpaceService.Default"/> so world UI
        /// (<c>BindToWorld</c>, healthbars, nameplates) has a known mount target.
        /// Enabled by default. Disable for pure screen-space apps.
        /// </summary>
        public SusApp UseWorldSpace(bool enabled = true)
        {
            _useWorldSpace = enabled;
            return this;
        }

        /// <summary>
        /// Layers brand/custom stylesheets on the root AFTER the token cascade (core L1–L3 +
        /// registered L4/L5), so they form the TOP override layer and can redefine
        /// <c>--base-*/--thm-*/--sus-*</c> and registered L4 custom properties. Only the
        /// variables they declare are overridden — the rest keeps cascading from the layers
        /// below (e.g. bump <c>--sus-font-size-body</c> without touching spacing/shape).
        /// Applied to the root AND the OverlayHost, so overrides reach popups/tooltips/modals
        /// too. Paths are full Resources paths (e.g. "SusRuntime/demo-tokens").
        /// </summary>
        public SusApp UseCustomStyles(params string[] resourcePaths)
        {
            if (resourcePaths != null)
                foreach (var p in resourcePaths)
                    if (!string.IsNullOrEmpty(p)) _customStyles.Add(p);
            return this;
        }

        /// <summary>
        /// Registers custom icon providers as the HIGHEST priority, so their icons override
        /// the built-in Phosphor set (project SVGs / an icon-set provider win). This is the
        /// "icons" axis of the 3-asset rebrand story — it wires the provider abstraction from
        /// <see cref="SusIconRegistry.RegisterProvider"/> into the fluent app setup. Registered
        /// during finalization, before mount, so components resolve icons from them.
        /// </summary>
        public SusApp UseIcons(params ISusIconProvider[] providers)
        {
            if (providers != null)
                foreach (var p in providers)
                    if (p != null) _iconProviders.Add(p);
            return this;
        }

        /// <summary>
        /// Applies a custom <see cref="SusFontAsset"/> as the "fonts" axis of the rebrand story.
        /// Sets the inherited <c>-unity-font-definition</c> (body typeface) on the root and
        /// OverlayHost, overriding the default Montserrat. Call once, before Run/Mount.
        /// </summary>
        public SusApp UseFonts(SusFontAsset fontAsset)
        {
            _fontAsset = fontAsset;
            return this;
        }

        /// <summary>
        /// Applies a custom <see cref="SusIconSetAsset"/> as the "icons" axis of the rebrand story.
        /// Wraps the asset in <see cref="AssetIconProvider"/> and registers it as highest-priority
        /// so project icons override the built-in Phosphor set. Convenience overload for
        /// <c>UseIcons(new AssetIconProvider(iconSet))</c>.
        /// </summary>
        public SusApp UseIcons(SusIconSetAsset iconSet)
        {
            if (iconSet != null)
                _iconProviders.Add(new AssetIconProvider(iconSet));
            return this;
        }

        /// <summary>
        /// Sets the process-wide <see cref="SusLog.Level"/> (call before <see cref="Run"/> /
        /// <see cref="Mount{T}"/>). Does not depend on scaffold finalization.
        /// When scripting define <c>SUS_VERBOSE_LOGS</c> is present, levels below Verbose
        /// are ignored (define floor).
        /// </summary>
        public SusApp UseLogLevel(SusLogLevel level)
        {
            SusLog.Level = level;
            return this;
        }

        /// <summary>
        /// Registers a callback run against the root during finalization, after the cascade
        /// and custom styles but before the theme is applied. Escape hatch for manual UI and
        /// for the sus-router <c>UseRouter</c> extension. Multiple callbacks run in order.
        /// </summary>
        public SusApp Configure(Action<VisualElement> configure)
        {
            if (configure != null) _configures.Add(configure);
            return this;
        }

        // ─── Finalization ────────────────────────────────────────────────

        /// <summary>
        /// Finalizes bootstrap WITHOUT a root component (router / manual-UI apps).
        /// Returns the root element.
        /// </summary>
        public VisualElement Run() => Finalize(null);

        /// <summary>
        /// Finalizes bootstrap and mounts a single root component (no router).
        /// Returns the created component.
        /// </summary>
        public T Mount<T>() where T : SusComponent, new()
        {
            T created = null;
            // Cascade + OverlayHost stay on the root; the component mounts into the ScreenHost slot.
            Finalize(() => created = SusBootstrap.MountInto<T>(_root, _screenHost));
            return created;
        }

        private VisualElement Finalize(Action mountAction)
        {
            if (_finalized)
                throw new InvalidOperationException("SusApp already finalized (Run/Mount called twice).");
            _finalized = true;

            // UI Toolkit needs an EventSystem for input; Mount<T> ensures it too, but the
            // router/manual paths would otherwise miss it.
            SusBootstrap.EnsureEventSystem();

            // Custom icon providers first (highest priority) so any component mounted below
            // resolves overridden icons. Order preserved: later-added = higher priority.
            foreach (var provider in _iconProviders)
                SusIconRegistry.RegisterProvider(provider, asHighestPriority: true);

            // Token cascade (L1–L5 + breakpoint/density/scale + OverlayHost). Idempotent, so
            // a later Mount<T> re-attempt is a no-op and preserves custom-style ordering.
            if (_useTokenCascade)
                SusBootstrap.LoadTokenCascade(_root);

            // Build the fixed three-layer scaffold on the root, in guaranteed z-order:
            //   WorldMarkerLayer (lowest) → ScreenHost (app content) → OverlayHost (topmost).
            // Everything else mounts into ScreenHost; world markers/overlays never cross.
            EnsureScaffold();

            // World-space panel (separate UIDocument under screens) + Default service.
            // Only while playing — edit-mode unit tests use Create(VisualElement).Run().
            // The screen-space host for the variant-B fallback is the WorldMarkerLayer built above
            // (UNDER screens) — never the OverlayHost, so world markers can't paint over UI.
            if (_useWorldSpace && Application.isPlaying)
                _worldPanel = SusBootstrap.EnsureWorldSpacePanel(Camera.main, _markerLayer);

            // Font override — after cascade, before custom styles. Sets the inherited
            // -unity-font-definition (body typeface) on root + OverlayHost.
            if (_fontAsset != null)
            {
                SusFontService.ApplyFonts(_root, _fontAsset);
                SusFontService.ApplyToOverlayHost(_root, _fontAsset);
            }

            // Brand/custom overrides AFTER the cascade — the TOP layer.
            // Applied to the root AND the OverlayHost so overrides (typography, spacing,
            // a few brand colors) also reach popups/tooltips/modals reparented into the
            // overlay. Overlay children inherit the overridden --sk-*/--sus-* via the DOM
            // cascade from the host, so a single add per host covers its whole subtree.
            if (_customStyles.Count > 0)
            {
                foreach (var path in _customStyles)
                {
                    SusBootstrap.AddStyleSheet(_root, path);
                    if (_overlayHost != null)
                        SusBootstrap.AddStyleSheet(_overlayHost, path);
                }
            }

            // Router registration/mount + manual UI construction.
            foreach (var cfg in _configures)
                cfg(_root);

            // Component mount (creates OverlayHost via cascade if not already present).
            mountAction?.Invoke();

            // Theme LAST: the OverlayHost created during cascade/mount must carry the theme
            // class for var(--thm-*/--sk-*) to resolve on popups/tooltips/modals.
            SusThemeService.Instance.SetTheme(_root, _theme);

            return _root;
        }

        /// <summary>
        /// Builds (idempotently) the fixed three-layer scaffold on <see cref="Root"/> and normalizes
        /// their z-order structurally — no reliance on add-order heuristics:
        /// <code>
        /// root
        /// ├── WorldMarkerLayer   ← SendToBack   (lowest: world markers under screens)
        /// ├── ScreenHost         ← after marker (app content: Mount&lt;T&gt; / router)
        /// └── OverlayHost        ← BringToFront  (topmost: popups/modals/tooltips)
        /// </code>
        /// Always present in exactly this composition, regardless of world-space / cascade options.
        /// </summary>
        private void EnsureScaffold()
        {
            _markerLayer = SusBootstrap.GetOrCreateWorldMarkerLayer(_root);
            _screenHost = SusBootstrap.GetOrCreateScreenHost(_root);
            _overlayHost = SusBootstrap.GetOrCreateOverlay(_root);

            _markerLayer.SendToBack();
            _screenHost.PlaceInFront(_markerLayer);
            _overlayHost.BringToFront();
        }
    }
}
