using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Bootstrap entry point for SUS applications.
    /// Analogue of Vue's createApp(App).mount('#app').
    ///
    /// Usage (from any MonoBehaviour):
    /// <code>
    /// public class MainMenuEntry : MonoBehaviour
    /// {
    ///     public UIDocument uiDocument;
    ///     void Start() => SusBootstrap.Mount&lt;MainMenuScreen&gt;(uiDocument);
    /// }
    /// </code>
    /// </summary>
    public static class SusBootstrap
    {
        /// <summary>Resources path prefix for all SUS stylesheets, icons, and fonts.</summary>
        public const string ResourcePath = "SusRuntime/";

        private static bool s_eventSystemEnsured;

        static VisualElement s_tokenCascadeRoot;

        /// <summary>
        /// VisualElement that received <see cref="LoadTokenCascade"/> / <see cref="Mount{T}(VisualElement)"/>.
        /// Theme / breakpoint / density classes and L1–L5 sheets live here — not on
        /// <c>panel.visualTree</c> (parent of <c>UIDocument.rootVisualElement</c>).
        /// Cleared automatically when the element leaves its panel.
        /// </summary>
        public static VisualElement TokenCascadeRoot
        {
            get
            {
                if (s_tokenCascadeRoot != null && s_tokenCascadeRoot.panel == null)
                    s_tokenCascadeRoot = null;
                return s_tokenCascadeRoot;
            }
            private set => s_tokenCascadeRoot = value;
        }

        /// <summary>
        /// Extra stylesheets appended to the design-token cascade after the core tokens
        /// (L4/L5). Downstream packages register their own token/style
        /// sheets here so core never hardcodes knowledge of packages built on top of it.
        /// Names are relative to <see cref="ResourcePath"/> (e.g. "my-tokens").
        /// </summary>
        private static readonly System.Collections.Generic.List<string> s_extraCascadeSheets = new();

        /// <summary>
        /// Registers an additional cascade stylesheet (resource name relative to
        /// <see cref="ResourcePath"/>, e.g. "my-tokens"). Appended after core tokens
        /// wherever the cascade is loaded (Mount, LoadTokenCascade, EnsureTokenCascade,
        /// OverlayHost). Idempotent. Call before the first Mount (e.g. from a
        /// [RuntimeInitializeOnLoadMethod] in the downstream package).
        /// </summary>
        public static void RegisterCascadeStyleSheet(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return;
            if (!s_extraCascadeSheets.Contains(resourceName))
                s_extraCascadeSheets.Add(resourceName);
        }

#if UNITY_EDITOR
        // With Domain Reload disabled these survive leaving Play Mode: a cascade root pointing
        // at a destroyed panel, an EventSystem flag for a scene that no longer exists, and a
        // consumer handler from the previous session.
        //
        // s_extraCascadeSheets is deliberately NOT cleared. Downstream packages register into it
        // from their own SubsystemRegistration hooks and the order between hooks of the same
        // load type is undefined, so clearing here could wipe a registration that already ran.
        // It holds resource names rather than objects from the old session, and re-registration
        // is idempotent, so keeping it costs nothing.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_eventSystemEnsured = false;
            s_tokenCascadeRoot = null;
            OnDuplicateMount = null;
        }
#endif

        /// <summary>Loads all registered extra cascade sheets (L4/L5) onto a container.</summary>
        private static void LoadExtraCascadeSheets(VisualElement container)
        {
            foreach (var name in s_extraCascadeSheets)
                TryLoadAndAdd(container, ResourcePath + name);
        }

        /// <summary>
        /// UI Toolkit requires an EventSystem + input module to process clicks/input.
        /// Idempotent. Public so the <see cref="SusApp"/> facade can guarantee it for
        /// router/manual apps that never go through <see cref="Mount{T}(VisualElement)"/>.
        /// </summary>
        public static void EnsureEventSystem()
        {
            var existing = UnityEngine.EventSystems.EventSystem.current;
            if (existing != null)
            {
                EnsureInputModule(existing.gameObject);
                s_eventSystemEnsured = true;
                return;
            }

            if (s_eventSystemEnsured)
            {
                // Flag set but EventSystem destroyed (domain / scene unload) — recreate.
            }

            var go = new GameObject("EventSystem (SusBootstrap)");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            EnsureInputModule(go);
            s_eventSystemEnsured = true;
        }

        /// <summary>
        /// EventSystem alone is not enough — without a BaseInputModule UITK/uGUI
        /// receive no pointer events (buttons look fine but never click).
        /// </summary>
        static void EnsureInputModule(UnityEngine.GameObject go)
        {
            if (go == null) return;
            if (go.GetComponent<UnityEngine.EventSystems.BaseInputModule>() != null) return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var inputSystemType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemType != null)
            {
                go.AddComponent(inputSystemType);
                return;
            }
#endif
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        /// <summary>
        /// Loads the design-token cascade onto a container.
        /// Order is strict: palette → fonts → theme → semantic → icons → registered extras (L4/L5).
        /// Downstream packages append their token/style sheets via
        /// <see cref="RegisterCascadeStyleSheet"/> — core has no hardcoded knowledge of them.
        ///
        /// _palette.uss and _font.uss duplicated here — container-level
        /// var() resolution doesn't reliably cross from panel TSS to container USS.
        /// _theme.uss, design-tokens.uss, _icon.uss are container-only.
        /// _theme.uss must be here because .theme-dark/.theme-light are on this container.
        /// </summary>
        private static void LoadDesignTokenCascade(VisualElement container)
        {
            TokenCascadeRoot = container;

            TryLoadAndAdd(container, ResourcePath + "_palette");     // L1 --base-* (needed by _theme)
            TryLoadAndAdd(container, ResourcePath + "_font");         // font defs (needed by design-tokens)
            TryLoadAndAdd(container, ResourcePath + "_theme");       // L2 --thm-*
            TryLoadAndAdd(container, ResourcePath + "design-tokens");// L3 --sus-*
            TryLoadAndAdd(container, ResourcePath + "_icon");        // icon utilities (needs --sus-*)
            LoadExtraCascadeSheets(container);                        // L4/L5 downstream tokens+styles

            // Resolution axis removed: .breakpoint-* only (driven like old
            // SusResolutionService — cascadeRoot.resolvedStyle.width on geometry).
            SusBreakpointService.Attach(container);

            SusDensityService.Attach(container);

            SusScaleService.Attach(container);

            // OverlayHost: global portal for popups/tooltips/modals — always last child.
            // GetOrCreateOverlay also EnsureLabelClassStrip(container + host).
            GetOrCreateOverlay(container);
        }

        /// <summary>
        /// Creates a root component of type T and mounts it into the given container.
        /// Automatically loads the design-token cascade onto the container (strict order):
        ///   1. _palette      — L1 --base-* raw palette (needed by _theme)
        ///   2. _font         — font families (needed by design-tokens)
        ///   3. _theme        — L2 --thm-* theme aliases (.theme-dark/.theme-light live here)
        ///   4. design-tokens — L3 --sus-* semantic tokens
        ///   5. _icon         — icon utilities (needs --sus-*)
        ///   6. registered extras — L4/L5 downstream package tokens+styles
        ///      (downstream UI packages register via RegisterCascadeStyleSheet)
        /// _palette + _font are ALSO applied at panel level via SusDefault.tss (with _global);
        /// they are duplicated here because var() doesn't reliably cross panel TSS → container USS.
        /// Theme variant switched via SusThemeService.Instance.SetTheme(root, theme).
        /// Returns the created component for further configuration.
        /// </summary>
        /// <summary>
        /// Optional fail-fast hook when <see cref="Mount{T}(VisualElement)"/> finds an existing
        /// instance of <typeparamref name="T"/> already under the container (duplicate creation).
        /// Wired by battle client to <c>BattleFailFast</c>.
        /// </summary>
        public static System.Action<string, string> OnDuplicateMount;

        public static T Mount<T>(VisualElement container) where T : SusComponent, new()
            => MountInto<T>(container, container);

        /// <summary>
        /// Loads the design-token cascade (and installs diagnostics) on
        /// <paramref name="cascadeContainer"/>, then mounts a new <typeparamref name="T"/> into
        /// <paramref name="mountTarget"/>. Splitting the two lets <see cref="SusApp"/> keep the
        /// cascade + OverlayHost on the UIDocument root while mounting screen content into the
        /// fixed <see cref="ScreenHost"/> slot below it. When both are the same element this is
        /// exactly <see cref="Mount{T}(VisualElement)"/>.
        /// </summary>
        public static T MountInto<T>(VisualElement cascadeContainer, VisualElement mountTarget)
            where T : SusComponent, new()
        {
            if (cascadeContainer == null)
                throw new System.ArgumentNullException(nameof(cascadeContainer));
            mountTarget ??= cascadeContainer;

            EnsureEventSystem();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Auto-install ClickAuditService on first Mount
            if (cascadeContainer.panel != null)
                Diagnostics.ClickAuditService.Instance.Install(cascadeContainer.panel);

            // Auto-install ScreenAudit hotkey (Ctrl+Shift+~) for screen dumps
            Diagnostics.ScreenAudit.InstallIfNeeded(cascadeContainer);
#endif

            // Load the design-token cascade in strict order (see roadmap/THEME_SYSTEM_WIRING.md R3).
            // Also creates the OverlayHost on the cascade container (root), never in the screen slot.
            LoadDesignTokenCascade(cascadeContainer);

            for (int i = 0; i < mountTarget.childCount; i++)
            {
                if (mountTarget[i] is T)
                {
                    OnDuplicateMount?.Invoke(typeof(T).Name, mountTarget.name ?? "?");
                    break;
                }
            }

            var app = new T();
            mountTarget.Add(app);
            return app;
        }

        /// <summary>
        /// Loads only the design-token cascade (stylesheets) onto the container
        /// without mounting a component. Use when you build UI manually
        /// and need --sus-* CSS variables and theme classes to work.
        /// </summary>
        public static void LoadTokenCascade(VisualElement container)
        {
            if (container == null) return;

            LoadDesignTokenCascade(container);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Auto-install ClickAuditService — warns when clicks are blocked by overlays
            if (container.panel != null)
                Diagnostics.ClickAuditService.Instance.Install(container.panel);
#endif
        }

        /// <summary>
        /// Loads token USS sheets on an overlay element (menu card, modal, tooltip)
        /// so var(--sk-*) resolves reliably after reparenting to OverlayHost.
        /// Skips services (Breakpoint, Density, Scale, OverlayHost).
        /// Idempotent per session — sheets are only added if not already present.
        /// </summary>
        public static void EnsureTokenCascade(VisualElement container)
        {
            if (container == null) return;
            TryLoadAndAdd(container, ResourcePath + "_palette");
            TryLoadAndAdd(container, ResourcePath + "_font");
            TryLoadAndAdd(container, ResourcePath + "_theme");
            TryLoadAndAdd(container, ResourcePath + "design-tokens");
            LoadExtraCascadeSheets(container);
        }

        /// <summary>
        /// Loads the SusDefault.tss theme and sets it on the UIDocument's PanelSettings.
        /// This REPLACES unity-theme://default with our TSS that imports our entire
        /// design-token cascade AFTER Unity's built-in defaults — so we keep layout
        /// (font-size, padding, controls) but override colors with our tokens.
        ///
        /// Call ONCE before Mount<T>() — panel-level theme loading.
        /// </summary>
        public static void ApplyDefaultTSS(UIDocument uiDocument)
        {
            if (uiDocument == null) return;

            var tss = UnityEngine.Resources.Load<UnityEngine.UIElements.ThemeStyleSheet>(ResourcePath + "SusDefault");
            if (tss == null)
            {
                SusLog.Warn("[SusBootstrap] SusDefault.tss not found in Resources/SusRuntime — using Unity default theme.");
                return;
            }

            var ps = uiDocument.panelSettings;
            if (ps == null)
            {
                SusLog.Warn(
                    "[SusBootstrap] UIDocument.panelSettings is null — creating fallback PanelSettings " +
                    "with SusDefault.tss. For production, assign a PanelSettings asset to the UIDocument " +
                    "in the scene to control resolution, scale mode, and theming.");
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.name = "SUS PanelSettings (auto-created)";
                ps.themeStyleSheet = tss;
                uiDocument.panelSettings = ps;
            }
            else
            {
                ps.themeStyleSheet = tss;
            }
        }

        private static void TryLoadAndAdd(VisualElement container, string resourcePath)
            => AddStyleSheet(container, resourcePath);

        /// <summary>
        /// Loads a StyleSheet from Resources and adds it to <paramref name="container"/>
        /// (idempotent — skips if already present). Returns true if the sheet exists.
        /// Public so <see cref="SusApp"/> can layer brand/custom sheets after the cascade.
        /// <paramref name="resourcePath"/> is a full Resources path (e.g. "SusRuntime/demo-tokens").
        /// </summary>
        public static bool AddStyleSheet(VisualElement container, string resourcePath)
        {
            if (container == null || string.IsNullOrEmpty(resourcePath)) return false;

            var sheet = UnityEngine.Resources.Load<StyleSheet>(resourcePath);
            if (sheet == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                SusLog.Warn($"[SusBootstrap] StyleSheet not found: {resourcePath}");
#endif
                return false;
            }

            if (!container.styleSheets.Contains(sheet))
                container.styleSheets.Add(sheet);
            return true;
        }

        /// <summary>
        /// Mounts into a UIDocument's rootVisualElement.
        /// Convenience overload.
        /// </summary>
        public static T Mount<T>(UIDocument uiDocument) where T : SusComponent, new()
        {
            if (uiDocument == null)
                throw new System.ArgumentNullException(nameof(uiDocument));
            return Mount<T>(uiDocument.rootVisualElement);
        }

        // ─── Overlay support ────────────────────────────────────────────────

        /// <summary>
        /// Strips Unity TSS <c>unity-*</c> classes from all <see cref="Label"/>s under
        /// <paramref name="root"/> and applies <c>sus-label</c> (see <c>_text.uss</c>).
        /// Idempotent. Called automatically by <see cref="LoadTokenCascade"/> /
        /// <see cref="Mount{T}(VisualElement)"/> / <see cref="GetOrCreateOverlay"/> —
        /// use this when a project boots manually (custom stylesheet list) and still
        /// needs theme tokens to win over Default Theme.
        /// </summary>
        public static void EnsureLabelClassStrip(VisualElement root)
        {
            SusLabelClassService.Attach(root);
        }

        /// <summary>
        /// Returns or creates an OverlayHost as the LAST child of the container.
        /// Last sibling = rendered on top (no z-index in USS). Idempotent.
        /// Also ensures <see cref="SusLabelClassService"/> on the container and host
        /// so manual bootstraps that skip <see cref="LoadTokenCascade"/> still strip
        /// <c>unity-*</c> label classes.
        /// </summary>
        public static OverlayHost GetOrCreateOverlay(VisualElement container)
        {
            if (container == null)
                throw new System.ArgumentNullException(nameof(container));

            // Manual bootstraps (e.g. project SusBootstrapper) often skip LoadTokenCascade
            // but always call GetOrCreateOverlay — attach stripper here so every consumer
            // gets unity-* label cleanup without duplicating the call.
            EnsureLabelClassStrip(container);

            // Walk the visual tree to find an existing OverlayHost.
            // GetOrCreateOverlay may be called with different container
            // references (UIDocument.root vs panel.visualTree) — we must
            // find the one already created, never duplicate.
            OverlayHost host = container.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            if (host != null)
            {
                host.BringToFront();
                host.InstallEscapeHandler();
                EnsureLabelClassStrip(host);
                return host;
            }

            host = new OverlayHost { name = OverlayHost.OverlayHostName };

            // Load global USS that overlay children may need (CSS vars + popup styles).
            // These are also on the root container, but USS cascading through reparented
            // elements can be unreliable in UITK — attach them directly to OverlayHost
            // so popups, tooltips, and modals always see them.
            TryLoadAndAdd(host, ResourcePath + "_palette");
            TryLoadAndAdd(host, ResourcePath + "_font");
            TryLoadAndAdd(host, ResourcePath + "_theme");
            TryLoadAndAdd(host, ResourcePath + "design-tokens");
            LoadExtraCascadeSheets(host);

            container.Add(host);
            host.InstallEscapeHandler();
            EnsureLabelClassStrip(host);
            // If panel was not ready at Add time, retry after attach.
            if (host.panel == null)
            {
                host.RegisterCallback<AttachToPanelEvent>(_ => host.InstallEscapeHandler());
            }
            return host;
        }

        /// <summary>
        /// Returns or creates the <see cref="WorldMarkerLayer"/> as the FIRST child of the
        /// container — the screen-space host for flat world markers (variant-B fallback of
        /// <see cref="WorldSpaceService"/>). First sibling = rendered UNDER screens and the
        /// <see cref="OverlayHost"/>, so world markers can never paint over screen UI. Idempotent;
        /// re-fetching sends the existing layer to the back so it stays lowest.
        /// </summary>
        public static WorldMarkerLayer GetOrCreateWorldMarkerLayer(VisualElement container)
        {
            if (container == null)
                throw new System.ArgumentNullException(nameof(container));

            var layer = container.Q<WorldMarkerLayer>(name: WorldMarkerLayer.LayerName);
            if (layer != null)
            {
                // Keep it below any screens/overlay added after creation.
                layer.SendToBack();
                return layer;
            }

            layer = new WorldMarkerLayer();
            // Insert as first child → lowest z-order, below screens and OverlayHost.
            container.Insert(0, layer);
            return layer;
        }

        /// <summary>
        /// Returns or creates the <see cref="ScreenHost"/> — the fixed middle layer of the
        /// <see cref="SusApp"/> scaffold and the single mount target for app content
        /// (<c>Mount&lt;T&gt;</c> component / router <c>SusRouteView</c>). Idempotent. Order among
        /// the three layers is normalized by <see cref="SusApp"/> (marker → screens → overlay);
        /// here it is simply placed above any existing <see cref="WorldMarkerLayer"/>.
        /// </summary>
        public static ScreenHost GetOrCreateScreenHost(VisualElement container)
        {
            if (container == null)
                throw new System.ArgumentNullException(nameof(container));

            var host = container.Q<ScreenHost>(name: ScreenHost.ScreenHostName);
            if (host != null)
                return host;

            host = new ScreenHost();
            var marker = container.Q<WorldMarkerLayer>(name: WorldMarkerLayer.LayerName);
            container.Add(host);
            // Keep screens above the world-marker layer (below the OverlayHost, which
            // BringToFront's itself as the last child).
            if (marker != null)
                host.PlaceInFront(marker);
            return host;
        }

        // ─── World-space panel ───────────────────────────────────────────────

        /// <summary>GameObject name for the auto-created world-space UIDocument.</summary>
        public const string WorldSpacePanelObjectName = "__SusWorldSpacePanel__";

        /// <summary>
        /// Finds or creates a <see cref="SusWorldSpacePanel"/> (separate UIDocument under
        /// all screen UI) and wires <see cref="WorldSpaceService.Default"/> so
        /// <c>BindToWorld</c> / healthbars know where to mount. Idempotent.
        /// Called automatically by <see cref="SusApp"/> unless <c>UseWorldSpace(false)</c>.
        /// </summary>
        /// <param name="camera">Billboard / projection camera; null → <see cref="Camera.main"/>.</param>
        /// <param name="markerLayer">
        /// Optional screen-space <see cref="WorldMarkerLayer"/> (below screens) kept on Default as
        /// the host for the variant-B flat-marker fallback. Never the OverlayHost — world markers
        /// must render under screen UI.
        /// </param>
        public static SusWorldSpacePanel EnsureWorldSpacePanel(
            Camera camera = null,
            VisualElement markerLayer = null)
        {
            var panel = FindExistingWorldSpacePanel();
            if (panel == null)
                panel = CreateWorldSpacePanel();

            camera ??= Camera.main;
            if (camera != null && panel.TargetCamera == null)
                panel.TargetCamera = camera;

            var root = panel.Root ?? panel.GetComponent<UIDocument>()?.rootVisualElement;
            if (root != null)
                EnsureTokenCascade(root);

            var svc = WorldSpaceService.Default;
            if (svc == null)
            {
                svc = new WorldSpaceService();
                WorldSpaceService.Default = svc;
            }

            if (camera != null)
                svc.MainCamera = camera;
            if (markerLayer != null)
                svc.MarkerLayer = markerLayer;

            svc.UseWorldSpacePanel(panel);
            return panel;
        }

        static SusWorldSpacePanel FindExistingWorldSpacePanel()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<SusWorldSpacePanel>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<SusWorldSpacePanel>(true);
#endif
        }

        static SusWorldSpacePanel CreateWorldSpacePanel()
        {
            var go = new GameObject(WorldSpacePanelObjectName);
            // Keep across scene loads when created at runtime from SusApp.
            UnityEngine.Object.DontDestroyOnLoad(go);

            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = CreateWorldSpacePanelSettings();
            // Draw under the screen UIDocument when both are overlay (WorldSpace mode
            // positions in 3D; lower sorting still helps hybrid / fallback setups).
            doc.sortingOrder = -100;

            var panel = go.AddComponent<SusWorldSpacePanel>();
            return panel;
        }

        /// <summary>
        /// Runtime PanelSettings for the world UIDocument. Stays in Unity's default render mode
        /// (Screen Space) until the FIRST element is actually attached — <see cref="SusWorldSpacePanel"/>
        /// switches it to WorldSpace lazily via <see cref="TrySetWorldSpaceRenderMode"/> at that
        /// point (T-645). <see cref="EnsureWorldSpacePanel"/> creates this panel unconditionally
        /// for every <see cref="SusApp"/> (unless <c>UseWorldSpace(false)</c>), so the common case —
        /// an app that never mounts world-space UI — must not pay for a live WorldSpace-mode panel
        /// that has zero attachments: on Unity 6000.3.17f1 an always-on, empty WorldSpace panel was
        /// found to trip the engine's internal "Access version should be odd when acquiring lock"
        /// assert continuously in Play mode (reproduced with SusWorldSpacePanel disabled → 0 asserts
        /// in 5s vs. continuous spam re-enabled, independent of any capture driver/MCP polling).
        /// </summary>
        public static PanelSettings CreateWorldSpacePanelSettings()
        {
            var existing = UnityEngine.Resources.Load<PanelSettings>(ResourcePath + "SusWorldPanelSettings");
            if (existing != null)
                return existing;

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "SusWorldPanelSettings (auto-created)";
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;

            var tss = UnityEngine.Resources.Load<ThemeStyleSheet>(ResourcePath + "SusDefault");
            if (tss != null)
                ps.themeStyleSheet = tss;

            return ps;
        }

        /// <summary>
        /// Sets PanelSettings.renderMode to WorldSpace via reflection so the package still
        /// compiles against Unity 6000.0 (API shipped in 6.2+). No-op if unavailable. Called
        /// lazily by <see cref="SusWorldSpacePanel"/> on first <c>AttachElement</c> (T-645) —
        /// NOT at panel creation time, so an app that never uses world-space UI never switches
        /// its always-on auto-created panel into WorldSpace mode.
        /// </summary>
        internal static void TrySetWorldSpaceRenderMode(PanelSettings ps)
        {
            if (ps == null) return;
            var prop = typeof(PanelSettings).GetProperty("renderMode");
            if (prop == null || !prop.CanWrite) return;
            var enumType = prop.PropertyType;
            try
            {
                var world = System.Enum.Parse(enumType, "WorldSpace");
                prop.SetValue(ps, world);
            }
            catch (System.Exception)
            {
                // Pre-6.2 or stripped player: panel stays Screen Space; SusWorldSpacePanel
                // LateUpdate still hosts elements for BindToWorld mounting.
            }
        }
    }
}
