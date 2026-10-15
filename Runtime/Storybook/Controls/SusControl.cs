using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.Controls
{
    /// <summary>
    /// Which widget a prop is driven by. One entry per row of the control table
    /// (plan ARCH-20260907-STORYBOOK-ENGINE §4.3).
    /// </summary>
    public enum SusControlKind
    {
        /// <summary><c>bool</c> - switch.</summary>
        Toggle = 0,
        /// <summary>
        /// Closed set of five values or fewer whose texts fit the label budget
        /// (<see cref="SusControlFactory.SegmentLabelBudget"/>) - segmented picker.
        /// </summary>
        Segment = 1,
        /// <summary>Any other closed set, and every colour name - dropdown.</summary>
        Dropdown = 2,
        /// <summary>Free <c>string</c> - text field (the empty string is a legal value).</summary>
        Text = 3,
        /// <summary>String prop named <c>*Icon</c> - glyph name field (picker: card T-3035).</summary>
        Icon = 4,
        /// <summary>
        /// Numeric - a numeric field, plus a slider when the range is declared, plus a chip per
        /// declared special value.
        /// </summary>
        Number = 5,
        /// <summary><c>Color</c> - skin token swatches plus a free value.</summary>
        Color = 6,
        /// <summary><c>List&lt;T&gt;</c> - row count with an expander.</summary>
        List = 7,
        /// <summary>Model / <c>ReadonlyProp</c> - display only, refreshed from change events.</summary>
        ReadOnly = 8,
    }

    /// <summary>
    /// One row of zone D (card T-3034): the prop's name, its badges, the widget that drives it,
    /// and the
    /// captions that explain why it may be doing nothing right now.
    ///
    /// A control NEVER decides what is legal: bounds come from <c>[SusRange]</c>, options from
    /// <c>DescribeAllowed</c>, the dependency verdict from the component. It writes through
    /// <see cref="SusPropInfo.TrySetValue"/> - a write that does not fit is refused, not thrown,
    /// because a control panel must not die on a typo.
    ///
    /// Appearance is entirely USS (<c>Storybook.uss</c>, section "zone D"); this file contains no
    /// <c>.style</c> write at all (R53/R120).
    /// </summary>
    public class SusControl : VisualElement, IDisposable
    {
        /// <summary>USS class that hides an element; the shell uses the same one.</summary>
        public const string HiddenClass = "sb-hidden";

        /// <summary>Caption of a text prop that currently holds the empty string.</summary>
        public const string EmptyStringNote = "empty string · legal";

        /// <summary>Dev caption of a numeric prop with no <c>[SusRange]</c>: it is a plain field.</summary>
        public const string NoRangeNote = "range not declared";

        /// <summary>Dev caption of a numeric prop whose <c>[SusRange]</c> / <c>[SusRangeFrom]</c> contradicts itself.</summary>
        public const string RangeInvalidNote = "range invalid";

        /// <summary>Dev caption of a numeric prop whose value lies outside its declared range.</summary>
        public const string OutOfRangeNote = "out of range";

        /// <summary>Dev caption of a closed set that listed one value twice (ignoring case).</summary>
        public const string DuplicateValuesNote = "duplicate values";

        /// <summary>Dev caption of a closed set that the story declared, not the component.</summary>
        public const string SetFromStoryNote = "set from story";

        /// <summary>Caption of a display-only row.</summary>
        public const string ReadOnlyNote = "updated from events";

        readonly Label _name = new();
        readonly Label _deadBadge = new("not read");
        readonly Label _manualBadge = new("manual");
        readonly Label _note = new();
        readonly Label _dependency = new();
        readonly VisualElement _body = new();

        IDisposable _subscription;
        bool _syncing;
        bool _disposed;

        protected SusControl(SusPropInfo prop, SusControlKind kind, SusControlContext context)
        {
            Prop = prop ?? throw new ArgumentNullException(nameof(prop));
            Context = context;
            Kind = kind;

            AddToClassList("sb-ctl");
            AddToClassList("sb-ctl--" + kind.ToString().ToLowerInvariant());
            name = "sus-storybook-control-" + prop.Name;

            var head = new VisualElement();
            head.AddToClassList("sb-ctl__head");

            _name.text = prop.Name;
            _name.AddToClassList("sb-ctl__name");
            head.Add(_name);

            _deadBadge.AddToClassList("sb-ctl__badge");
            _deadBadge.AddToClassList("sb-ctl__badge--dead");
            head.Add(_deadBadge);

            _manualBadge.AddToClassList("sb-ctl__badge");
            _manualBadge.AddToClassList("sb-ctl__badge--manual");
            head.Add(_manualBadge);

            _body.AddToClassList("sb-ctl__body");

            _note.AddToClassList("sb-ctl__note");
            _dependency.AddToClassList("sb-ctl__dependency");

            Add(head);
            Add(_body);
            Add(_note);
            Add(_dependency);

            IsManual = context != null && context.IsManual(prop.Name);

            // Snapshot, not a live read: every control subscribes to its prop, and a subscriber
            // makes the prop look alive. The observation is therefore taken HERE - before this
            // control attaches itself - and the panel is built after the story has rendered once,
            // which is exactly when SusPropInfo.Dead is meaningful.
            IsDead = prop.Dead;

            _manualBadge.EnableInClassList(HiddenClass, !IsManual);
            _deadBadge.EnableInClassList(HiddenClass, !IsDead);
            SetNote(null);
            UpdateDependency();
        }

        /// <summary>The prop this row drives.</summary>
        public SusPropInfo Prop { get; }

        /// <summary>Widget family - see <see cref="SusControlKind"/>.</summary>
        public SusControlKind Kind { get; }

        /// <summary>Panel-wide context (component, story, colour tokens); may be null in tests.</summary>
        public SusControlContext Context { get; }

        /// <summary>The story declared a hand-written control for this prop (grey badge).</summary>
        public bool IsManual { get; }

        /// <summary>
        /// Nobody read the prop when this control was built - the red badge of §4.3.1. A
        /// snapshot: see the constructor for why a live read cannot answer this question.
        /// </summary>
        public bool IsDead { get; }

        /// <summary>Every declared <c>[SusDependsOn]</c> currently holds.</summary>
        public bool IsActive { get; private set; } = true;

        /// <summary>The row where a subclass puts its widget.</summary>
        public VisualElement Body => _body;

        /// <summary>Caption under the widget (range, "empty string", "updated from events").</summary>
        public string Note => _note.text;

        /// <summary>Caption naming the dependency, or empty when the prop is unconditional.</summary>
        public string DependencyNote => _dependency.text;

        /// <summary>
        /// True when the current value can be written into a deep link and read back. Lists and
        /// models cannot, so they stay out of the query instead of producing a link that silently
        /// restores nothing.
        /// </summary>
        public virtual bool Restorable => true;

        /// <summary>Current value in the canonical string form used by the deep link.</summary>
        public virtual string StringValue => SusControlFactory.ToQueryValue(Prop.Value);

        /// <summary>Raised AFTER a write reached the prop.</summary>
        public event Action<SusControl> ValueChanged;

        /// <summary>Applies a value that arrived as text (deep link, test, probe).</summary>
        public virtual bool SetFromString(string text) => Write(text);

        /// <summary>
        /// Whether this control is ABLE to hold the given text — asked before a value is put back
        /// from somewhere else (the story seed of <c>SusControlPanel.ResetToStory</c>, card T-3406).
        ///
        /// The base control says yes and lets the prop refuse: an unclamped prop legitimately takes
        /// any string of its type. A CLOSED SET answers from its own options, because a picker has
        /// no place to show — let alone type — a value outside them (plan D26), so writing one
        /// would leave zone D showing a set of buttons with none of them active.
        /// </summary>
        public virtual bool Accepts(string text) => true;

        /// <summary>Pulls the current prop value back into the widget.</summary>
        public void Refresh()
        {
            if (_disposed) return;
            _syncing = true;
            try
            {
                OnRefresh();
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>Re-evaluates <c>[SusDependsOn]</c> and dims the row when it does not hold.</summary>
        public void UpdateDependency()
        {
            var component = Context?.Component;
            string declared = component?.DescribeDependency(Prop);
            if (string.IsNullOrEmpty(declared))
            {
                IsActive = true;
                _dependency.text = string.Empty;
                _dependency.AddToClassList(HiddenClass);
                RemoveFromClassList("sb-ctl--inert");
                return;
            }

            IsActive = component.IsDependencySatisfied(Prop);
            var text = "depends on " + declared;
            if (!IsActive) text += " · inert while it does not hold";
            _dependency.text = text;
            _dependency.RemoveFromClassList(HiddenClass);
            EnableInClassList("sb-ctl--inert", !IsActive);
        }

        /// <summary>Subclass hook: write the prop value into the widget.</summary>
        protected virtual void OnRefresh() { }

        /// <summary>Subclass hook: release what the subclass subscribed to (runs once, from <see cref="Dispose"/>).</summary>
        protected virtual void OnDispose() { }

        /// <summary>Writes through the prop; returns false when the value does not fit.</summary>
        protected bool Write(object value)
        {
            if (_syncing || _disposed) return false;
            if (!Prop.TrySetValue(value)) return false;
            Refresh();
            ValueChanged?.Invoke(this);
            return true;
        }

        /// <summary>
        /// True while the host wants the dev-only captions (<see cref="SusControlContext.ShowDiagnostics"/>).
        /// A caption that explains the NUMBER to a buyer (range, step, unit) is never gated by it;
        /// one that explains the declaration to a developer ("range not declared") always is
        /// (ARCH-20261014-SB-PROP-CONTROLS D8, d:b763b1).
        /// </summary>
        protected bool ShowDiagnostics => Context == null || Context.ShowDiagnostics;

        /// <summary>
        /// Sets the caption: the buyer-facing <paramref name="caption"/> first, then each
        /// non-empty diagnostic - those only while <see cref="ShowDiagnostics"/> holds. Joined with
        /// a middle dot; nothing left hides the line.
        /// </summary>
        protected void SetCaption(string caption, params string[] diagnostics)
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrEmpty(caption)) parts.Add(caption);
            if (ShowDiagnostics && diagnostics != null)
            {
                for (int i = 0; i < diagnostics.Length; i++)
                    if (!string.IsNullOrEmpty(diagnostics[i])) parts.Add(diagnostics[i]);
            }
            SetNote(parts.Count == 0 ? null : string.Join(" · ", parts));
        }

        /// <summary>Sets the caption under the widget; null or empty hides it.</summary>
        protected void SetNote(string text)
        {
            _note.text = text ?? string.Empty;
            _note.EnableInClassList(HiddenClass, string.IsNullOrEmpty(text));
        }

        /// <summary>Starts mirroring external changes of the prop into the widget.</summary>
        protected void Observe()
        {
            _subscription = Prop.SubscribeChanged(() =>
            {
                if (_syncing || _disposed) return;
                Refresh();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();
            _subscription = null;
            OnDispose();
            ValueChanged = null;
        }
    }

    // -- bool ----------------------------------------------------------------

    /// <summary><c>Prop&lt;bool&gt;</c>: a switch on the right of the row.</summary>
    public sealed class SusToggleControl : SusControl
    {
        readonly Button _switch = new();

        internal SusToggleControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.Toggle, context)
        {
            _switch.AddToClassList("sb-ctl__switch");
            var knob = new VisualElement();
            knob.AddToClassList("sb-ctl__knob");
            _switch.Add(knob);
            _switch.clicked += Flip;
            Body.Add(_switch);
            Refresh();
            Observe();
        }

        /// <summary>Value as the widget shows it.</summary>
        public bool Current => Prop.Value is bool b && b;

        /// <summary>The switch itself - what a driver clicks.</summary>
        public Button Switch => _switch;

        /// <summary>Flips the value; what the switch does when clicked.</summary>
        public void Flip() => Write(!Current);

        protected override void OnRefresh() =>
            _switch.EnableInClassList("sb-ctl__switch--on", Current);

        public override bool SetFromString(string text) =>
            bool.TryParse(text, out var b) && Write(b);
    }

    // -- closed sets: segment and dropdown -----------------------------------

    /// <summary>
    /// Closed set shown as a row of buttons: five values or fewer, and their texts fit the label
    /// budget (<see cref="SusControlFactory.SegmentLabelBudget"/>).
    /// </summary>
    public sealed class SusSegmentControl : SusControl
    {
        readonly List<Button> _buttons = new();
        readonly SusClosedSet _set;
        readonly IReadOnlyList<string> _options;

        internal SusSegmentControl(SusPropInfo prop, SusControlContext context, SusClosedSet set)
            : base(prop, SusControlKind.Segment, context)
        {
            _set = set;
            var options = set.Values;
            _options = options;
            SetCaption(null,
                set.FromStory ? SetFromStoryNote : null,
                set.HadDuplicates ? DuplicateValuesNote : null);
            var strip = new VisualElement();
            strip.AddToClassList("sb-ctl__segment");
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var b = new Button(() => Write(option)) { text = option };
                b.AddToClassList("sb-ctl__seg");
                strip.Add(b);
                _buttons.Add(b);
            }
            Body.Add(strip);
            Refresh();
            Observe();
        }

        /// <summary>Legal values, in declaration order.</summary>
        public IReadOnlyList<string> Options => _options;

        /// <summary>The buttons, in the order of <see cref="Options"/>.</summary>
        public IReadOnlyList<Button> Buttons => _buttons;

        /// <inheritdoc/>
        public override bool Accepts(string text) => _set.Accepts(text);

        protected override void OnRefresh()
        {
            var current = _set.Display(StringValue);
            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].EnableInClassList("sb-ctl__seg--active",
                    string.Equals(_options[i], current, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    /// <summary>Any closed set that is not a segment, and every colour name: a dropdown.</summary>
    public sealed class SusDropdownControl : SusControl
    {
        readonly DropdownField _field;
        readonly SusClosedSet _set;
        readonly IReadOnlyList<string> _options;

        internal SusDropdownControl(SusPropInfo prop, SusControlContext context, SusClosedSet set)
            : base(prop, SusControlKind.Dropdown, context)
        {
            _set = set;
            var options = set.Values;
            _options = options;
            SetCaption(null,
                set.FromStory ? SetFromStoryNote : null,
                set.HadDuplicates ? DuplicateValuesNote : null);
            _field = new DropdownField(new List<string>(options), 0);
            _field.AddToClassList("sb-ctl__dropdown");
            _field.RegisterValueChangedCallback(e => Write(e.newValue));
            Body.Add(_field);
            Refresh();
            Observe();
        }

        /// <summary>Legal values, in declaration order.</summary>
        public IReadOnlyList<string> Options => _options;

        /// <summary>The dropdown itself.</summary>
        public DropdownField Field => _field;

        /// <inheritdoc/>
        public override bool Accepts(string text) => _set.Accepts(text);

        protected override void OnRefresh()
        {
            var current = _set.Display(StringValue);
            for (int i = 0; i < _options.Count; i++)
            {
                if (!string.Equals(_options[i], current, StringComparison.OrdinalIgnoreCase)) continue;
                _field.SetValueWithoutNotify(_options[i]);
                return;
            }
            _field.SetValueWithoutNotify(current);
        }
    }

    // -- free text and icon names --------------------------------------------

    /// <summary>Free <c>string</c>: a text field. The empty string is a value, not a hole.</summary>
    public sealed class SusTextControl : SusControl
    {
        readonly TextField _field = new();

        internal SusTextControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.Text, context)
        {
            _field.AddToClassList("sb-ctl__text");
            _field.RegisterValueChangedCallback(e => Write(e.newValue));
            Body.Add(_field);
            Refresh();
            Observe();
        }

        /// <summary>The text field itself - what a driver types into.</summary>
        public TextField Field => _field;

        protected override void OnRefresh()
        {
            var value = StringValue;
            _field.SetValueWithoutNotify(value);
            SetCaption(null, value.Length == 0 ? EmptyStringNote : null);
        }
    }

    /// <summary>
    /// String prop named <c>*Icon</c>: glyph preview plus the glyph name. Deliberately a STUB -
    /// the searchable picker over the core glyph set is card T-3035, and it arrives as an
    /// <see cref="ISusControlProvider"/> without this file changing.
    /// </summary>
    public sealed class SusIconControl : SusControl
    {
        readonly TextField _field = new();
        readonly SusIconElement _glyph = new();

        internal SusIconControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.Icon, context)
        {
            _glyph.AddToClassList("sb-ctl__glyph");
            _field.AddToClassList("sb-ctl__text");
            _field.RegisterValueChangedCallback(e => Write(e.newValue));
            Body.Add(_glyph);
            Body.Add(_field);
            Refresh();
            Observe();
        }

        /// <summary>The name field - replaced by the picker of T-3035.</summary>
        public TextField Field => _field;

        /// <summary>The live glyph preview.</summary>
        public SusIconElement Glyph => _glyph;

        protected override void OnRefresh()
        {
            var value = StringValue;
            _field.SetValueWithoutNotify(value);
            _glyph.Name.Value = value;
            SetNote(value.Length == 0 ? "no glyph" : null);
        }
    }

    // -- numbers -------------------------------------------------------------

    /// <summary>What a numeric prop declared about its bounds.</summary>
    public enum SusRangeStatus
    {
        /// <summary>No <c>[SusRange]</c>: the control is a plain field, no slider and no clamp.</summary>
        Undeclared = 0,
        /// <summary>A coherent range: slider plus field, the field clamps.</summary>
        Valid = 1,
        /// <summary>
        /// The declaration contradicts itself (<c>min ≥ max</c>, negative step, a step wider than the
        /// span, <c>[SusRangeFrom]</c> without <c>[SusRange]</c> or naming a missing / non-numeric
        /// neighbour): treated like <see cref="Undeclared"/>, and the dev caption says so.
        /// </summary>
        Invalid = 2,
    }

    /// <summary>
    /// Numeric prop (ARCH-20261014-SB-PROP-CONTROLS §4.2). A numeric field always; a slider only
    /// where the component DECLARED a range (<c>[SusRange]</c>, optionally fed by neighbour props
    /// through <c>[SusRangeFrom]</c>); one chip per <c>[SusSpecial]</c> value. Without a declared
    /// range there is no made-up 0…100 scale and no clamp (D3, d:d05b9d): 0.55, 1.67 and 1500 ms
    /// are not on one scale, and a clamp would destroy a sentinel such as -1 = "auto".
    /// </summary>
    public sealed class SusNumberControl : SusControl
    {
        /// <summary>USS class that mutes the slider and the field while a special value is active (muted, not disabled).</summary>
        public const string DimClass = "sb-ctl--dim";

        /// <summary>USS class of a special-value chip.</summary>
        public const string SpecialClass = "sb-ctl__special";

        /// <summary>USS class of the chip whose value the prop currently holds.</summary>
        public const string SpecialOnClass = "sb-ctl__special--on";

        const double SpecialTolerance = 1e-6;

        readonly Slider _slider;
        readonly TextField _field = new();
        readonly VisualElement _specials = new();
        readonly List<Button> _chips = new();
        readonly IReadOnlyList<SusSpecialAttribute> _specialDefs;
        readonly Dictionary<string, SusPropInfo> _neighbours = new(StringComparer.Ordinal);
        readonly List<IDisposable> _neighbourSubscriptions = new();
        readonly bool _integral;

        SusRangeStatus _status;
        double _min = double.NaN;
        double _max = double.NaN;
        double _declaredStep;
        double _step;

        internal SusNumberControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.Number, context)
        {
            Unit = prop.Range?.Unit ?? string.Empty;
            _integral = prop.ValueType == typeof(int) || prop.ValueType == typeof(long) ||
                        prop.ValueType == typeof(short) || prop.ValueType == typeof(byte);
            _specialDefs = prop.Specials ?? Array.Empty<SusSpecialAttribute>();

            _specials.AddToClassList("sb-ctl__specials");
            for (int i = 0; i < _specialDefs.Count; i++)
            {
                int index = i;
                var chip = new Button(() => PickSpecial(index)) { text = _specialDefs[i].Label };
                // Literal on purpose (R140 reads the shell class off the call); it is SpecialClass.
                chip.AddToClassList("sb-ctl__special");
                _specials.Add(chip);
                _chips.Add(chip);
            }

            _slider = new Slider(0f, 1f);
            _slider.AddToClassList("sb-ctl__slider");
            _slider.RegisterValueChangedCallback(e => Drag(e.newValue));

            _field.AddToClassList("sb-ctl__number");
            // Commit on Enter / blur: a write per keystroke re-renders the field under the cursor and
            // eats the decimal point of "3.5" the moment "3." parses as 3.
            _field.isDelayed = true;
            _field.RegisterValueChangedCallback(e =>
            {
                if (!SetFromString(e.newValue)) Refresh();
            });

            if (_chips.Count > 0) Body.Add(_specials);
            Body.Add(_slider);
            Body.Add(_field);

            ResolveNeighbours();
            ApplyRange();
            Refresh();
            Observe();
        }

        /// <summary>What the prop declared about its bounds - see <see cref="SusRangeStatus"/>.</summary>
        public SusRangeStatus Status => _status;

        /// <summary>
        /// Current lower bound of the slider, or <see cref="double.NaN"/> when there is no slider.
        /// Follows the neighbour props of <c>[SusRangeFrom]</c>.
        /// </summary>
        public double Minimum => _min;

        /// <summary>Current upper bound of the slider, or <see cref="double.NaN"/> when there is no slider.</summary>
        public double Maximum => _max;

        /// <summary>
        /// Step the slider snaps to: the declared one, else 1 for a whole number and a hundredth of
        /// the span for a fraction. 0 when there is no slider.
        /// </summary>
        public double Step => _step;

        /// <summary>Unit from <c>[SusRange]</c>, or empty.</summary>
        public string Unit { get; }

        /// <summary>True when the prop carries a <c>[SusRange]</c> at all (it may still be invalid).</summary>
        public bool HasDeclaredRange => Prop.Range != null;

        /// <summary>True while the slider is shown: a coherent declared range.</summary>
        public bool HasSlider => _status == SusRangeStatus.Valid;

        /// <summary>The slider - what a driver drags. Hidden (see <see cref="HasSlider"/>) without a valid range.</summary>
        public Slider Slider => _slider;

        /// <summary>The numeric field.</summary>
        public TextField Field => _field;

        /// <summary>The special-value chips, in declaration order.</summary>
        public IReadOnlyList<Button> Chips => _chips;

        /// <summary>Index of the special value the prop holds right now, or -1.</summary>
        public int ActiveSpecial { get; private set; } = -1;

        /// <summary>True while a special value is active and the slider and field are muted.</summary>
        public bool IsDimmed => ActiveSpecial >= 0;

        /// <summary>
        /// What dragging the slider does: snap to the step, write. The slider's own callback calls
        /// this, and so does a test - a detached slider raises no change event of its own.
        /// </summary>
        public bool Drag(double value)
        {
            if (_status != SusRangeStatus.Valid || !IsFinite(value)) return false;
            return Write(Quantize(value));
        }

        /// <summary>What clicking chip <paramref name="index"/> does: write the special value as it is, unclamped.</summary>
        public bool PickSpecial(int index)
        {
            if (index < 0 || index >= _specialDefs.Count) return false;
            var value = _specialDefs[index].Value;
            return Write(_integral ? Math.Round(value, MidpointRounding.AwayFromZero) : value);
        }

        /// <summary>
        /// Text typed into the field (or arriving from a link). Exact: no snapping to the step.
        /// With a valid range it clamps into it, except for a special value, which is written as
        /// declared. Without one it writes the number as it is. <c>NaN</c> and infinities are refused.
        /// </summary>
        public override bool SetFromString(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
                !IsFinite(d))
                return false;
            if (_integral) d = Math.Round(d, MidpointRounding.AwayFromZero);
            if (_status == SusRangeStatus.Valid && SpecialIndexOf(d) < 0) d = Clamp(d);
            return Write(d);
        }

        protected override void OnDispose()
        {
            for (int i = 0; i < _neighbourSubscriptions.Count; i++) _neighbourSubscriptions[i].Dispose();
            _neighbourSubscriptions.Clear();
        }

        protected override void OnRefresh()
        {
            bool numeric = TryDouble(Prop.Value, out var current);
            bool finite = numeric && IsFinite(current);
            int special = finite ? SpecialIndexOf(current) : -1;
            ActiveSpecial = special;

            if (_status == SusRangeStatus.Valid)
                _slider.SetValueWithoutNotify((float)(finite ? Clamp(current) : _min));

            // A special value owns the row: the field is empty and its placeholder names the meaning.
            _field.SetValueWithoutNotify(special >= 0 ? string.Empty : StringValue);
            _field.textEdition.placeholder = special >= 0 ? _specialDefs[special].Label : string.Empty;

            for (int i = 0; i < _chips.Count; i++) _chips[i].EnableInClassList(SpecialOnClass, i == special);
            _slider.EnableInClassList(DimClass, special >= 0);
            _field.EnableInClassList(DimClass, special >= 0);

            UpdateCaption(current, finite, special);
        }

        // -- range -----------------------------------------------------------

        /// <summary>
        /// Finds the neighbour props <c>[SusRangeFrom]</c> names and subscribes to the numeric ones:
        /// moving a neighbour re-derives the bounds and leaves the value alone.
        /// </summary>
        void ResolveNeighbours()
        {
            var rangeFrom = Prop.RangeFrom;
            if (rangeFrom == null || Context?.Component == null) return;

            var wanted = new[] { rangeFrom.MinProp, rangeFrom.MaxProp, rangeFrom.StepProp };
            IReadOnlyList<SusPropInfo> all = null;
            for (int w = 0; w < wanted.Length; w++)
            {
                var name = wanted[w];
                if (string.IsNullOrEmpty(name) || _neighbours.ContainsKey(name)) continue;
                all ??= Context.Component.DescribeProps();
                for (int i = 0; i < all.Count; i++)
                {
                    if (!string.Equals(all[i].Name, name, StringComparison.Ordinal)) continue;
                    _neighbours[name] = all[i];
                    if (SusPropGrouping.IsNumeric(all[i].ValueType))
                        _neighbourSubscriptions.Add(all[i].SubscribeChanged(OnNeighbourChanged));
                    break;
                }
            }
        }

        void OnNeighbourChanged()
        {
            ApplyRange();
            Refresh();
        }

        bool Neighbour(string name, ref double target)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (!_neighbours.TryGetValue(name, out var info)) return false;
            if (!SusPropGrouping.IsNumeric(info.ValueType)) return false;
            if (!TryDouble(info.Value, out var v) || !IsFinite(v)) return false;
            target = v;
            return true;
        }

        void ApplyRange()
        {
            var range = Prop.Range;
            var rangeFrom = Prop.RangeFrom;
            double min = double.NaN, max = double.NaN, step = 0d;
            SusRangeStatus status;

            if (range == null)
            {
                // A [SusRangeFrom] with nothing to start from is a wrong declaration, not an absent one.
                status = rangeFrom != null ? SusRangeStatus.Invalid : SusRangeStatus.Undeclared;
            }
            else
            {
                min = range.Min;
                max = range.Max;
                step = range.Step;
                bool ok = true;
                if (rangeFrom != null)
                {
                    ok &= Neighbour(rangeFrom.MinProp, ref min);
                    ok &= Neighbour(rangeFrom.MaxProp, ref max);
                    ok &= Neighbour(rangeFrom.StepProp, ref step);
                }
                ok &= IsFinite(min) && IsFinite(max) && IsFinite(step) &&
                      min < max && step >= 0d && (step == 0d || step <= max - min);
                status = ok ? SusRangeStatus.Valid : SusRangeStatus.Invalid;
            }

            _status = status;
            if (status == SusRangeStatus.Valid)
            {
                _min = min;
                _max = max;
                _declaredStep = step;
                _step = step > 0d ? step : _integral ? 1d : (max - min) / 100d;
                _slider.lowValue = (float)min;
                _slider.highValue = (float)max;
            }
            else
            {
                _min = double.NaN;
                _max = double.NaN;
                _declaredStep = 0d;
                _step = 0d;
            }

            _slider.EnableInClassList(HiddenClass, status != SusRangeStatus.Valid);
            _field.EnableInClassList("sb-ctl__number--solo", status != SusRangeStatus.Valid);
        }

        double Clamp(double v) => v < _min ? _min : v > _max ? _max : v;

        /// <summary>
        /// Snaps to <c>min + round((v − min) / step) · step</c>, rounded to the decimals of the
        /// step, then back into the range; whole numbers stay whole.
        /// </summary>
        double Quantize(double v)
        {
            double q = _min + Math.Round((v - _min) / _step, MidpointRounding.AwayFromZero) * _step;
            q = Math.Round(q, Math.Max(Decimals(_step), Decimals(_min)));
            q = Clamp(q);
            return _integral ? Math.Round(q, MidpointRounding.AwayFromZero) : q;
        }

        int SpecialIndexOf(double value)
        {
            for (int i = 0; i < _specialDefs.Count; i++)
                if (Math.Abs(value - _specialDefs[i].Value) < SpecialTolerance) return i;
            return -1;
        }

        // -- captions --------------------------------------------------------

        void UpdateCaption(double current, bool finite, int special)
        {
            string caption = null;
            string diagnostic = null;
            switch (_status)
            {
                case SusRangeStatus.Valid:
                    caption = RangeCaption();
                    if (finite && special < 0 && (current < _min || current > _max))
                        diagnostic = OutOfRangeNote;
                    break;
                case SusRangeStatus.Invalid:
                    diagnostic = RangeInvalidNote;
                    break;
                default:
                    diagnostic = NoRangeNote;
                    break;
            }
            SetCaption(caption, diagnostic);
        }

        /// <summary>Buyer-facing range caption: <c>0…5 · step 0.5</c>, <c>0…1000 ms</c>.</summary>
        string RangeCaption()
        {
            var text = Format(_min) + "…" + Format(_max);
            if (Unit.Length > 0) text += " " + Unit;
            if (_declaredStep > 0d) text += " · step " + Format(_declaredStep);
            return text;
        }

        static string Format(double v) =>
            Math.Round(v, 6).ToString(CultureInfo.InvariantCulture);

        // -- numbers ---------------------------------------------------------

        static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>Digits after the point a step needs (0.02 → 2); capped so float noise cannot run away.</summary>
        static int Decimals(double x)
        {
            x = Math.Abs(x);
            if (!IsFinite(x)) return 0;
            double scale = 1d;
            for (int d = 0; d <= 9; d++, scale *= 10d)
            {
                double scaled = x * scale;
                if (Math.Abs(scaled - Math.Round(scaled)) < 1e-7 * Math.Max(1d, scaled)) return d;
            }
            return 9;
        }

        static bool TryDouble(object value, out double result)
        {
            result = double.NaN;
            if (value == null) return false;
            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // -- colour --------------------------------------------------------------

    /// <summary>
    /// <c>Prop&lt;Color&gt;</c>: one swatch per skin token plus a free hex field.
    ///
    /// The swatch colour is NOT named in C#: each swatch carries the USS class
    /// <c>sb-ctl__swatch--&lt;token&gt;</c>, the skin paints it, and the click reads the
    /// painted colour back through <c>resolvedStyle</c>. A skin swap therefore changes what the
    /// swatches offer, with no code change (R53/R120).
    /// </summary>
    public sealed class SusColorControl : SusControl
    {
        readonly List<Button> _swatches = new();
        readonly TextField _hex = new();
        readonly Label _value = new();

        internal SusColorControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.Color, context)
        {
            var tokens = context?.ColorTokens ?? SusControlContext.StandardColorTokens;
            var strip = new VisualElement();
            strip.AddToClassList("sb-ctl__swatches");
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var b = new Button();
                b.AddToClassList("sb-ctl__swatch");
                b.AddToClassList("sb-ctl__swatch--" + token);
                b.clicked += () => PickToken(b, token);
                strip.Add(b);
                _swatches.Add(b);
            }

            _hex.AddToClassList("sb-ctl__hex");
            _hex.RegisterValueChangedCallback(e => SetFromString(e.newValue));

            _value.AddToClassList("sb-ctl__value");

            Body.Add(strip);
            Body.Add(_hex);
            Body.Add(_value);
            Refresh();
            Observe();
        }

        /// <summary>Token names offered as swatches.</summary>
        public IReadOnlyList<string> Tokens => Context?.ColorTokens ?? SusControlContext.StandardColorTokens;

        /// <summary>The swatch buttons, in token order.</summary>
        public IReadOnlyList<Button> Swatches => _swatches;

        /// <summary>The free value field ("custom") next to the swatches.</summary>
        public TextField Custom => _hex;

        /// <summary>Token last picked, or null when the value came from the free field.</summary>
        public string PickedToken { get; private set; }

        void PickToken(VisualElement swatch, string token)
        {
            // The skin painted the swatch; the control only reads back what it painted.
            var color = swatch.resolvedStyle.backgroundColor;
            PickedToken = token;
            if (!Write(color)) PickedToken = null;
        }

        protected override void OnRefresh()
        {
            var text = StringValue;
            _hex.SetValueWithoutNotify(text);
            _value.text = PickedToken != null ? "token/" + PickedToken : text;
        }

        public override bool SetFromString(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();
            if (!t.StartsWith("#", StringComparison.Ordinal)) t = "#" + t;
            if (!ColorUtility.TryParseHtmlString(t, out var c)) return false;
            PickedToken = null;
            return Write(c);
        }
    }

    // -- list ----------------------------------------------------------------

    /// <summary><c>Prop&lt;List&lt;T&gt;&gt;</c>: "N rows" with an expander and one field per row.</summary>
    public sealed class SusListControl : SusControl
    {
        readonly Button _summary;
        readonly VisualElement _rows = new();
        readonly Button _add;
        bool _open;

        internal SusListControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.List, context)
        {
            ItemType = ResolveItemType(prop.ValueType);

            _summary = new Button(Toggle);
            _summary.AddToClassList("sb-ctl__list-summary");

            _rows.AddToClassList("sb-ctl__list");
            _add = new Button(AddRow) { text = "+ row" };
            _add.AddToClassList("sb-ctl__list-add");

            Body.Add(_summary);
            Add(_rows);
            Refresh();
            Observe();
        }

        /// <summary>Item type of the list, or null when it could not be resolved.</summary>
        public Type ItemType { get; }

        /// <summary>True while the rows are shown.</summary>
        public bool IsOpen => _open;

        /// <summary>Number of rows the list currently holds.</summary>
        public int Count => Prop.Value is IList list ? list.Count : 0;

        /// <summary>The summary button - "N rows".</summary>
        public Button Summary => _summary;

        /// <summary>A list cannot be restored from a deep link, so it stays out of the query.</summary>
        public override bool Restorable => false;

        /// <summary>Shows or hides the rows.</summary>
        public void Toggle()
        {
            _open = !_open;
            Refresh();
        }

        /// <summary>Appends one empty row and writes the new list back through the prop.</summary>
        public void AddRow()
        {
            var items = Snapshot();
            items.Add(ItemType == typeof(string) ? string.Empty : null);
            WriteList(items);
        }

        List<object> Snapshot()
        {
            var result = new List<object>();
            if (Prop.Value is IList list)
            {
                foreach (var item in list) result.Add(item);
            }
            return result;
        }

        void WriteList(List<object> items)
        {
            // A NEW instance on purpose: writing the same reference back would not read as a
            // change, and the stage would keep showing the old rows.
            IList target;
            try { target = (IList)Activator.CreateInstance(Prop.ValueType); }
            catch (Exception) { return; }

            for (int i = 0; i < items.Count; i++)
            {
                try
                {
                    target.Add(ItemType == null || items[i] == null || ItemType.IsInstanceOfType(items[i])
                        ? items[i]
                        : Convert.ChangeType(items[i], ItemType, CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                    return;
                }
            }
            Write(target);
        }

        protected override void OnRefresh()
        {
            int count = Count;
            _summary.text = count + (count == 1 ? " row ✎" : " rows ✎");
            _rows.EnableInClassList(HiddenClass, !_open);
            _rows.Clear();
            if (!_open) return;

            var items = Snapshot();
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var field = new TextField
                {
                    value = items[i] == null
                        ? string.Empty
                        : Convert.ToString(items[i], CultureInfo.InvariantCulture),
                };
                field.AddToClassList("sb-ctl__list-row");
                field.RegisterValueChangedCallback(e =>
                {
                    var next = Snapshot();
                    if (index >= next.Count) return;
                    next[index] = e.newValue;
                    WriteList(next);
                });
                _rows.Add(field);
            }
            _rows.Add(_add);
        }

        static Type ResolveItemType(Type listType)
        {
            if (listType == null) return null;
            if (listType.IsArray) return listType.GetElementType();
            if (listType.IsGenericType && listType.GetGenericArguments().Length == 1)
                return listType.GetGenericArguments()[0];
            return typeof(string);
        }
    }

    // -- model / readonly ----------------------------------------------------

    /// <summary>
    /// Model objects and <c>ReadonlyProp</c>: display only, refreshed from the prop's own change
    /// events. Writing here would fake a two-way binding the component does not have.
    /// </summary>
    public sealed class SusReadOnlyControl : SusControl
    {
        readonly Label _value = new();

        internal SusReadOnlyControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.ReadOnly, context)
        {
            _value.AddToClassList("sb-ctl__mono");
            Body.Add(_value);
            SetNote(ReadOnlyNote);
            Refresh();
            Observe();
        }

        /// <summary>A model cannot be rebuilt from a link, so it stays out of the query.</summary>
        public override bool Restorable => false;

        /// <summary>What the row prints ("-" for null and empty).</summary>
        public string Display => _value.text;

        protected override void OnRefresh()
        {
            var text = StringValue;
            _value.text = string.IsNullOrEmpty(text) ? "—" : text;
        }

        public override bool SetFromString(string text) => false;
    }
}
