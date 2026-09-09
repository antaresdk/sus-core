using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.UI
{
    /// <summary>
    /// Zone A of the shell: search, package tabs, groups, story rows, health and version
    /// (plan §6.1 step 3; layout from the Claude Design board "Storybook Shell", card T-3033).
    ///
    /// It is drawn from <see cref="SusStoryRegistry"/> and from nothing else. The panel never
    /// decides WHAT is shown on the stage — it raises <see cref="StorySelected"/> and the host
    /// turns that into a route; the highlight comes back FROM the route (§4.6), which is why
    /// entering by deep link highlights the right row and clicking cannot desynchronise it.
    ///
    /// Not one appearance value is written from here: every visual state is a USS class
    /// (R53/R120 now judge this code — it lives in Runtime, not in Samples~).
    /// </summary>
    public sealed class SusStoryNavPanel : VisualElement
    {
        const string SearchPlaceholder = "search stories";
        const string SearchShortcutHint = "Ctrl+K";

        readonly TextField _search = new();
        readonly Label _searchPlaceholder = new(SearchPlaceholder);
        readonly VisualElement _tabs = new();
        readonly ScrollView _list = new();
        readonly VisualElement _dot = new();
        readonly Label _health = new();
        readonly Label _version = new();

        readonly List<Button> _tabButtons = new();

        string _activePackage;
        string _activeStoryId;
        int _anomalies;

        public SusStoryNavPanel()
        {
            AddToClassList("sus-sb-nav");

            var head = new VisualElement();
            head.AddToClassList("sus-sb-nav__head");

            var searchBox = new VisualElement();
            searchBox.AddToClassList("sus-sb-nav__search");

            _search.AddToClassList("sus-sb-nav__search-input");
            _search.RegisterValueChangedCallback(_ => ApplyFilter());
            // UI Toolkit has no ":focus-within" pseudo-class (T-3077) — the focus ring on the
            // wrapping box is driven from here via FocusIn/FocusOut instead of USS alone.
            _search.RegisterCallback<FocusInEvent>(_ => searchBox.AddToClassList("sus-sb-nav__search--focus"));
            _search.RegisterCallback<FocusOutEvent>(_ => searchBox.RemoveFromClassList("sus-sb-nav__search--focus"));

            _searchPlaceholder.AddToClassList("sus-sb-nav__search-placeholder");
            _searchPlaceholder.pickingMode = PickingMode.Ignore;

            var hint = new Label(SearchShortcutHint);
            hint.AddToClassList("sus-sb-nav__search-hint");
            hint.pickingMode = PickingMode.Ignore;

            searchBox.Add(_search);
            searchBox.Add(_searchPlaceholder);
            searchBox.Add(hint);

            _tabs.AddToClassList("sus-sb-nav__tabs");

            head.Add(searchBox);
            head.Add(_tabs);

            _list.AddToClassList("sus-sb-nav__list");

            var foot = new VisualElement();
            foot.AddToClassList("sus-sb-nav__foot");
            var healthBox = new VisualElement();
            healthBox.AddToClassList("sus-sb-nav__health");
            _dot.AddToClassList("sus-sb-nav__dot");
            _health.AddToClassList("sus-sb-nav__health-text");
            healthBox.Add(_dot);
            healthBox.Add(_health);
            _version.AddToClassList("sus-sb-nav__version");
            foot.Add(healthBox);
            foot.Add(_version);

            Add(head);
            Add(_list);
            Add(foot);

            Rebuild();
        }

        /// <summary>Raised when a story row was clicked.</summary>
        public event Action<SusStoryEntry> StorySelected;

        /// <summary>Raised when a package tab was clicked (key of the package).</summary>
        public event Action<string> PackageSelected;

        /// <summary>Package tab currently shown; setting it redraws the list.</summary>
        public string ActivePackage
        {
            get => _activePackage;
            set
            {
                if (string.Equals(_activePackage, value, StringComparison.OrdinalIgnoreCase)) return;
                _activePackage = value;
                Rebuild();
            }
        }

        /// <summary>
        /// Story highlighted in the list. Set by the HOST from the current route — never from a
        /// click inside this panel, so a route that came from the address bar highlights correctly
        /// (plan §4.6, closes the "wrong item highlighted" half of T-2677).
        /// </summary>
        public string ActiveStoryId
        {
            get => _activeStoryId;
            set
            {
                if (string.Equals(_activeStoryId, value, StringComparison.Ordinal)) return;
                _activeStoryId = value;
                if (SusStoryRegistry.TryParseId(value, out var pkg, out _, out _))
                    _activePackage = pkg;
                Rebuild();
            }
        }

        /// <summary>
        /// Anomaly count shown at the bottom. Zone E computes it from step 6 onwards; until then
        /// the host reports 0, and the dot is green — a placeholder that says "nothing measured"
        /// with the same words it will later use for "nothing found" is the one honest option
        /// available before the probe exists.
        /// </summary>
        public int AnomalyCount
        {
            get => _anomalies;
            set
            {
                if (_anomalies == value) return;
                _anomalies = value;
                RefreshHealth();
            }
        }

        /// <summary>
        /// Current search text. Settable so the shell can clear it (and so a test can filter
        /// without a live panel: outside a panel UI Toolkit drops the change event a typed
        /// character would raise, and a filter that only works when a window is open cannot be
        /// checked in EditMode).
        /// </summary>
        public string Filter
        {
            get => _search.value ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (string.Equals(_search.value, next, StringComparison.Ordinal)) return;
                _search.SetValueWithoutNotify(next);
                ApplyFilter();
            }
        }

        /// <summary>
        /// Reports a story as chosen — what a row click calls. It does NOT move the highlight:
        /// the host turns this into a route and the route sets the highlight (plan §4.6).
        /// </summary>
        public void Select(SusStoryEntry entry)
        {
            if (entry == null) return;
            StorySelected?.Invoke(entry);
        }

        /// <summary>Puts the caret in the search field (the Ctrl/Cmd+K target).</summary>
        public void FocusSearch()
        {
            _search.Focus();
            _search.SelectAll();
        }

        /// <summary>Redraws tabs, list and footer from the registry.</summary>
        public void Rebuild()
        {
            RebuildTabs();
            RebuildList();
            RefreshHealth();
            RefreshVersion();
        }

        void RebuildTabs()
        {
            _tabs.Clear();
            _tabButtons.Clear();

            var packages = SusStoryRegistry.Packages;
            if (packages.Count > 0 && string.IsNullOrEmpty(_activePackage))
                _activePackage = packages[0].Key;

            foreach (var pkg in packages)
            {
                var key = pkg.Key;
                var tab = new Button(() => SelectPackage(key)) { text = key };
                tab.AddToClassList("sus-sb-nav__tab");
                tab.EnableInClassList("sus-sb-nav__tab--active",
                    string.Equals(key, _activePackage, StringComparison.OrdinalIgnoreCase));
                _tabs.Add(tab);
                _tabButtons.Add(tab);
            }
        }

        void SelectPackage(string key)
        {
            _activePackage = key;
            RebuildTabs();
            RebuildList();
            RefreshVersion();
            PackageSelected?.Invoke(key);
        }

        void ApplyFilter()
        {
            _searchPlaceholder.EnableInClassList("sus-sb-hidden", !string.IsNullOrEmpty(_search.value));
            RebuildList();
        }

        void RebuildList()
        {
            _list.Clear();

            var pkg = SusStoryRegistry.FindPackage(_activePackage);
            if (pkg == null) return;

            var filter = Filter.Trim();
            bool anyRow = false;

            foreach (var group in pkg.Groups)
            {
                var matching = new List<SusStoryEntry>();
                foreach (var story in group.Stories)
                    if (Matches(story, filter)) matching.Add(story);

                if (matching.Count == 0) continue;

                var label = new Label(group.Id.ToUpperInvariant());
                label.AddToClassList("sus-sb-nav__group");
                _list.Add(label);

                foreach (var story in matching)
                {
                    _list.Add(BuildRow(story));
                    anyRow = true;
                }
            }

            if (pkg.IsEmpty) _list.Add(BuildEmptyPlate(pkg));
            else if (!anyRow) _list.Add(BuildNoMatchPlate(filter));
        }

        static bool Matches(SusStoryEntry story, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return story.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   story.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        VisualElement BuildRow(SusStoryEntry story)
        {
            var row = new Button(() => Select(story));
            row.AddToClassList("sus-sb-nav__row");
            row.EnableInClassList("sus-sb-nav__row--active",
                string.Equals(story.Id, _activeStoryId, StringComparison.Ordinal));
            row.tooltip = string.IsNullOrEmpty(story.Purpose) ? story.Id : story.Id + " — " + story.Purpose;

            var name = new Label(story.Name);
            name.AddToClassList("sus-sb-nav__row-name");

            // The number is the prop count of the component, not a badge: it is the figure the
            // control panel of step 4 must match, so a row reading 38 next to a panel showing 9
            // controls is a visible hole (DoD §7 p. 1).
            int n = story.PropCount;
            var count = new Label(n < 0 ? "—" : n.ToString());
            count.AddToClassList("sus-sb-nav__row-count");

            row.Add(name);
            row.Add(count);
            return row;
        }

        static VisualElement BuildEmptyPlate(SusStoryPackage pkg)
        {
            var plate = new VisualElement();
            plate.AddToClassList("sus-sb-nav__empty");

            var title = new Label("No stories in " + pkg.PackageId);
            title.AddToClassList("sus-sb-nav__empty-title");

            var version = string.IsNullOrEmpty(pkg.Version) ? "version unknown" : pkg.Version;
            var text = new Label(
                "The package is loaded (" + version + "), but no story provider was found. " +
                "A package declares its own stories:");
            text.AddToClassList("sus-sb-nav__empty-text");

            var code = new Label("[SusStory(\"" + pkg.Key + "/<group>/<slug>\")]");
            code.AddToClassList("sus-sb-nav__empty-code");

            plate.Add(title);
            plate.Add(text);
            plate.Add(code);
            return plate;
        }

        static VisualElement BuildNoMatchPlate(string filter)
        {
            var plate = new VisualElement();
            plate.AddToClassList("sus-sb-nav__empty");
            var text = new Label("Nothing matches \"" + filter + "\".");
            text.AddToClassList("sus-sb-nav__empty-text");
            plate.Add(text);
            return plate;
        }

        void RefreshHealth()
        {
            _health.text = _anomalies == 1 ? "1 anomaly" : _anomalies + " anomalies";
            _dot.EnableInClassList("sus-sb-nav__dot--bad", _anomalies > 0);
        }

        void RefreshVersion()
        {
            var pkg = SusStoryRegistry.FindPackage(_activePackage);
            _version.text = pkg == null || string.IsNullOrEmpty(pkg.Version) ? string.Empty : pkg.Version;
        }
    }
}
