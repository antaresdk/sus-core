using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
            if (emitToConsole) SusLog.Verbose($"[SusUiProbe.tree] {json}");
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
            // T-2849: "closed" composite controls (ListView/MultiColumnListView/ScrollView —
            // anything overriding contentContainer to redirect Add()/Children() away from `this`)
            // report el.childCount==0 even when el.hierarchy.childCount>0 — the row/scroller
            // elements are real, physically attached, and visible on screen, just not reachable
            // through the LOGICAL Children() API. Walking only el.Children() made every such
            // control (and everything under it — table rows, list items) permanently invisible
            // to this probe: geometry.json for a MultiColumnListView showed `children:0` next to
            // a screenshot with 3 rendered rows, and no selector could ever resolve `--selected`
            // on a row because the row was never emitted. Fall back to the physical hierarchy
            // ONLY when the logical view is empty — ordinary elements (childCount>0, the common
            // case) keep walking via Children() unchanged, so components that legitimately
            // redirect Add() to an inner wrapper still show that curated view instead of raw
            // internals.
            var useHierarchy = el.childCount == 0 && el.hierarchy.childCount > 0;
            var kids = useHierarchy ? el.hierarchy.Children() : el.Children();
            var kidCount = useHierarchy ? el.hierarchy.childCount : el.childCount;
            sb.Append($"\"children\":{kidCount}");
            // T-2209: before the first layout pass, worldBound is NaN — JSON has no NaN literal,
            // so writing it via F() below would emit a bare `NaN` token and break JSON.parse for
            // the WHOLE sidecar (R36 G0, "does not parse"). Omit w/h/x/y entirely instead of coercing
            // to 0/null: the frame-geometry.mjs reader (`rectOf` + `Number.isFinite` gate) already
            // treats a MISSING key as "unresolved, skip" — the same outcome as `null`+`+null`=0
            // would instead read as a real zero-size element (false G2). `resolved:false` names
            // the reason for a human/agent reading the sidecar without guessing "sizer bug".
            var boundsResolved = IsFinite(wb.width) && IsFinite(wb.height) && IsFinite(wb.x) && IsFinite(wb.y);
            if (boundsResolved)
                sb.Append($",\"w\":{F(wb.width)},\"h\":{F(wb.height)},\"x\":{F(wb.x)},\"y\":{F(wb.y)}");
            else
                sb.Append(",\"resolved\":false");
            var text = GetElementText(el);
            var textClipped = false;
            if (!string.IsNullOrEmpty(text))
            {
                sb.Append($",\"text\":{Q(Truncate(text, 80))}");
                textClipped = IsTextClipped(el, text);
            }
            // Depth-cut (T-1975): node is emitted but children are not walked.
            // Same flag as text ellipsis — consumers treat either as truncated.
            var depthCut = depth >= maxDepth && kidCount > 0;
            if (textClipped || depthCut) sb.Append(",\"truncated\":true");
            if (el.resolvedStyle.display == DisplayStyle.None) sb.Append(",\"hidden\":true");
            if (!el.visible) sb.Append(",\"invisible\":true");
            if (el.pickingMode == PickingMode.Position) sb.Append(",\"pickable\":true");
            // R36 layer A / D-028: source texture size + scale mode — bbox alone cannot
            // detect stretch (stretched pixels look like a differently shaped image).
            TryAppendImageMeta(el, sb);
            sb.Append('}');

            if (depth >= maxDepth) return;
            foreach (var child in kids)
                AppendNode(child, sb, depth + 1, maxDepth, ref written);
        }

        // ── Props ───────────────────────────────────────────────

        /// <summary>JSON of all public Prop&lt;T&gt; values on a component (+ type, name, visualState).</summary>
        public static string GetPropsJson(SusComponent component, bool emitToConsole = false)
        {
            var json = BuildPropsJson(component);
            if (emitToConsole) SusLog.Verbose($"[SusUiProbe.props] {json}");
            return json;
        }

        /// <summary>Finds the first SusComponent by #name or type name under root and dumps its props.</summary>
        public static string GetPropsJson(VisualElement root, string nameOrType, bool emitToConsole = false)
        {
            var target = FindComponent(root, nameOrType);
            if (target == null)
            {
                var miss = $"{{\"error\":\"not found\",\"query\":{Q(nameOrType)}}}";
                if (emitToConsole) SusLog.Verbose($"[SusUiProbe.props] {miss}");
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

            // T-3031: values come through ISusPropAccess, not the tracked Value getter — a probe
            // dump must not register reactive dependencies nor make a dead prop look read.
            foreach (var prop in component.DescribeProps())
            {
                sb.Append(',').Append(Q(prop.Name)).Append(':').Append(JsonValue(prop.Value));
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

        /// <summary>Counts + anomalies (the whole class vocabulary, see <see cref="AnomalyClasses"/>) as JSON.</summary>
        public static string GetHealthJson(VisualElement root, bool emitToConsole = false)
        {
            int elements = 0, components = 0, children = 0, maxDepth = 0;
            var anomalies = new List<string>();
            var exempt = new List<string>();
            Walk(root, 0, ref elements, ref components, ref children, ref maxDepth, anomalies, exempt);

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
            sb.Append("],");
            // T-3474: the classes the detector KNOWS, printed next to the findings. A reader who
            // sees `"anomalies":[]` next to this list knows which questions were actually asked --
            // "0 anomalies" stops meaning "everything is fine" and starts meaning "none of these".
            sb.Append("\"vocabulary\":[");
            for (int i = 0; i < AnomalyClasses.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Q(AnomalyClasses[i]));
            }
            sb.Append("],");
            // Legitimate geometry declared by DATA (absolute overlay, scroll content, an explicit
            // `sus-anomaly-ok` class) is listed with its reason, not swallowed in silence.
            sb.Append("\"exempt\":[");
            for (int i = 0; i < exempt.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Q(exempt[i]));
            }
            sb.Append("]}");
            var json = sb.ToString();
            if (emitToConsole) SusLog.Verbose($"[SusUiProbe.health] {json}");
            return json;
        }

        /// <summary>
        /// Just the anomaly lines of <see cref="GetHealthJson"/> — the same detector, without the
        /// JSON around it. Zone E of the storybook (card T-3040) counts these for one stage
        /// canvas, and a shell that had to parse its own JSON to say "health 2" would be one
        /// parser away from disagreeing with <c>sus_ui_health</c> about the same tree.
        /// </summary>
        public static IReadOnlyList<string> GetAnomalies(VisualElement root)
            => GetAnomalies(root, null);

        /// <summary>
        /// Anomalies plus the legitimate-geometry lines (<paramref name="exempt"/>) that the
        /// detector measured and then spared, each carrying the reason it was spared.
        /// </summary>
        public static IReadOnlyList<string> GetAnomalies(VisualElement root, List<string> exempt)
        {
            int elements = 0, components = 0, children = 0, maxDepth = 0;
            var anomalies = new List<string>();
            Walk(root, 0, ref elements, ref components, ref children, ref maxDepth, anomalies, exempt);
            return anomalies;
        }

        private static void Walk(VisualElement el, int depth,
            ref int elements, ref int components, ref int children, ref int maxDepth,
            List<string> anomalies, List<string> exempt)
        {
            if (el == null) return;
            elements++;
            if (depth > maxDepth) maxDepth = depth;
            if (el is SusComponent)
            {
                components++;
                if (IsVisibleZeroSizeAnomaly(el))
                    Add(anomalies, $"{ClassZeroSize} {Addr(el)}: visible but zero-size");
            }

            CheckGeometry(el, anomalies, exempt);
            CheckOverlap(el, anomalies, exempt);

            children += el.childCount;
            // T-3474 / T-2849: the LOGICAL view of a "closed" composite (MultiColumnListView,
            // ListView, ScrollView) is empty -- walking only `Children()` made every table row and
            // every list item invisible to this detector. That is the second reason the owner's
            // frame printed "0 anomalies" next to an icon spilling out of a row: the row was never
            // visited at all. Same fallback rule as AppendNode, so tree and health agree.
            var useHierarchy = el.childCount == 0 && el.hierarchy.childCount > 0;
            var kids = useHierarchy ? el.hierarchy.Children() : el.Children();
            foreach (var child in kids)
                Walk(child, depth + 1, ref elements, ref components, ref children, ref maxDepth, anomalies, exempt);
        }

        /// <summary>
        /// True only for SusComponents that look broken: attached, visible, Flex display,
        /// finite zero world bounds, and NOT structurally collapsed (BoundsAudit parity).
        /// Structurally collapsed = decorative Ignore picker, self/ancestor display:none,
        /// self/ancestor invisible, or ancestor with zero/NaN bounds (closed popup host,
        /// empty clear slot, idle loader, layout-not-ready chrome).
        /// NaN bounds mean layout not computed yet -- not an actionable anomaly.
        /// </summary>
        private static bool IsVisibleZeroSizeAnomaly(VisualElement el)
        {
            if (el == null || el.panel == null) return false;
            if (!el.visible) return false;
            if (el.resolvedStyle.display == DisplayStyle.None) return false;
            // Decorative faces (icons inside chrome) are not layout defects -- same as BoundsAudit.
            if (el.pickingMode == PickingMode.Ignore) return false;

            var wb = el.worldBound;
            // Layout pending / indeterminate -- do not treat as zero-size defect.
            if (float.IsNaN(wb.width) || float.IsNaN(wb.height)) return false;
            if (wb.width > 0 || wb.height > 0) return false;

            // Ancestor collapsed or not laid out -> descendant 0x0 is expected.
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

        // -- Geometry anomaly vocabulary (T-3474) ----------------
        //
        // Before this block the detector knew ONE question -- "is a visible SusComponent 0x0?" --
        // and the storybook health strip printed its answer as "0 anomalies". A reader reads that
        // line as "the screen is fine"; it only ever meant "nothing collapsed". The owner's frame
        // of kit/data/table-cells is the proof: a 64px icon hanging out of a 40px row and a
        // progress bar cut by its column, both under a green "0 anomalies". Both are geometry,
        // and geometry was outside the vocabulary. Each class below has a name that appears
        // verbatim at the head of the health line, a threshold in DATA (the static fields below),
        // and a pixel count with the address of the element -- so the line answers what, where
        // and how much, not just how many.

        /// <summary>Visible SusComponent laid out to 0x0.</summary>
        public const string ClassZeroSize = "zero-size";
        /// <summary>Element sticks out of the box that is supposed to hold it (parent does not clip).</summary>
        public const string ClassOutOfBounds = "out-of-bounds";
        /// <summary>Element is cut by its container (parent clips, element is larger).</summary>
        public const string ClassClipped = "clipped";
        /// <summary>Two visible in-flow siblings cover each other.</summary>
        public const string ClassOverlap = "overlap";
        /// <summary>Element lies entirely outside the panel it is attached to.</summary>
        public const string ClassOffCanvas = "off-canvas";

        /// <summary>Every class this detector can name -- printed next to the findings.</summary>
        public static readonly string[] AnomalyClasses =
        {
            ClassZeroSize, ClassOutOfBounds, ClassClipped, ClassOverlap, ClassOffCanvas,
        };

        /// <summary>Threshold in data: below this the finding is rounding, not a defect.</summary>
        public static float OutOfBoundsMinPx = 2f;
        /// <inheritdoc cref="OutOfBoundsMinPx"/>
        public static float ClippedMinPx = 2f;
        /// <inheritdoc cref="OutOfBoundsMinPx"/>
        public static float OverlapMinPx = 2f;
        /// <summary>Stop after this many findings -- a broken layout must not flood the strip.</summary>
        public static int MaxAnomalies = 60;
        /// <summary>Parents with more children than this are skipped by the O(n^2) overlap pass.</summary>
        public static int OverlapMaxSiblings = 40;

        /// <summary>
        /// USS class -> why geometry under it is legitimate. A host declares its own exceptions
        /// HERE (data with a reason), and the probe still measures them and still prints them --
        /// into the `exempt` list instead of `anomalies`. Silence is never an exemption.
        /// </summary>
        public static readonly Dictionary<string, string> ExemptClasses = new()
        {
            ["sus-anomaly-ok"] = "declared legitimate by the author (class sus-anomaly-ok)",
        };

        /// <summary>Attached, visible, displayed, finite and non-empty -- worth measuring.</summary>
        static bool IsLaidOut(VisualElement el)
        {
            if (el == null || el.panel == null) return false;
            if (!el.visible) return false;
            if (el.resolvedStyle.display == DisplayStyle.None) return false;
            var r = el.worldBound;
            if (!IsFinite(r.width) || !IsFinite(r.height)) return false;
            return r.width > 0f && r.height > 0f;
        }

        static bool IsAbsolute(VisualElement el)
            => el != null && el.resolvedStyle.position == Position.Absolute;

        /// <summary>
        /// A scroller box: content legitimately larger than its viewport is what scrolling IS,
        /// and a row scrolled past the edge is legitimately cut. Never a defect.
        /// </summary>
        static bool IsScrollBox(VisualElement el)
        {
            if (el == null) return false;
            if (el is ScrollView || el is Scroller) return true;
            return el.ClassListContains("unity-scroll-view__content-viewport")
                || el.ClassListContains("unity-scroll-view__content-container")
                || el.ClassListContains("unity-scroll-view__content-and-vertical-scroll-container")
                || el.ClassListContains("unity-scroller")
                || el.ClassListContains("unity-collection-view__virtual-scrolling-container");
        }

        static bool IsInsideScroll(VisualElement el)
        {
            for (var p = el; p != null; p = p.hierarchy.parent)
                if (IsScrollBox(p)) return true;
            return false;
        }

        /// <summary>
        /// Declared reason this finding is legitimate, or null when it is a defect.
        /// <paramref name="absoluteIsLegit"/> is true for the parent-box classes -- an overlay on
        /// `position:absolute` leaves its host on purpose -- and false for off-canvas, where
        /// landing outside the panel entirely is a defect no matter how it was positioned.
        /// </summary>
        static string ExemptReason(VisualElement el, VisualElement parent, bool absoluteIsLegit = true)
        {
            for (var p = el; p != null; p = p.hierarchy.parent)
            {
                foreach (var kv in ExemptClasses)
                    if (p.ClassListContains(kv.Key))
                        return kv.Value;
            }
            if (absoluteIsLegit && IsAbsolute(el))
                return "position:absolute -- an overlay leaves its host on purpose";
            if (IsScrollBox(el) || IsScrollBox(parent))
                return "scroll content -- larger than the viewport by design";
            return null;
        }

        /// <summary>
        /// Does <paramref name="el"/> cut what sticks out of it? `overflow` is not on every Unity
        /// version's IResolvedStyle, so it is read reflectively once and cached; when the property
        /// is missing the detector degrades to naming everything out-of-bounds rather than going
        /// quiet.
        /// </summary>
        static bool ClipsContent(VisualElement el)
        {
            if (el == null) return false;
            if (!_overflowProbed)
            {
                _overflowProbed = true;
                _overflowProp = typeof(IResolvedStyle).GetProperty("overflow");
            }
            if (_overflowProp == null) return false;
            try
            {
                var v = _overflowProp.GetValue(el.resolvedStyle);
                return v != null && string.Equals(v.ToString(), "Hidden", System.StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        static bool _overflowProbed;
        static PropertyInfo _overflowProp;

        /// <summary>How far <paramref name="inner"/> escapes <paramref name="outer"/>, and where.</summary>
        static float EscapeOf(Rect inner, Rect outer, out string side)
        {
            var left = outer.xMin - inner.xMin;
            var top = outer.yMin - inner.yMin;
            var right = inner.xMax - outer.xMax;
            var bottom = inner.yMax - outer.yMax;
            var worst = left; side = "left";
            if (top > worst) { worst = top; side = "top"; }
            if (right > worst) { worst = right; side = "right"; }
            if (bottom > worst) { worst = bottom; side = "bottom"; }
            return worst;
        }

        /// <summary>`SusIcon #icon` / `VisualElement .sus-table__cell` -- findable in the tree dump.</summary>
        static string Addr(VisualElement el)
        {
            if (el == null) return "<null>";
            var t = el.GetType().Name;
            if (!string.IsNullOrEmpty(el.name)) return t + " #" + el.name;
            string first = null;
            foreach (var c in el.GetClasses())
            {
                if (string.IsNullOrEmpty(c)) continue;
                first ??= c;
                if (!c.StartsWith("unity-", System.StringComparison.Ordinal))
                    return t + " ." + c;
            }
            return first == null ? t : t + " ." + first;
        }

        static string Px(float v)
            => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);

        static string Box(Rect r)
            => Px(r.width) + "x" + Px(r.height);

        static void Add(List<string> sink, string line)
        {
            if (sink == null) return;
            if (sink.Count >= MaxAnomalies) return;
            sink.Add(line);
        }

        /// <summary>out-of-bounds / clipped / off-canvas for one element against the box that holds it.</summary>
        static void CheckGeometry(VisualElement el, List<string> anomalies, List<string> exempt)
        {
            if (!IsLaidOut(el)) return;

            var parent = el.hierarchy.parent;
            if (parent != null && IsLaidOut(parent))
            {
                var er = el.worldBound;
                var pr = parent.worldBound;
                var escape = EscapeOf(er, pr, out var side);
                var clips = ClipsContent(parent);
                var cls = clips ? ClassClipped : ClassOutOfBounds;
                var min = clips ? ClippedMinPx : OutOfBoundsMinPx;
                if (escape >= min)
                {
                    var verb = clips ? "cut by" : "sticks out of";
                    var line = $"{cls} {Addr(el)}: {verb} {Addr(parent)} by {Px(escape)}px {side} ({Box(er)} in {Box(pr)})";
                    var reason = ExemptReason(el, parent);
                    if (reason != null) Add(exempt, line + " -- " + reason);
                    else Add(anomalies, line);
                }
            }

            var canvas = el.panel?.visualTree;
            if (canvas != null && !ReferenceEquals(canvas, el) && IsLaidOut(canvas))
            {
                var er = el.worldBound;
                var cr = canvas.worldBound;
                if (!er.Overlaps(cr))
                {
                    var line = $"{ClassOffCanvas} {Addr(el)}: {Box(er)} at ({Px(er.x)},{Px(er.y)}) lies outside the panel {Box(cr)}";
                    var reason = ExemptReason(el, null, absoluteIsLegit: false)
                        ?? (IsInsideScroll(el) ? "scrolled out of view" : null);
                    if (reason != null) Add(exempt, line + " -- " + reason);
                    else Add(anomalies, line);
                }
            }
        }

        /// <summary>Two in-flow siblings covering each other -- a flex row never does that by accident.</summary>
        static void CheckOverlap(VisualElement parent, List<string> anomalies, List<string> exempt)
        {
            if (parent == null || parent.hierarchy.childCount < 2) return;
            if (parent.hierarchy.childCount > OverlapMaxSiblings) return;

            List<VisualElement> kids = null;
            foreach (var c in parent.hierarchy.Children())
            {
                if (!IsLaidOut(c)) continue;
                if (IsAbsolute(c) || IsScrollBox(c)) continue;
                (kids ??= new List<VisualElement>()).Add(c);
            }
            if (kids == null || kids.Count < 2) return;

            for (int i = 0; i < kids.Count; i++)
            for (int j = i + 1; j < kids.Count; j++)
            {
                var a = kids[i].worldBound;
                var b = kids[j].worldBound;
                var w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                var h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (w < OverlapMinPx || h < OverlapMinPx) continue;

                var line = $"{ClassOverlap} {Addr(kids[i])} x {Addr(kids[j])}: {Px(w)}x{Px(h)}px inside {Addr(parent)}";
                var reason = ExemptReason(kids[i], parent) ?? ExemptReason(kids[j], parent);
                if (reason != null) Add(exempt, line + " -- " + reason);
                else Add(anomalies, line);
            }
        }

        // ── Scroll (synthetic UX probe) ─────────────────────────

        /// <summary>
        /// Synthetic scroll for UX / MCP probes. Modes:
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
                if (emitToConsole) SusLog.Verbose($"[SusUiProbe.scroll] {miss}");
                return miss;
            }

            var resolved = ResolveScrollView(root, target, out var how);
            if (resolved == null)
            {
                var miss = $"{{\"ok\":false,\"error\":\"scroll view not found\",\"query\":{Q(target ?? string.Empty)}}}";
                if (emitToConsole) SusLog.Verbose($"[SusUiProbe.scroll] {miss}");
                return miss;
            }

            var m = string.IsNullOrWhiteSpace(mode) ? "offset" : mode.Trim().ToLowerInvariant();
            if (m != "offset" && m != "wheel")
            {
                var bad = $"{{\"ok\":false,\"error\":\"unknown mode\",\"mode\":{Q(mode)}}}";
                if (emitToConsole) SusLog.Verbose($"[SusUiProbe.scroll] {bad}");
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
                    if (emitToConsole) SusLog.Verbose($"[SusUiProbe.scroll] {miss}");
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
                    // Handlers require target set before SendEvent (AGENT_NEWS pattern).
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
            if (emitToConsole) SusLog.Verbose($"[SusUiProbe.scroll] {json}");
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

        /// <summary>
        /// True when the element's own single-line text measures wider than its content box
        /// (single-line only — Label/Button; multiline TextField content is not single-line
        /// truncation and is skipped). Feeds R36 slice G3 via the geometry sidecar.
        /// </summary>
        private static bool IsTextClipped(VisualElement el, string text)
        {
            if (el is not TextElement te || string.IsNullOrEmpty(text)) return false;
            if (te.resolvedStyle.whiteSpace != WhiteSpace.NoWrap) return false;
            var box = te.contentRect;
            if (box.width <= 0f || float.IsNaN(box.width)) return false;
            var measured = te.MeasureTextSize(text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            return measured.x > box.width + 1f;
        }

        /// <summary>
        /// Appends <c>"image":{src,w,h,scaleMode}</c> when the node paints a background image
        /// (D-028). <c>w</c>/<c>h</c> are source texture pixels — not the element bbox.
        /// Sidecar writers (ShowcaseShotCapture) embed <see cref="GetTreeJson"/> verbatim.
        /// </summary>
        private static bool TryAppendImageMeta(VisualElement el, StringBuilder sb)
        {
            if (el == null || sb == null) return false;
            if (!TryResolveBackground(el, out var bg, out var scaleMode)) return false;
            if (!TryBackgroundSource(bg, out var src, out var w, out var h)) return false;
            if (w <= 0 || h <= 0) return false;

            sb.Append(",\"image\":{");
            sb.Append($"\"src\":{Q(src ?? string.Empty)},");
            sb.Append($"\"w\":{w},\"h\":{h},");
            sb.Append($"\"scaleMode\":{Q(scaleMode)}");
            sb.Append('}');
            return true;
        }

        private static bool TryResolveBackground(VisualElement el, out Background bg, out string scaleMode)
        {
            bg = default;
            scaleMode = "stretch-to-fill";

            var resolved = el.resolvedStyle.backgroundImage;
            if (BackgroundHasSource(resolved))
            {
                bg = resolved;
                // T-1493: unityBackgroundScaleMode is obsolete in favour of `background-*` USS
                // properties (D-028 already migrated SusImg/SusIcon/… to `background-size`, which
                // silently WINS over this legacy field whenever both are present). The probe still
                // reads the legacy field on purpose: IResolvedStyle guarantees a concrete resolved
                // value here (never "unset"), while `backgroundSize` has no clean way to tell
                // "author left it at the modern default (auto/native-size)" apart from "author
                // wants stretch" without re-deriving the same ScaleMode/BackgroundSize duality
                // Unity itself keeps for back-compat. Migrating this diagnostic string would risk
                // silently changing the `scaleMode` field that committed visual-regression fixture
                // sidecars compare byte-for-byte — out of scope for a warning-count fix. Suppressed
                // locally, not globally, so any NEW obsolete usage elsewhere still warns.
#pragma warning disable CS0618
                scaleMode = ScaleModeToKebab(el.resolvedStyle.unityBackgroundScaleMode.value);
#pragma warning restore CS0618
                return true;
            }

            // Detached trees (EditMode fixtures): resolvedStyle may be empty while style is set.
            var styled = el.style.backgroundImage;
            if (styled.keyword == StyleKeyword.None || styled.keyword == StyleKeyword.Null)
                return false;
            bg = styled.value;
            if (!BackgroundHasSource(bg)) return false;

            // T-1493: same rationale as above — IStyle.unityBackgroundScaleMode is obsolete but
            // intentionally still read here for the detached-tree fallback path.
#pragma warning disable CS0618
            var modeStyle = el.style.unityBackgroundScaleMode;
#pragma warning restore CS0618
            if (modeStyle.keyword == StyleKeyword.Undefined || modeStyle.keyword == StyleKeyword.Null)
                scaleMode = "stretch-to-fill"; // UITK default = stretch (D-028)
            else
                scaleMode = ScaleModeToKebab(modeStyle.value);
            return true;
        }

        private static bool BackgroundHasSource(Background bg)
            => bg.texture != null || bg.sprite != null || bg.vectorImage != null || bg.renderTexture != null;

        private static bool TryBackgroundSource(Background bg, out string src, out int w, out int h)
        {
            src = string.Empty;
            w = 0;
            h = 0;

            if (bg.texture != null)
            {
                var tex = bg.texture;
                w = tex.width;
                h = tex.height;
                src = AssetPathOf(tex);
                return true;
            }

            if (bg.sprite != null)
            {
                var sp = bg.sprite;
                // Sprite.rect is the source pixel rect in the atlas — correct aspect for R36 A1.
                w = Mathf.RoundToInt(sp.rect.width);
                h = Mathf.RoundToInt(sp.rect.height);
                src = AssetPathOf(sp);
                return w > 0 && h > 0;
            }

            if (bg.renderTexture != null)
            {
                var rt = bg.renderTexture;
                w = rt.width;
                h = rt.height;
                src = AssetPathOf(rt);
                return true;
            }

            if (bg.vectorImage != null)
            {
                var vi = bg.vectorImage;
                w = Mathf.RoundToInt(vi.width);
                h = Mathf.RoundToInt(vi.height);
                src = AssetPathOf(vi);
                return w > 0 && h > 0;
            }

            return false;
        }

        private static string AssetPathOf(Object obj)
        {
            if (obj == null) return string.Empty;
#if UNITY_EDITOR
            var path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path)) return path.Replace('\\', '/');
#endif
            // Runtime / transient textures: name is better than empty for A1 diagnostics.
            return string.IsNullOrEmpty(obj.name) ? string.Empty : obj.name;
        }

        /// <summary>USS / frames-spec kebab: StretchToFill → stretch-to-fill.</summary>
        private static string ScaleModeToKebab(ScaleMode mode)
        {
            switch (mode)
            {
                case ScaleMode.ScaleAndCrop: return "scale-and-crop";
                case ScaleMode.ScaleToFit: return "scale-to-fit";
                default: return "stretch-to-fill";
            }
        }

        // T-2209: defense-in-depth for every OTHER caller of F() (viewport/content bounds
        // reported straight to an MCP agent, not through the geometry-sidecar gate above) —
        // `NaN`/`Infinity` have no JSON literal, so a bare `null` is the only value that keeps
        // the surrounding JSON parseable instead of emitting an unquoted `NaN` token.
        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static string F(float v) => IsFinite(v) ? v.ToString("F0", CultureInfo.InvariantCulture) : "null";

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
