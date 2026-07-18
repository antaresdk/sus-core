using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Parses the &lt;script&gt; section of a .sharq file.
    /// Extracts $using directives and separates them from the body.
    /// </summary>
    internal static class ScriptParser
    {
        // Matches: $using Namespace; (with optional whitespace and inline comment)
        private static readonly Regex UsingDirective = new(
            @"^\s*\$using\s+([\w.]+)\s*;?\s*(?://.*)?$",
            RegexOptions.Multiline);

        // Matches: $extends BaseClass; — sets the generated class's base type.
        // Default (no directive) is SusComponent. The base MUST itself derive from
        // SusComponent so the whole component hierarchy stays uniform (see C2 two-tier
        // model). Namespace must be resolvable ($using or Sharq.Core, always imported).
        private static readonly Regex ExtendsDirective = new(
            @"^\s*\$extends\s+([\w.]+)\s*;?\s*(?://.*)?$",
            RegexOptions.Multiline);

        public static ScriptParseResult Parse(string scriptBody)
        {
            var result = new ScriptParseResult();
            if (string.IsNullOrEmpty(scriptBody))
                return result;

            var matches = UsingDirective.Matches(scriptBody);
            foreach (Match match in matches)
            {
                var ns = match.Groups[1].Value.Trim();
                if (!result.Usings.Contains(ns))
                    result.Usings.Add(ns);
            }

            // $extends — first occurrence wins.
            var extendsMatch = ExtendsDirective.Match(scriptBody);
            if (extendsMatch.Success)
                result.BaseClass = extendsMatch.Groups[1].Value.Trim();

            // Remove $using and $extends lines from body
            var body = UsingDirective.Replace(scriptBody, string.Empty);
            body = ExtendsDirective.Replace(body, string.Empty);
            result.Body = body.Trim();

            return result;
        }
    }

    internal class ScriptParseResult
    {
        public List<string> Usings = new();
        public string Body = string.Empty;
        public string BaseClass; // null → default SusComponent
    }
}
