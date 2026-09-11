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

        /// <summary>
        /// Shortest gap between two refreshes of the strip, in milliseconds (plan
        /// ARCH-20260911-STORYBOOK-SHELL.md §4.8, decision D17, card T-3358).
        ///
        /// Until T-3358 the shell refreshed the strip on its 120 ms overlay tick, unconditionally
        /// - 8,3 refreshes a second, each of them recomputing health by walking the whole canvas
        /// subtree, rewriting three texts and rebuilding the anomaly list from scratch. The strip
        /// visibly trembled, and the trembling was not cosmetic: rewriting a text changes the
        /// width of a flex row, which is a layout pass, which is one of the three named sources of
        /// "everything jumps" (card T-3362).
        ///
        /// The number lives here and not in the shell because the shell only schedules it: one
        /// declared interval, two readers, no second literal to drift.
        /// </summary>
        public const long RefreshIntervalMs = 500;

        /// <summary>The strip's own label for "nothing is wrong".</summary>
        public const string HealthOkText = "0 anomalies";

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

        // Card T-3358: what the strip last WROTE, so a refresh that found nothing new writes
        // nothing. Comparing rendered text against the text already on screen is cheaper than
        // the layout pass an identical assignment costs.
        string _healthShown;
        string _frameShown;
        string _unfiredShown;
        string _chipsShown;
        string _anomaliesShown;
        bool _dirty = true;

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
            _dirty = false;
            Render();
        }

        /// <summary>
        /// Something happened that the strip may have to show: a story mounted, a prop was
        /// written, an environment axis moved, a layout pass finished (card T-3358, D17).
        ///
        /// This is the whole reason the strip no longer needs a fast tick. Health has no event of
        /// its own - a story that collapses to zero size does it silently, during layout - so the
        /// shell cannot subscribe to "health changed". But it CAN name every occasion on which
        /// health could have changed, and then the timer's only job is to be a floor under how
        /// often those occasions are honoured.
        /// </summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>True while something asked for a refresh that has not happened yet.</summary>
        public bool Dirty => _dirty;

        /// <summary>
        /// How many times the strip actually CHANGED something on screen (card T-3358). The
        /// acceptance figure of D17 is "writes without a change == 0", and a counter is the only
        /// way to state it: a screenshot cannot tell an identical rewrite from no rewrite, while
        /// the layout engine can.
        /// </summary>
        public int Writes { get; private set; }

        /// <summary>
        /// Refreshes only if <see cref="MarkDirty"/> was called since the last one. Returns
        /// whether it did. What the shell's <see cref="RefreshIntervalMs"/> tick calls: an idle
        /// story costs one boolean read per tick and not one canvas walk.
        /// </summary>
        public bool RefreshIfDirty()
        {
            if (_disposed || !_dirty) return false;
            Refresh();
            return true;
        }

        /// <summary>
        /// Re-reads health and the frame verdict and redraws. Public so a test asks for it
        /// directly instead of waiting for a scheduler; the shell goes through
        /// <see cref="RefreshIfDirty"/> instead (card T-3358).
        /// </summary>
        public void Refresh()
        {
            if (_disposed) return;
            _dirty = false;

            // Card T-3358, plan §4.8: "a field that does not exist is not polled at all". Core
            // ships a comparer that never has a canon, so while nothing else is registered the
            // verdict is not a dash worth computing - it is a line worth not drawing. The old
            // code called Compare 8,3 times a second to print "frame: -": the buyer lost nothing
            // by its absence, which is exactly the test for a dead indicator.
            _frameResult = _entry == null || SusStoryFrame.IsStub
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
                // ControlledProps, not Controls.Select(...): the report is built during teardown
                // (SusStorybookHost.Unmount disposes zone D, THEN calls Clear() which notifies the
                // sinks), and Dispose empties the live control list. Reading the snapshot makes the
                // report independent of teardown order — card T-3184, where that order turned a
                // fully covered 96-story sweep into 86 phantom R134 L1 control-gaps.
                controls: _panel == null ? Array.Empty<string>() : new List<string>(_panel.ControlledProps),
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
            RenderFrame();
            RenderAnomalies();
        }

        void RenderChips()
        {
            // Card T-3358: the four chips of "OnOpen() OnClose() OnOpen() OnClose()" collapse to
            // two carrying "x2". Same facts, half the width, and - because the signature below is
            // compared before the row is touched - no rebuild at all while nothing new fired.
            var recent = _events.RecentCollapsed;
            var signature = string.Join("␟", recent);
            if (signature == _chipsShown) return;
            _chipsShown = signature;
            Writes++;

            _chips.Clear();
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
            var text = unfired.Count == 0 ? string.Empty : UnfiredPrefix + string.Join(", ", unfired);
            if (text == _unfiredShown) return;
            _unfiredShown = text;
            Writes++;

            _unfired.text = text;
            _unfired.EnableInClassList("sus-sb-hidden", unfired.Count == 0);
        }

        void RenderHealth()
        {
            int n = _anomalies.Count;

            // Card T-3358 defect 2: the strip used to print "health 0" beside a GREEN dot, and
            // "health 0" reads as "no health left" while it means "no anomalies found" - the
            // caption promised the opposite of what the colour said. The number counts anomalies,
            // so the word next to it is the one it counts.
            var text = n == 0
                ? HealthOkText
                : n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " anomaly" : " anomalies");
            if (text == _healthShown) return;
            _healthShown = text;
            Writes++;

            _health.text = text;
            bool bad = n > 0;
            _dot.EnableInClassList("sus-sb-probe__dot--bad", bad);
            _health.EnableInClassList("sus-sb-probe__health--bad", bad);
        }

        void RenderFrame()
        {
            // While nothing can answer, the field is not drawn (card T-3358 defect 3): a dash
            // that never becomes anything else is a column of width taken from the strip for no
            // decision and no knowledge.
            bool available = !SusStoryFrame.IsStub;
            var text = available ? _frameResult.Describe() : string.Empty;
            if (text == _frameShown) return;
            _frameShown = text;
            Writes++;

            _frame.text = text;
            _frame.EnableInClassList("sus-sb-hidden", !available);
            _frame.EnableInClassList("sus-sb-probe__frame--bad", _frameResult.IsAnomaly);
        }

        void RenderAnomalies()
        {
            var signature = string.Join("␟", _anomalies);
            if (signature == _anomaliesShown) return;
            _anomaliesShown = signature;
            Writes++;

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
