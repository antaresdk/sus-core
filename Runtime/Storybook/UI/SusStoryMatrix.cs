using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Probe;   // cards T-3482 / T-3483: the mode and the cell geometry

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
    /// Two gates decide the columns, and they answer different questions (card T-3431):
    /// <list type="bullet">
    /// <item><see cref="SusStoryStates.Declares"/> — does the component's ROLE have this state?
    /// A <c>disabled</c> column over <c>SusDivider</c> or an <c>error</c> one over
    /// <c>SusAlert</c> is a caption over a copy of rest, and a role with no state at all leaves
    /// the matrix unbuilt (<see cref="SilentReason"/>);</item>
    /// <item><see cref="SusStoryStates.CanForce"/> — can it be forced on THIS instance? The ones
    /// that cannot are named under the grid; see <see cref="SusStateTwins"/> for why hover and
    /// active are usually those.</item>
    /// </list>
    ///
    /// A grid of miniatures is a tool for SMALL components, and only for them (owner, 2026-09-11:
    /// "there is no point stuffing large components into a table of variants or states - they
    /// simply cannot be shown whole there"; card T-3482, decision d:360869). So the FIRST question
    /// the matrix asks is not how many cells it would draw but how big ONE instance is, measured
    /// live rather than read off a stylesheet:
    /// <list type="bullet">
    /// <item>an instance that fits <see cref="CellMaxWidth"/> x <see cref="CellMaxHeight"/> gets a
    /// grid, and every cell of it GROWS to the instance's natural size (card T-3481, decision
    /// d:3750a2) - a cell promises the instance whole, and a clipped one is a forgery;</item>
    /// <item>anything larger gets no grid at all: one natural-size instance and a row of state
    /// switches (<see cref="SusStoryMatrixMode.Switcher"/>). Twenty-four 600x400 modals are a
    /// wall, not a comparison, and the matrix says so in words rather than leaving the buyer to
    /// guess why this story looks different.</item>
    /// </list>
    ///
    /// The measurement costs ONE instance and is taken BEFORE the grid is built, which is the
    /// whole point: a matrix that built twenty-four modals and then folded them away would have
    /// paid the very price it exists to avoid.
    /// </summary>
    public sealed class SusStoryMatrix : VisualElement, ISusStoryCellSource
    {
        /// <summary>Cells the matrix may build without being asked (plan §0.3).</summary>
        public const int CellBudget = 24;

        /// <summary>
        /// Widest cell a grid may contain, in pixels (card T-3482, decision d:360869).
        ///
        /// The number is named by VISIBILITY, not by taste: zone C's box is the stage width
        /// (777px) less the row-label column (<c>--sb-matrix-label-w</c>, 68px), about 709px, and
        /// at least a 2x2 block of natural cells has to be visible at once or the grid stops being
        /// a comparison and becomes a scrolling list of one. 709 / 2 is about 354, rounded down
        /// to 340.
        /// </summary>
        public const float CellMaxWidth = 340f;

        /// <summary>
        /// Tallest cell a grid may contain, in pixels. The same arithmetic on the other axis: the
        /// scroll box is 280px high (<c>.sb-matrix__scroll</c>), 280 / 2 = 140.
        /// </summary>
        public const float CellMaxHeight = 140f;

        /// <summary>
        /// The bound the mode fork actually compares against, defaulting to
        /// <see cref="CellMaxWidth"/> x <see cref="CellMaxHeight"/>.
        ///
        /// A settable property for the same reason <see cref="AxisPropName"/> is one: a rig has to
        /// be able to exercise the GRID path over a component that would never take it. The
        /// overlay-teardown corpus of T-3160 / T-3189 is exactly that - a modal that opens itself
        /// at t=0, which by measurement belongs in a switcher, while what those tests are about is
        /// where a CELL's popup lands. Without this seam the regression would have been deleted by
        /// the mode fork rather than kept.
        /// </summary>
        public static Vector2 CellMaxSize { get; set; } = new Vector2(CellMaxWidth, CellMaxHeight);

        /// <summary>
        /// Frames a measuring pass waits for the layout before it reads anyway (cards T-3482 /
        /// T-3483).
        ///
        /// It has to WAIT, and the first version of this did not, which is worth writing down
        /// because the failure was silent and looked like data. A scheduled item runs at the top
        /// of a frame, BEFORE that frame's layout pass, so "one frame after I added it" reads the
        /// element before anyone has sized it: the first live sweep came back with a natural size
        /// of 0x0 for 146 of 154 stories, every one of them then classified as small enough for a
        /// grid, and 318 cells accused of being squeezed against a reference that was never
        /// measured. A measurement that did not happen must not be able to look like a
        /// measurement that came out zero.
        ///
        /// And waiting for a box is not enough either: the first box is not the last one. The
        /// second live sweep measured buttons at 64px wide - the width of the label before the
        /// text had been laid out - and then reported all eighteen cells as squeezed against it,
        /// while the grid on screen was correct at 117. So a pass reads only when the numbers have
        /// STOPPED MOVING (<see cref="StableFrames"/>), which is the same condition the frame
        /// capture waits on, and for the same reason.
        /// </summary>
        public const int SettleFrames = 12;

        /// <summary>
        /// Times the column sizing may re-apply itself before it stops. It converges by
        /// construction - a pass that changes nothing is the last one - and this only bounds a
        /// component whose size oscillates with the room it is given.
        /// </summary>
        public const int MaxSizePasses = 8;

        /// <summary>
        /// Times the mode may be re-decided for one story. See <c>Revise</c>: a first measurement
        /// can be an intermediate layout, and the mode has to be allowed to follow the truth -
        /// but not forever.
        /// </summary>
        public const int MaxRevisions = 2;

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
        // Card T-3481: BOTH axes. With a horizontal-only scroller the rows below the 280px box
        // were not clipped, they were UNREACHABLE - and the only reason that looked survivable is
        // that the cells were clipped to 64x28 and never reached the bottom of the box.
        readonly ScrollView _scroll = new(ScrollViewMode.VerticalAndHorizontal);
        readonly VisualElement _grid = new();
        readonly Label _note = new();
        readonly OverlayHost _overlay = new() { name = OverlayName };
        // Where ONE instance is measured before anything is built (card T-3482). Laid out like
        // any other element - `visibility: hidden` keeps it out of the picture without taking it
        // out of layout - so what it reports is a real natural size and not a declaration.
        readonly VisualElement _measure = new();
        // The measuring box needs a host of its own for the same reason a cell does (card
        // T-3189): the instance being measured is a REAL one, and a story that opens itself at
        // t=0 - every modal story - would otherwise teleport into the nearest host it can reach
        // and be counted as an escaped cell. It also measures something useful there: an instance
        // that fills an overlay is exactly the kind that must not be put in a grid.
        readonly OverlayHost _measureHost = new() { name = CellOverlayName };

        readonly List<string> _rows = new();
        readonly List<string> _columns = new();
        readonly List<string> _skipped = new();
        readonly List<CellRef> _refs = new();
        readonly List<SusStoryCellGeometry> _cells = new();
        readonly List<Button> _stateButtons = new();

        SusStoryEntry _entry;
        string _axis;
        string _role;
        string _silent;
        SusStoryAxisSource _axisSource = SusStoryAxisSource.None;
        bool _open;
        bool _built;
        Vector2 _natural;
        bool _measured;
        bool _measurePending;
        bool _sized;
        bool _oversize;
        IVisualElementScheduledItem _measureTick;
        IVisualElementScheduledItem _sizeTick;
        IVisualElementScheduledItem _recordTick;
        int _passes;
        int _revisions;
        string _switcherState;
        int _created;
        SusComponent _probeInstance;

        /// <summary>One cell, kept by reference so the measuring pass never re-queries the tree.</summary>
        sealed class CellRef
        {
            public string Row;
            public string State;
            public int Column;
            public int Line;
            public VisualElement Cell;
            public VisualElement Item;
            public Vector2 Natural;
            public float AppliedW = float.NaN;
            public float AppliedH = float.NaN;
        }

        public SusStoryMatrix()
        {
            AddToClassList("sb-matrix");

            _toggle.clicked += Toggle;
            _toggle.AddToClassList("sb-matrix__toggle");

            _scroll.AddToClassList("sb-matrix__scroll");
            _grid.AddToClassList("sb-matrix__grid");
            _scroll.Add(_grid);

            _note.AddToClassList("sb-matrix__note");
            _measure.AddToClassList("sb-matrix__measure");
            _measure.pickingMode = PickingMode.Ignore;
            _measure.Add(_measureHost);
            // The cells' half of the same two doors (see BeginMeasure): in an EditorWindow rig
            // the panel does not tick, so the only thing that ever advances the sizing passes is
            // the layout event itself. SyncCells is idempotent, so being called from both costs
            // one comparison.
            _grid.RegisterCallback<GeometryChangedEvent>(_ => SyncCells());

            Add(_toggle);
            Add(_scroll);
            Add(_measure);
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
        /// Role of the component of the current story (card T-3431), or null when the registry
        /// has never heard of it — an engine fixture, or a component of the second corpus until wave 6
        /// of the plan. Null means "no opinion", never "no states".
        /// </summary>
        public string Role => _role;

        /// <summary>
        /// Why the matrix is not drawn AT ALL for this story, or null. Set when the role has no
        /// state to show (<c>display</c>, <c>feedback</c>, <c>overlay</c> — §4.6): the widget is
        /// hidden, <see cref="Columns"/> and <see cref="Rows"/> are empty, and
        /// <see cref="CellCount"/> is 0. Different from <see cref="CollapseReason"/>, which is a
        /// matrix that exists and starts folded.
        /// </summary>
        public string SilentReason => _silent;

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

        /// <summary>
        /// True when one instance does not fit <see cref="CellMaxWidth"/> x
        /// <see cref="CellMaxHeight"/> (card T-3482). Measured, never declared: what matters is
        /// what the skin actually lays out, and a good half of the kit disagrees with its own
        /// stylesheet about it.
        /// </summary>
        public bool OverCellSize => _oversize;

        /// <summary>True when the story declared itself heavy.</summary>
        public bool Heavy => _entry != null && _entry.Weight == SusStoryWeight.Heavy;

        /// <summary>
        /// Why the matrix starts folded, or null when it starts open.
        ///
        /// The cell budget applies to a GRID and to nothing else (card T-3482, the second of the
        /// two numbers of d:7250aa): a switcher is one instance whatever the row x column product
        /// would have been, so folding it would hide a cheap thing for an expensive thing's
        /// reason.
        /// </summary>
        public string CollapseReason =>
            Heavy ? "heavy story" : !_oversize && OverBudget ? "cell budget" : null;

        /// <summary>
        /// How zone C is showing the states (card T-3482). Public because the buyer sees the
        /// difference between a grid and a switcher and is owed a reason for it, and because the
        /// cell judge has to tell "too large for a grid" from "matrix broken".
        /// </summary>
        public SusStoryMatrixMode MatrixMode =>
            _entry == null || _silent != null ? SusStoryMatrixMode.None
            : !_open ? SusStoryMatrixMode.Collapsed
            : _oversize ? SusStoryMatrixMode.Switcher
            : SusStoryMatrixMode.Grid;

        /// <summary>Why that mode - the same words the caption carries.</summary>
        public string MatrixModeReason
        {
            get
            {
                if (_entry == null) return null;
                if (_silent != null) return _silent;
                if (!_open) return CollapseReason ?? "folded";
                if (_oversize)
                    return "one instance is " + Dim(_natural) + ", over the " + Dim(CellMaxSize) +
                           " a grid cell may take - states are shown one at a time";
                return _measured
                    ? "one instance is " + Dim(_natural) + ", within the " + Dim(CellMaxSize) +
                      " a grid cell may take"
                    : "grid";
            }
        }

        /// <summary>Natural size of one instance of this story; zero until it has been measured.</summary>
        public Vector2 NaturalCellSize => _natural;

        /// <summary>True once one instance has been measured live (card T-3482).</summary>
        public bool Measured => _measured;

        /// <summary>Live instances zone C holds right now - 1 in a switcher, N in a grid.</summary>
        public int MatrixInstanceCount =>
            _grid.Query<VisualElement>(className: "sb-matrix__item").ToList().Count;

        /// <summary>
        /// Instances this matrix has CREATED since the last <see cref="Show"/>, the measuring
        /// probe included. The acceptance figure of T-3482: a switcher that arrived at one
        /// instance by building twenty-four and throwing them away has paid the price anyway, and
        /// only a cumulative counter can say so.
        /// </summary>
        public int MatrixInstancesCreated => _created;

        /// <summary>
        /// Measured geometry of every cell (card T-3483). Empty until the layout has converged,
        /// and empty for good in <see cref="SusStoryMatrixMode.Switcher"/> - there are no cells.
        /// </summary>
        public IReadOnlyList<SusStoryCellGeometry> Cells => _cells;

        /// <summary>
        /// How much the WIDEST cell of the grid falls short of the natural size of one instance
        /// measured free of any cell, per axis; zero when it does not (card T-3481).
        ///
        /// The per-cell judge cannot see the defect this catches. A cell that imposes a size on
        /// its instance - <c>max-width: 64px</c>, which is what T-3355 did - makes the instance
        /// measure 64 too, so every per-cell comparison agrees with itself and the grid reports
        /// health. The only witness is a measurement taken somewhere else: one instance in the
        /// measuring box, where nothing but the stage's own width is around it. If every cell of
        /// the grid is narrower than that, the grid is the thing making them narrow.
        ///
        /// It is a per-STORY number and not a per-cell one on purpose, because the free
        /// measurement carries the story's default axis value while the cells carry the others.
        /// </summary>
        public Vector2 CellShortfall
        {
            get
            {
                if (_cells.Count == 0 || _natural == Vector2.zero) return Vector2.zero;
                float w = 0f, h = 0f;
                for (int i = 0; i < _cells.Count; i++)
                {
                    if (_cells[i].Cell.width > w) w = _cells[i].Cell.width;
                    if (_cells[i].Cell.height > h) h = _cells[i].Cell.height;
                }
                return new Vector2(Mathf.Max(0f, _natural.x - w), Mathf.Max(0f, _natural.y - h));
            }
        }

        /// <summary>Cells whose instance is not shown whole - the number T-3481 drives to zero.</summary>
        public int CroppedCellCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cells.Count; i++) if (_cells[i].Cropped) n++;
                return n;
            }
        }

        /// <summary>State the switcher is showing, or null outside switcher mode.</summary>
        public string SwitcherState => _oversize ? _switcherState : null;

        /// <summary>The <c>Variant × state · R × C</c> half of the toggle caption.</summary>
        public string MetaText =>
            _oversize
                ? "one instance " + Dim(_natural) + " · " +
                  _columns.Count.ToString(CultureInfo.InvariantCulture) + " states"
                : (_axis ?? "no axis") + " × state · " +
                  _rows.Count.ToString(CultureInfo.InvariantCulture) + " × " +
                  _columns.Count.ToString(CultureInfo.InvariantCulture);

        static string Dim(Vector2 v) =>
            Mathf.Round(v.x).ToString(CultureInfo.InvariantCulture) + "x" +
            Mathf.Round(v.y).ToString(CultureInfo.InvariantCulture);

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
                if (probe != null) _created++;
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] matrix probe for '" + entry.Id + "' failed: " + e.Message);
            }

            ResolveRows(probe);
            ResolveColumns(probe);

            if (_silent != null)
            {
                // Card T-3431: not a folded matrix, an absent one. The rows go too, so CellCount
                // is 0 and nobody downstream counts cells that will never be built.
                _rows.Clear();
                AddToClassList("sb-hidden");
                return;
            }

            // Card T-3482. The probe has already been built to answer the rows and the columns;
            // it now answers the third question - how big one instance is - instead of being
            // dropped. Nothing is built until it has, because the answer decides WHAT to build.
            //
            // Outside a panel there is no layout and therefore no answer, so the grid is built at
            // once with a natural size of zero: an EditMode rig asserting rows, columns and cell
            // counts must not have to spin a panel to see a cell.
            if (panel != null && probe != null)
            {
                BeginMeasure(probe);
                return;
            }

            Decide(Vector2.zero, false);
            SetOpen(CollapseReason == null);
        }

        // ── the measurement the mode is decided on (card T-3482) ─────────────

        /// <summary>
        /// Parks ONE instance in the measuring box and waits for the layout to size it. Nothing
        /// else is built meanwhile: the whole reason the measurement comes first is that building
        /// a grid of twenty-four modals and folding it away afterwards costs exactly as much as
        /// keeping it.
        /// </summary>
        void BeginMeasure(SusComponent probe)
        {
            _measurePending = true;
            _probeInstance = probe;
            _revisions = 0;
            probe.RegisterCallback<GeometryChangedEvent>(OnProbeGeometry);
            _measure.Add(probe);

            // Two doors, and they are not redundant - between them they cover the two rigs this
            // code has to work in. In Play the scheduler ticks every frame and the layout events
            // arrive with it. In an EditorWindow rig the panel does not tick unless something
            // asks it to, so a state machine driven only by `schedule` never advances: that is
            // what left the T-3160 corpus with zero cells after twenty frames, and it did not
            // look like a hang, it looked like "nothing escaped".
            int frames = 0;
            _measureTick?.Pause();
            _measureTick = _measure.schedule.Execute(() =>
            {
                if (!_measurePending) { _measureTick?.Pause(); return; }
                if (!Laid(_probeInstance) && ++frames < SettleFrames) return;
                FinishMeasure();
            }).Every(0);
        }

        /// <summary>Has the layout given this element a box yet?</summary>
        static bool Laid(VisualElement el) =>
            el != null && !float.IsNaN(el.layout.width) &&
            (el.layout.width > 0f || el.layout.height > 0f);

        /// <summary>Size the layout gave an element, or zero while it has none.</summary>
        static Vector2 BoxOf(VisualElement el) =>
            Laid(el) ? new Vector2(el.layout.width, el.layout.height) : Vector2.zero;

        void OnProbeGeometry(GeometryChangedEvent _)
        {
            if (_measurePending) { FinishMeasure(); return; }
            Revise();
        }

        /// <summary>
        /// Takes the first real size the layout gives the parked instance and lets the matrix
        /// build on it.
        ///
        /// The FIRST size and not the settled one, deliberately. Waiting for the numbers to stop
        /// moving is the right thing for the cells, which are read as evidence; it is the wrong
        /// thing here, because until this returns nothing is built at all, and a story whose
        /// instance never settles would show an empty zone C forever. An early reading can only
        /// be wrong in one direction that matters - a component that looks small now and grows
        /// past the threshold later - and <see cref="Revise"/> answers that when it happens.
        /// </summary>
        void FinishMeasure()
        {
            if (!_measurePending) return;
            _measurePending = false;
            _measureTick?.Pause();

            // The instance BY REFERENCE and not by index: a story that opened itself is no longer
            // a child of the measuring box at all, it is in the box's overlay host - and what it
            // measures there (a full overlay) is the honest answer for it.
            var size = BoxOf(_probeInstance);
            Decide(size, size != Vector2.zero);
            SetOpen(CollapseReason == null);
        }

        /// <summary>
        /// Re-decides the mode when the measured instance turns out to be a different size than it
        /// first looked - a table that fills in its rows, a card that loads an image.
        ///
        /// Bounded by <see cref="MaxRevisions"/>: a component whose size oscillates would
        /// otherwise rebuild the grid forever, and a matrix that rebuilds forever is worse than a
        /// matrix that chose the wrong mode once.
        /// </summary>
        void Revise()
        {
            if (_entry == null || _silent != null || _revisions >= MaxRevisions) return;
            var size = BoxOf(_probeInstance);
            if (size == Vector2.zero) return;
            bool wasOversize = _oversize;
            Decide(size, true);
            if (_oversize == wasOversize) return;
            _revisions++;
            ClearCells();
            SetOpen(CollapseReason == null);
        }

        /// <summary>
        /// The fork of d:360869, in one place: a natural size over the threshold gets a switcher,
        /// anything else gets a grid. Role is deliberately NOT consulted - a role says which
        /// states exist, never how much room one instance needs.
        /// </summary>
        void Decide(Vector2 natural, bool measured)
        {
            _natural = natural;
            _measured = measured;
            var bound = CellMaxSize;
            _oversize = measured && (natural.x > bound.x || natural.y > bound.y);
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
            // Card T-3482: the word changes with the mode, because the buyer is looking at two
            // stories that show their states differently and is owed the reason in words.
            _toggle.text = (_open ? "▾" : "▸") + (_oversize ? " states · " : " matrix · ") + MetaText +
                           (_open || CollapseReason == null ? string.Empty : " · " + CollapseReason);
            RenderNote();
        }

        /// <summary>
        /// Empties the measuring box. Host FIRST, for the reason spelled out in
        /// <see cref="ClearCells"/>: a removal through <c>ClearAll</c> carries the flag that stops
        /// a self-teleported instance from scheduling itself back into the box.
        /// </summary>
        void ClearMeasure()
        {
            _measureTick?.Pause();
            _probeInstance?.UnregisterCallback<GeometryChangedEvent>(OnProbeGeometry);
            _measureHost.ClearAll();
            for (int i = _measure.childCount - 1; i >= 0; i--)
                if (!ReferenceEquals(_measure[i], _measureHost))
                    _measure.RemoveAt(i);
            _probeInstance = null;
        }

        void Reset()
        {
            ClearCells();
            ClearMeasure();
            _measurePending = false;
            _measured = false;
            _oversize = false;
            _natural = Vector2.zero;
            _created = 0;
            _switcherState = null;
            _rows.Clear();
            _columns.Clear();
            _skipped.Clear();
            _entry = null;
            _axis = null;
            _role = null;
            _silent = null;
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
            // Card T-3431. Two different questions, and the order matters. FIRST: does the
            // component's ROLE have this state at all — a `disabled` column over SusDivider and
            // an `error` column over SusAlert promise transitions that do not exist, and a
            // promise nothing can keep is the same forgery as a state the role owes and nobody
            // drew (plan §4.6, D14 d:5ea31f). SECOND, only for the states that survived: can it
            // be forced on this instance — the twin question SusStateTwins already answered.
            //
            // A role that has no state at all (display, feedback, overlay — 36 of the 80 kit
            // components) leaves nothing but `rest`, and a one-column matrix of rest cells is
            // not a smaller matrix, it is a caption over the variant axis that is already drawn
            // below. So the whole widget stands down and says why.
            //
            // The role comes from the story's DECLARED component type first, and from the probe
            // only when the story names none. The probe is not the subject: a story is free to
            // build a scene wrapper around what it is about, and two of them do —
            // kit/world/floating-damage and kit/devtools/diagnostics instantiate a story-local
            // `Scene` component. In the first Play measurement of T-3431 those two came back
            // with four columns of state each, because the wrapper's type is in no registry and
            // "unknown" reads as "no opinion". The story had named its subject all along.
            var roleType = _entry != null && _entry.ComponentType != null
                ? _entry.ComponentType
                : probe != null ? probe.GetType() : null;
            _role = SusStateRoles.RoleOf(roleType);
            var all = SusStoryStates.All;
            for (int i = 0; i < all.Count; i++)
            {
                var state = all[i];
                if (!SusStoryStates.Declares(roleType, state)) continue;
                if (state == SusStoryStates.Rest) { _columns.Add(state); continue; }
                if (SusStoryStates.CanForce(probe, state)) _columns.Add(state);
                else _skipped.Add(state);
            }

            if (_columns.Count + _skipped.Count <= 1)
            {
                _silent = "role " + (_role ?? "?") + " declares no state";
                _columns.Clear();
                _skipped.Clear();
                return;
            }

            RenderNote();
        }

        /// <summary>
        /// The footnote under the widget. Two things can need saying and both are the buyer's
        /// business: which state columns could not be drawn at all, and - card T-3482 - why this
        /// story shows one instance with switches where the previous one showed a grid. A mode
        /// the reader has to infer from the shape of the widget is not a named mode.
        /// </summary>
        void RenderNote()
        {
            var text = string.Empty;
            if (_skipped.Count > 0)
                text = string.Join(", ", _skipped) +
                       " — no state twins in this component's USS, so the column is not drawn: " +
                       "UI Toolkit cannot force a pseudo-class, and a column showing the rest " +
                       "state under another name would be wrong.";
            if (_oversize && _open)
                text = (text.Length > 0 ? text + " " : string.Empty) +
                       "No grid for this one: " + MatrixModeReason + ".";

            _note.text = text;
            _note.EnableInClassList("sb-hidden", text.Length == 0);
        }

        void BuildCells()
        {
            if (_oversize) { BuildSwitcher(); return; }

            _grid.Clear();
            _refs.Clear();
            _cells.Clear();
            _sized = false;
            _grid.Add(HeaderRow());
            for (int r = 0; r < _rows.Count; r++) _grid.Add(BodyRow(_rows[r], r));
            _built = true;
            // Two more passes, and they cannot be one (card T-3481). The first reads what every
            // instance takes when nothing constrains it; only then is a column wide enough to be
            // given a width. The second records where everything ended up - which is the evidence
            // of T-3483, and it has to be taken after the columns, or it would describe the
            // intermediate layout instead of the one on screen.
            //
            // Neither pass counts frames. Both are IDEMPOTENT and both are driven by the layout
            // itself: sizing a column writes a width, writing a width provokes a layout, and the
            // layout calls back. When a pass finds nothing left to change, the numbers have
            // stopped moving - which is the same condition a frame capture waits on, established
            // by the thing itself rather than by a guess about how many frames it takes.
            _passes = 0;
            _sizeTick?.Pause();
            _sizeTick = _grid.schedule.Execute(SyncCells).Every(0);
        }

        /// <summary>
        /// Gives every column the width of its widest cell and every row the height of its
        /// tallest (card T-3481). A column is sized as a WHOLE - a per-cell width would make the
        /// grid a ragged pile and destroy the only thing a matrix is for, comparing the same
        /// place across states.
        /// </summary>
        void SyncCells()
        {
            if (_oversize || !_built || _refs.Count == 0) { _sizeTick?.Pause(); return; }
            if (_passes >= MaxSizePasses) { _sizeTick?.Pause(); return; }
            for (int i = 0; i < _refs.Count; i++)
                if (!Laid(_refs[i].Item)) return;   // not one box yet: nothing to size against

            if (SizeColumns())
            {
                _passes++;
                return;   // the widths just written are an input to the NEXT layout pass
            }

            _sizeTick?.Pause();
            _sized = true;
            RecordCells();
        }

        /// <summary>
        /// Gives every column the width of its widest cell and every row the height of its
        /// tallest, and says whether that CHANGED anything. Called until it changes nothing.
        /// </summary>
        bool SizeColumns()
        {
            var colW = new float[_columns.Count];
            var lineH = new float[_rows.Count];
            for (int i = 0; i < _refs.Count; i++)
            {
                var r = _refs[i];
                r.Natural = BoxOf(r.Item);
                if (r.Column >= 0 && r.Natural.x > colW[r.Column]) colW[r.Column] = r.Natural.x;
                if (r.Line >= 0 && r.Natural.y > lineH[r.Line]) lineH[r.Line] = r.Natural.y;
            }

            bool changed = false;
            for (int i = 0; i < _refs.Count; i++)
            {
                var r = _refs[i];
                if (Mathf.Abs(r.AppliedW - colW[r.Column]) > 0.01f)
                {
                    r.AppliedW = colW[r.Column];
                    changed = true;
                    // sus:uss-impossible the width is a measured size of live instances, computed
                    // from this layout pass - USS has no number for "as wide as the widest of
                    // these eighteen components turned out to be"
                    r.Cell.style.minWidth = colW[r.Column];
                }
                if (Mathf.Abs(r.AppliedH - lineH[r.Line]) > 0.01f)
                {
                    r.AppliedH = lineH[r.Line];
                    changed = true;
                    // sus:uss-impossible same measured height, from the same pass
                    r.Cell.style.minHeight = lineH[r.Line];
                }
            }

            if (!changed) return false;

            var heads = _grid.Query<Label>(className: "sb-matrix__colhead").ToList();
            for (int i = 0; i < heads.Count && i < colW.Length; i++)
                // sus:uss-impossible the column head follows the measured width of its column
                heads[i].style.minWidth = colW[i];
            return true;
        }

        /// <summary>
        /// Writes down what every cell ended up being (card T-3483). Read from LAYOUT and not
        /// from the rendered frame, which is the point: <c>overflow: hidden</c> can hide a crop
        /// from a screenshot, and it cannot hide it from here.
        /// </summary>
        void RecordCells()
        {
            _cells.Clear();
            for (int i = 0; i < _refs.Count; i++)
            {
                var r = _refs[i];
                var cellWorld = r.Cell.LocalToWorld(r.Cell.contentRect);
                var itemWorld = r.Item.worldBound;
                var scale = ScaleBetween(r.Cell, r.Item);
                // The stage reference is THIS cell's own unconstrained box, read while the
                // column had not been sized yet. It cannot be the story-level
                // NaturalCellSize, which is what this code tried second: that one instance
                // carries the story's default axis value, and the cells carry six others - on
                // kit/atoms/button the default measured 126 and the `elevated` row measured 117,
                // so a story-level reference accused all eighteen cells of a 9px crop that did
                // not exist. What the story-level number IS good for is the whole grid rather
                // than one cell of it: see CellShortfall.
                var stage = r.Natural;
                _cells.Add(new SusStoryCellGeometry(
                    r.Row, r.State, cellWorld, itemWorld, stage, scale,
                    SusStoryCellGeometry.Judge(cellWorld, itemWorld, stage, scale)));
            }
        }

        /// <summary>Scale of the transform chain between two elements; 1 when nothing scales.</summary>
        static float ScaleBetween(VisualElement outer, VisualElement inner)
        {
            var a = outer.worldTransform.lossyScale.x;
            var b = inner.worldTransform.lossyScale.x;
            return a > 0.0001f ? b / a : 1f;
        }

        /// <summary>
        /// The other half of the fork (card T-3482): ONE natural-size instance and a row of state
        /// switches. No grid is built, not even briefly - the counter
        /// <see cref="MatrixInstancesCreated"/> is what says so.
        /// </summary>
        void BuildSwitcher()
        {
            _grid.Clear();
            _refs.Clear();
            _cells.Clear();

            var bar = new VisualElement();
            bar.AddToClassList("sb-matrix__states");
            _stateButtons.Clear();
            for (int i = 0; i < _columns.Count; i++)
            {
                var state = _columns[i];
                var button = new Button { text = state };
                button.AddToClassList("sb-matrix__state");
                button.clicked += () => ShowState(state);
                _stateButtons.Add(button);
                bar.Add(button);
            }
            _grid.Add(bar);

            var solo = new VisualElement();
            solo.AddToClassList("sb-matrix__solo");
            solo.pickingMode = PickingMode.Ignore;
            _grid.Add(solo);

            _built = true;
            ShowState(_columns.Count > 0 ? _columns[0] : SusStoryStates.Rest);
        }

        /// <summary>
        /// Rebuilds the single instance in the state asked for. A rebuild and not an edit,
        /// because forcing a state is one-way (<see cref="SusStoryStates.Force"/> adds classes and
        /// writes props; there is no Unforce), and one instance at a time is still one instance.
        /// </summary>
        public void ShowState(string state)
        {
            if (!_oversize || !_built) return;
            var solo = _grid.Q<VisualElement>(className: "sb-matrix__solo");
            if (solo == null) return;

            _switcherState = state;
            solo.Clear();

            SusComponent item = null;
            try
            {
                _entry.Instantiate(null, out item);
                if (item != null) _created++;
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] matrix switcher '" + state + "' failed: " + e.Message);
            }

            if (item == null)
            {
                var dash = new Label("—");
                dash.AddToClassList("sb-matrix__cell-fail");
                solo.Add(dash);
            }
            else
            {
                SusStoryStates.Force(item, state);
                var box = new VisualElement();
                box.AddToClassList("sb-matrix__item");
                box.pickingMode = PickingMode.Ignore;
                item.pickingMode = PickingMode.Ignore;
                box.Add(item);
                solo.Add(box);
            }
            solo.Add(new OverlayHost { name = CellOverlayName });

            for (int i = 0; i < _stateButtons.Count; i++)
                _stateButtons[i].EnableInClassList(
                    "sb-matrix__state--on",
                    string.Equals(_stateButtons[i].text, state, StringComparison.Ordinal));

            RenderNote();
        }

        void ClearCells()
        {
            // Grid first, host second, and the order is the point (card T-3160). A cell that
            // teleported itself into the host answers a host-initiated removal by SCHEDULING a
            // restore into its original parent (SusOverlayComponent.UnmountSelfFromOverlay) — so
            // that parent, the cell, must already be detached when the host is emptied. A
            // detached element's scheduler never fires, and the restore dies with it; the other
            // order would put the cell's instance back on screen one frame later.
            _sizeTick?.Pause();
            _recordTick?.Pause();
            _grid.Clear();
            _overlay.ClearAll();
            _refs.Clear();
            _cells.Clear();
            _stateButtons.Clear();
            _sized = false;
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

        VisualElement BodyRow(string value, int line)
        {
            var row = new VisualElement();
            row.AddToClassList("sb-matrix__row");
            row.Add(RowLabel(value));
            for (int c = 0; c < _columns.Count; c++) row.Add(Cell(value, _columns[c], line, c));
            return row;
        }

        VisualElement Cell(string rowValue, string state, int line, int column)
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
                if (item != null) _created++;
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
            // Card T-3483: the measuring passes walk THIS list and not the tree. The indexer of a
            // container is not the hierarchy - on a list with columns it answers zero children -
            // and a measurement that silently found nothing to measure is the worst kind of green.
            _refs.Add(new CellRef
            {
                Row = rowValue, State = state, Column = column, Line = line, Cell = cell, Item = box,
            });
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
