using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Template expression helpers for <see cref="BuildMethodGenerator"/>:
    /// Prop/collection unwrap, single-quote → C# string, and F5 pipe converters.
    /// </summary>
    internal static class ExpressionTranslator
    {
        /// <summary>
        /// If expr is a simple identifier that matches a Prop&lt;T&gt; field in the script,
        /// appends .Value so that BindChildProp gets the raw value instead of the Prop wrapper.
        /// E.g. "Progress" (Prop&lt;int&gt;) → "Progress.Value"
        /// Leaves complex expressions (unit.HpPercent, squad.IsReady) unchanged.
        /// </summary>
        internal static string ResolvePropExpr(string expr, SharqFileModel model)
        {
            if (string.IsNullOrEmpty(expr) || expr.Contains('.'))
                return expr;

            var identifier = expr.Trim();
            if (string.IsNullOrEmpty(model?.ScriptBody))
                return expr;

            // Match: Prop<T> identifier  (case-sensitive to avoid false positives)
            if (Regex.IsMatch(model.ScriptBody, $@"Prop<\w+>\s+{Regex.Escape(identifier)}\b"))
                return $"{identifier}.Value";

            return expr;
        }

        /// <summary>
        /// Resolves a v-for collection expression for reactive BindList.
        /// If the collection is a Prop&lt;...&gt; field (e.g. Prop&lt;List&lt;T&gt;&gt; Items),
        /// appends ".Value" so the getter reads the underlying collection inside the
        /// ReactiveEffect (tracking the Prop dependency).
        /// Leaves complex expressions (already ".Value", method calls, member access) unchanged.
        /// </summary>
        internal static string ResolveCollectionExpr(string expr, SharqFileModel model)
        {
            if (string.IsNullOrEmpty(expr) || expr.Contains('.') || expr.Contains('('))
                return expr;

            var identifier = expr.Trim();
            if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(model?.ScriptBody))
                return expr;

            var collectionVar = identifier.StartsWith("this.")
                ? identifier.Substring(5)
                : identifier;

            // Match a Prop<...> field declaration (allows nested generics like Prop<List<T>>)
            if (Regex.IsMatch(model.ScriptBody, $@"Prop<[^=;]+?>\s+{Regex.Escape(collectionVar)}\b"))
                return $"{identifier}.Value";

            return expr;
        }

        /// <summary>
        /// Converts Vue-style single-quoted string literals in a template
        /// expression to C# double-quoted strings (e.g. Mode != 'delete' →
        /// Mode != "delete"). Content already inside double quotes is left as-is.
        /// </summary>
        internal static string TranslateExpr(string expr)
        {
            if (string.IsNullOrEmpty(expr) || expr.IndexOf('\'') < 0)
                return expr;

            var sb = new StringBuilder(expr.Length);
            var inDouble = false;
            for (int i = 0; i < expr.Length; i++)
            {
                var c = expr[i];
                if (c == '"')
                {
                    inDouble = !inDouble;
                    sb.Append(c);
                    continue;
                }
                if (c == '\'' && !inDouble)
                {
                    var j = i + 1;
                    sb.Append('"');
                    while (j < expr.Length && expr[j] != '\'')
                    {
                        if (expr[j] == '"') sb.Append("\\\"");
                        else sb.Append(expr[j]);
                        j++;
                    }
                    sb.Append('"');
                    i = j; // skip closing quote
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Translates a binding expression that may contain pipe converters (F5):
        /// <c>Hp.Value | format('{0}/{1}', MaxHp.Value)</c> →
        /// <c>SusBindingConverters.Format("{0}/{1}", Hp.Value, MaxHp.Value)</c>
        /// <c>Name.Value | upper</c> → <c>SusBindingConverters.Upper(Name.Value)</c>
        /// Without pipes — same as <see cref="TranslateExpr"/>.
        /// </summary>
        internal static string TranslateBindingExpr(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return expr;
            if (expr.IndexOf('|') < 0)
                return TranslateExpr(expr);

            // Split on top-level pipes (not inside quotes/parens)
            var parts = SplitPipes(expr);
            if (parts.Count == 0) return TranslateExpr(expr);

            var current = TranslateExpr(parts[0].Trim());
            for (int i = 1; i < parts.Count; i++)
            {
                var pipe = parts[i].Trim();
                if (string.IsNullOrEmpty(pipe)) continue;

                // pipeName(args...) or bare pipeName
                string name;
                string argsInside = null;
                var paren = pipe.IndexOf('(');
                if (paren > 0 && pipe.EndsWith(")"))
                {
                    name = pipe.Substring(0, paren).Trim();
                    argsInside = pipe.Substring(paren + 1, pipe.Length - paren - 2).Trim();
                }
                else
                {
                    name = pipe;
                }

                name = name.ToLowerInvariant();
                switch (name)
                {
                    case "format":
                        // format('{0}', arg2, ...) — first arg is format string; value is prepended as arg0
                        // OR format('x') alone → Format(value, 'x')
                        if (string.IsNullOrEmpty(argsInside))
                            current = $"SusBindingConverters.Format({current}, \"{{0}}\")";
                        else
                        {
                            var args = SplitArgs(argsInside);
                            if (args.Count == 1)
                                current = $"SusBindingConverters.Format({current}, {TranslateExpr(args[0])})";
                            else
                            {
                                // :text="Hp.Value | format('{0}/{1}', MaxHp.Value)"
                                // → Format("{0}/{1}", Hp.Value, MaxHp.Value)
                                var fmt = TranslateExpr(args[0]);
                                var rest = new StringBuilder();
                                rest.Append(current);
                                for (int a = 1; a < args.Count; a++)
                                {
                                    rest.Append(", ");
                                    rest.Append(TranslateExpr(args[a]));
                                }
                                current = $"SusBindingConverters.Format({fmt}, {rest})";
                            }
                        }
                        break;
                    case "upper":
                        current = $"SusBindingConverters.Upper({current})";
                        break;
                    case "lower":
                        current = $"SusBindingConverters.Lower({current})";
                        break;
                    case "round":
                        if (string.IsNullOrEmpty(argsInside))
                            current = $"SusBindingConverters.Round({current})";
                        else
                            current = $"SusBindingConverters.Round({current}, {TranslateExpr(argsInside)})";
                        break;
                    case "truncate":
                        current = string.IsNullOrEmpty(argsInside)
                            ? $"SusBindingConverters.Truncate({current}, 40)"
                            : $"SusBindingConverters.Truncate({current}, {TranslateExpr(argsInside)})";
                        break;
                    default:
                        // Unknown pipe — leave as comment-safe passthrough
                        current = $"/* unknown pipe:{name} */ ({current})";
                        break;
                }
            }

            return current;
        }

        private static List<string> SplitPipes(string expr)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;
            bool inSingle = false, inDouble = false;
            for (int i = 0; i < expr.Length; i++)
            {
                var c = expr[i];
                if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (!inSingle && !inDouble)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth = Math.Max(0, depth - 1);
                    else if (c == '|' && depth == 0)
                    {
                        parts.Add(sb.ToString());
                        sb.Clear();
                        continue;
                    }
                }
                sb.Append(c);
            }
            parts.Add(sb.ToString());
            return parts;
        }

        private static List<string> SplitArgs(string args)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;
            bool inSingle = false, inDouble = false;
            for (int i = 0; i < args.Length; i++)
            {
                var c = args[i];
                if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (!inSingle && !inDouble)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth = Math.Max(0, depth - 1);
                    else if (c == ',' && depth == 0)
                    {
                        parts.Add(sb.ToString().Trim());
                        sb.Clear();
                        continue;
                    }
                }
                sb.Append(c);
            }
            var last = sb.ToString().Trim();
            if (last.Length > 0) parts.Add(last);
            return parts;
        }
    }
}
