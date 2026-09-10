using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>One value of an axis (<c>xs "x-small" { … }</c>) — compile-time only.</summary>
    internal sealed class VariantValue
    {
        public string Name;
        public readonly List<string> Aliases = new();
    }

    /// <summary>
    /// One <c>@variants</c> axis, parsed from a <c>&lt;style&gt;</c> at-rule — compile-time only
    /// (the runtime-facing shape is <c>SusVariantRecipeInfo</c> in <c>Sharq.Core</c>, emitted by
    /// <see cref="BuildMethodGenerator"/>, T-3292, plan §4.1/§4.2).
    /// </summary>
    internal sealed class VariantAxis
    {
        public string Axis;
        public string PropName;
        public bool Ambient;
        /// <summary>Text of the <c>as "…"</c> clause, or null when absent (D-3).</summary>
        public string AsPrefix;
        public readonly List<VariantValue> Values = new();
        public readonly HashSet<string> Metrics = new(StringComparer.Ordinal);

        /// <summary>
        /// <c>&lt;block&gt;--&lt;prefix&gt;&lt;value&gt;</c> (D-3): prefix is <c>as</c>'s text
        /// verbatim when given (<c>as ""</c> → <c>sus-button--elevated</c>), else <c>&lt;axis&gt;-</c>.
        /// </summary>
        public string ClassFor(string block, string valueName) =>
            $"{block}--{AsPrefix ?? (Axis + "-")}{valueName}";
    }

    internal sealed class VariantError
    {
        public string Message;
    }

    /// <summary>
    /// Compiles <c>@variants</c> at-rules out of a parsed <c>&lt;style&gt;</c> tree (T-3292, plan
    /// §4.1/§4.2). Two callers share this, deliberately kept as ONE pure function so they can
    /// never disagree on what an axis means:
    ///  • <see cref="BuildMethodGenerator"/> — needs the <see cref="VariantAxis"/> list to emit
    ///    <c>BindClass</c>/<c>UseAllowed</c>/<c>RegisterVariantRecipe</c> into <c>Build()</c>;
    ///  • <see cref="StyleParser"/> — needs the REWRITTEN node list (the <c>@variants</c> node
    ///    replaced by plain <c>.&lt;class&gt; { … }</c> rule nodes) so the existing scoped/global
    ///    emitter (T-3291's <c>EmitNodes</c>) needs zero <c>@variants</c> awareness of its own.
    /// Both call sites gate on <c>StyleBody.Contains("@variants")</c> before parsing at all, so a
    /// component that never uses the at-rule pays nothing and its generat is untouched — the
    /// corpus-wide zero-diff guarantee (D-13) holds by construction, not by assertion.
    /// </summary>
    internal static class VariantsCompiler
    {
        // "@variants size from Size as "" ambient" — clauses are optional, this order fixed
        // (plan §4.1 grammar). `ambient` has no block; a blocked axis never sets it.
        private static readonly Regex HeaderRe = new(
            @"^@variants\s+(?<axis>[a-zA-Z][\w-]*)" +
            @"(?:\s+from\s+(?<prop>[A-Za-z_][\w.]*))?" +
            @"(?:\s+as\s+""(?<prefix>[^""]*)"")?" +
            @"(?:\s+(?<ambient>ambient))?\s*$",
            RegexOptions.Compiled);

        // "xs "x-small" "extra-small"" — a bare value name plus zero or more quoted aliases.
        private static readonly Regex ValueHeaderRe = new(
            @"^(?<value>[a-zA-Z][\w-]*)(?<aliases>(\s*""[^""]*"")*)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex AliasRe = new("\"([^\"]*)\"", RegexOptions.Compiled);

        // Property name at the start of a declaration or right after a ';' — same shape used
        // to WRITE declarations elsewhere in the pipeline, just read back here.
        private static readonly Regex PropertyNameRe = new(
            @"(?:^|;)\s*([a-zA-Z-]+)\s*:", RegexOptions.Compiled);

        private static readonly Regex ClassObjectFirstTrueRe = new(
            @"""([\w-]+)""\s*:\s*true\b", RegexOptions.Compiled);

        private static readonly Regex ClassAttrRe = new(
            @"\bclass\s*=\s*""([^""]*)""", RegexOptions.Compiled);

        /// <summary>
        /// Finds top-level <c>@variants</c> nodes in <paramref name="nodes"/>, REMOVES each and
        /// splices in its compiled plain-CSS rule nodes at the same position (empty splice for an
        /// <c>ambient</c> axis — it emits no rule of its own), and returns one
        /// <see cref="VariantAxis"/> per axis found, declaration order. Errors are collected, not
        /// thrown — the caller decides how to surface them (both current callers throw, keeping
        /// a malformed recipe from silently compiling into nothing).
        /// </summary>
        public static List<VariantAxis> ExtractAndRewrite(
            List<CssNode> nodes, SharqFileModel model, out List<VariantError> errors)
        {
            errors = new List<VariantError>();
            var axes = new List<VariantAxis>();
            if (nodes == null || nodes.Count == 0) return axes;

            var block = ResolveBlockClass(model);

            // Walk backwards so removing/inserting at `i` never invalidates earlier indices.
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var node = nodes[i];
                if (!node.IsAtRule ||
                    !node.Prelude.StartsWith("@variants", StringComparison.OrdinalIgnoreCase))
                    continue;

                nodes.RemoveAt(i);

                var axis = CompileAxis(node, errors);
                if (axis == null) continue; // parse error already recorded

                axes.Insert(0, axis); // restores left-to-right order despite the backward walk

                if (!axis.Ambient)
                    nodes.InsertRange(i, BuildRuleNodes(node, axis, block));
            }

            return axes;
        }

        /// <summary>
        /// First class of the root template element (D-3): the literal key of the first
        /// <c>"name": true</c> entry in a <c>:class</c> object map, or the first token of a plain
        /// <c>class="…"</c> attribute. Falls back to the component's own class name — should
        /// never trigger on a well-formed template, but a missing block must name SOMETHING
        /// rather than throw on every future component that adds `@variants` before its root.
        /// </summary>
        public static string ResolveBlockClass(SharqFileModel model)
        {
            var xml = model?.TemplateXml;
            if (string.IsNullOrEmpty(xml)) return model?.ClassName ?? "sus";

            var gt = xml.IndexOf('>');
            var head = gt > 0 ? xml.Substring(0, gt) : xml;

            var m = ClassObjectFirstTrueRe.Match(head);
            if (m.Success) return m.Groups[1].Value;

            var cm = ClassAttrRe.Match(head);
            if (cm.Success)
            {
                var tokens = cm.Groups[1].Value.Split(
                    new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 0) return tokens[0];
            }

            return model.ClassName;
        }

        private static VariantAxis CompileAxis(CssNode node, List<VariantError> errors)
        {
            var m = HeaderRe.Match(node.Prelude.Trim());
            if (!m.Success)
            {
                errors.Add(new VariantError { Message = $"@variants: cannot parse '{node.Prelude}'" });
                return null;
            }

            var axisName = m.Groups["axis"].Value;
            var axis = new VariantAxis
            {
                Axis = axisName,
                PropName = m.Groups["prop"].Success ? m.Groups["prop"].Value : ToPascalCase(axisName),
                Ambient = m.Groups["ambient"].Success,
                AsPrefix = m.Groups["prefix"].Success ? m.Groups["prefix"].Value : null,
            };

            if (axis.Ambient)
            {
                if (node.HasBlock)
                    errors.Add(new VariantError
                    { Message = $"@variants {axisName}: 'ambient' takes no block — did you mean to end it with ';'?" });
                return axis;
            }

            if (!node.HasBlock)
            {
                errors.Add(new VariantError
                { Message = $"@variants {axisName}: missing a block of values (or trailing 'ambient')" });
                return null;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in node.Children)
            {
                var vm = ValueHeaderRe.Match(child.Prelude.Trim());
                if (!vm.Success)
                {
                    errors.Add(new VariantError
                    { Message = $"@variants {axisName}: cannot parse value '{child.Prelude}'" });
                    continue;
                }

                var name = vm.Groups["value"].Value;
                if (!seen.Add(name))
                {
                    errors.Add(new VariantError
                    { Message = $"@variants {axisName}: duplicate value '{name}'" });
                    continue;
                }

                var value = new VariantValue { Name = name };
                foreach (Match am in AliasRe.Matches(vm.Groups["aliases"].Value))
                    value.Aliases.Add(am.Groups[1].Value);
                axis.Values.Add(value);

                CollectMetrics(child, axis.Metrics);
            }

            if (axis.Values.Count == 0)
                errors.Add(new VariantError { Message = $"@variants {axisName}: no values declared" });

            return axis;
        }

        private static void CollectMetrics(CssNode node, HashSet<string> metrics)
        {
            if (!string.IsNullOrEmpty(node.Declarations))
                foreach (Match pm in PropertyNameRe.Matches(node.Declarations))
                    metrics.Add(pm.Groups[1].Value);
            foreach (var child in node.Children)
                CollectMetrics(child, metrics);
        }

        private static List<CssNode> BuildRuleNodes(CssNode variantsNode, VariantAxis axis, string block)
        {
            var result = new List<CssNode>();
            foreach (var child in variantsNode.Children)
            {
                var vm = ValueHeaderRe.Match(child.Prelude.Trim());
                if (!vm.Success) continue; // already recorded as an error above

                var cls = axis.ClassFor(block, vm.Groups["value"].Value);
                var rule = new CssNode
                {
                    Prelude = "." + cls,
                    IsAtRule = false,
                    HasBlock = true,
                    Declarations = child.Declarations,
                };
                // Nested `&`/descendant rules already parsed by T-3291's scanner — reused
                // verbatim, so EmitNodes combines/scopes them exactly like an author-written
                // nested rule (D-10, D-12: this step rides that machinery, doesn't reimplement it).
                rule.Children.AddRange(child.Children);
                result.Add(rule);
            }
            return result;
        }

        private static string ToPascalCase(string kebab)
        {
            var parts = kebab.Split('-');
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p.Substring(1));
            }
            return sb.ToString();
        }
    }
}
