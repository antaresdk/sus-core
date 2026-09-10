using System;
using System.Collections.Generic;

namespace Sharq.Core
{
    /// <summary>
    /// Registry for <c>@variants</c> recipes compiled from a component's <c>&lt;style&gt;</c>
    /// (plan ARCH-20260910-SHARQ-STYLE-LAYER.md §4.1/§4.2, card T-3292, decision D-6). Mirrors
    /// <c>SusComponent.Allowed.cs</c>'s <c>RegisterAllowed</c>/<c>DescribeAllowed</c> (T-3031):
    /// the GENERATOR records one fact per axis at <c>Build()</c> time, this exposes it back to a
    /// caller that does not know the component's type.
    /// </summary>
    public abstract partial class SusComponent
    {
        List<SusVariantRecipeInfo> _recipeRecords;

        /// <summary>
        /// Called by GENERATED code only (<c>BuildMethodGenerator.EmitVariantRecipes</c>) — a
        /// hand-written <c>&lt;script&gt;</c> has no reason to call this directly. Re-registration
        /// for the same axis name replaces the record (same rule <c>RegisterAllowed</c> uses for
        /// a re-registered prop).
        /// </summary>
        /// <remarks>
        /// (T-3297) Takes PRIMITIVES, not a pre-built <see cref="SusVariantRecipeInfo"/> — that
        /// type's setters are deliberately <c>internal</c> (sus-core's own encapsulation), and
        /// generated <c>Build()</c> code for a <c>@variants</c> axis lands in WHATEVER package
        /// declared it (kit, game, a third-party paid package sus-core has never heard of) — a
        /// different assembly every time. The earlier shape (<c>RegisterVariantRecipe(new
        /// SusVariantRecipeInfo { Axis = …, … })</c>) compiled in the fixture-only tests T-3292
        /// shipped with (no real corpus file used a non-ambient axis yet) but broke on first real
        /// use here (CS0200, first migrated component: an object initializer needs `internal set`
        /// visible from the CALLER's assembly). The fix generalizes the SAME idiom <see cref="UseAllowed"/>/
        /// <c>AllowedRecord</c> (T-3031) already use for exactly this shape of problem: sus-core
        /// exposes a public METHOD with plain parameters, and only sus-core's own code (this
        /// method) ever constructs the internal-setter type — no <c>InternalsVisibleTo</c> grant
        /// naming a downstream package is needed, which R25 (public-scope, D-24) would have
        /// refused from sus-core's Runtime anyway.
        /// </remarks>
        protected void RegisterVariantRecipe(
            string axis,
            string propName,
            bool ambient = false,
            IReadOnlyList<string> values = null,
            IReadOnlyDictionary<string, string> aliases = null,
            string defaultValue = null,
            IReadOnlyList<string> metrics = null)
        {
            if (string.IsNullOrEmpty(axis)) throw new ArgumentException("axis must be non-empty", nameof(axis));

            var recipe = new SusVariantRecipeInfo
            {
                Axis = axis,
                PropName = propName,
                Ambient = ambient,
                Values = values ?? Array.Empty<string>(),
                Aliases = aliases ?? new Dictionary<string, string>(0, StringComparer.Ordinal),
                Default = defaultValue,
                Metrics = metrics ?? Array.Empty<string>(),
            };

            _recipeRecords ??= new List<SusVariantRecipeInfo>();
            for (int i = 0; i < _recipeRecords.Count; i++)
            {
                if (string.Equals(_recipeRecords[i].Axis, recipe.Axis, StringComparison.Ordinal))
                {
                    _recipeRecords[i] = recipe;
                    return;
                }
            }
            _recipeRecords.Add(recipe);
        }

        /// <summary>
        /// Every <c>@variants</c> axis this component's compiled <c>&lt;style&gt;</c> declared,
        /// in declaration order. Empty for a component that has not migrated to recipes yet
        /// (plan D-13: introducing the capability moves no existing component's behaviour).
        /// </summary>
        public IReadOnlyList<SusVariantRecipeInfo> DescribeRecipe() =>
            (IReadOnlyList<SusVariantRecipeInfo>)_recipeRecords ?? Array.Empty<SusVariantRecipeInfo>();
    }
}
