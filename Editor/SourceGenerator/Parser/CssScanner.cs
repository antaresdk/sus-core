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
        /// <summary>
        /// This node's OWN declarations, trimmed (empty string when a rule has none of its
        /// own — e.g. only nested rules; null for nesting at-rules / at-statements, which
        /// never carry declarations).
        /// </summary>
        public string Declarations;
        /// <summary>
        /// Nested nodes: children of a nesting at-rule (<c>@media</c>/<c>@supports</c>/…), or
        /// (T-3291) nested rules/at-rules found inside an ordinary rule's body.
        /// </summary>
        public readonly List<CssNode> Children = new();
    }

    /// <summary>
    /// Brace-balanced CSS scanner (P2.1) that replaces the fragile
    /// <c>selector { body }</c> regex used by <see cref="StyleParser"/> and
    /// <see cref="ScopedCssGenerator"/>.
    ///
    /// Correctly handles:
    ///  • nested at-rules (<c>@media</c>, <c>@supports</c>, <c>@container</c>, <c>@layer</c>);
    ///  • (T-3291) nested rules inside an ordinary rule's body — CSS Nesting shape, e.g.
    ///    <c>.a { color: red; &amp;:hover { color: blue; } }</c> — recursing rather than
    ///    reading the body as an opaque string, and separating the rule's own declarations
    ///    from its nested children;
    ///  • block comments <c>/* … */</c> — including a stray <c>/* } */</c>;
    ///  • string literals (<c>content: "}"</c>, attribute selectors <c>[x="{"]</c>);
    ///  • <c>url(data:…)</c> parens that may contain braces/quotes;
    ///  • at-statements terminated by <c>;</c> (e.g. <c>@import url(…);</c>).
    /// </summary>
    internal static class CssScanner
    {
        private static readonly string[] NestingAtRules =
        {
            "@media", "@supports", "@container", "@document", "@-moz-document", "@layer", "@scope",
            // T-3292 (plan §4.1): `@variants <axis> { <value> { … } … }` has the exact same
            // brace shape as @media — a nesting at-rule whose children are themselves parsed as
            // ordinary rules (own declarations + T-3291 nested `&` children). Interpreting that
            // shape as a recipe is VariantsCompiler's job, not the scanner's — this line only
            // buys it a correct parse tree to work from.
            "@variants",
        };

        public static List<CssNode> Parse(string css)
        {
            var nodes = new List<CssNode>();
            if (string.IsNullOrEmpty(css)) return nodes;
            int i = 0;
            ParseNodes(css, ref i, nodes, null);
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
        //
        // T-3291 (plan §4.3): the same scan now serves two contexts, told apart by whether
        // `ownDeclarations` is non-null —
        //   • top-level stylesheet / a nesting at-rule's body (@media, …): `ownDeclarations`
        //     is null, and a `;`-terminated segment becomes an at-statement sibling node
        //     (unchanged from before this card — e.g. `@import url(…);`).
        //   • the body of an ordinary rule: `ownDeclarations` collects that rule's OWN
        //     declaration text, appended verbatim exactly as the old (pre-nesting)
        //     `ReadDeclarations` did — this is what keeps a body with no nested rule
        //     byte-identical to before. A `{` is now ALWAYS a nested node (rule or nesting
        //     at-rule) rather than an opaque depth-tracked brace: hitting it flushes
        //     everything accumulated since the last flush as the nested node's own prelude
        //     and recurses, so an existing flat rule (no nested `{`) never takes this branch
        //     and its declarations text is unchanged.
        private static void ParseNodes(string css, ref int i, List<CssNode> outNodes, StringBuilder ownDeclarations)
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
                    if (ownDeclarations != null) ownDeclarations.Append(prelude);
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
                    if (ownDeclarations != null)
                    {
                        // Rule body: flush the declaration just closed (verbatim, ';'
                        // included) so a following '{' only sees the NEXT nested node's own
                        // prelude — not this declaration's text too (T-3291 fix: without this
                        // flush, `color: red; &:hover {` mis-parsed the whole run as one
                        // child prelude and dropped "color: red;" outside any block).
                        prelude.Append(c);
                        ownDeclarations.Append(prelude);
                        prelude.Clear();
                        continue;
                    }
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
                    {
                        ParseNodes(css, ref i, node.Children, null); // recurse; consumes matching '}'
                    }
                    else
                    {
                        // Rule body: recurse, separating this rule's OWN declarations from
                        // further-nested rule/at-rule children (T-3291).
                        var declSb = new StringBuilder();
                        ParseNodes(css, ref i, node.Children, declSb);
                        node.Declarations = declSb.ToString().Trim();
                    }
                    outNodes.Add(node);
                    continue;
                }

                prelude.Append(c);
                i++;
            }
            if (ownDeclarations != null) ownDeclarations.Append(prelude);
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
