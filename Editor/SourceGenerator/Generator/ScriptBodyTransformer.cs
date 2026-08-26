using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Transforms Sharq <c>&lt;script&gt;</c> body into partial-class members:
    /// <c>$using</c>/<c>$extends</c> directives and the CreateProperty DSL
    /// (<c>[CreateProperty(default:…)]</c> → <c>Prop&lt;T&gt;</c> + UxmlAttribute companion).
    /// </summary>
    internal static class ScriptBodyTransformer
    {
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
        /// Fallback: extract <c>$using</c> / <c>$extends</c> from script body into the model
        /// when the file parser did not already populate them.
        /// </summary>
        internal static void ApplyDirectives(SharqFileModel model)
        {
            if (string.IsNullOrEmpty(model?.ScriptBody))
                return;

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

        internal static bool HasCreateProperty(string scriptBody)
            => !string.IsNullOrEmpty(scriptBody)
               && Regex.IsMatch(scriptBody, @"\[CreateProperty");

        /// <summary>
        /// Emits script members into <paramref name="sb"/> (CreateProperty DSL + passthrough lines).
        /// Directives <c>$using</c>/<c>$extends</c> are skipped.
        /// </summary>
        internal static void EmitMembers(StringBuilder sb, string scriptBody, string memberIndent)
        {
            if (string.IsNullOrEmpty(scriptBody))
                return;

            sb.AppendLine($"{memberIndent}// ─── From <script> ───");
            var scriptLines = scriptBody.Split('\n');
            var prevWasCreateProp = false;
            string createPropDefault = null;

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
                var createPropMatch = Regex.Match(
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
                        var defMatch = Regex.Match(
                            paramStr, @"default\s*:\s*(?<val>(?:""[^""]*""|[^,\)])+)");
                        if (defMatch.Success)
                        {
                            // Keep the raw C# literal as written (quotes included for
                            // strings) — it is re-emitted verbatim into `new(...)`.
                            createPropDefault = defMatch.Groups["val"].Value.Trim();
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

                    // A trailing `// comment` after the `;` is tolerated and preserved.
                    var fieldMatch = Regex.Match(
                        trimmed,
                        @"^\s*(?<mod>public\s+)(?<type>\w+(?:<[\w,\s]+>)?)\s+(?<name>\w+)\s*(?:=\s*(?<val>[^;]+))?\s*;\s*(?<comment>//.*)?$");
                    if (fieldMatch.Success)
                    {
                        var typeName = fieldMatch.Groups["type"].Value;
                        var fieldName = fieldMatch.Groups["name"].Value;
                        var comment = fieldMatch.Groups["comment"].Success
                            ? " " + fieldMatch.Groups["comment"].Value.TrimEnd()
                            : "";

                        // Author may declare the plain type (int) or the wrapped
                        // reactive type (Prop<int>). Normalize to the element type so
                        // we never double-wrap into Prop<Prop<int>>.
                        var elemType = typeName;
                        var alreadyWrapped = typeName.StartsWith("Prop<") && typeName.EndsWith(">");
                        if (alreadyWrapped)
                            elemType = typeName.Substring(5, typeName.Length - 6);

                        // The author's explicit initializer always wins; the DSL
                        // `default:` param is only a fallback when the field has none.
                        string initVal = null;
                        if (fieldMatch.Groups["val"].Success)
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
                            initVal = defVal;
                        if (string.IsNullOrWhiteSpace(initVal))
                            initVal = $"default({elemType})";

                        // ─── [UxmlAttribute] companion property ───
                        var camelName = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1);
                        // Prepend `new` when the companion hides an inherited VisualElement
                        // member (e.g. Visible → visible hides VisualElement.visible, CS0108).
                        var newKw = ReservedVisualElementMembers.Contains(camelName) ? "new " : "";
                        sb.AppendLine($"{memberIndent}[UxmlAttribute(\"{fieldName}\")]");
                        sb.AppendLine($"{memberIndent}public {newKw}{elemType} {camelName} {{ get => {fieldName}.Value; set => {fieldName}.Value = value; }}");
                        sb.AppendLine($"{memberIndent}public Prop<{elemType}> {fieldName} = new({initVal});{comment}");

                        continue;
                    }
                }

                sb.AppendLine($"{memberIndent}{trimmed}");
            }
            sb.AppendLine();
        }
    }
}
