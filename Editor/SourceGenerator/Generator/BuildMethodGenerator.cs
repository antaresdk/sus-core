using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Generates the Build() method body from a parsed template tree.
    /// Phase 3: v-if, v-show, v-for, :text, :class, @event, slot/v-slot.
    ///
    /// P2.8: generation state (var/style counters + collected inline styles) lives on
    /// the INSTANCE, not in static fields, so a parallel batch (one generator per file)
    /// is thread-safe. Use <see cref="Generate(SharqFileModel)"/> for the code-only
    /// convenience, or new an instance to read <see cref="GeneratedStyles"/> from the
    /// same run (see <c>SharqCompilePipeline</c>).
    /// </summary>
    internal sealed class BuildMethodGenerator
    {
        private int _varCounter;
        private int _styleCounter;

        private readonly Dictionary<string, string> _generatedStyles = new();

        /// <summary>
        /// camelCase names that collide with inherited <c>VisualElement</c>/<c>Focusable</c>
        /// members. A prop like <c>Prop&lt;bool&gt; Visible</c> generates a companion
        /// <c>public bool visible {...}</c> that hides <c>VisualElement.visible</c> (CS0108),
        /// so the companion is emitted with the <c>new</c> keyword for these names.
        /// </summary>
        private static readonly HashSet<string> ReservedVisualElementMembers = new(StringComparer.Ordinal)
        {
            "visible", "name", "style", "parent", "tooltip", "layout", "transform",
            "panel", "hierarchy", "childCount", "userData", "viewDataKey", "pickingMode",
            "usageHints", "enabledSelf", "enabledInHierarchy", "resolvedStyle", "customStyle",
            "styleSheets", "schedule", "experimental", "contentRect", "worldBound",
            "localBound", "worldTransform", "visualTree", "dataSource", "dataSourcePath",
            "language", "languageDirection", "focusable", "tabIndex", "delegatesFocus",
            "canGrabFocus", "focusController", "contentContainer",
        };

        /// <summary>
        /// Maps raw inline style strings to generated USS class names, populated by the
        /// most recent <see cref="GenerateCode"/> call on THIS instance.
        /// Key: "font-size: 24px; color: white;", Value: "sharq-ComponentName-s0".
        /// Deduplicates — two elements with identical styles share one class.
        /// </summary>
        internal IReadOnlyDictionary<string, string> GeneratedStyles => _generatedStyles;

        /// <summary>
        /// Code-only convenience entry point (tests / CLI). Creates a throwaway instance;
        /// callers that also need <see cref="GeneratedStyles"/> should new an instance and
        /// call <see cref="GenerateCode"/> so code and styles come from the same run.
        /// </summary>
        public static string Generate(SharqFileModel model)
            => new BuildMethodGenerator().GenerateCode(model);

        /// <summary>
        /// Source path for the generated file's header, relative to the project folder.
        /// An absolute path would bake the machine that ran the generator into a file teams
        /// commit, so every teammate's regeneration shows a diff that is only their own path.
        /// </summary>
        private static string SourceLabel(SharqFileModel model)
        {
            var path = model.SourcePath;
            if (string.IsNullOrEmpty(path))
                return model.ClassName + ".sharq";

            path = path.Replace('\\', '/');
            var projectRoot = UnityEngine.Application.dataPath;              // <project>/Assets
            projectRoot = projectRoot.Substring(0, projectRoot.LastIndexOf('/') + 1);
            return path.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(projectRoot.Length)
                : System.IO.Path.GetFileName(path);
        }

        public string GenerateCode(SharqFileModel model)
        {
            if (model?.TemplateXml == null)
                return GenerateEmpty(model?.ClassName ?? "Unknown", model?.BaseClass, model?.Namespace);

            var root = TemplateParser.Parse(model.TemplateXml, model.ClassName);
            var sb = new StringBuilder();
            _varCounter = 0;
            _styleCounter = 0;
            _generatedStyles.Clear();

            // [CreateProperty] transient state — local to this generation pass.
            string createPropDefault = null;

            // ─── Fallback: extract $using / $extends from script body ──
            if (!string.IsNullOrEmpty(model.ScriptBody))
            {
                foreach (var line in model.ScriptBody.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("$using"))
                    {
                        var ns = trimmed.Substring(6).Trim().TrimEnd(';').Trim();
                        if (!string.IsNullOrEmpty(ns) && !model.Usings.Contains(ns))
                            model.Usings.Add(ns);
                    }
                    else if (trimmed.StartsWith("$extends") && string.IsNullOrEmpty(model.BaseClass))
                    {
                        var baseName = trimmed.Substring(8).Trim().TrimEnd(';').Trim();
                        var ci = baseName.IndexOf("//");
                        if (ci >= 0) baseName = baseName.Substring(0, ci).Trim();
                        if (!string.IsNullOrEmpty(baseName)) model.BaseClass = baseName;
                    }
                }
            }

            // ─── Class header ────────────────────────────────────────
            var hasCreateProp = !string.IsNullOrEmpty(model.ScriptBody)
                && Regex.IsMatch(model.ScriptBody, @"\[CreateProperty");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine.UIElements;");
            sb.AppendLine("using Sharq.Core;");
            if (hasCreateProp)
                sb.AppendLine("using Unity.Properties;");
            foreach (var useNs in model.Usings)
            {
                if (useNs != "UnityEngine" && useNs != "Sharq.Core"
                    && useNs != "System" && useNs != "System.Collections.Generic"
                    && useNs != "Unity.Properties" && useNs != "UnityEngine.UIElements")
                    sb.AppendLine($"using {useNs};");
            }
            if (model.Usings.Contains("UnityEngine"))
                sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"// Auto-generated by SharqSourceGenerator from {SourceLabel(model)}");
            // Generated code has no nullable annotations (fields are assigned in Build(),
            // not the ctor) — disable nullable analysis to avoid CS8618/CS86xx noise.
            sb.AppendLine("#nullable disable");
            var classNs = model.Namespace?.Trim();
            var hasNs = !string.IsNullOrEmpty(classNs);
            if (hasNs)
            {
                sb.AppendLine($"namespace {classNs}");
                sb.AppendLine("{");
            }
            // Base class: $extends directive, default SusComponent. The base MUST derive
            // from SusComponent so the whole hierarchy stays uniform (C2 two-tier model).
            var baseClass = string.IsNullOrEmpty(model.BaseClass) ? "SusComponent" : model.BaseClass;
            var indent = hasNs ? "    " : "";
            sb.AppendLine($"{indent}[UxmlElement]");
            sb.AppendLine($"{indent}public partial class {model.ClassName} : {baseClass}");
            sb.AppendLine($"{indent}{{");

            // ─── Script body (injected directly) ─────────────────────
            var memberIndent = indent + "    ";
            if (!string.IsNullOrEmpty(model.ScriptBody))
            {
                sb.AppendLine($"{memberIndent}// ─── From <script> ───");
                var scriptLines = model.ScriptBody.Split('\n');
                var prevWasCreateProp = false;
                foreach (var line in scriptLines)
                {
                    var trimmed = line.TrimEnd('\r');

                    // Skip $using / $extends directive lines
                    if (trimmed.TrimStart().StartsWith("$using")
                        || trimmed.TrimStart().StartsWith("$extends"))
                        continue;

                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        sb.AppendLine();
                        prevWasCreateProp = false;
                        continue;
                    }

                    // [CreateProperty] — parse optional default: / validate: params
                    var createPropMatch = System.Text.RegularExpressions.Regex.Match(
                        trimmed.Trim(),
                        @"^\[CreateProperty\s*(?:\(\s*(?<params>.*?)\s*\))?\s*\]\s*$");
                    if (createPropMatch.Success)
                    {
                        // Emit a bare [CreateProperty]; the default:/validate: DSL params
                        // are compiler directives, not valid C# attribute arguments.
                        sb.AppendLine($"{memberIndent}[CreateProperty]");
                        prevWasCreateProp = true;
                        createPropDefault = null;

                        var paramStr = createPropMatch.Groups["params"].Value;
                        if (!string.IsNullOrEmpty(paramStr))
                        {
                            // default: raw value up to comma or close-paren
                            var defMatch = System.Text.RegularExpressions.Regex.Match(
                                paramStr, @"default\s*:\s*(?<val>(?:""[^""]*""|[^,\)])+)");
                            if (defMatch.Success)
                            {
                                var defVal = defMatch.Groups["val"].Value.Trim();
                                // Strip surrounding quotes if string literal
                                if (defVal.Length >= 2 && defVal.StartsWith("\"") && defVal.EndsWith("\""))
                                    defVal = defVal.Substring(1, defVal.Length - 2);
                                createPropDefault = defVal;
                            }
                        }
                        continue;
                    }

                    // If previous line was [CreateProperty], transform:
                    //   public T FieldName = value; → public Prop<T> FieldName = new(value);
                    if (prevWasCreateProp)
                    {
                        prevWasCreateProp = false;
                        var defVal = createPropDefault;
                        createPropDefault = null;

                        var fieldMatch = System.Text.RegularExpressions.Regex.Match(
                            trimmed,
                            @"^(?<mod>public\s+)(?<type>\w+(?:<[\w,\s]+>)?)\s+(?<name>\w+)\s*(?:=\s*(?<val>[^;]+))?\s*;\s*$");
                        if (fieldMatch.Success)
                        {
                            var typeName = fieldMatch.Groups["type"].Value;
                            var fieldName = fieldMatch.Groups["name"].Value;

                            // Author may declare the plain type (int) or the wrapped
                            // reactive type (Prop<int>). Normalize to the element type so
                            // we never double-wrap into Prop<Prop<int>>.
                            var elemType = typeName;
                            var alreadyWrapped = typeName.StartsWith("Prop<") && typeName.EndsWith(">");
                            if (alreadyWrapped)
                                elemType = typeName.Substring(5, typeName.Length - 6);

                            var initVal = defVal;
                            if (initVal == null && fieldMatch.Groups["val"].Success)
                            {
                                var rawVal = fieldMatch.Groups["val"].Value.Trim();
                                // Author wrote "= new(x)" for a Prop<> field — unwrap to x,
                                // since we re-emit the "new(...)" ourselves.
                                var newMatch = Regex.Match(rawVal, @"^new\s*\(\s*(?<inner>.*?)\s*\)\s*$");
                                initVal = alreadyWrapped && newMatch.Success
                                    ? newMatch.Groups["inner"].Value.Trim()
                                    : rawVal;
                            }
                            if (string.IsNullOrWhiteSpace(initVal))
                                initVal = $"default({elemType})";

                            // ─── [UxmlAttribute] companion property ───
                            var camelName = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1);
                            // Prepend `new` when the companion hides an inherited VisualElement
                            // member (e.g. Visible → visible hides VisualElement.visible, CS0108).
                            var newKw = ReservedVisualElementMembers.Contains(camelName) ? "new " : "";
                            sb.AppendLine($"{memberIndent}[UxmlAttribute(\"{fieldName}\")]");
                            sb.AppendLine($"{memberIndent}public {newKw}{elemType} {camelName} {{ get => {fieldName}.Value; set => {fieldName}.Value = value; }}");
                            sb.AppendLine($"{memberIndent}public Prop<{elemType}> {fieldName} = new({initVal});");

                            continue;
                        }
                    }

                    sb.AppendLine($"{memberIndent}{trimmed}");
                }
                sb.AppendLine();
            }

            // ─── Build() ─────────────────────────────────────────────
            var bodyIndent = memberIndent + "    ";
            sb.AppendLine($"{memberIndent}protected override void Build()");
            sb.AppendLine($"{memberIndent}{{");
            sb.AppendLine($"{bodyIndent}LoadCompanionStyleSheets();");
            sb.AppendLine();
            // Apply scoped CSS
            if (model.IsStyleScoped)
            {
                var hash = GenerateScopedHash(model.ClassName);
                sb.AppendLine($"{bodyIndent}ApplyScopedAttribute(\"{hash}\");");
            }

            // Root class
            if (root.Attributes.TryGetValue("class", out var rootClass))
            {
                var classes = rootClass.Split(' ').Where(c => !string.IsNullOrEmpty(c));
                foreach (var cls in classes)
                    sb.AppendLine($"{bodyIndent}AddToClassList(\"{cls}\");");
            }

            // Root :class binding (same logic as GenerateCommonAttributes)
            foreach (var attr in root.Attributes)
            {
                if (attr.Key == ":class")
                {
                    var val = attr.Value.Trim();
                    if (val.StartsWith("{") && val.EndsWith("}"))
                    {
                        var pairs = ParseClassObjectSyntax(val);
                        foreach (var (cls, cond) in pairs)
                        {
                            sb.AppendLine($"{bodyIndent}BindClass(this, \"{cls}\", () => {TranslateExpr(cond)});");
                        }
                    }
                }
            }

            // Root styles — register as USS class
            if (root.Attributes.TryGetValue("style", out var rootStyle))
            {
                var styleClass = RegisterStaticStyle(model.ClassName, rootStyle);
                if (styleClass != null)
                    sb.AppendLine($"{bodyIndent}this.AddToClassList(\"{styleClass}\");");
            }

            // Generate children (handles v-if/v-else-if/v-else chains)
            if (root.Children.Count > 0)
            {
                GenerateChildren(sb, root.Children, bodyIndent, "this", model);
            }

            sb.AppendLine($"{memberIndent}}}");
            sb.AppendLine();

            // ─── Close class (+ optional namespace) ───────────────────
            sb.AppendLine($"{indent}}}");
            if (hasNs)
                sb.AppendLine("}");

            return sb.ToString();
        }

        private static string GenerateEmpty(string className, string baseClass = null, string classNamespace = null)
        {
            var b = string.IsNullOrEmpty(baseClass) ? "SusComponent" : baseClass;
            var ns = classNamespace?.Trim();
            if (string.IsNullOrEmpty(ns))
            {
                return $@"[UxmlElement]
public partial class {className} : {b}
{{
    protected override void Build()
    {{
        LoadCompanionStyleSheets();
        // Empty component (no template)
    }}
}}";
            }

            return $@"namespace {ns}
{{
    [UxmlElement]
    public partial class {className} : {b}
    {{
        protected override void Build()
        {{
            LoadCompanionStyleSheets();
            // Empty component (no template)
        }}
    }}
}}";
        }

        /// <summary>
        /// Iterates children, detecting and grouping v-if/v-else-if/v-else chains.
        /// v-else-if / v-else without a preceding v-if are emitted as warnings.
        /// </summary>
        private void GenerateChildren(StringBuilder sb, List<TemplateNode> children,
            string indent, string parentVar, SharqFileModel model)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];

                // v-else-if / v-else without preceding v-if — skip with comment
                if (child.Attributes.ContainsKey(Constants.VElseIf) ||
                    child.Attributes.ContainsKey(Constants.VElse))
                {
                    sb.AppendLine($"{indent}// WARNING: {child.TagName} has v-else-if/v-else without preceding v-if");
                    continue;
                }

                // v-if found? Check for following v-else-if/v-else chain
                if (child.Attributes.ContainsKey(Constants.VIf))
                {
                    var chain = new List<TemplateNode> { child };
                    int j = i + 1;
                    while (j < children.Count)
                    {
                        var next = children[j];
                        if (next.Attributes.ContainsKey(Constants.VElseIf) ||
                            next.Attributes.ContainsKey(Constants.VElse))
                        {
                            chain.Add(next);
                            j++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (chain.Count > 1)
                    {
                        GenerateConditionalChain(sb, chain, indent, parentVar, model);
                        i = j - 1; // skip consumed siblings
                        continue;
                    }
                }

                GenerateElement(sb, child, indent, parentVar, model);
            }
        }

        /// <summary>
        /// Generates if / else-if / else chain from v-if/v-else-if/v-else siblings.
        /// All elements share the same parentVar.
        /// </summary>
        private void GenerateConditionalChain(StringBuilder sb, List<TemplateNode> chain,
            string indent, string parentVar, SharqFileModel model)
        {
            for (int k = 0; k < chain.Count; k++)
            {
                var node = chain[k];
                string condition = null;

                if (node.Attributes.TryGetValue(Constants.VIf, out var vIfCond))
                {
                    condition = vIfCond;
                }
                else if (node.Attributes.TryGetValue(Constants.VElseIf, out var vElseIfCond))
                {
                    condition = vElseIfCond;
                }
                else
                {
                }

                sb.AppendLine();
                sb.Append(k == 0
                    ? $"{indent}if ({condition})"
                    : condition != null
                        ? $"{indent}else if ({condition})"
                        : $"{indent}else");
                sb.AppendLine($"{indent}{{");

                // Clean v-if/v-else-if/v-else from attributes so they don't interfere
                node.Attributes.Remove(Constants.VIf);
                node.Attributes.Remove(Constants.VElseIf);
                node.Attributes.Remove(Constants.VElse);

                // Create the element and add to parent (no BindVisibility wrapping)
                var varName = $"__el_{_varCounter++}";
                var typeName = ResolveTypeName(node.TagName);
                sb.AppendLine($"{indent}    var {varName} = new {typeName}();");
                GenerateCommonAttributes(sb, node, indent + "    ", varName, typeName, model);

                // v-show
                if (node.Attributes.TryGetValue(Constants.VShow, out var showCond))
                {
                    sb.AppendLine($"{indent}    BindShow({varName}, () => {TranslateExpr(showCond)});");
                }

                // v-slot
                var slotAttr = FindSlotAttr(node);
                if (slotAttr != null)
                {
                    var slotName = string.IsNullOrEmpty(slotAttr.Value.Value) ? "default" : slotAttr.Value.Value;
                    sb.AppendLine($"{indent}    RegisterSlotContent(\"{slotName}\", {varName}, null);");
                }

                // Nested children
                if (node.Children.Count > 0)
                {
                    GenerateChildren(sb, node.Children, indent + "    ", varName, model);
                }

                sb.AppendLine($"{indent}    {parentVar}.Add({varName});");
                sb.AppendLine($"{indent}}}");
            }
        }

        /// <summary>
        /// Converts Vue-style single-quoted string literals in a template
        /// expression to C# double-quoted strings (e.g. Mode != 'delete' →
        /// Mode != "delete"). Content already inside double quotes is left as-is.
        /// </summary>
        /// <summary>
        /// If expr is a simple identifier that matches a Prop&lt;T&gt; field in the script,
        /// appends .Value so that BindChildProp gets the raw value instead of the Prop wrapper.
        /// E.g. "Progress" (Prop&lt;int&gt;) → "Progress.Value"
        /// Leaves complex expressions (unit.HpPercent, squad.IsReady) unchanged.
        /// </summary>
        private static string ResolvePropExpr(string expr, SharqFileModel model)
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
        private static string ResolveCollectionExpr(string expr, SharqFileModel model)
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

        private void GenerateElement(StringBuilder sb, TemplateNode node, string indent, string parentVar, SharqFileModel model)
        {
            if (node == null) return;

            var typeName = ResolveTypeName(node.TagName);

            // ─── v-for ───────────────────────────────────────────
            if (node.Attributes.TryGetValue(Constants.VFor, out var vForExpr) && node.Children.Count > 0)
            {
                GenerateForElement(sb, node, indent, parentVar, vForExpr, model);
                return;
            }

            // ─── <slot> tag ───────────────────────────────────────
            if (node.TagName == "slot")
            {
                GenerateSlot(sb, node, indent, parentVar, model);
                return;
            }

            var varName = $"__el_{_varCounter++}";

            // Create element first; BindVisibility runs AFTER parent.Add (see below).
            sb.AppendLine($"{indent}var {varName} = new {typeName}();");

            // v-if condition captured; bind after Add so first hide can RemoveFromHierarchy
            var hasIf = node.Attributes.TryGetValue(Constants.VIf, out var ifCondition);
            string transitionName = null;
            if (hasIf)
            {
                sb.AppendLine($"{indent}// v-if=\"{ifCondition}\"");
                node.Attributes.TryGetValue(Constants.Transition, out transitionName);
            }

            // v-show
            var hasShow = node.Attributes.TryGetValue(Constants.VShow, out var showCondition);

            // ─── Common: class, style, bindings, events ──────────
            GenerateCommonAttributes(sb, node, indent, varName, typeName, model);

            // v-show after children
            if (hasShow)
            {
                sb.AppendLine($"{indent}// v-show=\"{showCondition}\"");
                sb.AppendLine($"{indent}BindShow({varName}, () => {TranslateExpr(showCondition)});");
            }

            // v-slot: register as slot content
            var slotAttr = FindSlotAttr(node);
            if (slotAttr != null)
            {
                var slotName = string.IsNullOrEmpty(slotAttr.Value.Value) ? "default" : slotAttr.Value.Value;
                sb.AppendLine($"{indent}// v-slot:{slotName}");
                sb.AppendLine($"{indent}RegisterSlotContent(\"{slotName}\", {varName}, null);");
            }

            // Recursively generate children (handles v-if/v-else-if/v-else chains)
            if (node.Children.Count > 0)
            {
                GenerateChildren(sb, node.Children, indent, varName, model);
            }

            // Add to parent, THEN BindVisibility (so false condition removes from tree)
            sb.AppendLine($"{indent}{parentVar}.Add({varName});");
            if (hasIf)
            {
                if (!string.IsNullOrEmpty(transitionName) && transitionName != "none")
                {
                    sb.AppendLine($"{indent}BindTransitionVisibility({varName}, () => {TranslateExpr(ifCondition)}, \"{EscapeCSharpString(transitionName)}\");");
                }
                else
                {
                    sb.AppendLine($"{indent}BindVisibility({varName}, () => {TranslateExpr(ifCondition)});");
                }
            }
        }

        /// <summary>
        /// Generates a reactive BindList call for the v-for directive.
        /// Parses "itemVar in collectionExpr" and generates a lambda
        /// that creates child elements for each item.
        /// When item type can be inferred, uses typed BindList&lt;T&gt; for property access.
        /// </summary>
        private void GenerateForElement(StringBuilder sb, TemplateNode node, string indent, string parentVar, string vForExpr, SharqFileModel model)
        {
            // Parse: "item in Items" or "item, index in Items"
            var match = Regex.Match(vForExpr, @"^\s*(\w+)(?:\s*,\s*(\w+))?\s+in\s+(.+)$");
            if (!match.Success)
            {
                sb.AppendLine($"{indent}// v-for parse error: \"{vForExpr}\"");
                return;
            }

            var itemVar = match.Groups[1].Value;
            var indexVar = match.Groups[2].Value; // optional
            var collectionExpr = match.Groups[3].Value.Trim();

            // Infer item type from script fields
            var itemType = InferItemType(model, collectionExpr);
            var isTyped = itemType != null;

            var keyExpr = "";
            node.Attributes.TryGetValue(Constants.KeyBind, out keyExpr);

            var varName = $"__el_{_varCounter++}";
            var subIndent = indent + "    ";

            // Container
            sb.AppendLine($"{indent}// v-for=\"{vForExpr}\"{(isTyped ? $" (typed: {itemType})" : "")}");
            sb.AppendLine($"{indent}var {varName} = new VisualElement();");

            // class on container
            if (node.Attributes.TryGetValue("class", out var className))
            {
                var classes = className.Split(' ').Where(c => !string.IsNullOrEmpty(c));
                foreach (var cls in classes)
                    sb.AppendLine($"{indent}{varName}.AddToClassList(\"{cls}\");");
            }

            // style on container → USS class
            if (node.Attributes.TryGetValue("style", out var styleStr))
            {
                var styleClass = RegisterStaticStyle(model.ClassName, styleStr);
                if (styleClass != null)
                    sb.AppendLine($"{indent}{varName}.AddToClassList(\"{styleClass}\");");
            }

            // Reactive v-for: BindList wraps the collection in a Func<> so the
            // ReactiveEffect re-renders when the underlying Prop/collection changes.
            // (BindListFor was a one-shot render — non-reactive, kept for legacy.)
            var reactiveSource = ResolveCollectionExpr(collectionExpr, model);
            if (isTyped)
                sb.AppendLine($"{indent}BindList<{itemType}>(");
            else
                sb.AppendLine($"{indent}BindList(");
            sb.AppendLine($"{indent}    {varName},");
            sb.AppendLine($"{indent}    () => {reactiveSource},");
            sb.AppendLine($"{indent}    ({itemVar}, __i) => {{");

            // Generate template children with typed/untyped info
            // ALWAYS create __wrap so nested children can reference it
            sb.AppendLine($"{subIndent}var __wrap = new VisualElement();");
            if (node.Children.Count == 1)
            {
                GenerateForTemplate(sb, node.Children[0], subIndent, itemVar, isTyped, false, model.ClassName);
            }
            else if (node.Children.Count > 1)
            {
                foreach (var child in node.Children)
                {
                    GenerateForTemplate(sb, child, subIndent, itemVar, isTyped, false, model.ClassName);
                }
            }
            sb.AppendLine($"{subIndent}return __wrap;");

            sb.Append($"{indent}    }}");
            if (!string.IsNullOrEmpty(keyExpr))
            {
                // Key selector: Func<T, object> (typed) / Func<object, object> (untyped).
                string keyLambda;
                if (keyExpr == itemVar)
                    keyLambda = $"{itemVar} => {itemVar}";
                else if (keyExpr.StartsWith(itemVar + "."))
                    keyLambda = isTyped
                        ? $"{itemVar} => {keyExpr}"
                        : $"{itemVar} => ((dynamic){itemVar}).{keyExpr.Substring(itemVar.Length + 1)}";
                else
                    keyLambda = $"{itemVar} => {keyExpr}";
                sb.AppendLine();
                sb.AppendLine($"{indent}    , {keyLambda}");
            }
            sb.AppendLine();
            sb.AppendLine($"{indent});");

            // Add container to parent
            sb.AppendLine($"{indent}{parentVar}.Add({varName});");
        }

        /// <summary>
        /// Generates a single template element inside a v-for lambda.
        /// When isTyped: item.Prop is accessed directly (IL2CPP-safe).
        /// When !isTyped: falls back to item?.ToString().
        /// </summary>
        private void GenerateForTemplate(StringBuilder sb, TemplateNode child, string indent, string itemVar, bool isTyped, bool isReturned, string className)
        {
            var varName = $"__el_{_varCounter++}";
            var typeName = ResolveTypeName(child.TagName);

            sb.AppendLine($"{indent}var {varName} = new {typeName}();");

            // class
            if (child.Attributes.TryGetValue("class", out var clsVal))
            {
                var classes = clsVal.Split(' ').Where(c => !string.IsNullOrEmpty(c));
                foreach (var c in classes)
                    sb.AppendLine($"{indent}{varName}.AddToClassList(\"{c}\");");
            }

            // style → USS class
            if (child.Attributes.TryGetValue("style", out var stVal))
            {
                var styleClass = RegisterStaticStyle(className, stVal);
                if (styleClass != null)
                    sb.AppendLine($"{indent}{varName}.AddToClassList(\"{styleClass}\");");
            }

            // :text → typed or untyped
            if (typeName == "Label" && TryGetBindAttr(child.Attributes, "text", out var textExpr))
            {
                if (isTyped)
                {
                    // Typed: item.Name?.ToString() ?? ""
                    var resolved = ResolveItemRefTyped(textExpr, itemVar);
                    sb.AppendLine($"{indent}BindText({varName}, () => {TranslateBindingExpr(resolved)}?.ToString() ?? \"\");");
                }
                else
                {
                    // Untyped: item?.ToString() ?? ""
                    var resolved = ResolveItemRef(textExpr, itemVar);
                    sb.AppendLine($"{indent}BindText({varName}, () => {TranslateBindingExpr(resolved)}?.ToString() ?? \"\");");
                }
            }

            // @events — with modifiers support
            foreach (var attr in child.Attributes)
            {
                if (attr.Key.StartsWith(Constants.EventPrefix))
                {
                    GenerateEventCallback(sb, indent, varName, attr.Key, attr.Value, typeName);
                }
            }

            // Recursive children
            if (child.Children.Count > 0)
            {
                foreach (var sub in child.Children)
                {
                    GenerateForTemplate(sb, sub, indent, itemVar, isTyped, false, className);
                }
            }

            if (isReturned)
                sb.AppendLine($"{indent}return {varName};");
            else
                sb.AppendLine($"{indent}__wrap.Add({varName});");
        }

        /// <summary>
        /// Infers the element type of a collection field from &lt;script&gt;.
        /// E.g. "List&lt;UnitData&gt; Items" → "UnitData"
        /// Returns null if inference fails.
        /// </summary>
        private static string InferItemType(SharqFileModel model, string collectionExpr)
        {
            if (string.IsNullOrEmpty(model.ScriptBody)) return null;

            // Strip "this." prefix
            var collectionVar = collectionExpr.StartsWith("this.")
                ? collectionExpr.Substring(5)
                : collectionExpr;

            // Match: List<T> varName  /  IList<T> varName  /  IEnumerable<T> varName
            var pattern = $@"(?:List|IList|IEnumerable|ObservableList)<([^>]+)>\s+{Regex.Escape(collectionVar)}\b";
            var m = Regex.Match(model.ScriptBody, pattern);
            if (m.Success) return m.Groups[1].Value.Trim();

            // Match: Prop<List<T>> varName  /  Prop<IList<T>> varName  (reactive wrapper)
            pattern = $@"Prop<(?:List|IList|IEnumerable|ObservableList)<([^>]+)>>\s+{Regex.Escape(collectionVar)}\b";
            m = Regex.Match(model.ScriptBody, pattern);
            if (m.Success) return m.Groups[1].Value.Trim();

            // Array: T[] varName
            pattern = $@"([\w.]+)\[\]\s+{Regex.Escape(collectionVar)}\b";
            m = Regex.Match(model.ScriptBody, pattern);
            if (m.Success) return m.Groups[1].Value.Trim();

            return null;
        }

        /// <summary>
        /// Resolves item references in bind expressions (untyped fallback).
        /// "Greeting" → unchanged (component field)
        /// "item.Name" → "item" (nested props via ToString fallback)
        /// "item" → "item" (reference to lambda param, .ToString() appended by caller)
        /// </summary>
        private static string ResolveItemRef(string expr, string itemVar)
        {
            if (expr == itemVar)
                return itemVar;
            if (expr.StartsWith(itemVar + "."))
                return itemVar;
            return expr;
        }

        /// <summary>
        /// Resolves typed item references. Returns the expression for property access.
        /// "item" → "item" (caller adds ?.ToString())
        /// "item.Name" → "item.Name" (typed, IL2CPP-safe)
        /// "Greeting" → "Greeting" (component field, not an item ref)
        /// </summary>
        private static string ResolveItemRefTyped(string expr, string itemVar)
        {
            if (expr == itemVar)
                return itemVar;
            if (expr.StartsWith(itemVar + "."))
                return expr; // e.g. "item.Name" — typed property access
            return expr; // component field, pass through
        }

        /// <summary>
        /// Generates slot projection: GetSlotContainer + scoped props (F3) + BuildSlot.
        /// Scoped props come from bind attributes on &lt;slot&gt;: <c>&lt;slot :item="Row"&gt;</c>.
        /// </summary>
        private void GenerateSlot(StringBuilder sb, TemplateNode node, string indent, string parentVar, SharqFileModel model)
        {
            var slotName = "default";
            if (node.Attributes.TryGetValue("name", out var name))
                slotName = name;

            sb.AppendLine($"{indent}// <slot name=\"{slotName}\">");
            sb.AppendLine($"{indent}var __slot_{slotName} = GetSlotContainer(\"{slotName}\");");
            sb.AppendLine($"{indent}{parentVar}.Add(__slot_{slotName});");

            // F3: scoped slot props from :prop="expr" on <slot>
            foreach (var attr in node.Attributes)
            {
                if (!attr.Key.StartsWith(Constants.BindPrefix)) continue;
                var propName = attr.Key.Substring(1);
                if (propName == "name" || propName == "class" || propName == "key") continue;
                sb.AppendLine($"{indent}ProvideSlotProp(\"{slotName}\", \"{propName}\", {TranslateExpr(attr.Value)});");
            }

            // Fallback content: children of <slot> rendered into the slot container
            if (node.Children.Count > 0)
            {
                GenerateChildren(sb, node.Children, indent, $"__slot_{slotName}", model);
            }
            sb.AppendLine($"{indent}BuildSlot(\"{slotName}\", null, __slot_{slotName});");
        }

        /// <summary>
        /// Finds v-slot or v-slot:name attribute on a node.
        /// </summary>
        private static KeyValuePair<string, string>? FindSlotAttr(TemplateNode node)
        {
            foreach (var attr in node.Attributes)
            {
                if (attr.Key == "v-slot")
                    return new KeyValuePair<string, string>("v-slot", attr.Value);
                if (attr.Key.StartsWith("v-slot:"))
                    return new KeyValuePair<string, string>("v-slot", attr.Key.Substring("v-slot:".Length));
            }
            return null;
        }

        // ─── Common attribute generation (shared between regular + v-for) ────

        private void GenerateCommonAttributes(StringBuilder sb, TemplateNode node, string indent, string varName, string typeName, SharqFileModel model)
        {
            // class
            if (node.Attributes.TryGetValue("class", out var clsName))
            {
                var classes = clsName.Split(' ').Where(c => !string.IsNullOrEmpty(c));
                foreach (var cls in classes)
                    sb.AppendLine($"{indent}{varName}.AddToClassList(\"{cls}\");");
            }

            // name — emit for ALL elements (built-in UITK + custom components)
            if (node.Attributes.TryGetValue("name", out var nameVal))
            {
                sb.AppendLine($"{indent}{varName}.name = \"{EscapeCSharpString(nameVal)}\";");
            }

            // style → USS class (all CSS properties, Unity-native parsing)
            if (node.Attributes.TryGetValue("style", out var styleStr))
            {
                var styleClass = RegisterStaticStyle(model?.ClassName ?? "", styleStr);
                if (styleClass != null)
                    sb.AppendLine($"{indent}{varName}.AddToClassList(\"{styleClass}\");");
            }

            // :text binding on Label (supports pipe converters: | format | upper | round)
            if (typeName == "Label" && TryGetBindAttr(node.Attributes, "text", out var textExpr))
            {
                sb.AppendLine($"{indent}BindText({varName}, () => {TranslateBindingExpr(textExpr)});");
            }

            // text="literal" on built-in Label / Button (non-bound text attribute)
            // Custom components (e.g. SusButton) get text as a prop via the block below.
            if (!IsCustomComponent(typeName) && node.Attributes.TryGetValue("text", out var literalText))
            {
                if (!literalText.StartsWith(":") && !literalText.StartsWith("@"))
                {
                    sb.AppendLine($"{indent}{varName}.text = \"{EscapeCSharpString(literalText)}\";");
                }
            }

            // text="literal" on custom components → SetChildProp
            if (IsCustomComponent(typeName) && node.Attributes.TryGetValue("text", out var customText))
            {
                if (!customText.StartsWith(":") && !customText.StartsWith("@"))
                {
                    sb.AppendLine($"{indent}SusComponent.SetChildProp({varName}, \"text\", \"{EscapeCSharpString(customText)}\");");
                }
            }

            // :class binding — object syntax: { class1: cond1, class2: cond2 }
            foreach (var attr in node.Attributes)
            {
                if (attr.Key.StartsWith(Constants.BindPrefix) && attr.Key != ":text")
                {
                    var bindKey = attr.Key.Substring(1);
                    if (bindKey == "class")
                    {
                        var val = attr.Value.Trim();
                        if (val.StartsWith("{") && val.EndsWith("}"))
                        {
                            // Parse { active: isActive, disabled: isDisabled }
                            var pairs = ParseClassObjectSyntax(val);
                            foreach (var (cls, cond) in pairs)
                            {
                                sb.AppendLine($"{indent}BindClass({varName}, \"{cls}\", () => {TranslateExpr(cond)});");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"{indent}// :class=\"{attr.Value}\"");
                        }
                    }
                }
            }

            // @event handlers (with optional .stop / .once modifiers)
            foreach (var attr in node.Attributes)
            {
                if (attr.Key.StartsWith(Constants.EventPrefix))
                {
                    GenerateEventCallback(sb, indent, varName, attr.Key, attr.Value, typeName);
                }
            }

            // ─── Custom component prop passing ─────────────────────────
            if (IsCustomComponent(typeName))
            {
                // Known attributes to skip (already handled above):
                // class, style, text, $MainElement, v-if, v-else-if, v-else,
                // v-show, v-for, v-slot, :class, :text, @*, key
                var skipAttrs = new HashSet<string>
                {
                    "class", "style", "text", "name", Constants.MainElement,
                    Constants.VIf, Constants.VElseIf, Constants.VElse,
                    Constants.VShow, Constants.VFor, "v-slot", "key", Constants.Transition
                };

                foreach (var attr in node.Attributes)
                {
                    var key = attr.Key;

                    // Skip already-handled and structural attributes
                    if (skipAttrs.Contains(key)) continue;
                    if (key.StartsWith(Constants.EventPrefix)) continue;
                    if (key.StartsWith(Constants.BindPrefix) && key != ":text" && key != ":class")
                    {
                        // Reactive prop binding (case-insensitive): :propName="expr"
                        var propName = key.Substring(1); // remove ':'
                        sb.AppendLine($"{indent}// :{propName}=\"{attr.Value}\" → reactive prop bind");
                        var expr = attr.Value.Replace("'", "\"");
                        expr = ResolvePropExpr(expr, model);
                        sb.AppendLine($"{indent}BindChildProp({varName}, \"{propName}\", () => {expr});");
                        continue;
                    }
                    if (key.StartsWith(Constants.BindPrefix))
                        continue; // already handled: :text, :class

                    // Literal prop: propName="value" — try typed assignment first
                    if (key.StartsWith("$") || key.StartsWith("v-"))
                        continue;

                    var value = attr.Value;
                    if (string.IsNullOrEmpty(value))
                    {
                        // Boolean attribute (e.g., <sus:SusButton disabled>)
                        sb.AppendLine($"{indent}SusComponent.SetChildProp({varName}, \"{key}\", true);");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}SusComponent.SetChildProp({varName}, \"{key}\", \"{EscapeCSharpString(value)}\");");
                    }
                }
            }
        }

        /// <summary>
        /// Registers an inline style string for USS generation.
        /// Returns a deduplicated class name (e.g. "sharq-MyComponent-s0").
        /// Multiple elements with identical styles share one class.
        /// The caller adds this class to the element via AddToClassList.
        /// </summary>
        private string RegisterStaticStyle(string componentName, string styleStr)
        {
            if (string.IsNullOrEmpty(styleStr)) return null;

            // Normalize: trim the raw style string
            var normalized = styleStr.Trim();
            if (string.IsNullOrEmpty(normalized)) return null;

            if (!_generatedStyles.TryGetValue(normalized, out var styleClass))
            {
                styleClass = $"sharq-{componentName}-s{_styleCounter++}";
                _generatedStyles[normalized] = styleClass;
            }
            return styleClass;
        }

        private static string MapEventType(string eventName)
        {
            return eventName.ToLowerInvariant() switch
            {
                "click" => "UnityEngine.UIElements.ClickEvent",
                "mouseenter" => "UnityEngine.UIElements.MouseEnterEvent",
                "mouseleave" => "UnityEngine.UIElements.MouseLeaveEvent",
                "change" => "UnityEngine.UIElements.ChangeEvent<string>",
                _ => $"UnityEngine.UIElements.EventBase<UnityEngine.UIElements.EventBase>"
            };
        }

        // ─── Event modifiers ─────────────────────────────────────────

        /// <summary>
        /// Parses "@click.stop" → ("click", stop: true, once: false).
        /// </summary>
        private static (string eventName, bool stop, bool once) ParseEventModifiers(string rawKey)
        {
            var name = rawKey.StartsWith(Constants.EventPrefix)
                ? rawKey.Substring(1)
                : rawKey;
            bool stop = name.Contains(".stop");
            bool once = name.Contains(".once");
            name = name.Replace(".stop", "").Replace(".once", "");
            return (name, stop, once);
        }

        /// <summary>
        /// Generates RegisterCallback line (UITK events) or On() wiring
        /// (custom component events). Supports .stop / .once modifiers.
        /// </summary>
        private void GenerateEventCallback(StringBuilder sb, string indent, string varName,
            string rawKey, string handler, string typeName)
        {
            var (eventName, stop, once) = ParseEventModifiers(rawKey);

            // Custom component events (e.g. @save on <sus:SusButton>)
            if (IsCustomComponent(typeName))
            {
                if (stop && once)
                {
                    // .stop.once: self-unregistering wrapper
                    var cbVar = $"__cb_{_varCounter++}";
                    sb.AppendLine($"{indent}System.Action {cbVar} = null;");
                    sb.AppendLine($"{indent}{cbVar} = () => {{");
                    sb.AppendLine($"{indent}    {varName}.Off(\"{eventName}\", {cbVar});");
                    sb.AppendLine($"{indent}    {handler}();");
                    sb.AppendLine($"{indent}}};");
                    sb.AppendLine($"{indent}{varName}.On(\"{eventName}\", {cbVar});");
                }
                else if (once)
                {
                    var cbVar = $"__cb_{_varCounter++}";
                    sb.AppendLine($"{indent}System.Action {cbVar} = null;");
                    sb.AppendLine($"{indent}{cbVar} = () => {{");
                    sb.AppendLine($"{indent}    {varName}.Off(\"{eventName}\", {cbVar});");
                    sb.AppendLine($"{indent}    {handler}();");
                    sb.AppendLine($"{indent}}};");
                    sb.AppendLine($"{indent}{varName}.On(\"{eventName}\", {cbVar});");
                }
                else
                {
                    sb.AppendLine($"{indent}{varName}.On(\"{eventName}\", () => {handler}());");
                }
                // .stop on component events — no equivalent (child controls emission)
                if (stop && !once)
                {
                    sb.AppendLine($"{indent}// NOTE: .stop has no effect on custom component events (child controls emission)");
                }
                return;
            }

            // UITK events (existing logic)
            var eventType = MapEventType(eventName);

            if (!stop && !once)
            {
                sb.AppendLine($"{indent}{varName}.RegisterCallback<{eventType}>(_ => {handler}());");
                return;
            }

            var cbVarU = $"__cb_{_varCounter++}";
            sb.AppendLine($"{indent}EventCallback<{eventType}> {cbVarU} = null;");
            sb.AppendLine($"{indent}{cbVarU} = evt => {{");

            if (once)
                sb.AppendLine($"{indent}    {varName}.UnregisterCallback<{eventType}>({cbVarU});");
            if (stop)
                sb.AppendLine($"{indent}    evt.StopPropagation();");
            sb.AppendLine($"{indent}    {handler}();");
            sb.AppendLine($"{indent}}};");
            sb.AppendLine($"{indent}{varName}.RegisterCallback<{eventType}>({cbVarU});");
        }

        // ─── :class object syntax parser ──────────────────────────────

        /// <summary>
        /// Parses "{ active: isActive, disabled: isDisabled }" → [("active","isActive"), ("disabled","isDisabled")].
        /// Handles simple expressions; avoids splitting inside angle brackets for generics.
        /// </summary>
        private static List<(string className, string condition)> ParseClassObjectSyntax(string val)
        {
            var result = new List<(string, string)>();
            // Remove outer braces
            var inner = val.Substring(1, val.Length - 2).Trim();
            if (string.IsNullOrEmpty(inner)) return result;

            // Split by top-level commas (not inside angle brackets or parens)
            var segments = SplitTopLevel(inner, ',');
            foreach (var seg in segments)
            {
                var trimmed = seg.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Split by first colon
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0) continue;
                var cls = trimmed.Substring(0, colonIdx).Trim().Trim('"', '\'');
                var cond = trimmed.Substring(colonIdx + 1).Trim();
                if (!string.IsNullOrEmpty(cls) && !string.IsNullOrEmpty(cond))
                    result.Add((cls, cond));
            }
            return result;
        }

        private static List<string> SplitTopLevel(string s, char delimiter)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                switch (s[i])
                {
                    case '<': case '(': case '{': depth++; break;
                    case '>': case ')': case '}': depth--; break;
                    default:
                        if (depth == 0 && s[i] == delimiter)
                        {
                            result.Add(s.Substring(start, i - start));
                            start = i + 1;
                        }
                        break;
                }
            }
            result.Add(s.Substring(start));
            return result;
        }

        private static string EscapeCSharpString(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool TryGetBindAttr(Dictionary<string, string> attrs, string bindKey, out string expr)
        {
            var key = $":{bindKey}";
            foreach (var kvp in attrs)
            {
                if (kvp.Key == key)
                {
                    expr = kvp.Value;
                    return true;
                }
            }
            expr = null;
            return false;
        }

        private static string ResolveTypeName(string tagName)
        {
            var name = tagName.Contains(":") ? tagName.Split(':')[1] : tagName;

            return name switch
            {
                "VisualElement" => "VisualElement",
                "Label" => "Label",
                "Button" => "Button",
                "TextField" => "TextField",
                "Toggle" => "Toggle",
                "Slider" => "Slider",
                "ScrollView" => "ScrollView",
                "Image" => "Image",
                "Box" => "Box",
                "GroupBox" => "GroupBox",
                "slot" => "VisualElement",
                "template" => "VisualElement",
                _ => name
            };
        }

        /// <summary>
        /// Returns true if the typeName is a custom component (not a UITK built-in element).
        /// Custom components receive prop assignments and component-level events.
        /// </summary>
        private static bool IsCustomComponent(string typeName)
        {
            return typeName switch
            {
                "VisualElement" => false,
                "Label" => false,
                "Button" => false,
                "TextField" => false,
                "Toggle" => false,
                "Slider" => false,
                "ScrollView" => false,
                "Image" => false,
                "Box" => false,
                "GroupBox" => false,
                _ => true
            };
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
