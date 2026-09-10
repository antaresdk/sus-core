using System;
using System.Collections.Generic;

namespace Sharq.Core
{
    /// <summary>
    /// One <c>@variants</c> axis recipe, compiled by the SharqSourceGenerator from a
    /// component's <c>&lt;style&gt;</c> (plan ARCH-20260910-SHARQ-STYLE-LAYER.md §4.1/§4.2,
    /// card T-3292, decision D-6). Generated code registers this via
    /// <c>SusComponent.RegisterVariantRecipe</c>; a caller reads it back through
    /// <see cref="SusComponent.DescribeRecipe"/> without knowing the component's type — the
    /// same "record at Build() time, expose at runtime" shape <c>UseAllowed</c>/
    /// <see cref="SusAllowedInfo"/> already use (T-3031).
    /// </summary>
    public sealed class SusVariantRecipeInfo
    {
        static readonly Dictionary<string, string> EmptyAliases = new(0, StringComparer.Ordinal);

        /// <summary>Axis name (<c>size</c>, <c>density</c>, <c>variant</c>…).</summary>
        public string Axis { get; internal set; }

        /// <summary>Prop the axis is driven by (<c>from &lt;Prop&gt;</c>, or PascalCase(axis)).</summary>
        public string PropName { get; internal set; }

        /// <summary>
        /// True for <c>@variants &lt;axis&gt; from &lt;Prop&gt; ambient;</c> — an axis that
        /// writes no rule of its own and works by token override (plan §4.1). <see cref="Values"/>
        /// and <see cref="Metrics"/> are then empty BY CONSTRUCTION (the grammar has no block to
        /// read values from), not by omission — that is what tells an ambient axis apart from a
        /// forgotten one, per the plan's §2.2 complaint.
        /// </summary>
        public bool Ambient { get; internal set; }

        /// <summary>Value names in declaration order (aliases excluded).</summary>
        public IReadOnlyList<string> Values { get; internal set; } = Array.Empty<string>();

        /// <summary>
        /// Accepted synonyms (quoted aliases written in the recipe), plus the implicit
        /// <c>"" → "default"</c> entry when a value literally named <c>default</c> is declared
        /// (D-4). Empty (never null) when the axis declares none.
        /// </summary>
        public IReadOnlyDictionary<string, string> Aliases { get; internal set; } = EmptyAliases;

        /// <summary>Value an unrecognised input is coerced to (the <c>default</c> value, or the
        /// first declared value when none is named <c>default</c>).</summary>
        public string Default { get; internal set; }

        /// <summary>
        /// CSS property names touched by ANY value of this axis, union across values — what D-6
        /// calls "the set of metrics moved". Reported per AXIS, not per value: a consumer (story
        /// matrix, step 11) wants to know the axis moves <c>min-height</c>/<c>font-size</c>, not
        /// which of its five values happens to set which.
        /// </summary>
        public IReadOnlyList<string> Metrics { get; internal set; } = Array.Empty<string>();

        public override string ToString() =>
            Ambient
                ? $"{Axis} (ambient, from {PropName})"
                : $"{Axis} in [{string.Join(", ", Values)}] -> {Default}";
    }
}
