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
        protected void RegisterVariantRecipe(SusVariantRecipeInfo recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
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
