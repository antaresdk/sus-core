using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.Controls
{
    /// <summary>
    /// One icon source behind the picker's footer line: the registered
    /// <see cref="ISusIconProvider"/> as the panel names it, and how many glyph names it
    /// actually supplies. A source with <see cref="Count"/> == 0 is registered but NOT imported -
    /// which is exactly what the footer hint has to say out loud, because "no glyph found" and
    /// "the set is not in the project" are different answers to the same empty grid.
    /// </summary>
    public readonly struct SusIconSource
    {
        public SusIconSource(string label, int count)
        {
            Label = label;
            Count = count;
        }

        /// <summary>How the panel names this source ("core", "Phosphor", a provider type name).</summary>
        public string Label { get; }

        /// <summary>Glyph names the source supplies right now.</summary>
        public int Count { get; }
    }

    /// <summary>
    /// The engine's control for icon props (card T-3035, plan ARCH-20260907-STORYBOOK-ENGINE
    /// §4.3.1): every <c>Prop&lt;string&gt;</c> whose name ends in <c>Icon</c>
    /// (<see cref="SusPropInfo.LooksLikeIcon"/>) is driven by a searchable GRID OF GLYPHS instead
    /// of a name field, because picking an icon is picking a picture, not guessing a string.
    ///
    /// Registered through <see cref="ISusControlProvider"/> rather than hard-wired into
    /// <see cref="SusControlFactory.BuildBuiltIn"/>: the built-in table keeps its honest stub
    /// (<see cref="SusIconControl"/>), and a package that wants a different picker registers
    /// LATER and wins - the extension point of T-3034 is used by core itself first.
    ///
    /// Read-only icon props are NOT claimed: a control that cannot write must not look writable.
    /// </summary>
    public sealed class SusIconControlProvider : ISusControlProvider
    {
        /// <summary>The instance the engine registers; a singleton so registration is idempotent.</summary>
        public static readonly SusIconControlProvider Default = new SusIconControlProvider();

        /// <inheritdoc/>
        public bool CanBuild(SusPropInfo prop) =>
            prop != null && !prop.ReadOnly && prop.LooksLikeIcon;

        /// <inheritdoc/>
        public SusControl Build(SusPropInfo prop, SusControlContext context) =>
            new SusIconPickerControl(prop, context);
    }

    /// <summary>
    /// Icon prop: a field showing the CURRENT GLYPH plus its name, and - on click - a popup with
    /// a search box and a grid of glyphs drawn from <see cref="SusIconRegistry.KnownAliases"/>,
    /// i.e. from EVERY registered provider (the core set, Phosphor when its sample is imported,
    /// a skin's own icons), never from a hand-written list of names.
    ///
    /// The popup is mounted in the nearest <see cref="OverlayHost"/> above this control
    /// (<see cref="SusBootstrap.ResolveOverlayHost"/> - ancestor-first, T-3032), so inside the
    /// storybook it stays in the stage's own host and cannot escape the shell.
    ///
    /// A page is <see cref="PageSize"/> cells and grows on scroll: a 9k-glyph set must not turn
    /// one popup into 9k elements, and the cap is a PAGE, not a filter - typing re-pages over the
    /// whole registry, so nothing stays unreachable behind it.
    ///
    /// No <c>.style</c> write lives here (R53/R120): position comes from
    /// <see cref="OverlayHost.ShowDropdown"/>, everything else from <c>Storybook.uss</c>.
    /// </summary>
    public sealed class SusIconPickerControl : SusControl
    {
        /// <summary>Cells added per page; scrolling to the end adds the next page.</summary>
        public const int PageSize = 400;

        /// <summary>The value that CLEARS the prop - an icon prop's empty string is a value.</summary>
        public const string NoneOption = "(none)";

        /// <summary>Field caption when the prop holds the empty string.</summary>
        public const string EmptyValueLabel = "not set";

        /// <summary>Placeholder of the popup's search box.</summary>
        public const string SearchPlaceholder = "search by name";

        /// <summary>Shown instead of the grid when the query matches no glyph.</summary>
        public const string NoMatchText = "Nothing in the registry matches this query.";

        /// <summary>Suffix of the hint naming a registered but not imported icon set.</summary>
        public const string NotImportedSuffix = " · not imported";

        /// <summary>Glyph of the chevron on the right of the field.</summary>
        public const string CaretGlyph = "caret-down";

        /// <summary>Glyph in front of the search box.</summary>
        public const string SearchGlyph = "magnifying-glass";

        readonly Button _field = new();
        readonly SusIconElement _glyph = new();
        readonly Label _value = new();

        readonly VisualElement _popup = new();
        readonly TextField _search = new();
        readonly Label _searchPlaceholder = new(SearchPlaceholder);
        readonly Button _none = new() { text = NoneOption };
        readonly ScrollView _scroll = new();
        readonly VisualElement _cellBox = new();
        readonly Label _empty = new(NoMatchText);
        readonly Label _count = new();
        readonly Label _hint = new();

        readonly List<string> _all = new();
        readonly List<string> _matches = new();
        readonly List<Button> _cells = new();

        string _query = string.Empty;
        int _shown;
        OverlayEntry _entry;

        internal SusIconPickerControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.Icon, context)
        {
            // -- the field: glyph + name + chevron --------------------------------
            _field.AddToClassList("sus-sb-ctl__iconfield");
            _glyph.AddToClassList("sus-sb-ctl__glyph");
            _value.AddToClassList("sus-sb-ctl__iconname");
            var caret = new SusIconElement(CaretGlyph);
            caret.AddToClassList("sus-sb-ctl__iconcaret");
            _field.Add(_glyph);
            _field.Add(_value);
            _field.Add(caret);
            _field.clicked += Toggle;
            Body.Add(_field);

            // -- the popup: search, "(none)", grid, empty state, footer ------------
            _popup.AddToClassList("sus-sb-iconpicker");
            _popup.name = "sus-storybook-iconpicker-" + prop.Name;

            var searchBox = new VisualElement();
            searchBox.AddToClassList("sus-sb-iconpicker__search");
            var searchGlyph = new SusIconElement(SearchGlyph);
            searchGlyph.AddToClassList("sus-sb-iconpicker__search-glyph");
            _search.AddToClassList("sus-sb-iconpicker__search-input");
            _search.RegisterValueChangedCallback(e => Search(e.newValue));
            _searchPlaceholder.AddToClassList("sus-sb-iconpicker__placeholder");
            _searchPlaceholder.pickingMode = PickingMode.Ignore;
            searchBox.Add(searchGlyph);
            searchBox.Add(_search);
            searchBox.Add(_searchPlaceholder);

            _none.AddToClassList("sus-sb-iconpicker__none");
            _none.clicked += () => Select(NoneOption);

            _scroll.AddToClassList("sus-sb-iconpicker__scroll");
            _cellBox.AddToClassList("sus-sb-iconpicker__grid");
            _scroll.Add(_cellBox);
            _scroll.verticalScroller.valueChanged += OnScrolled;

            _empty.AddToClassList("sus-sb-iconpicker__empty");

            var foot = new VisualElement();
            foot.AddToClassList("sus-sb-iconpicker__foot");
            _count.AddToClassList("sus-sb-iconpicker__count");
            _hint.AddToClassList("sus-sb-iconpicker__hint");
            foot.Add(_count);
            foot.Add(_hint);

            _popup.Add(searchBox);
            _popup.Add(_none);
            _popup.Add(_scroll);
            _popup.Add(_empty);
            _popup.Add(foot);

            Refresh();
            Observe();
        }

        // -- what a driver reads ---------------------------------------------------

        /// <summary>The field itself - clicking it opens and closes the popup.</summary>
        public Button Field => _field;

        /// <summary>Glyph shown in the field for the current value.</summary>
        public SusIconElement Glyph => _glyph;

        /// <summary>Field caption: the glyph name, or <see cref="EmptyValueLabel"/>.</summary>
        public string ValueLabel => _value.text;

        /// <summary>The popup element - only in the tree while it is open.</summary>
        public VisualElement Popup => _popup;

        /// <summary>Search box of the popup.</summary>
        public TextField SearchField => _search;

        /// <summary>Button that writes the empty string.</summary>
        public Button NoneButton => _none;

        /// <summary>True while the popup is mounted in an overlay host.</summary>
        public bool IsOpen => _popup.hierarchy.parent != null;

        /// <summary>The host the popup currently lives in, or null when it is closed.</summary>
        public OverlayHost Host => OverlayHost.HostOf(_popup);

        /// <summary>Glyph names matching the current query, in registry order.</summary>
        public IReadOnlyList<string> Matches => _matches;

        /// <summary>Cells currently built - one page at a time, see <see cref="PageSize"/>.</summary>
        public IReadOnlyList<Button> Cells => _cells;

        /// <summary>Every name the registry knows, as the grid pages over it.</summary>
        public IReadOnlyList<string> AllNames => _all;

        /// <summary>Footer counter: the source line, or "N of M" while filtering.</summary>
        public string CountText => _count.text;

        /// <summary>Footer hint about a registered but not imported set; empty when there is none.</summary>
        public string HintText => _hint.text;

        /// <summary>True while the grid is empty and the "nothing found" line is shown.</summary>
        public bool ShowsEmptyState => !_empty.ClassListContains(HiddenClass);

        // -- behaviour -------------------------------------------------------------

        /// <summary>Opens the popup when closed, closes it when open.</summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>
        /// Mounts the popup in the nearest overlay host and rebuilds the grid from the registry.
        /// Does nothing when there is no host and no panel to create one in - a picker without a
        /// place to open is a fact, not an exception.
        /// </summary>
        public void Open()
        {
            if (IsOpen) return;

            var host = SusBootstrap.ResolveOverlayHost(this);
            if (host == null) return;

            ReloadRegistry();
            _query = string.Empty;
            _search.SetValueWithoutNotify(string.Empty);
            _searchPlaceholder.EnableInClassList(HiddenClass, false);
            ApplyQuery();

            _entry = host.ShowDropdown(_field, _popup);
            if (_entry == null) _popup.RemoveFromHierarchy();
        }

        /// <summary>Removes the popup from the host it actually sits in.</summary>
        public void Close()
        {
            var host = Host;
            if (host != null && _entry != null) host.RemoveFromOverlay(_entry);
            else _popup.RemoveFromHierarchy();
            _entry = null;
        }

        /// <summary>Filters the grid; the cap is a page over the whole registry, not a filter.</summary>
        public void Search(string query)
        {
            _query = (query ?? string.Empty).Trim();
            _searchPlaceholder.EnableInClassList(HiddenClass, _query.Length > 0);
            ApplyQuery();
        }

        /// <summary>Adds the next page of cells; what scrolling to the end does.</summary>
        public void ShowMore()
        {
            if (_shown >= _matches.Count) return;
            int upTo = Math.Min(_matches.Count, _shown + PageSize);
            for (int i = _shown; i < upTo; i++) AddCell(_matches[i]);
            _shown = upTo;
            MarkSelected();
        }

        /// <summary>
        /// Writes the glyph name into the prop and closes the popup.
        /// <see cref="NoneOption"/> writes the empty string.
        /// </summary>
        public bool Select(string name)
        {
            bool none = string.IsNullOrEmpty(name) ||
                        string.Equals(name, NoneOption, StringComparison.Ordinal);
            bool written = Write(none ? string.Empty : name);
            Close();
            return written;
        }

        /// <summary>
        /// Picks the glyph of cell <paramref name="index"/> - exactly what clicking that cell
        /// does, so a driver (test, probe, deep link) goes through the same door as the mouse.
        /// </summary>
        public bool PickAt(int index) =>
            index >= 0 && index < _cells.Count && Select(_cells[index].userData as string);

        /// <inheritdoc/>
        public override bool SetFromString(string text) => Write(text ?? string.Empty);

        protected override void OnRefresh()
        {
            var value = StringValue;
            _glyph.Name.Value = value;
            _glyph.EnableInClassList(HiddenClass, value.Length == 0);
            _value.text = value.Length == 0 ? EmptyValueLabel : value;
            _value.EnableInClassList("sus-sb-ctl__iconname--empty", value.Length == 0);
            SetNote(null);
            MarkSelected();
        }

        // -- the registry side -----------------------------------------------------

        /// <summary>
        /// The sources behind the grid, in registry priority order. Public because the footer's
        /// claim ("core 126 glyphs") has to be checkable against the registry, not trusted.
        /// </summary>
        public static IReadOnlyList<SusIconSource> Sources()
        {
            var providers = SusIconRegistry.Providers;
            var list = new List<SusIconSource>(providers.Count);
            for (int i = 0; i < providers.Count; i++)
            {
                var p = providers[i];
                int n = 0;
                foreach (var unused in p.KnownNames) n++;
                list.Add(new SusIconSource(SourceLabel(p), n));
            }
            return list;
        }

        /// <summary>How the panel names one provider.</summary>
        public static string SourceLabel(ISusIconProvider provider)
        {
            if (provider == null) return string.Empty;
            if (provider is CoreIconProvider) return "core";
            if (provider is PhosphorIconProvider) return "Phosphor";

            var name = provider.GetType().Name;
            if (name.EndsWith("IconProvider", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "IconProvider".Length);
            else if (name.EndsWith("Provider", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Provider".Length);
            return name.Length == 0 ? provider.GetType().Name : name;
        }

        /// <summary>
        /// Hint about a set that IS registered and supplies nothing - the Phosphor case: the
        /// provider is always registered, the 9072 SVGs arrive only with the optional sample.
        /// Empty string when every registered source has glyphs.
        /// </summary>
        public static string NotImportedHint()
        {
            var providers = SusIconRegistry.Providers;
            for (int i = 0; i < providers.Count; i++)
            {
                var p = providers[i];
                bool any = false;
                foreach (var unused in p.KnownNames) { any = true; break; }
                if (any) continue;

                return p is PhosphorIconProvider
                    ? "Phosphor " + PhosphorIconProvider.DeclaredSvgCount + NotImportedSuffix
                    : SourceLabel(p) + NotImportedSuffix;
            }
            return string.Empty;
        }

        void ReloadRegistry()
        {
            _all.Clear();
            foreach (var name in SusIconRegistry.KnownAliases) _all.Add(name);
            _all.Sort(StringComparer.Ordinal);
        }

        void ApplyQuery()
        {
            if (_all.Count == 0) ReloadRegistry();

            _matches.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                if (_query.Length > 0 &&
                    _all[i].IndexOf(_query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _matches.Add(_all[i]);
            }

            _cellBox.Clear();
            _cells.Clear();
            _shown = 0;
            ShowMore();

            _empty.EnableInClassList(HiddenClass, _matches.Count > 0);
            _scroll.EnableInClassList(HiddenClass, _matches.Count == 0);

            _count.text = _query.Length > 0
                ? _matches.Count + " of " + _all.Count + " for \"" + _query + "\""
                : SourcesText();

            var hint = NotImportedHint();
            _hint.text = hint;
            _hint.EnableInClassList(HiddenClass, hint.Length == 0);
        }

        static string SourcesText()
        {
            var sources = Sources();
            var sb = new System.Text.StringBuilder();
            int total = 0;
            int named = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].Count == 0) continue;
                total += sources[i].Count;
                if (named > 0) sb.Append(" · ");
                sb.Append(sources[i].Label).Append(' ').Append(sources[i].Count);
                named++;
            }
            if (named == 0) return "no icon source registered";
            return named == 1
                ? sb.ToString() + " glyphs"
                : sb.ToString() + " · " + total + " glyphs";
        }

        void AddCell(string name)
        {
            var cell = new Button { tooltip = name };
            cell.AddToClassList("sus-sb-iconpicker__cell");
            var glyph = new SusIconElement(name);
            glyph.AddToClassList("sus-sb-iconpicker__cell-glyph");
            cell.Add(glyph);
            cell.clicked += () => Select(name);
            cell.userData = name;
            _cellBox.Add(cell);
            _cells.Add(cell);
        }

        void MarkSelected()
        {
            var current = StringValue;
            for (int i = 0; i < _cells.Count; i++)
            {
                var name = _cells[i].userData as string;
                _cells[i].EnableInClassList("sus-sb-iconpicker__cell--selected",
                    name != null && string.Equals(name, current, StringComparison.Ordinal));
            }
        }

        void OnScrolled(float value)
        {
            if (_shown >= _matches.Count) return;
            var scroller = _scroll.verticalScroller;
            if (value >= scroller.highValue - 1f) ShowMore();
        }
    }
}
