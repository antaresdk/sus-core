using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sharq.Core
{
    /// <summary>
    /// Coarse role of a prop, guessed from its NAME and TYPE (never from an attribute — see
    /// <see cref="SusPropGrouping"/> for why). A control panel groups its controls by this.
    /// </summary>
    public enum SusPropGroup
    {
        /// <summary>Design axis: Variant, Color, Size, Density, Rounded, Elevation… — or any
        /// prop that carries an allowed set. Rendered as a segmented / dropdown control.</summary>
        Axis = 0,
        /// <summary>What the component SHOWS: Text, Label, Icon, Items, Value, Max…</summary>
        Content = 1,
        /// <summary>Binary condition the component is IN: Disabled, Loading, Open, Selected…</summary>
        State = 2,
        /// <summary>How it BEHAVES: Clearable, Closable, Persistent, Delay, Duration…</summary>
        Behavior = 3,
        /// <summary>Bound data / model objects and collections of objects.</summary>
        Data = 4,
        /// <summary>Nothing above matched.</summary>
        Other = 5,
    }

    /// <summary>
    /// Name/type heuristic behind <see cref="SusPropGroup"/>.
    ///
    /// Deliberately NOT attribute-driven: <c>[CreateProperty]</c> — the only attribute present on
    /// props today — sits on 482 of 644 kit props and 80 of 266 game props, so an attribute-based
    /// classifier would silently lose ~70% of the game API (card T-3031). Name and type are on
    /// 100% of props.
    /// </summary>
    public static class SusPropGrouping
    {
        static readonly HashSet<string> AxisNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Variant", "Color", "Size", "Density", "Rounded", "Radius", "Elevation", "Shape",
            "Tone", "Appearance", "Theme", "Skin", "Severity", "Status", "Weight", "Emphasis",
            "Align", "AlignItems", "Justify", "Placement", "Position", "Side", "Anchor",
            "Orientation", "Direction", "Layout", "Mode", "Kind", "Level", "Shade",
        };

        static readonly HashSet<string> StateNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Disabled", "Enabled", "Loading", "Busy", "Selected", "Active", "Open", "Opened",
            "Expanded", "Collapsed", "Checked", "Indeterminate", "Visible", "Hidden", "Readonly",
            "Error", "Invalid", "Valid", "Required", "Focused", "Hover", "Hovered",
            "Pressed", "Dragging", "Dirty", "Muted", "Paused", "Playing", "Running", "Done",
        };

        static readonly string[] ContentSuffixes =
        {
            "Text", "Label", "Title", "Subtitle", "Caption", "Placeholder", "Hint", "Helper",
            "Description", "Message", "Icon", "Image", "Src", "Source", "Content", "Body",
            "Items", "Options", "Rows", "Columns", "Entries", "Children", "Badge", "Avatar",
            "Tooltip", "Format", "Prefix", "Suffix", "Unit", "Name",
        };

        static readonly HashSet<string> ContentNumberNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Value", "Min", "Max", "Step", "Progress", "Count", "Total", "Current", "Index",
            "Page", "PageSize", "Length", "Rating", "Score", "Amount", "Percent",
        };

        /// <summary>Classifies one prop. <paramref name="hasAllowedSet"/> wins over everything.</summary>
        public static SusPropGroup Classify(string name, Type valueType, bool hasAllowedSet)
        {
            if (hasAllowedSet) return SusPropGroup.Axis;
            if (string.IsNullOrEmpty(name) || valueType == null) return SusPropGroup.Other;

            if (AxisNames.Contains(name)) return SusPropGroup.Axis;

            // Enums and colours are design axes by TYPE: both render as a closed picker
            // (segmented control / swatch row), never as free input.
            if (valueType.IsEnum) return SusPropGroup.Axis;
            if (valueType == typeof(UnityEngine.Color) || valueType == typeof(UnityEngine.Color32))
                return SusPropGroup.Axis;

            if (valueType == typeof(bool))
                return StateNames.Contains(name) ? SusPropGroup.State : SusPropGroup.Behavior;

            if (IsNumeric(valueType))
            {
                if (ContentNumberNames.Contains(name) || EndsWithAny(name, ContentSuffixes))
                    return SusPropGroup.Content;
                return SusPropGroup.Behavior;
            }

            if (valueType == typeof(string)) return SusPropGroup.Content;

            // Sprite/Texture/List named like content (Items, Image, Rows…) is still content.
            if (EndsWithAny(name, ContentSuffixes)) return SusPropGroup.Content;

            // Everything else is an object / collection / model — bound data, not a control.
            return SusPropGroup.Data;
        }

        /// <summary>True for the numeric prop types a slider control can drive.</summary>
        public static bool IsNumeric(Type t) =>
            t == typeof(int) || t == typeof(float) || t == typeof(double) ||
            t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(decimal);

        static bool EndsWithAny(string name, string[] suffixes)
        {
            for (int i = 0; i < suffixes.Length; i++)
            {
                if (name.EndsWith(suffixes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// One prop of a component as seen from OUTSIDE: what it is called, what it holds now, what
    /// values are legal, what it depends on, and whether anybody actually reads it.
    /// Produced by <see cref="SusComponent.DescribeProps"/>.
    /// </summary>
    public sealed class SusPropInfo
    {
        readonly ISusPropAccess _access;

        /// <summary>Field name as declared on the component (<c>Size</c>, <c>ClearIcon</c>).</summary>
        public string Name { get; }

        /// <summary>The <c>T</c> of <c>Prop&lt;T&gt;</c>.</summary>
        public Type ValueType { get; }

        /// <summary>Type that declared the field (base-class props are included).</summary>
        public Type DeclaringType { get; }

        /// <summary>Guessed role — see <see cref="SusPropGrouping"/>.</summary>
        public SusPropGroup Group { get; }

        /// <summary>True when the prop is exposed through <see cref="ReadonlyProp{T}"/>.</summary>
        public bool ReadOnly { get; }

        /// <summary>Allowed set recorded by <c>UseAllowed</c>, or null.</summary>
        public SusAllowedInfo Allowed { get; internal set; }

        /// <summary>Declared numeric range, or null (consumer falls back to 0…100).</summary>
        public SusRangeAttribute Range { get; }

        /// <summary>Declared conditions (ALL must hold); empty when unconditional.</summary>
        public IReadOnlyList<SusDependsOnAttribute> DependsOn { get; }

        /// <summary>Current value, read WITHOUT tracking and WITHOUT counting as a read.</summary>
        public object Value => _access.BoxedValue;

        /// <summary>Tracked reads of <c>Value</c> since the prop was constructed.</summary>
        public int ReadCount => _access.ReadCount;

        /// <summary>True while something is subscribed to this prop.</summary>
        public bool HasObservers => _access.HasObservers;

        /// <summary>
        /// Declared but nobody reads it: no tracked read AND no subscriber since construction —
        /// the runtime form of "the control is there and changes nothing" (§4.3.1, R124 family).
        /// Meaningful only AFTER the component has been mounted and rendered at least once;
        /// before that every prop looks dead. Statically undecidable in C#, so this is an
        /// OBSERVATION — <see cref="ReadCount"/> is published next to it so a consumer can judge.
        /// </summary>
        public bool Dead => _access.ReadCount == 0 && !_access.HasObservers;

        /// <summary>A string prop whose name ends in <c>Icon</c> — control is a glyph picker (§4.3).</summary>
        public bool LooksLikeIcon =>
            ValueType == typeof(string) && Name.EndsWith("Icon", StringComparison.Ordinal);

        internal SusPropInfo(
            string name,
            Type valueType,
            Type declaringType,
            ISusPropAccess access,
            SusPropGroup group,
            bool readOnly,
            SusRangeAttribute range,
            IReadOnlyList<SusDependsOnAttribute> dependsOn)
        {
            Name = name;
            ValueType = valueType;
            DeclaringType = declaringType;
            _access = access;
            Group = group;
            ReadOnly = readOnly;
            Range = range;
            DependsOn = dependsOn ?? Array.Empty<SusDependsOnAttribute>();
        }

        /// <summary>
        /// Writes a value coming from a generic control (string from a text field, double from a
        /// slider, boxed enum). Converts to <see cref="ValueType"/> where that is meaningful.
        /// Returns false instead of throwing when the value does not fit — a control panel must
        /// not die on a typo.
        /// </summary>
        public bool TrySetValue(object value)
        {
            if (ReadOnly) return false;
            try
            {
                _access.BoxedValue = Coerce(value, ValueType);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Subscribe to changes of this prop (two-way controls); dispose to detach.</summary>
        public IDisposable SubscribeChanged(Action onChanged) => _access.SubscribeChanged(onChanged);

        static object Coerce(object value, Type target)
        {
            if (value == null)
                return target.IsValueType ? Activator.CreateInstance(target) : null;
            if (target.IsInstanceOfType(value)) return value;
            if (target.IsEnum)
                return value is string es
                    ? Enum.Parse(target, es, ignoreCase: true)
                    : Enum.ToObject(target, value);
            if (target == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }

        public override string ToString() =>
            Name + ":" + ValueType.Name + "=" + (Value ?? "null") + " [" + Group + "]" +
            (Dead ? " dead" : "");
    }

    /// <summary>
    /// The allowed set of one prop, as recorded by <c>UseAllowed</c>.
    /// Produced by <see cref="SusComponent.DescribeAllowed"/>.
    /// </summary>
    public sealed class SusAllowedInfo
    {
        /// <summary>Prop this set belongs to.</summary>
        public string PropName { get; internal set; }

        /// <summary>Value type of the prop the set clamps (<c>string</c> or <c>int</c> today).</summary>
        public Type ValueType { get; internal set; }

        /// <summary>Legal values in canonical string form, in declaration order.</summary>
        public IReadOnlyList<string> Values { get; internal set; }

        /// <summary>Legal values unboxed (same order as <see cref="Values"/>).</summary>
        public IReadOnlyList<object> RawValues { get; internal set; }

        /// <summary>Value an illegal input is coerced to.</summary>
        public string Fallback { get; internal set; }

        /// <summary>Accepted synonyms (<c>lg → large</c>); empty when none declared.</summary>
        public IReadOnlyDictionary<string, string> Aliases { get; internal set; }

        /// <summary>True when the empty string is a legal value on its own.</summary>
        public bool AllowEmpty { get; internal set; }

        /// <summary>True when the set comes from a resolver and can change between calls.</summary>
        public bool IsDynamic { get; internal set; }

        public override string ToString() =>
            PropName + " in [" + string.Join(", ", Values) + "] -> " + Fallback;
    }

    /// <summary>
    /// One public event of a component (<c>Action</c> / <c>Action&lt;T&gt;</c> field named
    /// <c>On*</c>). Produced by <see cref="SusComponent.DescribeEvents"/>.
    /// </summary>
    public sealed class SusEventInfo
    {
        /// <summary>Field name as declared (<c>OnValueChanged</c>).</summary>
        public string Name { get; internal set; }

        /// <summary>
        /// Name on the string bus (<see cref="SusComponent.On(string, Delegate)"/>):
        /// <c>OnValueChanged</c> becomes <c>valueChanged</c>.
        /// </summary>
        public string BusName { get; internal set; }

        /// <summary>Payload type, or null for a parameterless <c>Action</c>.</summary>
        public Type ArgType { get; internal set; }

        /// <summary>Type that declared the field.</summary>
        public Type DeclaringType { get; internal set; }

        /// <summary>True when somebody is already subscribed.</summary>
        public bool HasSubscribers { get; internal set; }

        public override string ToString() =>
            ArgType == null ? Name + "()" : Name + "(" + ArgType.Name + ")";
    }
}
