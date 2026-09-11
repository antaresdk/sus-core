using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.UI
{
    /// <summary>
    /// The state matrix of zone C: rows are the values of the component's <c>Variant</c> axis,
    /// columns are the six states (plan ARCH-20260907-STORYBOOK-ENGINE.md §4.5 p. 2 and §0.3, card T-3038).
    ///
    /// Every cell is a REAL instance — <c>Create()</c> + <c>Configure()</c> from the story, its
    /// axis prop set to the row and its state forced by <see cref="SusStoryStates"/>. That is the
    /// whole point of the matrix: a hand-drawn swatch would show what the author believed, and
    /// this shows what the skin actually does.
    ///
    /// Two brakes, because a cell costs a component:
    /// <list type="bullet">
    /// <item>a budget of <see cref="CellBudget"/> cells — over it the matrix starts collapsed;</item>
    /// <item><see cref="SusStoryWeight.Heavy"/> on the story — collapsed whatever the budget says.</item>
    /// </list>
    ///
    /// A column whose state cannot be forced on THIS component is not drawn at all, and the
    /// missing ones are named under the grid. See <see cref="SusStateTwins"/> for why hover and
    /// active are the ones that go missing.
    /// </summary>
    public sealed class SusStoryMatrix : VisualElement
    {
        /// <summary>Cells the matrix may build without being asked (plan §0.3).</summary>
        public const int CellBudget = 24;

        /// <summary>Label of the single row used when the component has no axis.</summary>
        public const string DefaultRow = "default";

        /// <summary>
        /// Name of the matrix's own <see cref="OverlayHost"/> (card T-3160). Deliberately NOT
        /// <see cref="OverlayHost.OverlayHostName"/>: that name is what
        /// <c>SusBootstrap.GetOrCreateOverlay</c> and
        /// <c>SusOverlayComponent.MountSelfInOverlay</c> look for when they search a WHOLE panel
        /// for "the" host — and a matrix host answering that search would adopt popups belonging
        /// to the application. Cells find this host by TYPE, walking up
        /// (<c>SusBootstrap.FindOverlayHost</c>), so it needs no name of that kind.
        /// </summary>
        public const string OverlayName = "sus-storybook-matrix-overlay";

        /// <summary>
        /// Name of the overlay host EVERY CELL owns (card T-3189). One host for the whole matrix
        /// (T-3160) kept the cells out of the application root, but it did not keep them apart
        /// from each other: an <see cref="OverlayHost"/> is <c>position: absolute</c> with zero
        /// insets, so a cell instance that teleports into the MATRIX's host stretches over the
        /// whole matrix box — every column at once, one on top of another. The 2026-09-10 kit
        /// sweep photographed exactly that: on <c>kit/molecules/modal</c> and
        /// <c>kit/molecules/tutorial-modal</c> the grid of four state columns was replaced by a
        /// single full-width dialog, and the frames were read as a GHOST of an earlier story
        /// (showcase-3, card T-3189) — it was the current story's own matrix, drawn over itself.
        /// Same name shape as <see cref="OverlayName"/>, and for the same reason: never
        /// <see cref="OverlayHost.OverlayHostName"/>, or a panel-wide search for "the" host would
        /// hand application popups to a miniature.
        /// </summary>
        public const string CellOverlayName = "sus-storybook-matrix-cell-overlay";

        /// <summary>
        /// Prop the rows come from. The plan names <c>Variant</c>; it is a property rather than a
        /// constant so a rig can point the matrix at another closed axis without a fork. The
        /// default is <see cref="SusStoryAxis.DefaultPropName"/> and not a second literal: the
        /// story attribute defaults to the same name, and two literals would drift (card T-3379).
        /// </summary>
        public static string AxisPropName { get; set; } = SusStoryAxis.DefaultPropName;

        readonly Button _toggle = new();
        readonly ScrollView _scroll = new(ScrollViewMode.Horizontal);
        readonly VisualElement _grid = new();
        readonly Label _note = new();
        readonly OverlayHost _overlay = new() { name = OverlayName };

        readonly List<string> _rows = new();
        readonly List<string> _columns = new();
        readonly List<string> _skipped = new();

        SusStoryEntry _entry;
        string _axis;
        SusStoryAxisSource _axisSource = SusStoryAxisSource.None;
        bool _open;
        bool _built;

        public SusStoryMatrix()
        {
            AddToClassList("sb-matrix");

            _toggle.clicked += Toggle;
            _toggle.AddToClassList("sb-matrix__toggle");

            _scroll.AddToClassList("sb-matrix__scroll");
            _grid.AddToClassList("sb-matrix__grid");
            _scroll.Add(_grid);

            _note.AddToClassList("sb-matrix__note");

            Add(_toggle);
            Add(_scroll);
            Add(_note);
            // Card T-3160. A cell is a REAL instance built by the story's own Create()+Configure(),
            // so a story that is open at t=0 (every modal story: Model = true) makes every cell
            // open ITSELF at mount. Without a host of its own here, the nearest OverlayHost such a
            // cell can reach is the APPLICATION's root one — the showcase sweep shot four modals
            // carrying sb-matrix__item at 0;0;1280;720, over the frames of later stories
            // (showcase-3, 2026-09-09). With it, the cell's popup stays inside the matrix box and
            // dies with the grid: ClearCells() empties this host right after it empties the grid.
            Add(_overlay);

            Reset();
        }

        // ── what the shell and the tests read ────────────────────────────────

        /// <summary>Row labels — the axis values, or one <see cref="DefaultRow"/>.</summary>
        public IReadOnlyList<string> Rows => _rows;

        /// <summary>States actually drawn as columns, in <see cref="SusStoryStates.All"/> order.</summary>
        public IReadOnlyList<string> Columns => _columns;

        /// <summary>States dropped because they cannot be forced on this component.</summary>
        public IReadOnlyList<string> SkippedStates => _skipped;

        /// <summary>Name of the axis prop the rows came from, or null.</summary>
        public string AxisProp => _axis;

        /// <summary>
        /// Who enumerated the axis the rows came from (card T-3379). What the acceptance figure
        /// of D26 counts: a story whose rows came from <see cref="SusStoryAxisSource.None"/> is
        /// still a story with one row, whatever the caption says.
        /// </summary>
        public SusStoryAxisSource AxisSource => _axisSource;

        /// <summary>Cells the matrix WOULD draw — the figure the budget is compared against.</summary>
        public int CellCount => _rows.Count * _columns.Count;

        /// <summary>Cells actually in the tree right now (0 while collapsed).</summary>
        public int BuiltCellCount => _built ? CellCount : 0;

        /// <summary>True when the grid is expanded.</summary>
        public bool Open => _open;

        /// <summary>True when <see cref="CellCount"/> exceeds <see cref="CellBudget"/>.</summary>
        public bool OverBudget => CellCount > CellBudget;

        /// <summary>True when the story declared itself heavy.</summary>
        public bool Heavy => _entry != null && _entry.Weight == SusStoryWeight.Heavy;

        /// <summary>Why the matrix starts folded, or null when it starts open.</summary>
        public string CollapseReason =>
            Heavy ? "heavy story" : OverBudget ? "cell budget" : null;

        /// <summary>The <c>Variant × state · R × C</c> half of the toggle caption.</summary>
        public string MetaText =>
            (_axis ?? "no axis") + " × state · " +
            _rows.Count.ToString(CultureInfo.InvariantCulture) + " × " +
            _columns.Count.ToString(CultureInfo.InvariantCulture);

        /// <summary>Text of the toggle, chevron included — what a test can read back.</summary>
        public string ToggleText => _toggle.text;

        /// <summary>Footnote under the grid; empty when every state could be drawn.</summary>
        public string NoteText => _note.text;

        /// <summary>
        /// The matrix's own overlay host (card T-3160) — where a cell that opens itself lands,
        /// instead of in the application root. Emptied by <see cref="Clear"/>.
        /// </summary>
        public OverlayHost Overlay => _overlay;

        /// <summary>
        /// Cell instances currently parked in an overlay host that belongs to this matrix — its
        /// own (<see cref="Overlay"/>) plus the per-cell hosts of <see cref="CellOverlayName"/>
        /// (card T-3189). What a teardown test counts: "the cells that teleported are inside the
        /// matrix" is a statement about the matrix, not about which of its hosts caught them.
        /// </summary>
        public int HostedCellCount
        {
            get
            {
                int n = _overlay.childCount;
                var cellHosts = _grid.Query<OverlayHost>(name: CellOverlayName).ToList();
                for (int i = 0; i < cellHosts.Count; i++) n += cellHosts[i].childCount;
                return n;
            }
        }

        // ── building ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the matrix for a story. Rows, columns and the budget verdict are computed on
        /// ONE probe instance; the cells themselves are built only if the matrix opens.
        /// </summary>
        public void Show(SusStoryEntry entry)
        {
            Reset();
            _entry = entry;
            if (entry == null)
            {
                AddToClassList("sb-hidden");
                return;
            }
            RemoveFromClassList("sb-hidden");

            SusComponent probe = null;
            try
            {
                entry.Instantiate(null, out probe);
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] matrix probe for '" + entry.Id + "' failed: " + e.Message);
            }

            ResolveRows(probe);
            ResolveColumns(probe);

            SetOpen(CollapseReason == null);
        }

        /// <summary>Empties the matrix and hides it — what an empty stage needs.</summary>
        public void Clear()
        {
            Reset();
            AddToClassList("sb-hidden");
        }

        /// <summary>Flips the fold; builds the cells the first time it opens.</summary>
        public void Toggle() => SetOpen(!_open);

        /// <summary>Opens or folds the grid.</summary>
        public void SetOpen(bool open)
        {
            _open = open && _entry != null && _rows.Count > 0 && _columns.Count > 0;
            if (_open && !_built) BuildCells();
            if (!_open) ClearCells();

            _scroll.EnableInClassList("sb-hidden", !_open);
            _toggle.text = (_open ? "▾" : "▸") + " matrix · " + MetaText +
                           (_open || CollapseReason == null ? string.Empty : " · " + CollapseReason);
        }

        void Reset()
        {
            ClearCells();
            _rows.Clear();
            _columns.Clear();
            _skipped.Clear();
            _entry = null;
            _axis = null;
            _axisSource = SusStoryAxisSource.None;
            _open = false;
            _scroll.AddToClassList("sb-hidden");
            _note.AddToClassList("sb-hidden");
            _note.text = string.Empty;
            _toggle.text = "▸ matrix · " + MetaText;
        }

        void ResolveRows(SusComponent probe)
        {
            // Card T-3379: the rows come from the CLOSED AXIS of the story, and the axis has two
            // producers - the component's own UseAllowed set, and the enumeration a story
            // declares when the component never clamped the prop. Reading DescribeAllowed()
            // here directly (as this method did until T-3379) saw only the first producer, so on
            // the nine kit components whose Variant was an unclamped string the matrix printed
            // one row and the caption said "no axis" (those nine clamp it themselves since
            // T-3395, which is why the rows now come from the component again - through the
            // axis, not through a second reading of DescribeAllowed).
            var axis = SusStoryAxis.Resolve(_entry, probe, AxisPropName);
            _axisSource = axis.Source;

            if (axis.IsClosed)
            {
                _axis = axis.PropName;
                for (int i = 0; i < axis.Values.Count; i++) _rows.Add(axis.Values[i]);
                return;
            }

            // No closed axis under that name: one row, and the caption says "no axis" rather than
            // inventing a second axis nobody declared.
            _rows.Add(DefaultRow);
        }

        void ResolveColumns(SusComponent probe)
        {
            var all = SusStoryStates.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (SusStoryStates.CanForce(probe, all[i])) _columns.Add(all[i]);
                else _skipped.Add(all[i]);
            }

            if (_skipped.Count == 0)
            {
                _note.text = string.Empty;
                _note.AddToClassList("sb-hidden");
                return;
            }

            _note.text = string.Join(", ", _skipped) +
                         " — no state twins in this component's USS, so the column is not drawn: " +
                         "UI Toolkit cannot force a pseudo-class, and a column showing the rest " +
                         "state under another name would be wrong.";
            _note.RemoveFromClassList("sb-hidden");
        }

        void BuildCells()
        {
            _grid.Clear();
            _grid.Add(HeaderRow());
            for (int r = 0; r < _rows.Count; r++) _grid.Add(BodyRow(_rows[r]));
            _built = true;
        }

        void ClearCells()
        {
            // Grid first, host second, and the order is the point (card T-3160). A cell that
            // teleported itself into the host answers a host-initiated removal by SCHEDULING a
            // restore into its original parent (SusOverlayComponent.UnmountSelfFromOverlay) — so
            // that parent, the cell, must already be detached when the host is emptied. A
            // detached element's scheduler never fires, and the restore dies with it; the other
            // order would put the cell's instance back on screen one frame later.
            _grid.Clear();
            _overlay.ClearAll();
            _built = false;
        }

        VisualElement HeaderRow()
        {
            var row = new VisualElement();
            row.AddToClassList("sb-matrix__row");
            row.AddToClassList("sb-matrix__row--head");
            row.Add(RowLabel(string.Empty));
            for (int c = 0; c < _columns.Count; c++)
            {
                var head = new Label(_columns[c]);
                head.AddToClassList("sb-matrix__colhead");
                row.Add(head);
            }
            return row;
        }

        VisualElement BodyRow(string value)
        {
            var row = new VisualElement();
            row.AddToClassList("sb-matrix__row");
            row.Add(RowLabel(value));
            for (int c = 0; c < _columns.Count; c++) row.Add(Cell(value, _columns[c]));
            return row;
        }

        VisualElement Cell(string rowValue, string state)
        {
            var cell = new VisualElement();
            cell.AddToClassList("sb-matrix__cell");
            // A miniature is a picture, not a control: it must not eat the pointer aiming at the
            // live instance below, and it must never take focus away from zone D.
            cell.pickingMode = PickingMode.Ignore;

            SusComponent item = null;
            try
            {
                _entry.Instantiate(null, out item);
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] matrix cell '" + rowValue + "/" + state + "' failed: " + e.Message);
            }

            if (item == null)
            {
                var dash = new Label("—");
                dash.AddToClassList("sb-matrix__cell-fail");
                cell.Add(dash);
                return cell;
            }

            if (_axis != null && rowValue != DefaultRow) SetAxis(item, rowValue);
            SusStoryStates.Force(item, state);

            // Card T-3421. The shell's name goes on a WRAPPER; the instance inside stays clean.
            // Before this the class sat on the SusComponent itself, so a kit component on the
            // stand wore a name from the SHELL's namespace - the exact thing campaign T-3388
            // forbids, and the reason SusStorybookShellClassIsolationTests had to carve mounted
            // instances out of its corpus (every matrix cell read as "shell chrome wearing
            // sus-alert"). The box contract of D19 (card T-3355) is unchanged: the wrapper is
            // what the cell measures and what clips on both axes.
            var box = new VisualElement();
            box.AddToClassList("sb-matrix__item");
            box.pickingMode = PickingMode.Ignore;
            item.pickingMode = PickingMode.Ignore;
            box.Add(item);
            cell.Add(box);
            // The cell's OWN host, added after the instance so the back-to-front scan of
            // SusBootstrap.FindOverlayHost meets it first (card T-3189). A cell that opens itself
            // now fills its cell instead of the whole matrix.
            cell.Add(new OverlayHost { name = CellOverlayName });
            return cell;
        }

        void SetAxis(SusComponent item, string value)
        {
            var props = item.DescribeProps();
            for (int i = 0; i < props.Count; i++)
            {
                if (!string.Equals(props[i].Name, _axis, StringComparison.Ordinal)) continue;
                props[i].TrySetValue(value);
                return;
            }
        }

        static Label RowLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("sb-matrix__rowlabel");
            return label;
        }
    }
}
