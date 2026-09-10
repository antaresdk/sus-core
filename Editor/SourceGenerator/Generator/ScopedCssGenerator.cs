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
        public static string Generate(SharqFileModel model) => Generate(model, out _);

        /// <summary>
        /// Same as <see cref="Generate(SharqFileModel)"/>, plus the per-line source map
        /// (T-3295, plan §4.4) — <paramref name="map"/>'s <c>Src</c> is local to
        /// <c>model.StyleBody</c> (the caller adds the file offset, see
        /// <see cref="SharqSourceMapWriter"/>).
        /// </summary>
        public static string Generate(SharqFileModel model, out List<SharqMapLine> map)
        {
            if (string.IsNullOrEmpty(model.StyleBody))
            {
                map = new List<SharqMapLine>();
                return null;
            }

            // P2.1: brace-balanced scan (handles @media nesting, nested braces,
            // comments, strings and url(...)) instead of the old fragile regex.
            // T-3291: the scanner now also recurses into an ordinary rule's body, so this
            // tree can carry real nested rules — flatten them here before scoping.
            var nodes = CssScanner.Parse(model.StyleBody);
            return GenerateFromNodes(nodes, model.ClassName, out map);
        }

        /// <summary>
        /// Same as <see cref="Generate(SharqFileModel)"/> but from an ALREADY-PARSED (and
        /// possibly rewritten) node tree — the hook <c>StyleParser</c> uses after
        /// <c>VariantsCompiler</c> has spliced <c>@variants</c> value blocks in as plain rule
        /// nodes (T-3292), so this method itself stays entirely unaware of recipes: it only
        /// ever sees ordinary <see cref="CssNode"/>s.
        /// </summary>
        public static string GenerateFromNodes(List<CssNode> nodes, string className) =>
            GenerateFromNodes(nodes, className, out _);

        /// <summary>Same as above, plus the per-line source map (T-3295, plan §4.4).</summary>
        public static string GenerateFromNodes(List<CssNode> nodes, string className, out List<SharqMapLine> map)
        {
            var hash = GenerateScopedHash(className);
            var scoped = new StringBuilder();
            map = new List<SharqMapLine>();
            var ussLine = 1;
            EmitNodes(nodes, hash, scoped, indent: "", parentSelector: null, map, ref ussLine);
            return scoped.ToString();
        }

        /// <summary>
        /// Same tree walk as <see cref="GenerateFromNodes(List{CssNode}, string)"/> but for
        /// UNSCOPED (<c>&lt;style&gt;</c> without <c>scoped</c>) output — no <c>.s-хеш</c> is
        /// appended to any selector. Used only when the style body contains <c>@variants</c>
        /// (T-3292): a plain global style with none keeps the old byte-for-byte raw-text path
        /// in <c>StyleParser</c>, this method is never on that path.
        /// </summary>
        public static string GenerateGlobalFromNodes(List<CssNode> nodes) => GenerateGlobalFromNodes(nodes, out _);

        /// <summary>Same as above, plus the per-line source map (T-3295, plan §4.4).</summary>
        public static string GenerateGlobalFromNodes(List<CssNode> nodes, out List<SharqMapLine> map)
        {
            var sb = new StringBuilder();
            map = new List<SharqMapLine>();
            var ussLine = 1;
            EmitNodes(nodes, hash: null, sb, indent: "", parentSelector: null, map, ref ussLine);
            return sb.ToString();
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
        /// <summary><paramref name="hash"/> null ⇒ unscoped (T-3292 global-with-@variants path);
        /// non-null ⇒ appends <c>.s-хеш</c> to every rule, same as before this parameter existed.
        /// (T-3295) <paramref name="map"/>/<paramref name="ussLine"/> track the source map
        /// alongside the SAME emission — one entry per PHYSICAL line of a rule's own
        /// declarations (declarations may embed <c>\n</c> verbatim from the source, so one
        /// <c>AppendLine</c> call can still grow the output by several lines); selector/brace/
        /// blank lines get no entry — R22 (step 7) only ever needs to resolve a DECLARATION
        /// line back to <c>.sharq</c>, never a selector line.</summary>
        private static void EmitNodes(IReadOnlyList<CssNode> nodes,
            string hash, StringBuilder sb, string indent, string parentSelector,
            List<SharqMapLine> map, ref int ussLine)
        {
            foreach (var node in nodes)
            {
                if (!node.HasBlock)
                {
                    // At-statement (e.g. @import ...;) — emit verbatim.
                    sb.AppendLine($"{indent}{node.Prelude};");
                    ussLine++;
                    sb.AppendLine();
                    ussLine++;
                    continue;
                }

                if (node.Declarations == null)
                {
                    // Nesting at-rule (@media/@supports/…): keep prelude verbatim, scope the
                    // inner selectors against the SAME parent context.
                    sb.AppendLine($"{indent}{node.Prelude} {{");
                    ussLine++;
                    EmitNodes(node.Children, hash, sb, indent + "    ", parentSelector, map, ref ussLine);
                    sb.AppendLine($"{indent}}}");
                    ussLine++;
                    sb.AppendLine();
                    ussLine++;
                    continue;
                }

                // Ordinary rule: combine with the parent selector BEFORE scoping (D-10), then
                // recurse into nested rules using the combined (still unscoped) selector as
                // THEIR parent. `combined == node.Prelude` verbatim when parentSelector is
                // null (top-level rule) — the unchanged pre-nesting path.
                var combined = CombineSelector(parentSelector, node.Prelude);
                if (string.IsNullOrEmpty(combined)) continue;

                var selectorText = hash != null ? ScopeSelector(combined, hash) : combined;
                sb.AppendLine($"{indent}{selectorText} {{");
                ussLine++;

                var declLineSpan = string.IsNullOrEmpty(node.Declarations)
                    ? 1
                    : TextLines.CountNewlines(node.Declarations, 0, node.Declarations.Length) + 1;
                if (map != null && node.DeclLine > 0)
                {
                    for (var k = 0; k < declLineSpan; k++)
                        map.Add(new SharqMapLine
                        {
                            Uss = ussLine + k,
                            Src = node.DeclLine + k,
                            Origin = node.Origin ?? "style",
                        });
                }
                sb.AppendLine($"{indent}    {node.Declarations}");
                ussLine += declLineSpan;

                sb.AppendLine($"{indent}}}");
                ussLine++;
                sb.AppendLine();
                ussLine++;

                if (node.Children.Count > 0)
                    EmitNodes(node.Children, hash, sb, indent, combined, map, ref ussLine);
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
