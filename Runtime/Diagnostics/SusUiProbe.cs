using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR || DEVELOPMENT_BUILD

namespace Sharq.Core.Diagnostics
{
    /// <summary>
    /// SusUiProbe — machine-readable (JSON) snapshot of the live UI, for AI agents / MCP.
    ///
    /// Phase 0 of SUS_MCP_PLAN: a pure C# facade with NO MCP dependency and NO Console
    /// output by default — agents parse the returned string instead of read_console.
    /// Pass emitToConsole:true only for manual human debugging.
    ///
    /// Editor-only setup validation lives in SusUiProbeEditor (Editor assembly), since
    /// core Runtime cannot reference the Editor validator.
    ///
    /// Guarded by UNITY_EDITOR || DEVELOPMENT_BUILD (same policy as ScreenAudit) — never
    /// ships in release player builds.
    /// </summary>
    public static class SusUiProbe
    {
        // ── Tree ────────────────────────────────────────────────

        /// <summary>Flat JSON array of the UI subtree under root (name, type, bounds, sus?, text).</summary>
        public static string GetTreeJson(VisualElement root, int maxDepth = 10, bool emitToConsole = false)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            int written = 0;
            AppendNode(root, sb, 0, maxDepth, ref written);
            sb.Append(']');
            var json = sb.ToString();
            if (emitToConsole) Debug.Log($"[SusUiProbe.tree] {json}");
            return json;
        }

        private static void AppendNode(VisualElement el, StringBuilder sb, int depth, int maxDepth, ref int written)
        {
            if (el == null || depth > maxDepth) return;
            if (written > 0) sb.Append(',');
            written++;

            var wb = el.worldBound;
            sb.Append('{');
            sb.Append($"\"depth\":{depth},");
            sb.Append($"\"type\":{Q(el.GetType().Name)},");
            sb.Append($"\"name\":{Q(el.name ?? string.Empty)},");
            sb.Append($"\"classes\":{Q(string.Join(" ", el.GetClasses()))},");
            sb.Append($"\"sus\":{(el is SusComponent ? "true" : "false")},");
            sb.Append($"\"children\":{el.childCount},");
            sb.Append($"\"w\":{F(wb.width)},\"h\":{F(wb.height)},\"x\":{F(wb.x)},\"y\":{F(wb.y)}");
            var text = GetElementText(el);
            if (!string.IsNullOrEmpty(text)) sb.Append($",\"text\":{Q(Truncate(text, 80))}");
            if (el.resolvedStyle.display == DisplayStyle.None) sb.Append(",\"hidden\":true");
            if (!el.visible) sb.Append(",\"invisible\":true");
            if (el.pickingMode == PickingMode.Position) sb.Append(",\"pickable\":true");
            sb.Append('}');

            if (depth >= maxDepth) return;
            foreach (var child in el.Children())
                AppendNode(child, sb, depth + 1, maxDepth, ref written);
        }

        // ── Props ───────────────────────────────────────────────

        /// <summary>JSON of all public Prop&lt;T&gt; values on a component (+ type, name, visualState).</summary>
        public static string GetPropsJson(SusComponent component, bool emitToConsole = false)
        {
            var json = BuildPropsJson(component);
            if (emitToConsole) Debug.Log($"[SusUiProbe.props] {json}");
            return json;
        }

        /// <summary>Finds the first SusComponent by #name or type name under root and dumps its props.</summary>
        public static string GetPropsJson(VisualElement root, string nameOrType, bool emitToConsole = false)
        {
            var target = FindComponent(root, nameOrType);
            if (target == null)
            {
                var miss = $"{{\"error\":\"not found\",\"query\":{Q(nameOrType)}}}";
                if (emitToConsole) Debug.Log($"[SusUiProbe.props] {miss}");
                return miss;
            }
            return GetPropsJson(target, emitToConsole);
        }

        private static string BuildPropsJson(SusComponent component)
        {
            if (component == null) return "null";
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"type\":{Q(component.GetType().Name)}");
            if (!string.IsNullOrEmpty(component.name)) sb.Append($",\"name\":{Q(component.name)}");
            if (!string.IsNullOrEmpty(component.VisualState)) sb.Append($",\"visualState\":{Q(component.VisualState)}");

            foreach (var field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!field.FieldType.IsGenericType) continue;
                if (field.FieldType.GetGenericTypeDefinition() != typeof(Prop<>)) continue;
                object val = null;
                var propObj = field.GetValue(component);
                if (propObj != null) val = field.FieldType.GetProperty("Value")?.GetValue(propObj);
                sb.Append(',').Append(Q(field.Name)).Append(':').Append(JsonValue(val));
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static SusComponent FindComponent(VisualElement el, string nameOrType)
        {
            if (el == null) return null;
            if (el is SusComponent sc && (sc.name == nameOrType || sc.GetType().Name == nameOrType))
                return sc;
            foreach (var child in el.Children())
            {
                var found = FindComponent(child, nameOrType);
                if (found != null) return found;
            }
            return null;
        }

        // ── Health ──────────────────────────────────────────────

        /// <summary>Counts + anomalies (visible-but-zero-size SusComponents) as JSON.</summary>
        public static string GetHealthJson(VisualElement root, bool emitToConsole = false)
        {
            int elements = 0, components = 0, children = 0, maxDepth = 0;
            var anomalies = new List<string>();
            Walk(root, 0, ref elements, ref components, ref children, ref maxDepth, anomalies);

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"totalElements\":{elements},");
            sb.Append($"\"susComponents\":{components},");
            sb.Append($"\"totalChildren\":{children},");
            sb.Append($"\"maxDepth\":{maxDepth},");
            sb.Append("\"anomalies\":[");
            for (int i = 0; i < anomalies.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Q(anomalies[i]));
            }
            sb.Append("]}");
            var json = sb.ToString();
            if (emitToConsole) Debug.Log($"[SusUiProbe.health] {json}");
            return json;
        }

        private static void Walk(VisualElement el, int depth,
            ref int elements, ref int components, ref int children, ref int maxDepth, List<string> anomalies)
        {
            if (el == null) return;
            elements++;
            if (depth > maxDepth) maxDepth = depth;
            if (el is SusComponent)
            {
                components++;
                var wb = el.worldBound;
                if (el.visible && el.resolvedStyle.display != DisplayStyle.None && wb.width <= 0 && wb.height <= 0)
                    anomalies.Add($"{el.GetType().Name}{(string.IsNullOrEmpty(el.name) ? string.Empty : " #" + el.name)}: visible but zero-size");
            }
            children += el.childCount;
            foreach (var child in el.Children())
                Walk(child, depth + 1, ref elements, ref components, ref children, ref maxDepth, anomalies);
        }

        // ── helpers ─────────────────────────────────────────────

        private static string GetElementText(VisualElement el)
        {
            if (el is Label lbl && !string.IsNullOrEmpty(lbl.text)) return lbl.text;
            if (el is Button btn && !string.IsNullOrEmpty(btn.text)) return btn.text;
            if (el is TextField tf && !string.IsNullOrEmpty(tf.value)) return tf.value;
            return null;
        }

        private static string F(float v) => v.ToString("F0", CultureInfo.InvariantCulture);

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "…");

        private static string Q(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty) + "\"";
        }

        private static string JsonValue(object val)
        {
            if (val == null) return "null";
            if (val is bool b) return b ? "true" : "false";
            if (val is int or long or float or double or decimal)
                return System.Convert.ToString(val, CultureInfo.InvariantCulture);
            return Q(val.ToString());
        }
    }
}
#endif
