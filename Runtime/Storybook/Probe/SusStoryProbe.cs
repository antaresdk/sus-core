using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Controls;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Sharq.Core.Diagnostics;   // SusUiProbe compiles only in the editor and in dev builds
#endif

namespace Sharq.Core.Storybook.Probe
{
    /// <summary>
    /// Zone E — the probe strip under the stage (plan ARCH-20260907-STORYBOOK-ENGINE.md §4.7,
    /// card T-3040, mock-up "Storybook Shell" zone E).
    ///
    /// Three answers, all about the story that is mounted RIGHT NOW:
    /// <list type="bullet">
    /// <item><b>events</b> — chips of the last <see cref="SusStoryEventLog.ChipCount"/> calls with
    /// their arguments, and, in the warning colour, the declared events that never fired this
    /// session. Wiring is automatic (<see cref="SusStoryEventLog"/>), so the strip cannot fall
    /// behind a component that grew a new event.</item>
    /// <item><b>health</b> — the anomaly count of the stage canvas from
    /// <c>SusUiProbe</c>, as a dot plus "health N", with the anomalies listed underneath.
    /// The same number is mirrored into zone A (<c>SusStoryNavPanel.AnomalyCount</c>) so a reader
    /// who is looking at the story list still sees that something is broken.</item>
    /// <item><b>frame</b> — the canon-versus-live verdict from
    /// <see cref="SusStoryFrame.Comparer"/>. Core ships the stub, so out of the box the line
    /// reads "frame: —" rather than a comforting green (the real comparer is card T-3045).</item>
    /// </list>
    ///
    /// The count and the list are the SAME set on purpose: "health 3" with four lines under it
    /// would be two different claims about one story. Unfired events and a frame mismatch have
    /// their own places in the strip and are carried to the session report by
    /// <see cref="BuildReport"/>, which is what the judging rule of plan §5 reads.
    ///
    /// Every visual state is a USS class; this file writes no <c>.style.*</c> (R53/R120).
    /// </summary>
    public sealed class SusStoryProbe : VisualElement, IDisposable
    {
        /// <summary>Label in front of the chips.</summary>
        public const string EventsLabel = "events";

        /// <summary>Printed instead of chips when nothing has fired yet.</summary>
        public const string NoCallsText = "—";

        /// <summary>Prefix of the warning line listing events that never fired.</summary>
        public const string UnfiredPrefix = "not fired: ";

        readonly SusStoryEventLog _events = new();

        readonly VisualElement _strip = new();
        readonly Label _eventsLabel = new(EventsLabel);
        readonly VisualElement _chips = new();
        readonly Label _noCalls = new(NoCallsText);
        readonly Label _unfired = new();
        readonly VisualElement _healthBox = new();
        readonly VisualElement _dot = new();
        readonly Label _health = new();
        readonly Label _frame = new();
        readonly VisualElement _anomalyList = new();

        readonly List<string> _anomalies = new();

        SusStoryEntry _entry;
        SusComponent _instance;
        VisualElement _canvas;
        SusControlPanel _panel;   // card T-3143: zone D's ledger, reused instead of re-plumbing SusStoryContext
        SusStoryFrameResult _frameResult = SusStoryFrameResult.Unavailable;
        bool _disposed;

        public SusStoryProbe()
        {
            AddToClassList("sus-sb-probe__body");

            _strip.AddToClassList("sus-sb-probe__strip");
            _eventsLabel.AddToClassList("sus-sb-probe__title");
            _chips.AddToClassList("sus-sb-probe__chips");
            _noCalls.AddToClassList("sus-sb-probe__none");
            _unfired.AddToClassList("sus-sb-probe__unfired");

            _healthBox.AddToClassList("sus-sb-probe__status");
            _dot.AddToClassList("sus-sb-probe__dot");
            _health.AddToClassList("sus-sb-probe__health");
            _frame.AddToClassList("sus-sb-probe__frame");
            _healthBox.Add(_dot);
            _healthBox.Add(_health);
            _healthBox.Add(_frame);

            _strip.Add(_eventsLabel);
            _strip.Add(_chips);
            _strip.Add(_noCalls);
            _strip.Add(_unfired);
            _strip.Add(_healthBox);

            _anomalyList.AddToClassList("sus-sb-probe__anomalies");

            Add(_strip);
            Add(_anomalyList);

            _events.Fired += OnEventFired;

            Render();
        }

        // ── what a reader (and a test) can ask ────────────────────────────────

        /// <summary>The event log behind the chips — the seam a test drives without a click.</summary>
        public SusStoryEventLog Events => _events;

        /// <summary>Story the probe is attached to, or null.</summary>
        public SusStoryEntry Story => _entry;

        /// <summary>
        /// Zone D's control panel for the currently attached story, or null. Settable
        /// separately from <see cref="Attach"/> because the shell builds the panel lazily —
        /// <see cref="SusStorybookHost"/> waits a frame for <c>component.MountCompleted</c>
        /// before <c>SusControlPanel</c> exists (T-3096) — so the report must pick up the panel
        /// whenever it becomes ready, not only at attach time (card T-3143).
        /// </summary>
        public SusControlPanel Panel
        {
            get => _panel;
            set => _panel = value;
        }

        /// <summary>
        /// Where the anomalies come from. Defaults to <c>SusUiProbe.GetAnomalies</c> —
        /// the same detector the MCP health probe uses, so zone E and <c>sus_ui_health</c> can
        /// never disagree about one canvas. It is a settable seam because that detector answers
        /// only for an ATTACHED panel with a computed layout, which an EditMode test does not
        /// have; a driver with more to say (the frame conveyor, a QA sink) can also widen it.
        /// </summary>
        public Func<VisualElement, IReadOnlyList<string>> HealthSource { get; set; } = DefaultHealthSource;

        /// <summary>
        /// The default of <see cref="HealthSource"/>: <c>SusUiProbe.GetAnomalies</c> where the
        /// probe exists (editor and development builds) and "no anomalies" in a release player,
        /// where the diagnostics layer is compiled out and there is nothing honest to report.
        /// </summary>
        public static IReadOnlyList<string> DefaultHealthSource(VisualElement canvas)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return SusUiProbe.GetAnomalies(canvas);
#else
            return Array.Empty<string>();
#endif
        }

        /// <summary>Anomalies of the stage canvas as of the last <see cref="Refresh"/>.</summary>
        public IReadOnlyList<string> Anomalies => _anomalies;

        /// <summary>How many anomalies the strip is showing — zone A mirrors this.</summary>
        public int AnomalyCount => _anomalies.Count;

        /// <summary>Raised when <see cref="AnomalyCount"/> changes.</summary>
        public event Action<int> AnomalyCountChanged;

        /// <summary>Chip texts currently on the strip, oldest first.</summary>
        public IReadOnlyList<string> Chips
        {
            get
            {
                var texts = new List<string>();
                foreach (var child in _chips.Children())
                    if (child is Label l) texts.Add(l.text);
                return texts;
            }
        }

        /// <summary>The warning line, empty when every declared event has fired.</summary>
        public string UnfiredText => _unfired.text;

        /// <summary>True while the warning line is shown.</summary>
        public bool HasUnfired => !_unfired.ClassListContains("sus-sb-hidden");

        /// <summary>The "health N" text.</summary>
        public string HealthText => _health.text;

        /// <summary>The frame line ("frame: —" until a comparer is registered).</summary>
        public string FrameText => _frame.text;

        /// <summary>Frame verdict as of the last <see cref="Refresh"/>.</summary>
        public SusStoryFrameResult Frame => _frameResult;

        // ── driven by the shell ──────────────────────────────────────────────

        /// <summary>
        /// Points the probe at a freshly mounted story: drops the previous session (chips,
        /// unfired list, anomalies) and subscribes to every event of the new instance. This is
        /// the reset the mock-up asks for when the story changes — a chip from the previous
        /// component would be a lie about this one.
        ///
        /// <paramref name="panel"/> is zone D's control panel for the SAME instance, optional and
        /// null by default so every existing caller (three EditMode tests, T-3040) keeps
        /// compiling unchanged. The shell (<see cref="SusStorybookHost"/>) passes its own
        /// <c>SusControlPanel</c> — built moments earlier from the same <c>SusStoryContext</c> —
        /// so <see cref="BuildReport"/> can read props/controls/exclusions/manual controls without
        /// a second wire to the ledger (card T-3143; the panel already paid that cost).
        /// </summary>
        public void Attach(SusStoryEntry entry, SusComponent instance, VisualElement canvas, SusControlPanel panel = null)
        {
            if (_disposed) return;

            _entry = entry;
            _instance = instance;
            _canvas = canvas;
            _panel = panel;
            _events.Attach(instance);

            SusStoryQa.NotifyMounted(entry, instance, canvas);
            Refresh();
        }

        /// <summary>
        /// Detaches from the current story, handing the finished session to the QA sinks first
        /// (that report is the input of the session JSON of plan §4.7).
        /// </summary>
        public void Clear()
        {
            if (_entry != null || _instance != null) SusStoryQa.NotifyFinished(BuildReport());

            _entry = null;
            _instance = null;
            _canvas = null;
            _panel = null;
            _events.Reset();
            _frameResult = SusStoryFrameResult.Unavailable;
            SetAnomalies(Array.Empty<string>());
            Render();
        }

        /// <summary>
        /// Re-reads health and the frame verdict and redraws. Called by the shell on its stage
        /// tick; public so a test asks for it directly instead of waiting for a scheduler.
        /// </summary>
        public void Refresh()
        {
            if (_disposed) return;

            _frameResult = _entry == null
                ? SusStoryFrameResult.Unavailable
                : SusStoryFrame.Compare(_entry.Id, _canvas);

            SetAnomalies(ReadAnomalies());
            Render();
        }

        /// <summary>Everything zone E saw of the current story — the session report row.</summary>
        public SusStoryProbeReport BuildReport() =>
            new SusStoryProbeReport(
                _entry?.Id,
                new List<string>(_events.Declared),
                new List<string>(_events.Calls),
                new List<string>(_events.Unfired),
                new List<string>(_anomalies),
                _frameResult,
                props: ReadProps(),
                controls: _panel == null ? Array.Empty<string>() : _panel.Controls.Select(c => c.Prop.Name).ToList(),
                uncovered: _panel == null ? Array.Empty<string>() : new List<string>(_panel.Uncovered),
                excluded: _panel?.Context?.Story == null ? Array.Empty<string>() : new List<string>(_panel.Context.Story.Exclusions.Keys),
                manualControls: _panel?.Context?.Story == null ? Array.Empty<string>() : new List<string>(_panel.Context.Story.ManualControls.Keys));

        /// <summary>
        /// Every prop the mounted instance declares. Read straight off <see cref="_instance"/>
        /// (not off the panel) so the numerator of R134 L1 exists even when zone D failed to
        /// build a panel for some other reason — the count of props a component HAS does not
        /// depend on whether a control panel exists to show them.
        /// </summary>
        IReadOnlyList<string> ReadProps()
        {
            if (_instance == null) return Array.Empty<string>();
            try
            {
                return _instance.DescribeProps().Select(p => p.Name).ToList();
            }
            catch (Exception e)
            {
                SusLog.Error("[storybook] could not describe props for report: " + e);
                return Array.Empty<string>();
            }
        }

        // ── internals ────────────────────────────────────────────────────────

        IReadOnlyList<string> ReadAnomalies()
        {
            if (_canvas == null || _entry == null) return Array.Empty<string>();
            var source = HealthSource;
            if (source == null) return Array.Empty<string>();
            try
            {
                return source(_canvas) ?? (IReadOnlyList<string>)Array.Empty<string>();
            }
            catch (Exception e)
            {
                SusLog.Error("[storybook] health probe failed: " + e);
                return Array.Empty<string>();
            }
        }

        void SetAnomalies(IReadOnlyList<string> anomalies)
        {
            int before = _anomalies.Count;
            _anomalies.Clear();
            for (int i = 0; i < anomalies.Count; i++) _anomalies.Add(anomalies[i]);
            if (_anomalies.Count != before) AnomalyCountChanged?.Invoke(_anomalies.Count);
        }

        void OnEventFired(string eventName, string call)
        {
            SusStoryQa.NotifyEvent(_entry, eventName, call);
            Render();
        }

        void Render()
        {
            RenderChips();
            RenderUnfired();
            RenderHealth();
            _frame.text = _frameResult.Describe();
            _frame.EnableInClassList("sus-sb-probe__frame--bad", _frameResult.IsAnomaly);
            RenderAnomalies();
        }

        void RenderChips()
        {
            _chips.Clear();
            var recent = _events.Recent;
            for (int i = 0; i < recent.Count; i++)
            {
                var chip = new Label(recent[i]);
                chip.AddToClassList("sus-sb-probe__chip");
                _chips.Add(chip);
            }
            _noCalls.EnableInClassList("sus-sb-hidden", recent.Count > 0);
        }

        void RenderUnfired()
        {
            var unfired = _events.Unfired;
            _unfired.text = unfired.Count == 0 ? string.Empty : UnfiredPrefix + string.Join(", ", unfired);
            _unfired.EnableInClassList("sus-sb-hidden", unfired.Count == 0);
        }

        void RenderHealth()
        {
            int n = _anomalies.Count;
            _health.text = "health " + n.ToString(CultureInfo.InvariantCulture);
            bool bad = n > 0;
            _dot.EnableInClassList("sus-sb-probe__dot--bad", bad);
            _health.EnableInClassList("sus-sb-probe__health--bad", bad);
        }

        void RenderAnomalies()
        {
            _anomalyList.Clear();
            for (int i = 0; i < _anomalies.Count; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("sus-sb-probe__anomaly");

                var icon = new SusIconElement("warning");
                icon.AddToClassList("sus-sb-probe__anomaly-icon");

                var text = new Label(_anomalies[i]);
                text.AddToClassList("sus-sb-probe__anomaly-text");

                row.Add(icon);
                row.Add(text);
                _anomalyList.Add(row);
            }
            _anomalyList.EnableInClassList("sus-sb-hidden", _anomalies.Count == 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _events.Fired -= OnEventFired;
            _events.Dispose();
            _entry = null;
            _instance = null;
            _canvas = null;
            _panel = null;
        }
    }
}
