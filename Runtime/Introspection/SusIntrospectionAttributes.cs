using System;
using System.Globalization;

namespace Sharq.Core
{
    /// <summary>
    /// Declares the numeric range of a <see cref="Prop{T}"/> field (<c>int</c>/<c>float</c>/
    /// <c>double</c>) so a generated control (slider + numeric field) knows its bounds instead of
    /// guessing. Without the attribute a consumer draws a plain numeric field - no slider and no
    /// clamp, because a made-up scale would lie about what the number means
    /// (ARCH-20261014-SB-PROP-CONTROLS D3, d:d05b9d).
    /// <code>
    /// [SusRange(0, 5, 0.5)]        public Prop&lt;float&gt; Value = new(0);
    /// [SusRange(0, 100, "%")]      public Prop&lt;int&gt;   Progress = new(0);
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class SusRangeAttribute : Attribute
    {
        /// <summary>Lower bound (inclusive).</summary>
        public double Min { get; }

        /// <summary>Upper bound (inclusive).</summary>
        public double Max { get; }

        /// <summary>
        /// Increment of the slider; 0 means "the control decides" (whole numbers step by 1,
        /// fractions by a hundredth of the span).
        /// </summary>
        public double Step { get; }

        /// <summary>Unit shown next to the value ("px", "%", "s"); null when unitless.</summary>
        public string Unit { get; set; }

        /// <summary>Range with an explicit step (plan form: <c>[SusRange(min, max, step)]</c>).</summary>
        public SusRangeAttribute(double min, double max, double step = 0d)
        {
            Min = min;
            Max = max;
            Step = step;
        }

        /// <summary>Range with a unit (card form: <c>[SusRange(min, max, unit)]</c>).</summary>
        public SusRangeAttribute(double min, double max, string unit)
        {
            Min = min;
            Max = max;
            Step = 0d;
            Unit = unit;
        }

        /// <summary>Clamps <paramref name="value"/> into [Min, Max].</summary>
        public double Clamp(double value) => value < Min ? Min : value > Max ? Max : value;

        public override string ToString()
        {
            var s = string.Format(CultureInfo.InvariantCulture, "{0}…{1}", Min, Max);
            if (Step > 0d) s += string.Format(CultureInfo.InvariantCulture, " /{0}", Step);
            if (!string.IsNullOrEmpty(Unit)) s += " " + Unit;
            return s;
        }
    }

    /// <summary>
    /// Declares that a prop only matters while another prop of the SAME component holds a given
    /// value. A consumer (control panel) disables the dependent control and shows the reason
    /// instead of rendering a control that silently does nothing — today an unexplained control
    /// reads as a defect (ARCH-20260907-STORYBOOK-ENGINE §4.3.1, T-3026 item 3).
    /// <code>
    /// [SusDependsOn(nameof(ContentMode), "icon")] public Prop&lt;string&gt; Icon = new("");
    /// [SusDependsOn(nameof(Clearable))]           public Prop&lt;string&gt; ClearIcon = new("x");
    /// </code>
    /// Several attributes on one field mean ALL conditions must hold (AND) — unless they share a
    /// <see cref="Group"/>, in which case ONE of the group is enough (OR). Both shapes come from
    /// the corpus, not from theory (T-3080): <c>SusTextfield.ClearIcon</c> is live while
    /// <c>Clearable</c> OR <c>PersistentClear</c> holds, and <c>SusHudUnitCard.Icon</c> is the
    /// fallback that only paints while <c>ImageSrc</c> is EMPTY — hence <see cref="Negate"/>.
    /// <code>
    /// [SusDependsOn(nameof(Closable), "false")]           public Prop&lt;string&gt; AppendIcon = new("");
    /// [SusDependsOn(nameof(ImageSrc), Negate = true)]     public Prop&lt;string&gt; Icon = new("");
    /// [SusDependsOn(nameof(Clearable), Group = "clear")]
    /// [SusDependsOn(nameof(PersistentClear), Group = "clear")] public Prop&lt;string&gt; ClearIcon = new("x");
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public sealed class SusDependsOnAttribute : Attribute
    {
        /// <summary>Name of the prop this one depends on.</summary>
        public string Prop { get; }

        /// <summary>
        /// Required value of <see cref="Prop"/>, compared case-insensitively against its string
        /// form. <c>null</c> means "any non-empty / non-default value" (bool: true).
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Name of an OR-group. Conditions of one field that carry the SAME non-empty group hold
        /// when ANY of them holds; different groups (and ungrouped conditions) still AND together.
        /// </summary>
        public string Group { get; set; }

        /// <summary>
        /// Inverts the condition: it holds while <see cref="Prop"/> does NOT have
        /// <see cref="Value"/> (with <c>Value == null</c>: while the prop is empty / false / null).
        /// A fallback glyph that only appears while a portrait is missing is this shape.
        /// </summary>
        public bool Negate { get; set; }

        public SusDependsOnAttribute(string prop, string value = null)
        {
            Prop = prop;
            Value = value;
        }

        /// <summary>
        /// Human-readable condition: <c>ContentMode = icon</c> / <c>Clearable</c> /
        /// <c>ImageSrc empty</c> / <c>Phase ≠ completed</c>.
        /// </summary>
        public string Describe() =>
            Negate
                ? (Value == null ? Prop + " empty" : Prop + " ≠ " + Value)
                : (Value == null ? Prop : Prop + " = " + Value);

        public override string ToString() => Describe();
    }

    /// <summary>
    /// Declares a value of a numeric prop that has its own meaning and may lie OUTSIDE (or inside)
    /// the <see cref="SusRangeAttribute"/> range: <c>-1</c> = "auto", <c>-1</c> = "none". A control
    /// offers each one as a chip that writes the value as it is, so a sentinel is never clamped
    /// into the range by the control (ARCH-20261014-SB-PROP-CONTROLS D4, d:420c0c).
    /// <code>
    /// [SusRange(0, 600, 10, Unit = "px")]
    /// [SusSpecial(-1, "auto")] public Prop&lt;float&gt; Width = new(-1f);
    /// </code>
    /// Repeatable: one chip per attribute, in declaration order.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public sealed class SusSpecialAttribute : Attribute
    {
        /// <summary>The value the prop holds while this special meaning is active.</summary>
        public double Value { get; }

        /// <summary>Name of the special meaning, shown on the chip ("auto", "none", "hidden").</summary>
        public string Label { get; }

        public SusSpecialAttribute(double value, string label)
        {
            Value = value;
            Label = label ?? string.Empty;
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} = {1}", Value, Label);
    }

    /// <summary>
    /// Takes the bounds (and the step) of a prop's slider from NEIGHBOUR props of the same
    /// component, on top of the static <see cref="SusRangeAttribute"/> that stays on the field and
    /// supplies whatever is not named here (ARCH-20261014-SB-PROP-CONTROLS D4, d:420c0c).
    /// <code>
    /// [SusRange(0, 5, 0.5)]
    /// [SusRangeFrom(MaxProp = nameof(Max))] public Prop&lt;float&gt; Value = new(0);
    /// </code>
    /// Any subset of the three names may be set. A sibling that does not exist or is not numeric,
    /// or a missing <see cref="SusRangeAttribute"/>, makes the declaration INVALID: the control
    /// falls back to a plain field and says so (dev captions).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class SusRangeFromAttribute : Attribute
    {
        /// <summary>Name of the prop that holds the lower bound, or null.</summary>
        public string MinProp { get; set; }

        /// <summary>Name of the prop that holds the upper bound, or null.</summary>
        public string MaxProp { get; set; }

        /// <summary>Name of the prop that holds the slider step, or null.</summary>
        public string StepProp { get; set; }

        public override string ToString() =>
            "min=" + (MinProp ?? "-") + " max=" + (MaxProp ?? "-") + " step=" + (StepProp ?? "-");
    }
}
