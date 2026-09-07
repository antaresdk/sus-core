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

                var control = SusControlFactory.Build(prop, Context);
                if (control == null)
                {
                    // No control and no declared reason: a hole, and the footer will say so.
                    _uncovered.Add(prop.Name);
                    continue;
                }

                control.ValueChanged += OnControlChanged;
                _controls.Add(control);

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
