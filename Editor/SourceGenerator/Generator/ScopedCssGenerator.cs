using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Generates scoped USS files from .sharq &lt;style scoped&gt; sections.
    /// Phase 0: wraps each CSS rule with [data-s-xxxx] attribute selector.
    /// </summary>
    internal static class ScopedCssGenerator
    {
        public static string Generate(SharqFileModel model)
        {
            if (string.IsNullOrEmpty(model.StyleBody))
                return null;

            var hash = GenerateScopedHash(model.ClassName);
            var scoped = new StringBuilder();

            // P2.1: brace-balanced scan (handles @media nesting, nested braces,
            // comments, strings and url(...)) instead of the old fragile regex.
            var nodes = CssScanner.Parse(model.StyleBody);
            EmitNodes(nodes, hash, scoped, indent: "");

            return scoped.ToString();
        }

        private static void EmitNodes(System.Collections.Generic.IReadOnlyList<CssNode> nodes,
            string hash, StringBuilder sb, string indent)
        {
            foreach (var node in nodes)
            {
                if (!node.HasBlock)
                {
                    // At-statement (e.g. @import ...;) — emit verbatim.
                    sb.AppendLine($"{indent}{node.Prelude};");
                    sb.AppendLine();
                    continue;
                }

                if (node.Children.Count > 0 || (node.IsAtRule && node.Declarations == null))
                {
                    // Nesting at-rule: keep prelude, scope the inner selectors.
                    sb.AppendLine($"{indent}{node.Prelude} {{");
                    EmitNodes(node.Children, hash, sb, indent + "    ");
                    sb.AppendLine($"{indent}}}");
                    sb.AppendLine();
                    continue;
                }

                var selector = node.Prelude;
                if (string.IsNullOrEmpty(selector)) continue;

                sb.AppendLine($"{indent}{ScopeSelector(selector, hash)} {{");
                sb.AppendLine($"{indent}    {node.Declarations}");
                sb.AppendLine($"{indent}}}");
                sb.AppendLine();
            }
        }

        /// <summary>
        /// Appends the scope class <c>.s-{hash}</c> to each comma-separated selector part,
        /// inserting it before any trailing pseudo-class/element (<c>:hover</c>, <c>::before</c>).
        /// </summary>
        private static string ScopeSelector(string selector, string hash)
        {
            var parts = selector.Split(',');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                if (i > 0) sb.Append(", ");
                if (string.IsNullOrEmpty(part)) continue;

                var pseudoMatch = Regex.Match(part, @"(:{1,2}[\w-]+.*)$");
                if (pseudoMatch.Success)
                {
                    var baseSelector = part.Substring(0, part.Length - pseudoMatch.Length);
                    sb.Append($"{baseSelector.Trim()}.{hash}{pseudoMatch.Value}");
                }
                else
                {
                    sb.Append($"{part}.{hash}");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes the scoped USS to disk next to the .sharq file.
        /// Called by the Source Generator during compilation.
        /// </summary>
        public static void WriteToDisk(SharqFileModel model, string scopedCss)
        {
            if (string.IsNullOrEmpty(model.SourcePath) || string.IsNullOrEmpty(scopedCss))
                return;

            var dir = Path.GetDirectoryName(model.SourcePath);
            var ussPath = Path.Combine(dir, $"{model.ClassName}_scoped.uss");

            try
            {
                File.WriteAllText(ussPath, scopedCss, Encoding.UTF8);
            }
            catch
            {
                // Silently fail — USS is non-critical; C# generation takes priority
            }
        }

        private static string GenerateScopedHash(string className)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in className)
                    hash = hash * 31 + c;
                return $"s-{Math.Abs(hash):x6}";
            }
        }
    }
}
