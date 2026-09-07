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
        /// <summary>Closed set of five values or fewer - segmented picker.</summary>
        Segment = 1,
        /// <summary>Closed set of more than five values - dropdown.</summary>
        Dropdown = 2,
        /// <summary>Free <c>string</c> - text field (the empty string is a legal value).</summary>
        Text = 3,
        /// <summary>String prop named <c>*Icon</c> - glyph name field (picker: card T-3035).</summary>
        Icon = 4,
        /// <summary>Numeric - slider plus a numeric field plus the range caption.</summary>
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
        public const string HiddenClass = "sus-sb-hidden";

        /// <summary>Caption of a text prop that currently holds the empty string.</summary>
        public const string EmptyStringNote = "empty string · legal";

        /// <summary>Caption of a numeric prop with no <c>[SusRange]</c>.</summary>
        public const string NoRangeNote = "range not declared · 0…100";

        /// <summary>Caption of a display-only row.</summary>
        public const string ReadOnlyNote = "updated from events";

        /// <summary>Lower bound used when <c>[SusRange]</c> is absent.</summary>
        public const double FallbackMin = 0d;

        /// <summary>Upper bound used when <c>[SusRange]</c> is absent.</summary>
        public const double FallbackMax = 100d;

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

            AddToClassList("sus-sb-ctl");
            AddToClassList("sus-sb-ctl--" + kind.ToString().ToLowerInvariant());
            name = "sus-storybook-control-" + prop.Name;

            var head = new VisualElement();
            head.AddToClassList("sus-sb-ctl__head");

            _name.text = prop.Name;
            _name.AddToClassList("sus-sb-ctl__name");
            head.Add(_name);

            _deadBadge.AddToClassList("sus-sb-ctl__badge");
            _deadBadge.AddToClassList("sus-sb-ctl__badge--dead");
            head.Add(_deadBadge);

            _manualBadge.AddToClassList("sus-sb-ctl__badge");
            _manualBadge.AddToClassList("sus-sb-ctl__badge--manual");
            head.Add(_manualBadge);

            _body.AddToClassList("sus-sb-ctl__body");

            _note.AddToClassList("sus-sb-ctl__note");
            _dependency.AddToClassList("sus-sb-ctl__dependency");

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
                RemoveFromClassList("sus-sb-ctl--inert");
                return;
            }

            IsActive = component.IsDependencySatisfied(Prop);
            var text = "depends on " + declared;
            if (!IsActive) text += " · inert while it does not hold";
            _dependency.text = text;
            _dependency.RemoveFromClassList(HiddenClass);
            EnableInClassList("sus-sb-ctl--inert", !IsActive);
        }

        /// <summary>Subclass hook: write the prop value into the widget.</summary>
        protected virtual void OnRefresh() { }

        /// <summary>Writes through the prop; returns false when the value does not fit.</summary>
        protected bool Write(object value)
        {
            if (_syncing || _disposed) return false;
            if (!Prop.TrySetValue(value)) return false;
            Refresh();
            ValueChanged?.Invoke(this);
            return true;
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
            _switch.AddToClassList("sus-sb-ctl__switch");
            var knob = new VisualElement();
            knob.AddToClassList("sus-sb-ctl__knob");
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
            _switch.EnableInClassList("sus-sb-ctl__switch--on", Current);

        public override bool SetFromString(string text) =>
            bool.TryParse(text, out var b) && Write(b);
    }

    // -- closed sets: segment and dropdown -----------------------------------

    /// <summary>Closed set of values shown as a row of buttons (five or fewer).</summary>
    public sealed class SusSegmentControl : SusControl
    {
        readonly List<Button> _buttons = new();
        readonly IReadOnlyList<string> _options;

        internal SusSegmentControl(SusPropInfo prop, SusControlContext context, IReadOnlyList<string> options)
            : base(prop, SusControlKind.Segment, context)
        {
            _options = options;
            var strip = new VisualElement();
            strip.AddToClassList("sus-sb-ctl__segment");
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var b = new Button(() => Write(option)) { text = option };
                b.AddToClassList("sus-sb-ctl__seg");
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

        protected override void OnRefresh()
        {
            var current = StringValue;
            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].EnableInClassList("sus-sb-ctl__seg--active",
                    string.Equals(_options[i], current, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    /// <summary>Closed set of more than five values shown as a dropdown.</summary>
    public sealed class SusDropdownControl : SusControl
    {
        readonly DropdownField _field;
        readonly IReadOnlyList<string> _options;

        internal SusDropdownControl(SusPropInfo prop, SusControlContext context, IReadOnlyList<string> options)
            : base(prop, SusControlKind.Dropdown, context)
        {
            _options = options;
            _field = new DropdownField(new List<string>(options), 0);
            _field.AddToClassList("sus-sb-ctl__dropdown");
            _field.RegisterValueChangedCallback(e => Write(e.newValue));
            Body.Add(_field);
            Refresh();
            Observe();
        }

        /// <summary>Legal values, in declaration order.</summary>
        public IReadOnlyList<string> Options => _options;

        /// <summary>The dropdown itself.</summary>
        public DropdownField Field => _field;

        protected override void OnRefresh()
        {
            var current = StringValue;
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
            _field.AddToClassList("sus-sb-ctl__text");
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
            SetNote(value.Length == 0 ? EmptyStringNote : null);
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
            _glyph.AddToClassList("sus-sb-ctl__glyph");
            _field.AddToClassList("sus-sb-ctl__text");
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

    /// <summary>Numeric prop: slider, numeric field and the range caption.</summary>
    public sealed class SusNumberControl : SusControl
    {
        readonly Slider _slider;
        readonly TextField _field = new();
        readonly bool _integral;

        internal SusNumberControl(SusPropInfo prop, SusControlContext context)
            : base(prop, SusControlKind.Number, context)
        {
            var range = prop.Range;
            Minimum = range?.Min ?? FallbackMin;
            Maximum = range?.Max ?? FallbackMax;
            Unit = range?.Unit ?? string.Empty;
            _integral = prop.ValueType == typeof(int) || prop.ValueType == typeof(long) ||
                        prop.ValueType == typeof(short) || prop.ValueType == typeof(byte);

            _slider = new Slider((float)Minimum, (float)Maximum);
            _slider.AddToClassList("sus-sb-ctl__slider");
            _slider.RegisterValueChangedCallback(e =>
                Write(_integral ? Math.Round(e.newValue) : (double)e.newValue));

            _field.AddToClassList("sus-sb-ctl__number");
            _field.RegisterValueChangedCallback(e =>
            {
                if (double.TryParse(e.newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    Write(Clamp(d));
            });

            Body.Add(_slider);
            Body.Add(_field);
            SetNote(range == null
                ? NoRangeNote
                : Minimum.ToString(CultureInfo.InvariantCulture) + "…" +
                  Maximum.ToString(CultureInfo.InvariantCulture) +
                  (Unit.Length > 0 ? " " + Unit : string.Empty));
            Refresh();
            Observe();
        }

        /// <summary>Lower bound: <c>[SusRange]</c> or the declared fallback.</summary>
        public double Minimum { get; }

        /// <summary>Upper bound: <c>[SusRange]</c> or the declared fallback.</summary>
        public double Maximum { get; }

        /// <summary>Unit from <c>[SusRange]</c>, or empty.</summary>
        public string Unit { get; }

        /// <summary>True when the range came from <c>[SusRange]</c> rather than the fallback.</summary>
        public bool HasDeclaredRange => Prop.Range != null;

        /// <summary>The slider - what a driver drags.</summary>
        public Slider Slider => _slider;

        /// <summary>The numeric field next to the slider.</summary>
        public TextField Field => _field;

        double Clamp(double v) => v < Minimum ? Minimum : v > Maximum ? Maximum : v;

        protected override void OnRefresh()
        {
            double current = ToDouble(Prop.Value);
            _slider.SetValueWithoutNotify((float)Clamp(current));
            _field.SetValueWithoutNotify(StringValue);
        }

        public override bool SetFromString(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
            Write(Clamp(d));

        static double ToDouble(object value)
        {
            try { return value == null ? 0d : Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return 0d; }
        }
    }

    // -- colour --------------------------------------------------------------

    /// <summary>
    /// <c>Prop&lt;Color&gt;</c>: one swatch per skin token plus a free hex field.
    ///
    /// The swatch colour is NOT named in C#: each swatch carries the USS class
    /// <c>sus-sb-ctl__swatch--&lt;token&gt;</c>, the skin paints it, and the click reads the
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
            strip.AddToClassList("sus-sb-ctl__swatches");
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var b = new Button();
                b.AddToClassList("sus-sb-ctl__swatch");
                b.AddToClassList("sus-sb-ctl__swatch--" + token);
                b.clicked += () => PickToken(b, token);
                strip.Add(b);
                _swatches.Add(b);
            }

            _hex.AddToClassList("sus-sb-ctl__hex");
            _hex.RegisterValueChangedCallback(e => SetFromString(e.newValue));

            _value.AddToClassList("sus-sb-ctl__value");

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
            _summary.AddToClassList("sus-sb-ctl__list-summary");

            _rows.AddToClassList("sus-sb-ctl__list");
            _add = new Button(AddRow) { text = "+ row" };
            _add.AddToClassList("sus-sb-ctl__list-add");

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
                field.AddToClassList("sus-sb-ctl__list-row");
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
            _value.AddToClassList("sus-sb-ctl__mono");
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
