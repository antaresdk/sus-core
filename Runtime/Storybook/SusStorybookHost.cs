using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Controls;   // zone D, card T-3034
using Sharq.Core.Storybook.Nav;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// The one storybook shell (plan §1, §6.1 step 3). A plain <see cref="VisualElement"/>, so it
    /// mounts into a UIDocument, an Editor window or a screenshot rig without a MonoBehaviour in
    /// between; <see cref="SusStorybookBehaviour"/> is the convenience driver for the scene case.
    ///
    /// Zones (brief §4): A navigation and C stage — built here (the selected story is
    /// instantiated and mounted into the canvas, with an <see cref="OverlayHost"/> of its own so
    /// popups stay inside the canvas, T-3032); D controls — built by <see cref="BuildControls"/>
    /// (card T-3034); B environment is <see cref="SusStoryEnvBar"/> (card T-3036) — chips over
    /// core services, no story-specific state and no reset of D's controls on a click (plan §4.4).
    ///
    /// Names kept from the pre-engine shell (plan D11) so the 85 driver call sites in
    /// <c>sus-dev</c> keep compiling while stories migrate: <see cref="ShowStoryById"/>,
    /// <see cref="QaCanvas"/>, <see cref="LastRegisteredStoryIds"/>,
    /// <see cref="ParseStoryIdFromAbsoluteUrl"/>.
    /// </summary>
    public sealed class SusStorybookHost : VisualElement, IDisposable
    {
        /// <summary>Panel width below which zone A becomes a drawer (card T-3033, mock-up).</summary>
        public const float NarrowWidth = 800f;

        /// <summary>Panel width at and above which zone B shows the deep-link (card T-3036, mock-up).</summary>
        public const float WideWidth = 1200f;

        /// <summary>How long the share button says "copied" (mock-up: 1.6 s).</summary>
        public const long ShareFeedbackMs = 1600;

        /// <summary>How often the stage looks at its overlay host (card T-3038).</summary>
        public const long OverlayWatchMs = 120;

        const string ShareLabel = "share";
        const string ShareDoneLabel = "copied";

        readonly SusStoryNavPanel _nav = new();
        readonly SusStoryHistory _history = new();
        readonly SusStoryUrl _url;

        readonly Label _crumbs = new();
        readonly Label _address = new();
        readonly Button _share;
        readonly SusIconElement _shareIcon = new("link");
        readonly Label _shareLabel = new(ShareLabel);
        readonly Button _burger;
        readonly VisualElement _scrim = new();
        readonly SusStoryEnvBar _env;

        readonly VisualElement _zoneEnv = new();
        readonly VisualElement _zonePanel = new();
        readonly VisualElement _zoneProbe = new();
        readonly ScrollView _stage = new();
        // Zone C (card T-3038).
        readonly Label _stageCrumbs = new();
        readonly Label _stageTitle = new();
        readonly Label _stagePurpose = new();
        // Zone C, card T-3038.
        readonly SusStoryMatrix _matrix = new();
        readonly Label _liveHint = new();
        readonly VisualElement _canvas = new();
        readonly SusStorySizes _sizes = new();
        readonly VisualElement _stageEmpty = new();
        readonly Label _stageEmptyTitle = new();
        readonly Label _stageEmptyText = new();

        OverlayHost _canvasOverlay;
        SusControlPanel _controls;
        IVisualElementScheduledItem _shareReset;
        IVisualElementScheduledItem _overlayWatch;   // card T-3038
        SusComponent _current;
        bool _disposed;

        public SusStorybookHost(StyleSheet styleSheet = null)
        {
            AddToClassList("sus-sb");
            if (styleSheet != null) styleSheets.Add(styleSheet);

            // ── top bar ──────────────────────────────────────────────────
            var top = new VisualElement();
            top.AddToClassList("sus-sb__topbar");

            _burger = new Button(ToggleDrawer) { text = "≡" };
            _burger.AddToClassList("sus-sb__burger");

            _crumbs.AddToClassList("sus-sb__crumbs");

            top.Add(_burger);
            top.Add(_crumbs);

            // ── body: zone A + main ──────────────────────────────────────
            var body = new VisualElement();
            body.AddToClassList("sus-sb__body");

            _scrim.AddToClassList("sus-sb__scrim");
            _scrim.RegisterCallback<PointerDownEvent>(_ => CloseDrawer());

            var main = new VisualElement();
            main.AddToClassList("sus-sb-main");

            var center = new VisualElement();
            center.AddToClassList("sus-sb-center");

            // Zone B — environment (plan §4.4, card T-3036): chip group left, deep-link and share
            // right. The deep-link label and the share button are the SAME instances the T-3033
            // scaffold put in the top bar — moved here, not duplicated (card text: "уже есть в
            // каркасе — переиспользуй, не дублируй"). Share stops setting Button.text directly so
            // narrow mode can hide the label and keep only the icon (mock-up, card T-3036).
            _zoneEnv.name = "sus-storybook-zone-b";
            _zoneEnv.AddToClassList("sus-sb-zone");
            _zoneEnv.AddToClassList("sus-sb-env");

            _env = new SusStoryEnvBar(this, _canvas);
            _env.Changed += RefreshAddress;

            _address.AddToClassList("sus-sb__link");
            _address.AddToClassList("sus-sb-env__link");

            _share = new Button(ShareCurrentAddress);
            _share.AddToClassList("sus-sb__share");
            _share.AddToClassList("sus-sb-env__share");
            _shareIcon.AddToClassList("sus-sb-env__share-icon");
            _shareLabel.AddToClassList("sus-sb-env__share-label");
            _share.Add(_shareIcon);
            _share.Add(_shareLabel);

            _zoneEnv.Add(_env);
            _zoneEnv.Add(_address);
            _zoneEnv.Add(_share);

            // Zone C — stage (card T-3038): crumbs, header, state matrix, live instance on a
            // dotted canvas, live measurements.
            _stage.name = "sus-storybook-zone-c";
            _stage.AddToClassList("sus-sb-stage");
            _stageCrumbs.AddToClassList("sus-sb-stage__crumbs");
            _stageTitle.AddToClassList("sus-sb-stage__title");
            _stagePurpose.AddToClassList("sus-sb-stage__purpose");
            _liveHint.text = "live instance · driven by the props panel";
            _liveHint.AddToClassList("sus-sb-stage__live-hint");
            _canvas.name = "sus-storybook-canvas";
            _canvas.AddToClassList("sus-sb-stage__canvas");

            _stageEmpty.AddToClassList("sus-sb-stage__empty");
            _stageEmptyTitle.AddToClassList("sus-sb-stage__empty-title");
            _stageEmptyText.AddToClassList("sus-sb-stage__empty-text");
            _stageEmpty.Add(_stageEmptyTitle);
            _stageEmpty.Add(_stageEmptyText);

            // Zone C order, card T-3038.
            _stage.Add(_stageCrumbs);
            _stage.Add(_stageTitle);
            _stage.Add(_stagePurpose);
            _stage.Add(_matrix);
            _stage.Add(_liveHint);
            _stage.Add(_canvas);
            _stage.Add(_sizes);
            _stage.Add(_stageEmpty);

            center.Add(_zoneEnv);
            center.Add(_stage);

            // Zone E — probe (empty slot, step 6).
            _zoneProbe.name = "sus-storybook-zone-e";
            _zoneProbe.AddToClassList("sus-sb-zone");
            _zoneProbe.AddToClassList("sus-sb-probe");
            center.Add(_zoneProbe);

            // Zone D — the control panel, built from the mounted story (card T-3034).
            _zonePanel.name = "sus-storybook-zone-d";
            _zonePanel.AddToClassList("sus-sb-zone");
            _zonePanel.AddToClassList("sus-sb-panel");
            _zonePanel.Add(SlotHint("zone D — no story mounted"));

            main.Add(center);
            main.Add(_zonePanel);

            body.Add(_nav);
            body.Add(main);
            body.Add(_scrim);

            Add(top);
            Add(body);

            // ── wiring ───────────────────────────────────────────────────
            _nav.StorySelected += story => Navigate(new SusStoryRoute(story.Id));
            _nav.PackageSelected += _ => CloseDrawer();

            _history.Changed += ApplyRoute;

            _url = new SusStoryUrl();
            _url.ExternalChanged += route =>
            {
                // Marked fromUrl by SusStoryUrl, so ApplyRoute will not push it back (§4.6).
                _history.Go(route);
            };

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            ShowStoryFromUrlOrFirst();
        }

        // ── contract kept from the pre-engine shell (D11) ────────────────────

        /// <summary>
        /// The element a story is mounted into — what screenshot and QA drivers grab.
        /// Name preserved from the pre-engine shell.
        /// </summary>
        public VisualElement QaCanvas => _canvas;

        /// <summary>Ids of every registered story, in zone A order. Name preserved.</summary>
        public IReadOnlyList<string> LastRegisteredStoryIds => SusStoryRegistry.LastRegisteredStoryIds;

        /// <summary>
        /// Story id the page was opened with, or null. Name and behaviour preserved; the
        /// implementation now lives in <see cref="SusStoryUrl"/>.
        /// </summary>
        public static string ParseStoryIdFromAbsoluteUrl() => SusStoryUrl.ParseStoryIdFromAbsoluteUrl();

        /// <summary>
        /// Shows a story by id and records it in history. Returns false when the id is unknown —
        /// the shell then says so instead of blanking the stage, because "nothing happened" and
        /// "that story does not exist" are different answers to a broken link.
        /// </summary>
        public bool ShowStoryById(string id)
        {
            if (SusStoryRegistry.Find(id) == null)
            {
                ShowMissing(id);
                return false;
            }
            Navigate(new SusStoryRoute(id));
            return true;
        }

        // ── navigation ───────────────────────────────────────────────────────

        /// <summary>Back / forward stack of the shell (plan §4.6).</summary>
        public SusStoryHistory History => _history;

        /// <summary>Address bar bridge (real in WebGL, an internal field elsewhere).</summary>
        public SusStoryUrl Url => _url;

        /// <summary>Zone A, exposed so a step-4 panel can read the selection.</summary>
        public SusStoryNavPanel Nav => _nav;

        /// <summary>Zone B — the environment chip group (<see cref="SusStoryEnvBar"/>, card T-3036).</summary>
        public VisualElement ZoneEnvironment => _zoneEnv;

        /// <summary>The environment bar itself, exposed so a test can drive a chip directly.</summary>
        public SusStoryEnvBar Env => _env;

        /// <summary>Zone D slot — the generated control panel lands here in step 4.</summary>
        public VisualElement ZoneControls => _zonePanel;

        /// <summary>Zone E slot — the probe lands here in step 6.</summary>
        public VisualElement ZoneProbe => _zoneProbe;

        /// <summary>Story currently mounted, or null.</summary>
        public SusStoryEntry CurrentStory { get; private set; }

        /// <summary>Zone C state matrix (card T-3038).</summary>
        public SusStoryMatrix Matrix => _matrix;

        /// <summary>Zone C live-measurement line (card T-3038).</summary>
        public SusStorySizes Sizes => _sizes;

        /// <summary>
        /// The stage's OWN overlay host — where a story's popup must land (T-3032). Null until a
        /// story is mounted.
        /// </summary>
        public OverlayHost CanvasOverlay => _canvasOverlay;

        /// <summary>
        /// Re-reads the stage overlay and mirrors its state into zone C: the canvas grows so a
        /// popup is not clipped out of the frame, and the bottom line says where the popup went.
        /// Called on a timer while a story is mounted; public so a test can ask for it directly
        /// instead of waiting for the scheduler.
        /// </summary>
        public void SyncOverlay()
        {
            bool open = _canvasOverlay != null && _canvasOverlay.Count > 0;
            _canvas.EnableInClassList("sus-sb-stage__canvas--overlay", open);
            _sizes.SetOverlayOpen(open);
        }

        /// <summary>Goes back one route; false when there is nowhere to go.</summary>
        public bool Back() => _history.Back();

        /// <summary>Goes forward one route; false when there is nowhere to go.</summary>
        public bool Forward() => _history.Forward();

        /// <summary>Navigates to a route, pushing it onto the history.</summary>
        public void Navigate(SusStoryRoute route) => _history.Go(route);

        void ShowStoryFromUrlOrFirst()
        {
            var route = SusStoryUrl.ParseRouteFromAbsoluteUrl();
            if (route != null && SusStoryRegistry.Find(route.StoryId) != null)
            {
                _history.Go(route);
                return;
            }

            var stories = SusStoryRegistry.Stories;
            if (stories.Count > 0)
            {
                _history.Go(new SusStoryRoute(stories[0].Id));
                return;
            }

            // No stories at all: still draw the shell, and say why it is empty.
            ApplyRoute(null);
        }

        void ApplyRoute(SusStoryRoute route)
        {
            if (_disposed) return;

            // env.* entries apply to their axis and leave the route BEFORE anything renders, so a
            // story mounted from a shared link comes up already in the environment it was shared
            // from (plan §4.4: "чтение из адреса при старте"). What Mount/BuildControls see below
            // is the clean route — control props only, exactly like before this card.
            route = _env.ConsumeFromRoute(route);

            // The address bar is written only for routes that did NOT come from it (§4.6 loop
            // guard). SusStoryUrl.Push enforces the same rule; asking twice is cheap and makes
            // the intention readable at the call site. WithEnv puts env.* BACK for display/share
            // only (plan §4.4: "поделиться" must reproduce the environment).
            if (route != null)
            {
                var withEnv = WithEnv(route);
                if (!route.FromUrl) _url.Push(withEnv);
                _address.text = withEnv.ToHash();
            }
            else
            {
                _address.text = _url.Address;
            }

            var entry = route == null ? null : SusStoryRegistry.Find(route.StoryId);

            // Highlight comes FROM the route, never from the click (§4.6).
            _nav.ActiveStoryId = entry?.Id;
            _nav.Rebuild();

            if (entry == null)
            {
                Unmount();
                CurrentStory = null;
                if (route == null) ShowNoStories();
                else ShowMissing(route.StoryId);
                return;
            }

            CurrentStory = entry;
            _crumbs.text = entry.Package + " / " + entry.Group + " / " + entry.Name;
            _stageCrumbs.text = _crumbs.text;   // zone C header, card T-3038
            _stageTitle.text = entry.Name;
            _stagePurpose.text = entry.Purpose;
            Mount(entry, route);
        }

        void Mount(SusStoryEntry entry, SusStoryRoute route)
        {
            Unmount();

            SusComponent component;
            SusStoryContext story;   // card T-3034: zone D reads the story's ledger
            try
            {
                story = entry.Instantiate(route, out component);
            }
            catch (Exception e)
            {
                SusLog.Error("[storybook] story '" + entry.Id + "' failed to build: " + e);
                ShowBuildFailure(entry, e);
                return;
            }

            _current = component;
            _canvas.Add(component);

            // The story's popups belong to the canvas, not to the panel root (T-3032).
            _canvasOverlay = SusBootstrap.GetOrCreateOverlay(_canvas);

            // Zone D is derived from the mounted instance and from nothing else (card T-3034).
            BuildControls(entry, component, story, route);

            // Zone C, card T-3038.
            _matrix.Show(entry);
            _sizes.Track(component);

            // UI Toolkit raises no event when an overlay gains a child, so the stage looks. The
            // tick is cheap (two class flips) and stops with the story.
            _overlayWatch?.Pause();
            _overlayWatch = schedule.Execute(SyncOverlay).Every(OverlayWatchMs);
            SyncOverlay();

            SetStageEmpty(false);
        }

        /// <summary>The generated control panel of zone D, or null while nothing is mounted.</summary>
        public SusControlPanel Controls => _controls;

        void BuildControls(SusStoryEntry entry, SusComponent component, SusStoryContext story, SusStoryRoute route)
        {
            _zonePanel.Clear();
            _controls = new SusControlPanel(component, entry.Name, story, route);
            _controls.ValueChanged += _ => OnControlValueChanged();
            _zonePanel.Add(_controls);
        }

        // A control write must not remount the story - that would throw away the very value just
        // set - so the address is rewritten in place while history keeps holding stories only.
        void OnControlValueChanged()
        {
            if (_disposed || _controls == null || CurrentStory == null) return;
            var route = _controls.BuildRoute(CurrentStory.Id);
            var withEnv = WithEnv(route);
            _address.text = withEnv.ToHash();
            _url.Push(withEnv);
        }

        // ── environment (zone B, card T-3036) ─────────────────────────────────

        /// <summary>
        /// <paramref name="route"/>'s query plus <c>env.*</c> for every axis away from its
        /// default (plan §4.4). Never mutates <paramref name="route"/> — env state lives in the
        /// core services themselves (<see cref="SusStoryEnvBar"/> reads them live), not in any
        /// <see cref="SusStoryRoute"/>, so switching stories never resets it.
        /// </summary>
        SusStoryRoute WithEnv(SusStoryRoute route)
        {
            var deltas = _env.CurrentDeltas();

            // A route read back from History can still carry env.* from whatever link it was
            // opened with (History stores routes as-is — see SusStoryHistory.Go) even after the
            // environment moved on since. Never trust old env.* in route.Query; only fresh
            // CurrentDeltas() may contribute one.
            bool routeCarriesEnv = false;
            foreach (var key in route.Query.Keys)
            {
                if (!key.StartsWith(SusStoryEnvBar.EnvQueryPrefix, StringComparison.Ordinal)) continue;
                routeCarriesEnv = true;
                break;
            }
            if (deltas.Count == 0 && !routeCarriesEnv) return route;

            var merged = new Dictionary<string, string>();
            foreach (var kv in route.Query)
            {
                if (kv.Key.StartsWith(SusStoryEnvBar.EnvQueryPrefix, StringComparison.Ordinal)) continue;
                merged[kv.Key] = kv.Value;
            }
            foreach (var kv in deltas) merged[kv.Key] = kv.Value;
            return route.WithQuery(merged);
        }

        /// <summary>
        /// Re-derives the address text from the CURRENT route and the environment, without
        /// touching history — a chip click never navigates. Wired to
        /// <see cref="SusStoryEnvBar.Changed"/> so "поделиться" is never stale by one click.
        /// </summary>
        void RefreshAddress()
        {
            var route = _history.Current;
            if (route == null || route.FromUrl)
            {
                _address.text = _url.Address;
                return;
            }
            var withEnv = WithEnv(route);
            _url.Push(withEnv);
            _address.text = withEnv.ToHash();
        }

        void Unmount()
        {
            if (_controls != null)
            {
                _controls.Dispose();
                _controls.RemoveFromHierarchy();
                _controls = null;
            }
            if (_zonePanel.childCount == 0) _zonePanel.Add(SlotHint("zone D — no story mounted"));
            // Zone C, card T-3038.
            _overlayWatch?.Pause();
            _overlayWatch = null;
            _matrix.Clear();
            _sizes.Track(null);
            _sizes.SetOverlayOpen(false);
            if (_canvasOverlay != null) _canvasOverlay.ClearAll();
            if (_current != null)
            {
                _current.RemoveFromHierarchy();
                _current = null;
            }
            _canvas.Clear();
            _canvasOverlay = null;
        }

        void ShowNoStories()
        {
            _crumbs.text = string.Empty;
            _stageTitle.text = string.Empty;
            _stagePurpose.text = string.Empty;
            _stageEmptyTitle.text = "No stories registered";
            _stageEmptyText.text =
                "No loaded assembly carries [assembly: SusStoryAssembly]. " +
                "Import a story sample, or mark the assembly that declares your stories.";
            SetStageEmpty(true);
        }

        void ShowMissing(string id)
        {
            _stageEmptyTitle.text = "Story not found";
            _stageEmptyText.text = "'" + id + "' is not in the registry. The link may point at a " +
                                  "story whose package is not loaded in this build.";
            SetStageEmpty(true);
        }

        void ShowBuildFailure(SusStoryEntry entry, Exception e)
        {
            _stageEmptyTitle.text = "Story failed to build";
            _stageEmptyText.text = entry.Id + ": " + e.Message;
            SetStageEmpty(true);
        }

        // Visibility is a USS class, not a C# style write: R53/R120 judge this assembly now that
        // it lives in Runtime, and "state through classes" is the house rule either way.
        void SetStageEmpty(bool empty)
        {
            _stageEmpty.EnableInClassList("sus-sb-hidden", !empty);
            _canvas.EnableInClassList("sus-sb-hidden", empty);
            _stageTitle.EnableInClassList("sus-sb-hidden", empty);
            _stagePurpose.EnableInClassList("sus-sb-hidden", empty);
            // Zone C, card T-3038.
            _stageCrumbs.EnableInClassList("sus-sb-hidden", empty);
            _liveHint.EnableInClassList("sus-sb-hidden", empty);
            _sizes.EnableInClassList("sus-sb-hidden", empty);
            if (empty) _matrix.Clear();
        }

        // ── chrome ───────────────────────────────────────────────────────────

        void ShareCurrentAddress()
        {
            bool ok = _url.CopyToClipboard();
            _shareLabel.text = ok ? ShareDoneLabel : ShareLabel;
            if (!ok) return;

            _shareReset?.Pause();
            _shareReset = schedule.Execute(() => _shareLabel.text = ShareLabel).StartingIn(ShareFeedbackMs);
        }

        void ToggleDrawer() => EnableInClassList("sus-sb--drawer-open", !ClassListContains("sus-sb--drawer-open"));

        void CloseDrawer() => RemoveFromClassList("sus-sb--drawer-open");

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float width = evt.newRect.width;

            bool narrow = width > 0 && width < NarrowWidth;
            if (narrow != ClassListContains("sus-sb--narrow"))
            {
                EnableInClassList("sus-sb--narrow", narrow);
                if (!narrow) CloseDrawer();
            }

            // Zone B's deep-link only fits a host wide enough to show it without crowding the
            // chips (card T-3036, mock-up: "деролинк... только ≥1200").
            EnableInClassList("sus-sb--wide", width >= WideWidth);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            // Ctrl+K / Cmd+K focuses the search field of zone A (mock-up "поиск стори ⌘K").
            if (evt.keyCode == UnityEngine.KeyCode.K && (evt.ctrlKey || evt.commandKey))
            {
                if (ClassListContains("sus-sb--narrow")) EnableInClassList("sus-sb--drawer-open", true);
                _nav.FocusSearch();
                evt.StopPropagation();
                return;
            }

            // Alt+Left / Alt+Right mirror the browser buttons (§4.6).
            if (evt.altKey && evt.keyCode == UnityEngine.KeyCode.LeftArrow)
            {
                if (Back()) evt.StopPropagation();
            }
            else if (evt.altKey && evt.keyCode == UnityEngine.KeyCode.RightArrow)
            {
                if (Forward()) evt.StopPropagation();
            }
        }

        static Label SlotHint(string text)
        {
            var l = new Label(text);
            l.AddToClassList("sus-sb-slot-hint");
            return l;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _shareReset?.Pause();
            _overlayWatch?.Pause();   // card T-3038
            _history.Changed -= ApplyRoute;
            _env.Changed -= RefreshAddress;
            _env.Dispose();
            _url.Dispose();
            Unmount();
        }
    }
}
