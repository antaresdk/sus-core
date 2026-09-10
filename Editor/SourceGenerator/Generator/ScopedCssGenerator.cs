using System;
using System.Collections.Generic;
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
            // T-3291: the scanner now also recurses into an ordinary rule's body, so this
            // tree can carry real nested rules — flatten them here before scoping.
            var nodes = CssScanner.Parse(model.StyleBody);
            EmitNodes(nodes, hash, scoped, indent: "", parentSelector: null);

            return scoped.ToString();
        }

        /// <summary>
        /// Walks the parsed tree, flattening nested rules into flat selectors (T-3291,
        /// plan §4.3). <paramref name="parentSelector"/> is the already-combined, still
        /// UNSCOPED selector of the enclosing rule (null at the top of the stylesheet or
        /// inside a nesting at-rule with no enclosing rule) — combined first, scoped after
        /// (D-10): scoping the parent alone before splicing in a nested `&amp;:hover` would
        /// produce `.a.s-hash:hover`'s reverse, `.a:hover.s-hash`, which UITK does not match.
        /// Emission order is this rule's own declarations first, then its descendants in the
        /// order they were written (plan §4.3) — matching the pre-nesting output exactly
        /// when a rule has no nested children.
        /// </summary>
        private static void EmitNodes(IReadOnlyList<CssNode> nodes,
            string hash, StringBuilder sb, string indent, string parentSelector)
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

                if (node.Declarations == null)
                {
                    // Nesting at-rule (@media/@supports/…): keep prelude verbatim, scope the
                    // inner selectors against the SAME parent context.
                    sb.AppendLine($"{indent}{node.Prelude} {{");
                    EmitNodes(node.Children, hash, sb, indent + "    ", parentSelector);
                    sb.AppendLine($"{indent}}}");
                    sb.AppendLine();
                    continue;
                }

                // Ordinary rule: combine with the parent selector BEFORE scoping (D-10), then
                // recurse into nested rules using the combined (still unscoped) selector as
                // THEIR parent. `combined == node.Prelude` verbatim when parentSelector is
                // null (top-level rule) — the unchanged pre-nesting path.
                var combined = CombineSelector(parentSelector, node.Prelude);
                if (string.IsNullOrEmpty(combined)) continue;

                sb.AppendLine($"{indent}{ScopeSelector(combined, hash)} {{");
                sb.AppendLine($"{indent}    {node.Declarations}");
                sb.AppendLine($"{indent}}}");
                sb.AppendLine();

                if (node.Children.Count > 0)
                    EmitNodes(node.Children, hash, sb, indent, combined);
            }
        }

        /// <summary>
        /// Combines a nested rule's own selector with its parent's (already-combined, still
        /// unscoped) selector — CSS Nesting semantics (D-8): <c>&amp;</c> stands for the
        /// WHOLE parent selector; a child part without it is an implicit descendant
        /// combinator. A comma-separated parent/child list cross-expands textually (D-9).
        /// No budget or depth error is enforced here: that belongs to the authorial `&lt;style&gt;`
        /// surface (step 12 / T-3301), which this card does not open — no author-reachable
        /// `.sharq` can grow this list today, so there is nothing yet to reject.
        /// </summary>
        private static string CombineSelector(string parentSelector, string childPrelude)
        {
            if (parentSelector == null) return childPrelude;

            var parentParts = SplitSelectorList(parentSelector);
            var childParts = SplitSelectorList(childPrelude);
            var combined = new StringBuilder();
            var first = true;
            foreach (var p in parentParts)
            {
                foreach (var c in childParts)
                {
                    if (!first) combined.Append(", ");
                    first = false;
                    combined.Append(c.Contains("&") ? c.Replace("&", p) : $"{p} {c}");
                }
            }
            return combined.ToString();
        }

        private static string[] SplitSelectorList(string selector)
        {
            var raw = selector.Split(',');
            var parts = new List<string>();
            foreach (var r in raw)
            {
                var t = r.Trim();
                if (t.Length > 0) parts.Add(t);
            }
            return parts.ToArray();
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
