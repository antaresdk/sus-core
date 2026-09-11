using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Nav;

namespace Sharq.Core.Storybook.Controls
{
    /// <summary>
    /// Zone D of the storybook: every prop of the mounted component, grouped, with the control
    /// the table of §4.3 gives it, and - at the bottom - the number that makes the panel
    /// falsifiable: "props N · controls M". When M plus the story's declared exclusions does not
    /// reach N, the panel says "story defect" instead of quietly showing fewer controls than the
    /// component has props (gate §5 of the plan, card T-3034).
    ///
    /// The panel builds itself from <c>DescribeProps()</c> and from nothing else. It never knows
    /// which component it is looking at, so the paid packages need no per-component control code.
    /// </summary>
    public sealed class SusControlPanel : VisualElement, IDisposable
    {
        /// <summary>Headline of a component that declares no props at all.</summary>
        public const string EmptyTitle = "The component has no props";

        /// <summary>Body of the empty state - an empty panel is a fact, not a failure.</summary>
        public const string EmptyText =
            "The panel is empty and that is normal: no [CreateProperty] prop is declared. " +
            "Environment axes still act on the stage.";

        /// <summary>Footer badge when every prop is accounted for.</summary>
        public const string CoverageOkBadge = "1 : 1";

        /// <summary>Footer badge when at least one prop has neither a control nor an exclusion.</summary>
        public const string CoverageDefectBadge = "story defect";

        static readonly SusPropGroup[] GroupOrder =
        {
            SusPropGroup.Axis,
            SusPropGroup.State,
            SusPropGroup.Content,
            SusPropGroup.Behavior,
            SusPropGroup.Data,
            SusPropGroup.Other,
        };

        readonly List<SusControl> _controls = new();
        readonly List<string> _controlledProps = new();
        readonly Dictionary<string, string> _defaults = new(StringComparer.Ordinal);
        readonly List<string> _uncovered = new();
        readonly List<string> _dead = new();
        readonly List<string> _excluded = new();

        readonly Label _title = new();
        readonly ScrollView _groups = new();
        readonly VisualElement _empty = new();
        readonly VisualElement _foot = new();
        readonly Label _count = new();
        readonly Label _badge = new();
        readonly Label _uncoveredLine = new();
        readonly Label _deadLine = new();
        readonly Label _excludedLine = new();

        bool _disposed;

        public SusControlPanel(
            SusComponent component,
            string title = null,
            SusStoryContext story = null,
            SusStoryRoute route = null,
            IReadOnlyList<string> colorTokens = null)
        {
            Component = component ?? throw new ArgumentNullException(nameof(component));
            Context = new SusControlContext(component, story, colorTokens);

            // The engine's own providers (icon picker, T-3035). Idempotent, and here rather than
            // in a bootstrap so a panel built by a test or a probe is the panel the buyer sees.
            SusControlFactory.RegisterDefaults();

            AddToClassList("sus-sb-ctlpanel");
            name = "sus-storybook-controls";

            _title.AddToClassList("sus-sb-ctlpanel__title");
            _title.text = string.IsNullOrEmpty(title) ? "Props" : "Props · " + title;

            _groups.AddToClassList("sus-sb-ctlpanel__groups");

            _empty.AddToClassList("sus-sb-ctlpanel__empty");
            var emptyTitle = new Label(EmptyTitle);
            emptyTitle.AddToClassList("sus-sb-ctlpanel__empty-title");
            var emptyText = new Label(EmptyText);
            emptyText.AddToClassList("sus-sb-ctlpanel__empty-text");
            _empty.Add(emptyTitle);
            _empty.Add(emptyText);

            _foot.AddToClassList("sus-sb-ctlpanel__foot");
            _count.AddToClassList("sus-sb-ctlpanel__count");
            _badge.AddToClassList("sus-sb-ctlpanel__badge");
            _uncoveredLine.AddToClassList("sus-sb-ctlpanel__hole");
            _deadLine.AddToClassList("sus-sb-ctlpanel__hole");
            _deadLine.AddToClassList("sus-sb-ctlpanel__hole--dead");
            _excludedLine.AddToClassList("sus-sb-ctlpanel__hole");
            _excludedLine.AddToClassList("sus-sb-ctlpanel__hole--excluded");

            var counters = new VisualElement();
            counters.AddToClassList("sus-sb-ctlpanel__counters");
            counters.Add(_count);
            counters.Add(_badge);

            _foot.Add(counters);
            _foot.Add(_uncoveredLine);
            _foot.Add(_deadLine);
            _foot.Add(_excludedLine);

            Add(_title);
            Add(_groups);
            Add(_empty);
            Add(_foot);

            BuildControls(story);
            ApplyRoute(route);
            RefreshFooter();
        }

        /// <summary>The component the controls drive.</summary>
        public SusComponent Component { get; }

        /// <summary>Context handed to every control (component, story, colour tokens).</summary>
        public SusControlContext Context { get; }

        /// <summary>Controls in panel order.</summary>
        public IReadOnlyList<SusControl> Controls => _controls;

        /// <summary>
        /// Names of the props zone D actually built a control for — the SAME set as
        /// <see cref="Controls"/>, but snapshotted at build time and kept for the life of the
        /// object, <see cref="Dispose"/> included.
        ///
        /// It exists because the session report of plan §4.7 is written during teardown, and
        /// teardown had an order (card T-3184). <c>SusStorybookHost.Unmount</c> disposes zone D
        /// and only then calls <c>_probe.Clear()</c>, which is what hands the finished report to
        /// the QA sinks; <see cref="Dispose"/> empties <see cref="Controls"/>, so every report
        /// but the last one of a session named ZERO controls for a panel that had built them all.
        /// Measured on the live sweep of 2026-09-09
        /// (<c>sus-dev/docs/qa/reports/storybook-session.json</c>): 96 stories, 88 with props,
        /// exactly ONE (the last, never unmounted) with a non-empty <c>controls</c> — and R134
        /// layer 1 read that as 86 stories whose props have no control. The panel was fine; the
        /// list was gone. A report about what the buyer just saw must not depend on whether the
        /// thing he saw has been torn down yet, so the answer is snapshotted rather than derived
        /// from live objects.
        /// </summary>
        public IReadOnlyList<string> ControlledProps => _controlledProps;

        /// <summary>Props the component declares - the N of "props N · controls M".</summary>
        public int PropCount { get; private set; }

        /// <summary>Controls the panel built - the M.</summary>
        public int ControlCount => _controls.Count;

        /// <summary>Props with neither a control nor a declared exclusion.</summary>
        public IReadOnlyList<string> Uncovered => _uncovered;

        /// <summary>Props nobody reads (<see cref="SusPropInfo.Dead"/>) at the last refresh.</summary>
        public IReadOnlyList<string> DeadProps => _dead;

        /// <summary>Props the story deliberately left without a control.</summary>
        public IReadOnlyList<string> Excluded => _excluded;

        /// <summary>True when props == controls + exclusions.</summary>
        public bool IsCovered => _uncovered.Count == 0;

        /// <summary>Text of the footer counter, exactly as shown.</summary>
        public string CountText => _count.text;

        /// <summary>Text of the coverage badge, exactly as shown.</summary>
        public string BadgeText => _badge.text;

        /// <summary>True when the component has no props and the empty state is showing.</summary>
        public bool IsEmpty => PropCount == 0;

        /// <summary>Values at the moment the panel was built, before any deep link was applied.</summary>
        public IReadOnlyDictionary<string, string> Defaults => _defaults;

        /// <summary>Current values of every control that can be restored from a link.</summary>
        public IReadOnlyDictionary<string, string> Values
        {
            get
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < _controls.Count; i++)
                {
                    var c = _controls[i];
                    if (!c.Restorable) continue;
                    values[c.Prop.Name] = c.StringValue;
                }
                return values;
            }
        }

        /// <summary>Raised after any control wrote its prop.</summary>
        public event Action<SusControl> ValueChanged;

        /// <summary>
        /// The deep link for the current state: only what differs from the values the story was
        /// built with reaches the query (<see cref="SusStoryRoute.FromValues"/>).
        /// </summary>
        public SusStoryRoute BuildRoute(string storyId) =>
            SusStoryRoute.FromValues(storyId, Values, _defaults);

        /// <summary>Finds a control by prop name, or null.</summary>
        public SusControl Find(string propName)
        {
            for (int i = 0; i < _controls.Count; i++)
                if (string.Equals(_controls[i].Prop.Name, propName, StringComparison.Ordinal))
                    return _controls[i];
            return null;
        }

        /// <summary>Pulls every control back from its prop and re-reads the footer numbers.</summary>
        public void Refresh()
        {
            for (int i = 0; i < _controls.Count; i++)
            {
                _controls[i].Refresh();
                _controls[i].UpdateDependency();
            }
            RefreshFooter();
        }

        void BuildControls(SusStoryContext story)
        {
            IReadOnlyList<SusPropInfo> props;
            try
            {
                props = Component.DescribeProps();
            }
            catch (Exception e)
            {
                SusLog.Error("[storybook] could not describe props: " + e);
                props = Array.Empty<SusPropInfo>();
            }

            PropCount = props.Count;

            var buckets = new Dictionary<SusPropGroup, List<SusControl>>();
            for (int i = 0; i < props.Count; i++)
            {
                var prop = props[i];
                _defaults[prop.Name] = SusControlFactory.ToQueryValue(prop.Value);

                var reason = story != null && story.Exclusions.TryGetValue(prop.Name, out var r) ? r : null;
                if (reason != null)
                {
                    _excluded.Add(prop.Name + " (" + reason + ")");
                    continue;
                }

                var control = SusControlFactory.Build(prop, Context, DeclaredAxisSet(story, prop));
                if (control == null)
                {
                    // No control and no declared reason: a hole, and the footer will say so.
                    _uncovered.Add(prop.Name);
                    continue;
                }

                control.ValueChanged += OnControlChanged;
                _controls.Add(control);
                _controlledProps.Add(prop.Name);   // survives Dispose — card T-3184

                if (!buckets.TryGetValue(prop.Group, out var bucket))
                {
                    bucket = new List<SusControl>();
                    buckets[prop.Group] = bucket;
                }
                bucket.Add(control);
            }

            for (int i = 0; i < GroupOrder.Length; i++)
            {
                var group = GroupOrder[i];
                if (!buckets.TryGetValue(group, out var bucket) || bucket.Count == 0) continue;

                var section = new VisualElement();
                section.AddToClassList("sus-sb-ctlpanel__group");
                section.name = "sus-storybook-group-" + group.ToString().ToLowerInvariant();

                var head = new Label(GroupTitle(group));
                head.AddToClassList("sus-sb-ctlpanel__group-title");
                section.Add(head);

                for (int j = 0; j < bucket.Count; j++) section.Add(bucket[j]);
                _groups.Add(section);
            }

            bool empty = PropCount == 0;
            _empty.EnableInClassList(SusControl.HiddenClass, !empty);
            _groups.EnableInClassList(SusControl.HiddenClass, empty);
        }

        /// <summary>
        /// The closed set the STORY declared for this prop, or null (card T-3379, decision D26).
        ///
        /// Zone D already turned a closed set into a segmented picker - but only when the
        /// COMPONENT clamped the prop with <c>UseAllowed</c>. On the 9 of 17 kit components whose
        /// <c>Variant</c> is an unclamped <c>Prop&lt;string&gt;</c> the buyer got a bare text
        /// field and had to guess the spelling of a value the component's own USS enumerates.
        /// This is the other producer: the story names the values once, and zone D can no longer
        /// accept a value outside them, because a picker has no way to type one.
        ///
        /// Returns null the moment the component speaks for itself: <see cref="SusStoryAxis"/>
        /// resolves the two producers in one place, and a second resolution order here would
        /// eventually disagree with the matrix.
        /// </summary>
        static IReadOnlyList<string> DeclaredAxisSet(SusStoryContext story, SusPropInfo prop)
        {
            var entry = story?.Entry;
            if (entry == null || !entry.DeclaresAxis) return null;
            if (prop.ReadOnly || prop.ValueType != typeof(string)) return null;

            var axis = SusStoryAxis.FromStory(entry, prop.Name);
            return axis.IsClosed ? axis.Values : null;
        }

        void ApplyRoute(SusStoryRoute route)
        {
            if (route == null || route.Query.Count == 0) return;
            foreach (var kv in route.Query)
            {
                var control = Find(kv.Key);
                if (control == null) continue;
                if (!control.SetFromString(kv.Value))
                {
                    SusLog.Warn("[storybook] link value '" + kv.Value + "' does not fit prop '" +
                                kv.Key + "'; the story keeps its own value.");
                }
            }
        }

        void OnControlChanged(SusControl control)
        {
            // One prop can gate another, so the whole panel re-reads its conditions after a write.
            for (int i = 0; i < _controls.Count; i++) _controls[i].UpdateDependency();
            RefreshFooter();
            ValueChanged?.Invoke(control);
        }

        void RefreshFooter()
        {
            _dead.Clear();
            for (int i = 0; i < _controls.Count; i++)
            {
                if (_controls[i].IsDead) _dead.Add(_controls[i].Prop.Name);
            }

            _count.text = "props " + PropCount + " · controls " + ControlCount;
            _badge.text = IsCovered ? CoverageOkBadge : CoverageDefectBadge;
            _badge.EnableInClassList("sus-sb-ctlpanel__badge--ok", IsCovered);
            _badge.EnableInClassList("sus-sb-ctlpanel__badge--defect", !IsCovered);

            SetLine(_uncoveredLine, "uncovered: ", _uncovered);
            SetLine(_deadLine, "dead props: ", _dead);
            SetLine(_excludedLine, "no control on purpose: ", _excluded);
            _foot.EnableInClassList(SusControl.HiddenClass, IsEmpty);
        }

        static void SetLine(Label label, string prefix, List<string> items)
        {
            bool has = items.Count > 0;
            label.text = has ? prefix + string.Join(" · ", items) : string.Empty;
            label.EnableInClassList(SusControl.HiddenClass, !has);
        }

        static string GroupTitle(SusPropGroup group)
        {
            switch (group)
            {
                case SusPropGroup.Axis: return "DESIGN AXES";
                case SusPropGroup.State: return "STATE";
                case SusPropGroup.Content: return "CONTENT";
                case SusPropGroup.Behavior: return "BEHAVIOUR";
                case SusPropGroup.Data: return "DATA";
                default: return "OTHER";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _controls.Count; i++)
            {
                _controls[i].ValueChanged -= OnControlChanged;
                _controls[i].Dispose();
            }
            _controls.Clear();
            ValueChanged = null;
        }
    }
}
