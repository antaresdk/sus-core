using System;
using System.Globalization;

namespace Sharq.Core
{
    /// <summary>
    /// Declares the numeric range of a <see cref="Prop{T}"/> field (<c>int</c>/<c>float</c>/
    /// <c>double</c>) so a generated control (slider + numeric field) knows its bounds instead of
    /// guessing. Without the attribute a consumer falls back to 0…100
    /// (ARCH-20260907-STORYBOOK-ENGINE §4.2/§4.3).
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

        /// <summary>Increment of the control; 0 means "the control decides".</summary>
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
    /// reads as a defect (ARCH-20260907-STORYBOOK-ENGINE §4.3.1, T-3026 п. 3).
    /// <code>
    /// [SusDependsOn(nameof(ContentMode), "icon")] public Prop&lt;string&gt; Icon = new("");
    /// [SusDependsOn(nameof(Clearable))]           public Prop&lt;string&gt; ClearIcon = new("x");
    /// </code>
    /// Several attributes on one field mean ALL conditions must hold (AND).
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

        public SusDependsOnAttribute(string prop, string value = null)
        {
            Prop = prop;
            Value = value;
        }

        /// <summary>Human-readable condition: <c>ContentMode = icon</c> / <c>Clearable</c>.</summary>
        public string Describe() =>
            Value == null ? Prop : Prop + " = " + Value;

        public override string ToString() => Describe();
    }
}
