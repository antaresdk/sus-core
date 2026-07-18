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

            if (model.IsStyleScoped)
            {
                // Scoped: delegate to ScopedCssGenerator
                result.ScopedCss = ScopedCssGenerator.Generate(model);
                result.HasScopedCss = true;
            }
            else
            {
                // Global: return raw CSS
                result.GlobalCss = model.StyleBody.Trim();
                result.HasGlobalCss = true;
            }

            return result;
        }
    }

    internal class StyleParseResult
    {
        public bool HasScopedCss;
        public bool HasGlobalCss;
        public string ScopedCss;
        public string GlobalCss;
        public int RuleCount;
    }
}
