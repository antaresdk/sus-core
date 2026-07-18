using System.Collections.Generic;
using System.Text;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// A single parsed CSS node: either a rule (<c>selector { declarations }</c>),
    /// a nesting at-rule (<c>@media (...) { children }</c>), or an at-statement
    /// (<c>@import ...;</c>).
    /// </summary>
    internal sealed class CssNode
    {
        /// <summary>Selector, or at-rule prelude (e.g. <c>@media (min-width: 600px)</c>), trimmed.</summary>
        public string Prelude;
        /// <summary>True when <see cref="Prelude"/> starts with <c>@</c>.</summary>
        public bool IsAtRule;
        /// <summary>True when the node has a <c>{ ... }</c> block (rule or nesting at-rule).</summary>
        public bool HasBlock;
        /// <summary>Raw declarations of a leaf rule (null for nesting at-rules / at-statements).</summary>
        public string Declarations;
        /// <summary>Nested nodes for nesting at-rules (<c>@media</c>/<c>@supports</c>/…).</summary>
        public readonly List<CssNode> Children = new();
    }

    /// <summary>
    /// Brace-balanced CSS scanner (P2.1) that replaces the fragile
    /// <c>selector { body }</c> regex used by <see cref="StyleParser"/> and
    /// <see cref="ScopedCssGenerator"/>.
    ///
    /// Correctly handles:
    ///  • nested at-rules (<c>@media</c>, <c>@supports</c>, <c>@container</c>, <c>@layer</c>);
    ///  • nested braces inside declarations;
    ///  • block comments <c>/* … */</c> — including a stray <c>/* } */</c>;
    ///  • string literals (<c>content: "}"</c>, attribute selectors <c>[x="{"]</c>);
    ///  • <c>url(data:…)</c> parens that may contain braces/quotes;
    ///  • at-statements terminated by <c>;</c> (e.g. <c>@import url(…);</c>).
    /// </summary>
    internal static class CssScanner
    {
        private static readonly string[] NestingAtRules =
        {
            "@media", "@supports", "@container", "@document", "@-moz-document", "@layer", "@scope"
        };

        public static List<CssNode> Parse(string css)
        {
            var nodes = new List<CssNode>();
            if (string.IsNullOrEmpty(css)) return nodes;
            int i = 0;
            ParseNodes(css, ref i, nodes);
            return nodes;
        }

        /// <summary>Counts leaf rules (non-nesting <c>selector { }</c> blocks) recursively.</summary>
        public static int CountRules(IReadOnlyList<CssNode> nodes)
        {
            int n = 0;
            foreach (var node in nodes)
            {
                if (node.HasBlock && node.Declarations != null) n++;
                if (node.Children.Count > 0) n += CountRules(node.Children);
            }
            return n;
        }

        // Parses sibling nodes until end-of-input or the enclosing block's '}' (consumed here).
        private static void ParseNodes(string css, ref int i, List<CssNode> outNodes)
        {
            var prelude = new StringBuilder();
            while (i < css.Length)
            {
                char c = css[i];

                if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
                {
                    i = SkipComment(css, i);
                    continue;
                }
                if (c == '}')
                {
                    i++; // consume the enclosing block's closing brace
                    return;
                }
                if (c == '"' || c == '\'')
                {
                    ReadString(css, ref i, prelude);
                    continue;
                }
                if (c == '(')
                {
                    ReadParen(css, ref i, prelude);
                    continue;
                }
                if (c == ';')
                {
                    i++;
                    var stmt = prelude.ToString().Trim();
                    prelude.Clear();
                    if (stmt.Length > 0)
                        outNodes.Add(new CssNode { Prelude = stmt, IsAtRule = stmt.StartsWith("@"), HasBlock = false });
                    continue;
                }
                if (c == '{')
                {
                    i++;
                    var text = prelude.ToString().Trim();
                    prelude.Clear();
                    var node = new CssNode { Prelude = text, IsAtRule = text.StartsWith("@"), HasBlock = true };
                    if (IsNestingAtRule(text))
                        ParseNodes(css, ref i, node.Children); // recurse; consumes matching '}'
                    else
                        node.Declarations = ReadDeclarations(css, ref i); // reads to matching '}', consumes it
                    outNodes.Add(node);
                    continue;
                }

                prelude.Append(c);
                i++;
            }
        }

        // Reads a leaf block body until the matching '}' (consumed). Respects nested
        // braces, strings, url() parens and comments (comments are dropped).
        private static string ReadDeclarations(string css, ref int i)
        {
            var sb = new StringBuilder();
            int depth = 0;
            while (i < css.Length)
            {
                char c = css[i];
                if (c == '/' && i + 1 < css.Length && css[i + 1] == '*') { i = SkipComment(css, i); continue; }
                if (c == '"' || c == '\'') { ReadString(css, ref i, sb); continue; }
                if (c == '(') { ReadParen(css, ref i, sb); continue; }
                if (c == '{') { depth++; sb.Append(c); i++; continue; }
                if (c == '}')
                {
                    if (depth == 0) { i++; return sb.ToString().Trim(); }
                    depth--; sb.Append(c); i++; continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString().Trim();
        }

        private static void ReadString(string css, ref int i, StringBuilder sink)
        {
            char quote = css[i];
            sink.Append(quote);
            i++;
            while (i < css.Length)
            {
                char c = css[i];
                sink.Append(c);
                i++;
                if (c == '\\' && i < css.Length) { sink.Append(css[i]); i++; continue; }
                if (c == quote) break;
            }
        }

        private static void ReadParen(string css, ref int i, StringBuilder sink)
        {
            int depth = 0;
            while (i < css.Length)
            {
                char c = css[i];
                if (c == '"' || c == '\'') { ReadString(css, ref i, sink); continue; }
                sink.Append(c);
                i++;
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0) break; }
            }
        }

        private static int SkipComment(string css, int i)
        {
            i += 2; // skip "/*"
            while (i + 1 < css.Length && !(css[i] == '*' && css[i + 1] == '/'))
                i++;
            return i + 2 <= css.Length ? i + 2 : css.Length;
        }

        private static bool IsNestingAtRule(string prelude)
        {
            if (string.IsNullOrEmpty(prelude) || prelude[0] != '@') return false;
            foreach (var at in NestingAtRules)
                if (prelude.StartsWith(at, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
