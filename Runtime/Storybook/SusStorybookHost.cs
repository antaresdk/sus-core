using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Controls;   // zone D, card T-3034
using Sharq.Core.Storybook.Nav;
using Sharq.Core.Storybook.Probe;   // zone E, card T-3040
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
        readonly SusStoryProbe _probe = new();   // zone E, card T-3040
        // Zone C scrolls VERTICALLY only (card T-3708, plan ARCH-20260922-STORYBOOK-CANVAS-
        // VIEWPORT.md, decision D1). It scrolled on both axes for one wave (card T-3389, plan
        // ARCH-20260911-STORYBOOK-SHELL.md §4.7) so a subject wider than the canvas would be
        // reachable at all, but that made the CANVAS itself the thing that scrolled sideways, and
        // a canvas that is max-width:100% against a content container of automatic width grows
        // with it instead of staying put (measured 880 -> 1806 for a 1760px-wide element,
        // T-3389). D1 moves the horizontal scroll to a viewport INSIDE the canvas
        // (<see cref="_canvasViewport"/>): the stage itself never needs to reach sideways again,
        // so it is back to the one axis its own content (crumbs, title, matrix, canvas) actually
        // uses.
        readonly ScrollView _stage = new(ScrollViewMode.Vertical);
        // Zone C (card T-3038).
        readonly Label _stageCrumbs = new();
        readonly Label _stageTitle = new();
        readonly Label _stagePurpose = new();
        // Zone C, card T-3038.
        readonly SusStoryMatrix _matrix = new();
        readonly Label _liveHint = new();
        readonly VisualElement _canvas = new();
        // The canvas's own two-axis viewport (card T-3708, decision D1): first in-flow child of
        // _canvas, holds the subject and any scene pieces a story declared. _canvas itself keeps
        // ONE declared box (D18) and never scrolls or grows; this is what scrolls in its place.
        readonly ScrollView _canvasViewport = new(ScrollViewMode.VerticalAndHorizontal);
        readonly SusStorySizes _sizes = new();
        readonly VisualElement _stageEmpty = new();
        readonly Label _stageEmptyTitle = new();
        readonly Label _stageEmptyText = new();

        OverlayHost _canvasOverlay;
        // What the CURRENT story asked the stage to put beside its component (card T-3168): the
        // engine parented these, so the engine — and nothing else — takes them away again.
        readonly List<VisualElement> _scene = new();
        SusControlPanel _controls;
        IVisualElementScheduledItem _shareReset;
        IVisualElementScheduledItem _overlayWatch;   // card T-3038
        IVisualElementScheduledItem _probeTick;      // card T-3358: zone E, its own slower tick
        bool _overlayOpen;
        SusComponent _current;
        // The element the CURRENT story declared through SusStoryContext.SetHost (card T-3905), or
        // null when it declared none — the stage's own reference to what Unmount must take back
        // by reference, same reasoning as _scene above.
        VisualElement _currentHost;
        bool _disposed;

        public SusStorybookHost(StyleSheet styleSheet = null)
        {
            AddToClassList("sb-shell");
            if (styleSheet != null) styleSheets.Add(styleSheet);

            // ── top bar ──────────────────────────────────────────────────
            var top = new VisualElement();
            top.AddToClassList("sb-shell__topbar");

            _burger = new Button(ToggleDrawer) { text = "≡" };
            _burger.AddToClassList("sb-shell__burger");

            _crumbs.AddToClassList("sb-shell__crumbs");

            top.Add(_burger);
            top.Add(_crumbs);

            // ── body: zone A + main ──────────────────────────────────────
            var body = new VisualElement();
            body.AddToClassList("sb-shell__body");

            _scrim.AddToClassList("sb-shell__scrim");
            _scrim.RegisterCallback<PointerDownEvent>(_ => CloseDrawer());

            var main = new VisualElement();
            main.AddToClassList("sb-main");

            var center = new VisualElement();
            center.AddToClassList("sb-center");

            // Zone B — environment (plan §4.4, card T-3036): chip group left, deep-link and share
            // right. The deep-link label and the share button are the SAME instances the T-3033
            // scaffold put in the top bar — moved here, not duplicated (card text: "already in
            // the scaffold — reuse it, do not duplicate"). Share stops setting Button.text
            // directly so
            // narrow mode can hide the label and keep only the icon (mock-up, card T-3036).
            _zoneEnv.name = "sus-storybook-zone-b";
            _zoneEnv.AddToClassList("sb-zone");
            _zoneEnv.AddToClassList("sb-env");

            // T-4101: previewRoot is the STAGE subtree (D27, plan ARCH-20260911-STORYBOOK-SHELL.md:
            // "the environment axis applies to the STAGE SUBTREE, not the shell root"), not the
            // canvas alone. The matrix
            // (_matrix, zone C) is a SIBLING of the canvas under _stage, not a descendant of it —
            // binding previewRoot to _canvas left every matrix cell outside every axis's reach: a
            // live measurement (ux-reviewer 2026-09-24) found the "theme" chip reading "light"
            // while all 14 matrix cells stayed on the dark background var() resolves to above the
            // stage. _stage is the element the mock-up actually means by "the stage" — matrix and
            // canvas both live under it, so an axis bound there reaches both.
            _env = new SusStoryEnvBar(this, _stage);
            _env.Changed += RefreshAddress;
            // An environment axis (breakpoint, density, theme, scale, input) re-lays out the
            // stage, so it is one of D17's named occasions for zone E (card T-3358).
            _env.Changed += () => { _probe.MarkDirty(); _sizes.Refresh(); };

            _address.AddToClassList("sb-shell__link");
            _address.AddToClassList("sb-env__link");

            _share = new Button(ShareCurrentAddress);
            _share.AddToClassList("sb-shell__share");
            _share.AddToClassList("sb-env__share");
            _shareIcon.AddToClassList("sb-env__share-icon");
            _shareLabel.AddToClassList("sb-env__share-label");
            _share.Add(_shareIcon);
            _share.Add(_shareLabel);

            _zoneEnv.Add(_env);
            _zoneEnv.Add(_address);
            _zoneEnv.Add(_share);

            // Zone C — stage (card T-3038): crumbs, header, state matrix, live instance on a
            // dotted canvas, live measurements.
            _stage.name = "sus-storybook-zone-c";
            _stage.AddToClassList("sb-stage");
            // The STAGE is the cascade root of its own (card T-3400, plan D27; corrected T-4101 —
            // see the comment on _env above for why _canvas alone was the wrong element). Without
            // the declaration SusThemeService.ResolveCascadeRoot answers with
            // SusBootstrap.TokenCascadeRoot — in the live storybook the UIDocument root, ABOVE the
            // shell — so every service routed through it (theme here, and anything else that
            // resolves a root from inside the stage) painted the whole instrument instead of the
            // subject on display. Declared here rather than only in the axis, so it holds for
            // every caller that resolves a root from inside the stage, not only for the chip that
            // was measured.
            SusThemeService.MarkScopedCascadeRoot(_stage);
            _stageCrumbs.AddToClassList("sb-stage__crumbs");
            _stageTitle.AddToClassList("sb-stage__title");
            _stagePurpose.AddToClassList("sb-stage__purpose");
            _liveHint.text = "live instance · driven by the props panel";
            _liveHint.AddToClassList("sb-stage__live-hint");
            _canvas.name = "sus-storybook-canvas";
            _canvas.AddToClassList("sb-stage__canvas");

            // Card T-3708, decision D1: the canvas's own two-axis viewport, first in-flow child
            // of the canvas. contentContainer gets its own class (AddToClassList, not a style-API
            // write — R53/R120) so a subject without its own width still stretches to the
            // canvas's width (plan §6 risk row) exactly as it did when the canvas held it
            // directly.
            _canvasViewport.name = "sus-storybook-canvas-viewport";
            _canvasViewport.AddToClassList("sb-stage__viewport");
            _canvasViewport.contentContainer.AddToClassList("sb-stage__viewport-content");
            _canvas.Add(_canvasViewport);

            _stageEmpty.AddToClassList("sb-stage__empty");
            _stageEmptyTitle.AddToClassList("sb-stage__empty-title");
            _stageEmptyText.AddToClassList("sb-stage__empty-text");
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

            // Card T-3708 (was card T-3389): what the size line compares against, so that a
            // subject reaching past what the reader can see is SAID and not left to be noticed.
            // Two questions, two witnesses, and since D1 both are asked of the CANVAS, not the
            // stage: downwards the subject against the canvas, which keeps one declared height
            // (D18); sideways the subject's own content against the canvas viewport it scrolls
            // inside of (decision D1) — the canvas viewport's content container is, in practice,
            // sized to the mounted subject, the same relationship StageContent/StageViewport used
            // to have with the whole of zone C before the viewport moved inside the canvas.
            _sizes.Canvas = _canvas;
            _sizes.StageContent = _canvasViewport.contentContainer;
            _sizes.StageViewport = _canvasViewport.contentViewport;

            center.Add(_zoneEnv);
            center.Add(_stage);

            // Zone E — probe (card T-3040): event feed, health of the stage canvas, frame verdict.
            _zoneProbe.name = "sus-storybook-zone-e";
            _zoneProbe.AddToClassList("sb-zone");
            _zoneProbe.AddToClassList("sb-probe");
            _zoneProbe.Add(_probe);
            center.Add(_zoneProbe);

            // Zone D — the control panel, built from the mounted story (card T-3034).
            _zonePanel.name = "sus-storybook-zone-d";
            _zonePanel.AddToClassList("sb-zone");
            _zonePanel.AddToClassList("sb-panel");
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

            // The anomaly count of zone E is the one shown at the bottom of zone A (card T-3040):
            // a reader scanning the story list must see that this story is broken without
            // looking down at the strip.
            _probe.AnomalyCountChanged += count => _nav.AnomalyCount = count;
            // Card T-3483: zone E reports what zone C measured. One wire, set once - the matrix
            // outlives every story, so there is nothing to re-attach on a mount.
            _probe.CellSource = _matrix;

            _url = new SusStoryUrl();
            _url.ExternalChanged += route =>
            {
                // Marked fromUrl by SusStoryUrl, so ApplyRoute will not push it back (§4.6).
                _history.Go(route);
            };

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // A finished layout pass of the CANVAS is the fourth named occasion of D17: a story
            // that collapses to zero size does it during layout and raises nothing else (cards
            // T-3358, T-3362). Registered on the canvas and not on the mounted instance on
            // purpose — the canvas is a fixture of the shell, so the subscription survives every
            // story switch and needs no re-registration.
            _canvas.RegisterCallback<GeometryChangedEvent>(OnCanvasGeometryChanged);

            ShowStoryFromUrlOrFirst();
        }

        // ── contract kept from the pre-engine shell (D11) ────────────────────

        /// <summary>
        /// The element a story is mounted into — what screenshot and QA drivers grab.
        /// Name preserved from the pre-engine shell.
        /// </summary>
        public VisualElement QaCanvas => _canvas;

        /// <summary>
        /// Where a story's subject and scene actually mount (card T-3708, decision D1): the
        /// content container of <see cref="_canvasViewport"/>, the canvas's own two-axis viewport.
        /// <see cref="QaCanvas"/> stays the fixed box a compare judges; this is one level narrower
        /// — what <c>QaCanvas.Children()</c> used to answer before the viewport existed.
        /// </summary>
        public VisualElement QaSubjectRoot => _canvasViewport.contentContainer;

        /// <summary>
        /// The element the current story declared through <see cref="SusStoryContext.SetHost"/>
        /// (card T-3905), or null when the mounted story declared none. QA's ready-made hook onto
        /// the engine-placed host — same purpose as <see cref="QaCanvas"/>, one level narrower.
        /// </summary>
        public VisualElement QaSubjectHost => _currentHost;

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

        /// <summary>Zone E slot — holds <see cref="Probe"/>.</summary>
        public VisualElement ZoneProbe => _zoneProbe;

        /// <summary>Zone E itself: event feed, health count and frame verdict (card T-3040).</summary>
        public SusStoryProbe Probe => _probe;

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
        /// Re-reads the stage overlay and tells zone C where a popup went. Called on the
        /// <see cref="OverlayWatchMs"/> timer while a story is mounted, because UI Toolkit raises
        /// no event when an overlay host gains a child; public so a test can ask for it directly
        /// instead of waiting for the scheduler.
        ///
        /// Two things this used to do and no longer does.
        ///
        /// It used to GROW THE CANVAS (<c>sb-stage__canvas--overlay</c>, min-height 120 → 350
        /// in the shell sheet): every popover, tooltip and menu opening on the stage moved the
        /// canvas and everything under it by 230 px, and closing moved it back — the single most
        /// visible source of "all the elements jump" (card T-3362). It was done deliberately, so
        /// a screenshot would catch the popup; plan ARCH-20260911-STORYBOOK-SHELL §4.7 and D18
        /// decide that fork the other way: the canvas has ONE declared height, and the popup is
        /// kept in the frame by positioning the overlay host inside the canvas — which is what
        /// <c>SusBootstrap.GetOrCreateOverlay(_canvas)</c> already does (T-3032). Frames of one
        /// address then stay comparable between runs, which a canvas of two heights never was.
        ///
        /// It used to REFRESH ZONE E, so the probe inherited this tick: 8,3 canvas walks and
        /// three text rewrites a second, unconditionally (card T-3358). Zone E now has its own
        /// <see cref="SusStoryProbe.RefreshIntervalMs"/> tick over a dirty flag, and this poll
        /// only raises that flag — and only when the overlay state actually changed.
        /// </summary>
        public void SyncOverlay()
        {
            bool open = _canvasOverlay != null && _canvasOverlay.Count > 0;
            if (open == _overlayOpen) return;

            _overlayOpen = open;
            _sizes.SetOverlayOpen(open);
            _probe.MarkDirty();
        }

        /// <summary>
        /// Honours a pending zone E refresh, if one is pending (card T-3358). The seam a test
        /// drives instead of waiting <see cref="SusStoryProbe.RefreshIntervalMs"/>; returns
        /// whether the strip was actually re-read.
        /// </summary>
        public bool RefreshProbe()
        {
            // The live-size line rides this tick too (card T-3362, D16): it lost its own
            // subscription to the geometry of the element it measures, because that subscription
            // WAS the loop. One throttled reader for both, so the two can never disagree about
            // how often the stage is allowed to be re-read.
            bool sizes = _sizes.RefreshIfStale();
            bool probe = _probe.RefreshIfDirty();
            return probe || sizes;
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

        /// <summary>
        /// Query key that turns the engine BENCHES on from the address (card T-3410):
        /// <c>#/enginetests/showcase/bogus?fixtures=1</c>. The stage opens a fixture by direct
        /// address with or without it; this key is about the LISTING - tabs, tree and search.
        /// </summary>
        public const string FixtureQueryKey = "fixtures";

        static bool AsksForFixtures(SusStoryRoute route)
        {
            if (route == null) return false;
            if (!route.Query.TryGetValue(FixtureQueryKey, out var v)) return false;
            return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
        }

        void ApplyRoute(SusStoryRoute route)
        {
            if (_disposed) return;

            // The address is the second way into the benches (card T-3410, D22). Only ever ON:
            // see SusStoryRegistry.FixturesRequestedByAddress for why the way back belongs to the
            // editor toggle and not to the next click.
            if (AsksForFixtures(route)) SusStoryRegistry.FixturesRequestedByAddress = true;

            // env.* entries apply to their axis and leave the route BEFORE anything renders, so a
            // story mounted from a shared link comes up already in the environment it was shared
            // from (plan §4.4: "read from the address at start"). What Mount/BuildControls see
            // below
            // is the clean route — control props only, exactly like before this card.
            route = _env.ConsumeFromRoute(route);

            // The address bar is written only for routes that did NOT come from it (§4.6 loop
            // guard). SusStoryUrl.Push enforces the same rule; asking twice is cheap and makes
            // the intention readable at the call site. WithEnv puts env.* BACK for display/share
            // only (plan §4.4: "share" must reproduce the environment).
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
            // Card T-3905: a story that declared a host gets the HOST in the place the instance
            // would otherwise have taken, with the instance parented last inside the host's slot;
            // a story that declared none mounts byte-for-byte as before (plan D6).
            // Card T-3708, decision D1: the subject mounts into the canvas's OWN viewport, not
            // the canvas directly — the canvas stays the fixed box; the viewport is what scrolls.
            if (story.Host != null)
            {
                _currentHost = story.Host;
                story.Slot.Add(component);
                _canvasViewport.Add(story.Host);
            }
            else
            {
                _canvasViewport.Add(component);
            }
            MountScene(story, story.Host ?? component);

            // The story's popups belong to the canvas, not to the panel root (T-3032), and stay
            // on the canvas rather than the viewport (decision D2): a host parked on a ScrollView
            // must sit BESIDE the scrolled content so it is never scrolled away or wiped by a
            // ClearContent, which is what SusBootstrap.FindOverlayHost already assumes.
            _canvasOverlay = SusBootstrap.GetOrCreateOverlay(_canvas);

            // Zone D is derived from the mounted instance and from nothing else (card T-3034).
            BuildControls(entry, component, story, route);

            // Zone C, card T-3038.
            _matrix.Show(entry);
            _sizes.Track(component);

            // Zone E, card T-3040: the feed subscribes to every declared event of THIS instance
            // and forgets the previous story's session. Zone D's panel rides along (card T-3143)
            // so the session report can read props/controls/exclusions/manual controls without
            // plumbing SusStoryContext a second time; when the panel is still waiting on the
            // mount signal (T-3096) it comes in null here and BuildControls hands it over later.
            _probe.Attach(entry, component, _canvas, _controls);

            // UI Toolkit raises no event when an overlay gains a child, so the stage looks. The
            // tick is cheap (one bool compare, then one class flip on a change) and stops with
            // the story.
            _overlayWatch?.Pause();
            _overlayWatch = schedule.Execute(SyncOverlay).Every(OverlayWatchMs);
            _overlayOpen = false;
            SyncOverlay();

            // Zone E on its OWN tick, four times slower, and over a dirty flag (card T-3358,
            // D17). Health cannot be subscribed to, but every occasion on which it could change
            // can be named — a mount, a prop write, an environment axis, a finished layout pass —
            // and this tick is the floor under how often those are honoured.
            _probeTick?.Pause();
            _probeTick = schedule.Execute(() => RefreshProbe()).Every(SusStoryProbe.RefreshIntervalMs);
            _probe.MarkDirty();

            SetStageEmpty(false);
        }

        /// <summary>
        /// Parents the scenery the story declared through <see cref="SusStoryContext.AddSibling"/>
        /// (card T-3168) — trigger buttons, demo stages, captions — around the mounted instance,
        /// and remembers each one so <see cref="Unmount"/> can take back exactly what it gave.
        ///
        /// Doing it here, once, is the whole point: the stories that used to do it themselves had
        /// to wait for <c>AttachToPanelEvent</c> to have a parent at all, and that event fires
        /// again every time the component re-attaches — including when it teleports into an
        /// <see cref="OverlayHost"/> to open. Each of those re-fires inserted another copy, in a
        /// place the story never meant and no teardown could reach.
        ///
        /// <paramref name="anchor"/> is the element actually sitting in
        /// <see cref="_canvasViewport"/> (card T-3708, decision D1) — the instance itself, or,
        /// when the story declared a host (card T-3905), that host: the scenery goes beside
        /// whatever occupies the instance's place in the viewport, not beside an instance buried
        /// inside a host it does not sit in (plan D3). <c>Insert</c>/<c>IndexOf</c>/<c>childCount</c>
        /// on a ScrollView route to its content container the same way <c>Add</c> already does, so
        /// no extra indirection is needed here.
        /// </summary>
        void MountScene(SusStoryContext story, VisualElement anchor)
        {
            var scene = story?.Scene;
            if (scene == null) return;

            for (int i = 0; i < scene.Count; i++)
            {
                var piece = scene[i];
                if (piece?.Element == null) continue;

                int at = _canvasViewport.IndexOf(anchor);
                if (at < 0) at = _canvasViewport.childCount;
                else if (piece.After) at += 1;

                if (at > _canvasViewport.childCount) at = _canvasViewport.childCount;
                _canvasViewport.Insert(at, piece.Element);
                _scene.Add(piece.Element);
            }
        }

        /// <summary>The generated control panel of zone D, or null while nothing is mounted.</summary>
        public SusControlPanel Controls => _controls;

        /// <summary>
        /// Zone D, but NOT before the story has actually mounted (T-3096). Every control snapshots
        /// <c>SusPropInfo.Dead</c> when it is built, and <c>Mounted()</c> — where a component
        /// registers the <c>Watch</c>es of its content props — runs a frame LATER than the
        /// constructor. Building the panel inside <see cref="Mount"/> therefore judged an
        /// unmounted instance and printed "dead props: Text · PrependIcon · AppendIcon · Icon" on
        /// a perfectly live SusButton (kadr kit-button.png, T-3041): four props whose only readers
        /// live in Mounted(), against thirteen that Build()'s :class bindings already read. The
        /// panel now waits for the mount signal, which is what the snapshot always assumed.
        /// </summary>
        void BuildControls(SusStoryEntry entry, SusComponent component, SusStoryContext story, SusStoryRoute route)
        {
            _zonePanel.Clear();

            if (!component.IsMounted)
            {
                _zonePanel.Add(SlotHint("zone D — waiting for the story to mount"));
                component.MountCompleted += () =>
                {
                    // The story may have been swapped while the frame passed.
                    if (_disposed || !ReferenceEquals(_current, component)) return;
                    BuildControls(entry, component, story, route);
                };
                return;
            }

            _controls = new SusControlPanel(component, entry.Name, story, route);
            _controls.ValueChanged += _ => OnControlValueChanged();
            _zonePanel.Add(_controls);

            // The deferred branch above builds the panel a frame after Attach() ran with panel
            // still null (T-3096); hand it to zone E now so the session report it writes on
            // Clear() sees real coverage instead of "panel was never built" (card T-3143).
            if (ReferenceEquals(_current, component)) _probe.Panel = _controls;
        }

        // A control write must not remount the story - that would throw away the very value just
        // set - so the address is rewritten in place while history keeps holding stories only.
        void OnControlValueChanged()
        {
            if (_disposed || _controls == null || CurrentStory == null) return;

            // A prop write can change the health of the canvas and the live-size line; both are
            // named occasions of D17 rather than things with an event of their own (T-3358/T-3362).
            _probe.MarkDirty();
            _sizes.Refresh();

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
        /// <see cref="SusStoryEnvBar.Changed"/> so "share" is never stale by one click.
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
            _probeTick?.Pause();       // card T-3358
            _probeTick = null;
            _overlayOpen = false;
            // Zone E FIRST, zone C second (card T-3483). Clear() is where the probe hands the
            // finished session to the sinks, and the cell geometry it hands over lives in the
            // matrix - clearing the matrix first would have emptied the report of the very
            // evidence it exists to carry.
            _probe.Clear();   // card T-3040: reset on story change
            _matrix.Clear();
            _sizes.Track(null);
            _sizes.SetOverlayOpen(false);
            // T-3131: a story that opens an overlay before _canvasOverlay exists (ModalStory
            // sets Model=true from Configure(), which runs during entry.Instantiate() — BEFORE
            // Mount() adds the component to _canvas and calls GetOrCreateOverlay(_canvas)) never
            // resolves to _canvasOverlay at all. SusBootstrap.ResolveOverlayHost walks ancestors,
            // finds none yet, and falls back to panel.visualTree — the document root, an ANCESTOR
            // of this host, not a descendant — so the modal lands in a second, ROOT OverlayHost
            // that _canvasOverlay.ClearAll() alone never touches. Clear both, same two-step lookup
            // SusThemeService.SetTheme uses for the same host (descendant Q<>, then panel.visualTree
            // Q<>) — env axis state (breakpoint/density/theme/scale/input) is deliberately NOT
            // reset here: it is core-service state the env bar only reflects, not per-story data
            // (class doc above, plan §4.4).
            // Hosts FIRST, story elements second — the opposite of SusStoryMatrix.ClearCells, and
            // for a reason that only holds here (card T-3168). A self-teleporting component sits
            // in the host while its ORIGINAL parent, the canvas, is a fixture of the shell that
            // outlives every story: detaching the component before the host is emptied is a
            // dismissal, not a teardown, so SusOverlayComponent schedules a restore into that
            // still-living canvas and the previous story reappears there one frame later
            // (SusStoryMatrixOverlayTeardownTests catches exactly this). Going through
            // ClearAll first means the removal carries IsClearing, which is the signal that
            // suppresses the restore (T-3160). The matrix can afford the other order because a
            // cell's original parent is the grid it is about to throw away.
            ClearAllOverlayHosts();
            // Scenery is plain elements the engine parented (MountScene) — no restore logic
            // anywhere near them, so their turn comes after the hosts either way. Removed by
            // reference rather than by clearing the canvas, because a story's own code may have
            // moved a piece elsewhere in the panel.
            for (int i = 0; i < _scene.Count; i++) _scene[i]?.RemoveFromHierarchy();
            _scene.Clear();
            if (_current != null)
            {
                _current.RemoveFromHierarchy();
                _current = null;
            }
            // Card T-3905: the host comes off LAST of "its own", by reference — after the instance
            // it was carrying, same reasoning as the instance coming off after the hosts above (a
            // self-teleporting instance still targets the host as its ORIGINAL parent while
            // IsClearing is unset). The sweep below is the belt-and-suspenders catch-all for a
            // story with no host at all, and for anything this reference-based teardown missed —
            // same job the old unconditional `_canvas.Clear()` did, before the canvas held a
            // permanent viewport of its own (card T-3708, decision D1) that a blanket Clear()
            // would have taken down along with the story.
            if (_currentHost != null)
            {
                _currentHost.RemoveFromHierarchy();
                _currentHost = null;
            }
            _canvasViewport.contentContainer.Clear();
            for (int i = _canvas.hierarchy.childCount - 1; i >= 0; i--)
            {
                var child = _canvas.hierarchy.ElementAt(i);
                if (!ReferenceEquals(child, _canvasViewport)) child.RemoveFromHierarchy();
            }
            _canvasOverlay = null;
        }

        /// <summary>
        /// Empties every <see cref="OverlayHost"/> this shell can reach: the ones nested inside it
        /// (normally just <see cref="_canvasOverlay"/>) AND the one that may have been created on
        /// <c>panel.visualTree</c> — an ancestor of this host, not a descendant, and therefore
        /// invisible to any query rooted on <c>this</c> (T-3131). Leaves the hosts themselves in
        /// place (idempotent, matches <see cref="OverlayHost.ClearAll"/> semantics); only their
        /// content is torn down.
        /// </summary>
        void ClearAllOverlayHosts()
        {
            foreach (var host in this.Query<OverlayHost>().ToList()) host.ClearAll();
            var tree = panel?.visualTree;
            if (tree == null || ReferenceEquals(tree, this)) return;
            foreach (var host in tree.Query<OverlayHost>().ToList()) host.ClearAll();
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
            _stageEmpty.EnableInClassList("sb-hidden", !empty);
            _canvas.EnableInClassList("sb-hidden", empty);
            _stageTitle.EnableInClassList("sb-hidden", empty);
            _stagePurpose.EnableInClassList("sb-hidden", empty);
            // Zone C, card T-3038.
            _stageCrumbs.EnableInClassList("sb-hidden", empty);
            _liveHint.EnableInClassList("sb-hidden", empty);
            _sizes.EnableInClassList("sb-hidden", empty);
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

        void ToggleDrawer() => EnableInClassList("sb-shell--drawer-open", !ClassListContains("sb-shell--drawer-open"));

        void CloseDrawer() => RemoveFromClassList("sb-shell--drawer-open");

        // Card T-3362: this handler writes NOTHING that participates in layout. It raises the
        // dirty flag of zone E and asks the size line for a re-read; the size line is what used
        // to close the loop by re-measuring itself (D16), and it no longer does.
        void OnCanvasGeometryChanged(GeometryChangedEvent _)
        {
            if (_disposed) return;
            _probe.MarkDirty();
            _sizes.MarkStale();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float width = evt.newRect.width;

            bool narrow = width > 0 && width < NarrowWidth;
            if (narrow != ClassListContains("sb-shell--narrow"))
            {
                EnableInClassList("sb-shell--narrow", narrow);
                if (!narrow) CloseDrawer();
            }

            // Zone B's deep-link only fits a host wide enough to show it without crowding the
            // chips (card T-3036, mock-up: "deep-link... only ≥1200").
            EnableInClassList("sb-shell--wide", width >= WideWidth);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            // Ctrl+K / Cmd+K focuses the search field of zone A (mock-up "story search ⌘K").
            if (evt.keyCode == UnityEngine.KeyCode.K && (evt.ctrlKey || evt.commandKey))
            {
                if (ClassListContains("sb-shell--narrow")) EnableInClassList("sb-shell--drawer-open", true);
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
            l.AddToClassList("sb-slot-hint");
            return l;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _shareReset?.Pause();
            _overlayWatch?.Pause();   // card T-3038
            _probeTick?.Pause();      // card T-3358
            _history.Changed -= ApplyRoute;
            _env.Changed -= RefreshAddress;
            _env.Dispose();
            _url.Dispose();
            Unmount();
            _probe.Dispose();   // card T-3040 — after Unmount, so the last session still reports
        }
    }
}
