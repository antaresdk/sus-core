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
                    result.ScopedCss = ScopedCssGenerator.GenerateFromNodes(nodes, model.ClassName, out var map);
                    result.MapLines = map;
                }
                else
                {
                    result.ScopedCss = ScopedCssGenerator.Generate(model, out var map);
                    result.MapLines = map;
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
                    result.GlobalCss = ScopedCssGenerator.GenerateGlobalFromNodes(nodes, out var map).Trim();
                    result.MapLines = map;
                }
                else
                {
                    // Global, no @variants: raw CSS pass-through (byte-identical to the source
                    // body, D-13) — CssScanner/EmitNodes never run, so the source map can't come
                    // from DeclLine bookkeeping. It doesn't need to: verbatim text means EVERY
                    // physical output line IS the corresponding source line, 1:1, shifted only by
                    // however many leading blank lines Trim() ate (T-3295) — this is in fact the
                    // MOST common corpus shape today (every component of the largest downstream
                    // package, as of this card).
                    var trimmed = model.StyleBody.Trim();
                    result.GlobalCss = trimmed;
                    result.MapLines = BuildIdentityMap(model.StyleBody, trimmed);
                }
                result.HasGlobalCss = true;
            }

            return result;
        }

        /// <summary>
        /// (T-3295) Source map for the raw verbatim pass-through path: <paramref name="trimmed"/>
        /// is <paramref name="original"/> with leading/trailing whitespace stripped and NOTHING
        /// else changed, so USS line 1 is source line <c>1 + (leading blank lines)</c>, and every
        /// following line just increments both sides together.
        /// </summary>
        private static List<SharqMapLine> BuildIdentityMap(string original, string trimmed)
        {
            var list = new List<SharqMapLine>();
            if (trimmed.Length == 0) return list;

            var leadingLen = original.Length - original.TrimStart().Length;
            var srcLine = 1 + TextLines.CountNewlines(original, 0, leadingLen);
            var lineCount = TextLines.CountNewlines(trimmed, 0, trimmed.Length) + 1;

            for (var k = 0; k < lineCount; k++)
                list.Add(new SharqMapLine { Uss = 1 + k, Src = srcLine + k, Origin = "style" });
            return list;
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
        /// <summary>
        /// (T-3295) Source map rows, USS line ↔ line WITHIN <c>model.StyleBody</c> (local —
        /// the caller adds the file offset, see <see cref="SharqSourceMapWriter"/>). Empty
        /// when the style body produced no mappable declaration (e.g. <c>@media</c>-only with
        /// no rules), never null.
        /// </summary>
        public List<SharqMapLine> MapLines = new();
    }
}
