using System;
using System.Collections.Generic;
using System.Linq;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Parses the &lt;style&gt; section of a .sharq file.
    /// Handles both global (non-scoped) and scoped styles.
    /// For scoped: delegates to ScopedCssGenerator.
    /// For global: validates CSS rules and returns the body as-is.
    /// </summary>
    internal static class StyleParser
    {
        public static StyleParseResult Parse(SharqFileModel model)
        {
            var result = new StyleParseResult();

            if (string.IsNullOrEmpty(model.StyleBody))
                return result;

            // P2.1: count leaf rules via the brace-balanced scanner (handles @media,
            // nested braces, comments and strings) instead of the fragile regex.
            result.RuleCount = CssScanner.CountRules(CssScanner.Parse(model.StyleBody));

            // T-3292: only a style body that actually USES @variants pays for the extra parse —
            // every one of the 194 corpus files (and every fixture without the at-rule) takes the
            // exact code path it took before this card, byte for byte (D-13's zero-diff holds by
            // construction, not assertion).
            var hasVariants = model.StyleBody.Contains("@variants");

            if (model.IsStyleScoped)
            {
                if (hasVariants)
                {
                    var nodes = CssScanner.Parse(model.StyleBody);
                    var axes = VariantsCompiler.ExtractAndRewrite(nodes, model, out var errors);
                    ThrowIfErrors(model, errors);
                    result.VariantRecipes = axes;
                    result.ScopedCss = ScopedCssGenerator.GenerateFromNodes(nodes, model.ClassName);
                }
                else
                {
                    result.ScopedCss = ScopedCssGenerator.Generate(model);
                }
                result.HasScopedCss = true;
            }
            else
            {
                if (hasVariants)
                {
                    var nodes = CssScanner.Parse(model.StyleBody);
                    var axes = VariantsCompiler.ExtractAndRewrite(nodes, model, out var errors);
                    ThrowIfErrors(model, errors);
                    result.VariantRecipes = axes;
                    result.GlobalCss = ScopedCssGenerator.GenerateGlobalFromNodes(nodes).Trim();
                }
                else
                {
                    // Global: return raw CSS
                    result.GlobalCss = model.StyleBody.Trim();
                }
                result.HasGlobalCss = true;
            }

            return result;
        }

        private static void ThrowIfErrors(SharqFileModel model, List<VariantError> errors)
        {
            if (errors == null || errors.Count == 0) return;
            throw new InvalidOperationException(
                $"{model.ClassName}.sharq: " + string.Join(" | ", errors.Select(e => e.Message)));
        }
    }

    internal class StyleParseResult
    {
        public bool HasScopedCss;
        public bool HasGlobalCss;
        public string ScopedCss;
        public string GlobalCss;
        public int RuleCount;
        /// <summary>@variants axes found (T-3292); null when the style body has none.</summary>
        public List<VariantAxis> VariantRecipes;
    }
}
