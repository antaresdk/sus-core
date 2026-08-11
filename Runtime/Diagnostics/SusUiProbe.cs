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
                if (IsVisibleZeroSizeAnomaly(el))
                    anomalies.Add($"{el.GetType().Name}{(string.IsNullOrEmpty(el.name) ? string.Empty : " #" + el.name)}: visible but zero-size");
            }
            children += el.childCount;
            foreach (var child in el.Children())
                Walk(child, depth + 1, ref elements, ref components, ref children, ref maxDepth, anomalies);
        }

        /// <summary>
        /// True only for SusComponents that look broken: attached, visible, Flex display,
        /// finite zero world bounds, and NOT structurally collapsed (BoundsAudit parity).
        /// Structurally collapsed = decorative Ignore picker, self/ancestor display:none,
        /// self/ancestor invisible, or ancestor with zero/NaN bounds (closed popup host,
        /// empty clear slot, idle loader, layout-not-ready chrome).
        /// NaN bounds mean layout not computed yet — not an actionable anomaly.
        /// </summary>
        private static bool IsVisibleZeroSizeAnomaly(VisualElement el)
        {
            if (el == null || el.panel == null) return false;
            if (!el.visible) return false;
            if (el.resolvedStyle.display == DisplayStyle.None) return false;
            // Decorative faces (icons inside chrome) are not layout defects — same as BoundsAudit.
            if (el.pickingMode == PickingMode.Ignore) return false;

            var wb = el.worldBound;
            // Layout pending / indeterminate — do not treat as zero-size defect.
            if (float.IsNaN(wb.width) || float.IsNaN(wb.height)) return false;
            if (wb.width > 0 || wb.height > 0) return false;

            // Ancestor collapsed or not laid out → descendant 0×0 is expected.
            for (var p = el.parent; p != null; p = p.parent)
            {
                if (!p.visible) return false;
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
                var pwb = p.worldBound;
                if (float.IsNaN(pwb.width) || float.IsNaN(pwb.height)) return false;
                if (pwb.width <= 0 && pwb.height <= 0) return false;
            }

            return true;
        }

        // ── Scroll (synthetic UX probe) ─────────────────────────

        /// <summary>
        /// Synthetic scroll for UX / MCP probes (T-040). Modes:
        /// <list type="bullet">
        /// <item><c>offset</c> — set <see cref="ScrollView.scrollOffset"/> absolutely (<c>x</c>/<c>y</c>)
        /// and/or relatively (<c>dx</c>/<c>dy</c>). Default when unspecified.</item>
        /// <item><c>wheel</c> — dispatch <see cref="WheelEvent"/> (user-like mouse wheel).</item>
        /// </list>
        /// Optional <paramref name="into"/> scrolls that descendant into view via <see cref="ScrollView.ScrollTo"/>.
        /// Target resolves UITK <see cref="ScrollView"/> by #name / type name, optional public
        /// <c>View</c> property (downstream wrappers), or first descendant ScrollView.
        /// Returns JSON: ok/error, mode, target, before/after offset, content/viewport sizes.
        /// </summary>
        public static string ScrollJson(
            VisualElement root,
            string target = null,
            string mode = "offset",
            float? x = null,
            float? y = null,
            float? dx = null,
            float? dy = null,
            string into = null,
            bool emitToConsole = false)
        {
            if (root == null)
            {
                var miss = "{\"ok\":false,\"error\":\"root is null\"}";
                if (emitToConsole) Debug.Log($"[SusUiProbe.scroll] {miss}");
                return miss;
            }

            var resolved = ResolveScrollView(root, target, out var how);
            if (resolved == null)
            {
                var miss = $"{{\"ok\":false,\"error\":\"scroll view not found\",\"query\":{Q(target ?? string.Empty)}}}";
                if (emitToConsole) Debug.Log($"[SusUiProbe.scroll] {miss}");
                return miss;
            }

            var m = string.IsNullOrWhiteSpace(mode) ? "offset" : mode.Trim().ToLowerInvariant();
            if (m != "offset" && m != "wheel")
            {
                var bad = $"{{\"ok\":false,\"error\":\"unknown mode\",\"mode\":{Q(mode)}}}";
                if (emitToConsole) Debug.Log($"[SusUiProbe.scroll] {bad}");
                return bad;
            }

            var before = resolved.scrollOffset;
            string applied = m;

            if (!string.IsNullOrEmpty(into))
            {
                var child = FindElement(resolved, into) ?? FindElement(root, into);
                if (child == null)
                {
                    var miss = $"{{\"ok\":false,\"error\":\"into target not found\",\"into\":{Q(into)}}}";
                    if (emitToConsole) Debug.Log($"[SusUiProbe.scroll] {miss}");
                    return miss;
                }
                resolved.ScrollTo(child);
                applied = "scrollTo";
            }
            else if (m == "wheel")
            {
                // UITK ScrollWheel: positive delta.y typically scrolls content down (offset increases).
                float wdx = dx ?? 0f;
                float wdy = dy ?? 120f;
                if (wdx == 0f && wdy == 0f) wdy = 120f;
                var center = resolved.worldBound.center;
                var sys = new Event
                {
                    type = EventType.ScrollWheel,
                    delta = new Vector2(wdx, wdy),
                    mousePosition = center,
                };
                using (var evt = WheelEvent.GetPooled(sys))
                {
                    // Handlers require target set before SendEvent (AGENT_NEWS T-055 pattern).
                    evt.target = resolved;
                    resolved.SendEvent(evt);
                }
                applied = "wheel";
            }
            else
            {
                var next = before;
                if (x.HasValue) next.x = x.Value;
                if (y.HasValue) next.y = y.Value;
                if (dx.HasValue) next.x += dx.Value;
                if (dy.HasValue) next.y += dy.Value;
                // No params → nudge down so a bare call is still observable for UX smoke.
                if (!x.HasValue && !y.HasValue && !dx.HasValue && !dy.HasValue)
                    next.y += 120f;
                resolved.scrollOffset = next;
                applied = "offset";
            }

            var after = resolved.scrollOffset;
            var content = resolved.contentContainer;
            var cwb = content != null ? content.worldBound : default;
            var vwb = resolved.worldBound;

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"ok\":true,");
            sb.Append($"\"mode\":{Q(applied)},");
            sb.Append($"\"target\":{Q(Describe(resolved))},");
            sb.Append($"\"resolvedVia\":{Q(how)},");
            sb.Append($"\"before\":{{\"x\":{F(before.x)},\"y\":{F(before.y)}}},");
            sb.Append($"\"after\":{{\"x\":{F(after.x)},\"y\":{F(after.y)}}},");
            sb.Append($"\"delta\":{{\"x\":{F(after.x - before.x)},\"y\":{F(after.y - before.y)}}},");
            sb.Append($"\"viewport\":{{\"w\":{F(vwb.width)},\"h\":{F(vwb.height)}}},");
            sb.Append($"\"content\":{{\"w\":{F(cwb.width)},\"h\":{F(cwb.height)}}}");
            sb.Append('}');
            var json = sb.ToString();
            if (emitToConsole) Debug.Log($"[SusUiProbe.scroll] {json}");
            return json;
        }

        /// <summary>Resolves a live UITK ScrollView under root (by name/type or first found).</summary>
        public static ScrollView ResolveScrollView(VisualElement root, string nameOrType, out string resolvedVia)
        {
            resolvedVia = null;
            if (root == null) return null;

            VisualElement anchor = root;
            if (!string.IsNullOrEmpty(nameOrType))
            {
                anchor = FindElement(root, nameOrType);
                if (anchor == null) return null;
                resolvedVia = "query";
            }
            else
            {
                resolvedVia = "first";
            }

            if (anchor is ScrollView direct)
            {
                resolvedVia = string.IsNullOrEmpty(nameOrType) ? "first" : "self";
                return direct;
            }

            // Downstream wrappers often expose public ScrollView View without core knowing their type.
            var viewProp = anchor.GetType().GetProperty("View", BindingFlags.Public | BindingFlags.Instance);
            if (viewProp != null && typeof(ScrollView).IsAssignableFrom(viewProp.PropertyType))
            {
                if (viewProp.GetValue(anchor) is ScrollView viaView)
                {
                    resolvedVia = "View";
                    return viaView;
                }
            }

            var q = anchor.Q<ScrollView>();
            if (q != null)
            {
                resolvedVia = "descendant";
                return q;
            }

            // When no target: already searched from root via Q; done.
            if (string.IsNullOrEmpty(nameOrType) && ReferenceEquals(anchor, root))
                return null;

            return null;
        }

        private static VisualElement FindElement(VisualElement el, string nameOrType)
        {
            if (el == null || string.IsNullOrEmpty(nameOrType)) return null;
            var key = nameOrType.StartsWith("#") ? nameOrType.Substring(1) : nameOrType;
            if (el.name == key || el.GetType().Name == key || el.GetType().Name == nameOrType)
                return el;
            foreach (var child in el.Children())
            {
                var found = FindElement(child, nameOrType);
                if (found != null) return found;
            }
            return null;
        }

        private static string Describe(VisualElement el)
        {
            if (el == null) return string.Empty;
            var n = el.name ?? string.Empty;
            return string.IsNullOrEmpty(n) ? el.GetType().Name : el.GetType().Name + " #" + n;
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
