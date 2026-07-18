using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Runtime template hot-reload interpreter (E3).
    ///
    /// Parses a .sharq &lt;template&gt; section at runtime and reconstructs the
    /// VisualElement tree on a live <see cref="SusComponent"/>, preserving Prop&lt;T&gt;
    /// state across the rebuild via <see cref="SusComponentSnapshot"/>.
    ///
    /// Works in DEVELOPMENT_BUILD and Editor. Safe to include in release builds
    /// if the hot-reload feature flag is enabled, but has no effect — the push
    /// mechanism (E4) is #if'd to dev builds only.
    ///
    /// Supported template features
    /// ────────────────────────────
    ///   Root:     template root attrs apply to the component itself ($MainElement semantics,
    ///             matching BuildMethodGenerator — root tag is not re-created as a child).
    ///   Elements: any VisualElement subclass by simple type name (Label, Button, VisualElement,
    ///             sus:SusButton → SusButton, etc.)
    ///   Attrs:    class, :class (object syntax), :text (Label), name, :name
    ///             v-if (literal true/false, !Expr, Prop.Value, == / !=, || / &amp;&amp;)
    ///             v-show (same eval)
    ///             Prop assignment: PropName="value" / :PropName="expr"
    ///   Slots:    &lt;slot /&gt; → GetSlotContainer / BuildSlot pattern
    ///   @events:  logged + skipped (tree still applies; handlers need full recompile)
    ///   Fallback: v-for, unknown types, expressions too complex → FallbackRebuild()
    ///
    /// See sus-core/Docs/09-compilation.md § «Template interpreter support matrix».
    /// </summary>
    public sealed class SharqTemplateInterpreter
    {
        // ─── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Attempt to hot-reload <paramref name="component"/>'s template from the
        /// given raw &lt;template&gt; XML string.
        /// Returns <c>true</c> when the tree was successfully rebuilt in-place.
        /// Returns <c>false</c> when the expression is too complex; callers should
        /// fall back to a full recompile + reload.
        /// </summary>
        public static bool TryApply(SusComponent component, string templateXml)
        {
            if (component == null || string.IsNullOrEmpty(templateXml))
                return false;

            var ctx = new InterpretContext(component);
            List<SusComponentSnapshot.Entry> snap = null;

            try
            {
                var ast = SharqTemplateParser.Parse(templateXml);

                // Snapshot state before clearing the tree
                snap = SusComponentSnapshot.Capture(component);

                // Rebuild: root of the template IS the component ($MainElement / generator parity).
                component.Clear();
                if (!ApplyRootToComponent(ast, component, ctx))
                    return FallbackFailed(component, snap);

                // Restore Prop values
                SusComponentSnapshot.Restore(component, snap);

                // Reload styles
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                component.ReloadCompanionStyleSheets();
#endif
                component.MarkDirtyRepaint();
                return true;
            }
            catch (FallbackException ex)
            {
                Debug.LogWarning($"[SharqInterp] Fallback on {component.GetType().Name}: {ex.Message}");
                return FallbackFailed(component, snap ?? new List<SusComponentSnapshot.Entry>());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SharqInterp] Unexpected error on {component.GetType().Name}: {ex.Message}");
                if (snap != null)
                    FallbackFailed(component, snap);
                return false;
            }
        }

        /// <summary>
        /// Apply template root attributes onto the host component, then build children
        /// into it — same contract as <c>BuildMethodGenerator</c> ($MainElement).
        /// </summary>
        private static bool ApplyRootToComponent(SharqTemplateNode root, SusComponent component,
            InterpretContext ctx)
        {
            if (root == null) return true;

            // v-for on root → fall back
            if (root.Attributes.ContainsKey("v-for"))
                throw new FallbackException("v-for on root");

            // Root v-if: if false, leave component empty (attrs still applied for class parity)
            if (root.Attributes.TryGetValue("v-if", out var ifExpr) && !EvalBool(ifExpr, ctx))
                return true;

            if (!ApplyAttributes(root, component, ctx))
                return false;

            if (root.Attributes.TryGetValue("v-show", out var showExpr))
                component.style.display = EvalBool(showExpr, ctx)
                    ? DisplayStyle.Flex : DisplayStyle.None;

            foreach (var child in root.Children)
            {
                if (!BuildElement(child, component, ctx))
                    return false;
            }
            return true;
        }

        // ─── Tree builder ─────────────────────────────────────────────────

        private static bool BuildElement(SharqTemplateNode node, VisualElement parent,
            InterpretContext ctx)
        {
            if (node == null) return true;

            // v-for → too complex, fall back
            if (node.Attributes.ContainsKey("v-for"))
                throw new FallbackException("v-for");

            // <slot> handling
            if (node.TagName == "slot")
            {
                node.Attributes.TryGetValue("name", out var slotName);
                if (string.IsNullOrEmpty(slotName)) slotName = "default";
                var slotContainer = ctx.GetSlotContainer(slotName);
                parent.Add(slotContainer);
                ctx.BuildSlot(slotName, slotContainer);
                return true;
            }

            // v-if evaluation
            if (node.Attributes.TryGetValue("v-if", out var ifExpr))
            {
                var shown = EvalBool(ifExpr, ctx);
                if (!shown) return true; // element excluded
            }

            var el = CreateElement(node.TagName, ctx.Component);
            if (el == null)
                throw new FallbackException($"unknown type: {node.TagName}");

            // Apply attributes
            if (!ApplyAttributes(node, el, ctx))
                return false;

            // v-show
            if (node.Attributes.TryGetValue("v-show", out var showExpr))
                el.style.display = EvalBool(showExpr, ctx)
                    ? DisplayStyle.Flex : DisplayStyle.None;

            // Recurse children
            foreach (var child in node.Children)
            {
                if (!BuildElement(child, el, ctx))
                    return false;
            }

            parent.Add(el);
            return true;
        }

        // ─── Element creation ─────────────────────────────────────────────

        private static VisualElement CreateElement(string tagName, SusComponent host)
        {
            // Strip namespace prefix (sus:SusButton → SusButton, ui:Label → Label)
            var colon = tagName.LastIndexOf(':');
            var simpleName = colon >= 0 ? tagName.Substring(colon + 1) : tagName;

            // Well-known UITK elements
            switch (simpleName)
            {
                case "VisualElement": return new VisualElement();
                case "Label":        return new Label();
                case "Button":       return new Button();
                case "TextField":    return new TextField();
                case "Toggle":       return new Toggle();
                case "ScrollView":   return new ScrollView();
                case "Image":        return new Image();
                case "Slider":       return new Slider();
                case "SliderInt":    return new SliderInt();
                case "IntegerField": return new IntegerField();
                case "FloatField":   return new FloatField();
            }

            // Try to find a SusComponent subclass in all loaded assemblies
            var type = FindType(simpleName, host);
            if (type == null) return null;

            try { return (VisualElement)Activator.CreateInstance(type); }
            catch { return null; }
        }

        private static Type FindType(string name, SusComponent host)
        {
            // Fast path: same assembly as the host component
            var hostAssembly = host?.GetType().Assembly;
            var t = hostAssembly?.GetType(name, false, false)
                 ?? hostAssembly?.GetType($"Sharq.Core.{name}", false, false)
                 ?? hostAssembly?.GetType($"Sharq.Router.{name}", false, false);
            if (t != null) return t;

            // Slow path: scan all loaded assemblies by simple type name
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var candidate in new[]
                             { name, $"Sharq.Core.{name}", $"Sharq.Router.{name}" })
                    {
                        t = asm.GetType(candidate, false, false);
                        if (t != null && typeof(VisualElement).IsAssignableFrom(t))
                            return t;
                    }

                    // Last resort: match by unqualified type name
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.Name == name && typeof(VisualElement).IsAssignableFrom(type))
                            return type;
                    }
                }
                catch { /* skip unloadable assemblies */ }
            }
            return null;
        }

        // ─── Attribute application ─────────────────────────────────────────

        private static bool ApplyAttributes(SharqTemplateNode node, VisualElement el,
            InterpretContext ctx)
        {
            foreach (var kv in node.Attributes)
            {
                var key = kv.Key;
                var val = kv.Value;

                if (key == "v-if" || key == "v-show" || key == "v-else"
                    || key == "$MainElement" || key == "v-for" || key == "transition")
                    continue;

                // static class
                if (key == "class")
                {
                    foreach (var cls in val.Split(' '))
                        if (!string.IsNullOrEmpty(cls)) el.AddToClassList(cls);
                    continue;
                }

                // :class={ "cls": condExpr }
                if (key == ":class")
                {
                    if (!ApplyClassBinding(val, el, ctx)) return false;
                    continue;
                }

                // name=
                if (key == "name") { el.name = val; continue; }
                if (key == ":name") { el.name = EvalString(val, ctx); continue; }

                // :text on Label
                if (key == ":text" && el is Label lbl)
                {
                    lbl.text = EvalString(val, ctx);
                    continue;
                }
                if (key == "text" && el is Label lbl2)
                {
                    lbl2.text = val;
                    continue;
                }

                // @events → log + skip (do not hard-fail the whole template)
                if (key.StartsWith("@"))
                {
                    Debug.LogWarning(
                        $"[SharqInterp] Skipping unsupported event binding '{key}' on {el.GetType().Name} " +
                        $"({ctx.Component.GetType().Name}). Full recompile needed for live handlers.");
                    continue;
                }

                // :PropName="expr" or PropName="literal" → assign via reflection
                if (key.StartsWith(":"))
                {
                    var propName = key.Substring(1);
                    if (!AssignProp(el, propName, EvalString(val, ctx)))
                        throw new FallbackException($"complex binding: {key}={val}");
                    continue;
                }

                // Static attribute (e.g. Variant="elevated")
                if (key.Length > 0 && char.IsUpper(key[0]))
                {
                    AssignProp(el, key, val);
                    // Non-fatal if the prop doesn't exist
                }
            }
            return true;
        }

        private static bool ApplyClassBinding(string objectExpr, VisualElement el,
            InterpretContext ctx)
        {
            // Expect: { "cls-name": expr, ... }
            objectExpr = objectExpr.Trim();
            if (!objectExpr.StartsWith("{") || !objectExpr.EndsWith("}"))
                return false;

            var inner = objectExpr.Substring(1, objectExpr.Length - 2).Trim();
            // Split on ',' but only at top-level (simplified: no nested braces)
            foreach (var part in inner.Split(','))
            {
                var colon = part.IndexOf(':');
                if (colon < 0) continue;
                var clsPart = part.Substring(0, colon).Trim().Trim('"', '\'');
                var condPart = part.Substring(colon + 1).Trim();
                var active = EvalBool(condPart, ctx);
                if (active) el.AddToClassList(clsPart);
                else el.RemoveFromClassList(clsPart);
            }
            return true;
        }

        private static bool AssignProp(VisualElement el, string propName, string value)
        {
            // Try Prop<T>.Value first
            var type = el.GetType();
            var field = type.GetField(propName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(Prop<>))
            {
                var prop = field.GetValue(el);
                var valueProp = field.FieldType.GetProperty("Value");
                var elemType = field.FieldType.GetGenericArguments()[0];
                var converted = Convert(value, elemType);
                if (converted != null) { valueProp?.SetValue(prop, converted); return true; }
                return false;
            }

            // Try plain property
            var pi = type.GetProperty(propName,
                BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.CanWrite)
            {
                var converted = Convert(value, pi.PropertyType);
                if (converted != null) { pi.SetValue(el, converted); return true; }
            }
            return false;
        }

        // ─── Expression evaluator (simple subset) ─────────────────────────

        /// <summary>
        /// Evaluates a simple boolean binding expression on the host component.
        /// Supported: literal true/false, !Expr, Prop.Value, Prop, Expr == 'x' / !=,
        /// top-level || / &amp;&amp;. Anything else → FallbackException.
        /// </summary>
        private static bool EvalBool(string expr, InterpretContext ctx)
        {
            expr = expr.Trim();

            if (expr == "true") return true;
            if (expr == "false") return false;

            // Top-level || / && (no paren nesting)
            if (ContainsTopLevel(expr, "||"))
            {
                foreach (var part in SplitTopLevel(expr, "||"))
                    if (EvalBool(part, ctx)) return true;
                return false;
            }
            if (ContainsTopLevel(expr, "&&"))
            {
                foreach (var part in SplitTopLevel(expr, "&&"))
                    if (!EvalBool(part, ctx)) return false;
                return true;
            }

            // Negate
            if (expr.StartsWith("!"))
                return !EvalBool(expr.Substring(1).Trim(), ctx);

            // Equality: expr == 'x' / expr != 'x'
            var eqIdx = IndexOfTopLevel(expr, "==");
            var neIdx = IndexOfTopLevel(expr, "!=");
            if (eqIdx > 0 || neIdx > 0)
            {
                var op = eqIdx > 0 && (neIdx < 0 || eqIdx < neIdx) ? "==" : "!=";
                var parts = expr.Split(new[] { op }, 2, StringSplitOptions.None);
                var left = EvalString(parts[0].Trim(), ctx);
                var right = parts[1].Trim().Trim('\'', '"');
                return op == "==" ? left == right : left != right;
            }

            // Simple field/prop resolution
            var val = ReadField(expr, ctx);
            if (val is bool b) return b;
            if (val is string s) return !string.IsNullOrEmpty(s);
            if (val != null) return true;

            throw new FallbackException($"complex bool expr: {expr}");
        }

        private static string EvalString(string expr, InterpretContext ctx)
        {
            expr = expr.Trim();
            // Literal string
            if ((expr.StartsWith("'") && expr.EndsWith("'"))
                || (expr.StartsWith("\"") && expr.EndsWith("\"")))
                return expr.Substring(1, expr.Length - 2);

            var val = ReadField(expr, ctx);
            return val?.ToString() ?? "";
        }

        private static object ReadField(string expr, InterpretContext ctx)
        {
            // Strip .Value suffix — we resolve the Prop and read its value ourselves
            var clean = expr.EndsWith(".Value")
                ? expr.Substring(0, expr.Length - 6)
                : expr;

            // Simple identifier — look up public field or property on host
            var host = ctx.Component;
            var type = host.GetType();
            var field = type.GetField(clean, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                var raw = field.GetValue(host);
                // Unwrap Prop<T>
                if (raw != null && raw.GetType().IsGenericType
                    && raw.GetType().GetGenericTypeDefinition() == typeof(Prop<>))
                {
                    return raw.GetType().GetProperty("Value")?.GetValue(raw);
                }
                return raw;
            }

            var pi = type.GetProperty(clean, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null) return pi.GetValue(host);

            return null;
        }

        private static bool ContainsTopLevel(string expr, string op)
        {
            return IndexOfTopLevel(expr, op) >= 0;
        }

        private static int IndexOfTopLevel(string expr, string op)
        {
            var depth = 0;
            var inStr = '\0';
            for (int i = 0; i <= expr.Length - op.Length; i++)
            {
                var c = expr[i];
                if (inStr != '\0')
                {
                    if (c == inStr) inStr = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') { inStr = c; continue; }
                if (c == '(' || c == '{') { depth++; continue; }
                if (c == ')' || c == '}') { depth--; continue; }
                if (depth == 0 && string.CompareOrdinal(expr, i, op, 0, op.Length) == 0)
                    return i;
            }
            return -1;
        }

        private static IEnumerable<string> SplitTopLevel(string expr, string op)
        {
            var start = 0;
            var depth = 0;
            var inStr = '\0';
            for (int i = 0; i <= expr.Length - op.Length; i++)
            {
                var c = expr[i];
                if (inStr != '\0')
                {
                    if (c == inStr) inStr = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') { inStr = c; continue; }
                if (c == '(' || c == '{') { depth++; continue; }
                if (c == ')' || c == '}') { depth--; continue; }
                if (depth == 0 && string.CompareOrdinal(expr, i, op, 0, op.Length) == 0)
                {
                    yield return expr.Substring(start, i - start).Trim();
                    start = i + op.Length;
                    i = start - 1;
                }
            }
            yield return expr.Substring(start).Trim();
        }

        // ─── Type conversion ──────────────────────────────────────────────

        private static object Convert(string value, Type targetType)
        {
            try
            {
                if (targetType == typeof(string)) return value;
                if (targetType == typeof(bool))
                    return value == "true" || value == "True" || value == "1";
                if (targetType == typeof(int)) return int.Parse(value);
                if (targetType == typeof(float))
                    return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                if (targetType.IsEnum) return Enum.Parse(targetType, value, true);
            }
            catch { }
            return null;
        }

        // ─── Fallback helpers ─────────────────────────────────────────────

        private static bool FallbackFailed(SusComponent comp, List<SusComponentSnapshot.Entry> snap)
        {
            // Attempt to restore whatever was there by rebuilding via Build()
            // Build() is protected but since we ARE SusComponent's friend, we use reflection.
            try
            {
                comp.Clear();
                var build = comp.GetType().GetMethod("Build",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                build?.Invoke(comp, null);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                comp.ReloadCompanionStyleSheets();
#endif
                SusComponentSnapshot.Restore(comp, snap);
                comp.MarkDirtyRepaint();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SharqInterp] FallbackFailed recovery error: {ex.Message}");
            }
            return false;
        }

        // ─── Slot helpers (delegates to host SusComponent) ───────────────

        private sealed class InterpretContext
        {
            public readonly SusComponent Component;

            private static readonly MethodInfo s_getSlot =
                typeof(SusComponent).GetMethod("GetSlotContainer",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            private static readonly MethodInfo s_buildSlot =
                typeof(SusComponent).GetMethod("BuildSlot",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            public InterpretContext(SusComponent component)
            {
                Component = component;
            }

            public VisualElement GetSlotContainer(string slotName)
            {
                if (s_getSlot != null)
                {
                    try { return s_getSlot.Invoke(Component, new object[] { slotName }) as VisualElement; }
                    catch { }
                }
                return new VisualElement { name = $"slot-{slotName}" };
            }

            public void BuildSlot(string slotName, VisualElement container)
            {
                if (s_buildSlot != null)
                {
                    try { s_buildSlot.Invoke(Component, new object[] { slotName, null, container }); }
                    catch { }
                }
            }
        }

        private sealed class FallbackException : Exception
        {
            public FallbackException(string reason)
                : base($"[SharqInterp] Fallback: {reason}") { }
        }
    }
}
