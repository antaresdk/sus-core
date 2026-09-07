using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Nav;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// The one storybook shell (plan §1, §6.1 step 3). A plain <see cref="VisualElement"/>, so it
    /// mounts into a UIDocument, an Editor window or a screenshot rig without a MonoBehaviour in
    /// between; <see cref="SusStorybookBehaviour"/> is the convenience driver for the scene case.
    ///
    /// Zones (brief §4): A navigation — built here; B environment, D controls, E probe — EMPTY
    /// SLOTS with stable names and classes, filled by steps 4, 5 and 6; C stage — the minimum that
    /// makes the skeleton provable: the selected story is instantiated and mounted into the canvas,
    /// with an <see cref="OverlayHost"/> of its own so popups stay inside the canvas (T-3032).
    /// No matrix and no control panel here on purpose — those are cards T-3034 / T-3038.
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

        /// <summary>How long the share button says "copied" (mock-up: 1.6 s).</summary>
        public const long ShareFeedbackMs = 1600;

        const string ShareLabel = "share";
        const string ShareDoneLabel = "copied";

        readonly SusStoryNavPanel _nav = new();
        readonly SusStoryHistory _history = new();
        readonly SusStoryUrl _url;

        readonly Label _crumbs = new();
        readonly Label _address = new();
        readonly Button _share;
        readonly Button _burger;
        readonly VisualElement _scrim = new();

        readonly VisualElement _zoneEnv = new();
        readonly VisualElement _zonePanel = new();
        readonly VisualElement _zoneProbe = new();
        readonly ScrollView _stage = new();
        readonly Label _stageTitle = new();
        readonly Label _stagePurpose = new();
        readonly VisualElement _canvas = new();
        readonly VisualElement _stageEmpty = new();
        readonly Label _stageEmptyTitle = new();
        readonly Label _stageEmptyText = new();

        OverlayHost _canvasOverlay;
        IVisualElementScheduledItem _shareReset;
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
            _address.AddToClassList("sus-sb__link");

            var spacer = new VisualElement();
            spacer.AddToClassList("sus-sb__spacer");

            _share = new Button(ShareCurrentAddress) { text = ShareLabel };
            _share.AddToClassList("sus-sb__share");

            top.Add(_burger);
            top.Add(_crumbs);
            top.Add(spacer);
            top.Add(_address);
            top.Add(_share);

            // ── body: zone A + main ──────────────────────────────────────
            var body = new VisualElement();
            body.AddToClassList("sus-sb__body");

            _scrim.AddToClassList("sus-sb__scrim");
            _scrim.RegisterCallback<PointerDownEvent>(_ => CloseDrawer());

            var main = new VisualElement();
            main.AddToClassList("sus-sb-main");

            var center = new VisualElement();
            center.AddToClassList("sus-sb-center");

            // Zone B — environment bar (empty slot, step 5).
            _zoneEnv.name = "sus-storybook-zone-b";
            _zoneEnv.AddToClassList("sus-sb-zone");
            _zoneEnv.AddToClassList("sus-sb-env");
            _zoneEnv.Add(SlotHint("zone B — environment (step 5)"));

            // Zone C — stage.
            _stage.name = "sus-storybook-zone-c";
            _stage.AddToClassList("sus-sb-stage");
            _stageTitle.AddToClassList("sus-sb-stage__title");
            _stagePurpose.AddToClassList("sus-sb-stage__purpose");
            _canvas.name = "sus-storybook-canvas";
            _canvas.AddToClassList("sus-sb-stage__canvas");

            _stageEmpty.AddToClassList("sus-sb-stage__empty");
            _stageEmptyTitle.AddToClassList("sus-sb-stage__empty-title");
            _stageEmptyText.AddToClassList("sus-sb-stage__empty-text");
            _stageEmpty.Add(_stageEmptyTitle);
            _stageEmpty.Add(_stageEmptyText);

            _stage.Add(_stageTitle);
            _stage.Add(_stagePurpose);
            _stage.Add(_canvas);
            _stage.Add(_stageEmpty);

            center.Add(_zoneEnv);
            center.Add(_stage);

            // Zone E — probe (empty slot, step 6).
            _zoneProbe.name = "sus-storybook-zone-e";
            _zoneProbe.AddToClassList("sus-sb-zone");
            _zoneProbe.AddToClassList("sus-sb-probe");
            center.Add(_zoneProbe);

            // Zone D — control panel (empty slot, step 4).
            _zonePanel.name = "sus-storybook-zone-d";
            _zonePanel.AddToClassList("sus-sb-zone");
            _zonePanel.AddToClassList("sus-sb-panel");
            _zonePanel.Add(SlotHint("zone D — controls (step 4)"));

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

        /// <summary>Zone B slot — environment axes land here in step 5.</summary>
        public VisualElement ZoneEnvironment => _zoneEnv;

        /// <summary>Zone D slot — the generated control panel lands here in step 4.</summary>
        public VisualElement ZoneControls => _zonePanel;

        /// <summary>Zone E slot — the probe lands here in step 6.</summary>
        public VisualElement ZoneProbe => _zoneProbe;

        /// <summary>Story currently mounted, or null.</summary>
        public SusStoryEntry CurrentStory { get; private set; }

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

            // The address bar is written only for routes that did NOT come from it (§4.6 loop
            // guard). SusStoryUrl.Push enforces the same rule; asking twice is cheap and makes
            // the intention readable at the call site.
            if (route != null && !route.FromUrl) _url.Push(route);
            _address.text = route != null ? route.ToHash() : _url.Address;

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
            _stageTitle.text = entry.Name;
            _stagePurpose.text = entry.Purpose;
            Mount(entry, route);
        }

        void Mount(SusStoryEntry entry, SusStoryRoute route)
        {
            Unmount();

            SusComponent component;
            try
            {
                entry.Instantiate(route, out component);
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

            SetStageEmpty(false);
        }

        void Unmount()
        {
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
        }

        // ── chrome ───────────────────────────────────────────────────────────

        void ShareCurrentAddress()
        {
            bool ok = _url.CopyToClipboard();
            _share.text = ok ? ShareDoneLabel : ShareLabel;
            if (!ok) return;

            _shareReset?.Pause();
            _shareReset = schedule.Execute(() => _share.text = ShareLabel).StartingIn(ShareFeedbackMs);
        }

        void ToggleDrawer() => EnableInClassList("sus-sb--drawer-open", !ClassListContains("sus-sb--drawer-open"));

        void CloseDrawer() => RemoveFromClassList("sus-sb--drawer-open");

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            bool narrow = evt.newRect.width > 0 && evt.newRect.width < NarrowWidth;
            if (narrow == ClassListContains("sus-sb--narrow")) return;
            EnableInClassList("sus-sb--narrow", narrow);
            if (!narrow) CloseDrawer();
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
            _history.Changed -= ApplyRoute;
            _url.Dispose();
            Unmount();
        }
    }
}
