using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR || DEVELOPMENT_BUILD

namespace Sharq.Core.Diagnostics
{
    /// <summary>
    /// ScreenAudit — text dumps of what the user SEES on screen.
    ///
    /// MCP read_console returns ONLY the first line of each Debug.Log.
    /// Therefore every dump line is a separate Debug.Log with a tag prefix.
    /// Agent filter: read_console -> filter_text="[LA] | [PA] | [FP]"
    /// </summary>
    public static class ScreenAudit
    {
        private const string LA = "[LA]"; // LayoutAudit
        private const string PA = "[PA]"; // PickableLayerAudit
        private const string FP = "[FP]"; // FullPropsDump

        private static bool _hotkeyInstalled;

        /// <summary>
        /// Writes each line of sb as a separate Debug.Log with a tag prefix.
        /// Unity Debug.Log(multiline) = one entry → MCP sees only the first line.
        /// </summary>
        internal static void LogLines(StringBuilder sb, string tag)
        {
            var text = sb.ToString();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (string.IsNullOrEmpty(trimmed))
                    Debug.Log($"{tag} "); // empty line
                else
                    Debug.Log($"{tag} {trimmed}");
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  1. LayoutDump — element map with coordinates
        // ════════════════════════════════════════════════════════════════

        public static void LayoutDump(VisualElement root, int maxDepth = 8)
        {
            var sb = new StringBuilder();
            var hiddenCount = 0;
            sb.AppendLine("══════════ LayoutDump ══════════");
            sb.AppendLine($"Root: {root?.GetType().Name ?? "null"}  panel={root?.panel != null}");
            sb.AppendLine($"Screen: {Screen.width}×{Screen.height}");
            sb.AppendLine();

            DumpLayoutNode(root, sb, 0, maxDepth, ref hiddenCount, isRootLevel: true);

            if (hiddenCount > 0)
                sb.AppendLine($"  (+ {hiddenCount} hidden/zero-size elements skipped)");

            LogLines(sb, LA);
        }

        private static void DumpLayoutNode(VisualElement el, StringBuilder sb,
            int depth, int maxDepth, ref int hiddenCount, bool isRootLevel = false)
        {
            if (el == null) return;

            var wb = el.worldBound;
            var display = el.resolvedStyle.display;
            var picking = el.pickingMode;
            var visible = el.visible;

            var isHidden = !isRootLevel && (display == DisplayStyle.None || !visible ||
                           (wb.width <= 0 && wb.height <= 0 && depth > 0));

            if (isHidden && depth > 2)
            {
                hiddenCount++;
                return;
            }

            var indent = new string('│', depth);
            var typeName = el.GetType().Name;
            var isSus = el is SusComponent;

            // Status line: type, name, classes, bounds, picking, display
            var name = !string.IsNullOrEmpty(el.name) ? $" #{el.name}" : "";
            var susMarker = isSus ? " ⚙" : "";
            var pickingStr = picking == PickingMode.Position ? "🖱" :
                             picking == PickingMode.Ignore ? "⊘" : "○";
            var hiddenStr = isHidden ? " HIDDEN" : "";
            var displayStr = display == DisplayStyle.Flex ? "flex" : display.ToString().ToLower();

            // Bounds compact: (x,y w×h)
            var boundsStr = $" ({wb.x:F0},{wb.y:F0} {wb.width:F0}×{wb.height:F0})";

            // Classes (first 3 only to keep compact)
            var classes = string.Join(" ", el.GetClasses());
            if (classes.Length > 60)
                classes = classes[..57] + "...";

            sb.Append(indent);
            sb.AppendLine($"{typeName}{name}{susMarker} {pickingStr}{hiddenStr} "
                + $"[{el.hierarchy.childCount}ch] {displayStr}{boundsStr}");
            if (!string.IsNullOrEmpty(classes))
            {
                sb.Append(indent);
                sb.AppendLine($"  classes: {classes}");
            }

            // One more line for interactive elements: bounding box info
            if (!isHidden && !isRootLevel && picking == PickingMode.Position && wb.width > 0 && wb.height > 0)
            {
                sb.Append(indent);
                sb.AppendLine($"  📍clickable area: ({wb.xMin:F0},{wb.yMin:F0})→({wb.xMax:F0},{wb.yMax:F0})");
            }

            if (depth >= maxDepth) return;

            foreach (var child in el.Children())
            {
                if (child == null) continue;
                DumpLayoutNode(child, sb, depth + 1, maxDepth, ref hiddenCount);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  2. PickableLayerAudit — z-order and overlaps
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sorts all pickable elements by z-order (DOM order = paint order
        /// in Unity UITK — no z-index) and checks overlaps between neighbors.
        /// </summary>
        public static void PickableLayerAudit(VisualElement root)
        {
            var sb = new StringBuilder();
            sb.AppendLine("══════ PickableLayerAudit ══════");
            sb.AppendLine("Z-order = DOM order (last sibling = topmost). No z-index in Unity.");
            sb.AppendLine();

            // Flat list: all pickable elements with their world bound
            var entries = new List<PickableEntry>();
            CollectPickables(root, entries, 0);

            var overlapping = new HashSet<int>();

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var typeName = e.Element.GetType().Name;
                var classes = string.Join(" ", e.Element.GetClasses());
                var name = !string.IsNullOrEmpty(e.Element.name) ? $" #{e.Element.name}" : "";
                var isSus = e.Element is SusComponent;
                var susMark = isSus ? " ⚙" : "";

                // Check overlap with elements LATER in list (higher z-order → on top)
                var overlaps = "";
                for (int j = i + 1; j < entries.Count; j++)
                {
                    var other = entries[j];
                    if (e.Bounds.Overlaps(other.Bounds))
                    {
                        overlapping.Add(i);
                        overlapping.Add(j);
                        overlaps += $" ← blocked by [{j}]{other.Element.GetType().Name}";
                        break;
                    }
                }

                var overlapMark = overlapping.Contains(i) ? " ⚠OVERLAPPED" : "";
                sb.AppendLine($"[LAYER {i:000}] {typeName}{name}{susMark}{overlapMark}");
                sb.AppendLine($"  area: ({e.Bounds.x:F0},{e.Bounds.y:F0} {e.Bounds.width:F0}×{e.Bounds.height:F0})");
                sb.AppendLine($"  picking={e.Element.pickingMode} visible={e.Element.visible} enabled={e.Element.enabledInHierarchy}");
                if (!string.IsNullOrEmpty(overlaps))
                    sb.AppendLine($"  {overlaps}");
            }

            if (overlapping.Count > 0)
                sb.AppendLine($"\n⚠ {overlapping.Count} elements have overlapping bounds with higher z-order elements.");
            else
                sb.AppendLine("\n✓ No overlapping pickables — all clicks are unambiguous.");

            LogLines(sb, PA);
        }

        private struct PickableEntry
        {
            public VisualElement Element;
            public Rect Bounds;
            public int DomIndex;
        }

        private static void CollectPickables(VisualElement el, List<PickableEntry> entries, int index)
        {
            if (el == null) return;
            var wb = el.worldBound;
            var display = el.resolvedStyle.display;

            if (el.pickingMode == PickingMode.Position && el.visible &&
                display != DisplayStyle.None && wb.width > 0 && wb.height > 0)
            {
                entries.Add(new PickableEntry
                {
                    Element = el,
                    Bounds = wb,
                    DomIndex = index
                });
            }

            var childIdx = 0;
            foreach (var child in el.Children())
            {
                CollectPickables(child, entries, childIdx);
                childIdx++;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  3. FullPropsDump — all Prop values of all SusComponents
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Dump of all Prop&lt;T&gt; values for ALL SusComponents in the tree.
        /// </summary>
        public static void FullPropsDump(VisualElement root)
        {
            var sb = new StringBuilder();
            sb.AppendLine("══════ FullPropsDump ══════");
            var count = 0;
            CollectProps(root, sb, ref count);

            if (count == 0)
                sb.AppendLine("No SusComponents found in tree.");
            else
                sb.AppendLine($"\nTotal: {count} SusComponents dumped.");

            LogLines(sb, FP);
        }

        private static void CollectProps(VisualElement el, StringBuilder sb, ref int count)
        {
            if (el is SusComponent sc)
            {
                count++;
                sb.AppendLine($"── {sc.GetType().Name}" +
                    (!string.IsNullOrEmpty(sc.name) ? $" #{sc.name}" : "") +
                    $" ({sc.worldBound.width:F0}×{sc.worldBound.height:F0})");

                var flags = System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance;

                foreach (var field in sc.GetType().GetFields(flags))
                {
                    if (!field.FieldType.IsGenericType) continue;
                    if (field.FieldType.GetGenericTypeDefinition() != typeof(Prop<>)) continue;

                    try
                    {
                        var propObj = field.GetValue(sc);
                        if (propObj == null)
                        {
                            sb.AppendLine($"  {field.Name} = null");
                            continue;
                        }

                        var valueProp = field.FieldType.GetProperty("Value");
                        var val = valueProp?.GetValue(propObj);

                        var valStr = val switch
                        {
                            null => "null",
                            string s => $"\"{Truncate(s, 50)}\"",
                            bool b => b ? "true" : "false",
                            float f => f.ToString("F2"),
                            double d => d.ToString("F2"),
                            int i => i.ToString(),
                            _ => Truncate(val.ToString(), 40)
                        };

                        sb.AppendLine($"  {field.Name} = {valStr}");
                    }
                    catch
                    {
                        sb.AppendLine($"  {field.Name} = <error>");
                    }
                }
            }

            foreach (var child in el.Children())
                CollectProps(child, sb, ref count);
        }

        private static string Truncate(string s, int max)
            => s == null ? "null" : s.Length <= max ? s : s[..max] + "...";

        // ════════════════════════════════════════════════════════════════
        //  4. ConsoleProfile — hotkey Ctrl+Shift+~
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Registers Ctrl+Shift+~ on root: on press — three dumps to the console.
        /// Call once at application startup.
        /// </summary>
        public static void InstallHotkey(VisualElement root)
        {
            if (_hotkeyInstalled || root == null) return;
            _hotkeyInstalled = true;

            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.ctrlKey && evt.shiftKey && evt.keyCode == KeyCode.BackQuote)
                {
                    Debug.Log("═════════ ScreenAudit triggered (Ctrl+Shift+~) ═════════");
                    LayoutDump(root);
                    PickableLayerAudit(root);
                    FullPropsDump(root);
                    Debug.Log("═════════ ScreenAudit complete ═════════");
                    evt.StopPropagation();
                }
            });

            // Make root focusable so keyboard events work
            if (!root.focusable)
                root.focusable = true;

            Debug.Log("[ScreenAudit] Hotkey installed: Ctrl+Shift+~ to dump screen state.");
        }

        /// <summary>
        /// Called automatically from SusBootstrap on the first Mount.
        /// If the hotkey conflicts with SusDevtools (F12) or SusConsole (~),
        /// you can skip hotkey install and only call the dump methods manually.
        /// </summary>
        public static void InstallIfNeeded(VisualElement root)
        {
            if (_hotkeyInstalled) return;
            InstallHotkey(root);
        }
    }

    /// <summary>
    /// Lightweight variant — a third dump to the console when ClickAuditService is registered.
    /// Does not replace a full LayoutDump, but gives a quick answer to "who is on screen".
    /// </summary>
    internal static class ScreenAuditExtensions
    {
        /// <summary>
        /// Quick one-shot dump: which components are on screen, their sizes and visibility.
        /// Called at application startup for an overview.
        /// </summary>
        public static void QuickStartupAudit(VisualElement root)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[ScreenAudit] Quick startup audit:");
            var count = 0;
            CollectComponentBounds(root, sb, ref count, 0, 3);
            sb.AppendLine($"[ScreenAudit] {count} SusComponents found (depth≤3).");
            ScreenAudit.LogLines(sb, "[SA]");
        }

        private static void CollectComponentBounds(VisualElement el, StringBuilder sb,
            ref int count, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            if (el is SusComponent sc)
            {
                count++;
                var wb = sc.worldBound;
                sb.AppendLine($"  {(depth > 0 ? new string('·', depth * 2) : "")}" +
                    $"{sc.GetType().Name}" +
                    (!string.IsNullOrEmpty(sc.name) ? $" #{sc.name}" : "") +
                    $" ({wb.width:F0}×{wb.height:F0} @ {wb.x:F0},{wb.y:F0}) " +
                    $"visible={sc.visible} picking={sc.pickingMode}");
            }
            foreach (var child in el.Children())
                CollectComponentBounds(child, sb, ref count, depth + 1, maxDepth);
        }
    }
}
#endif
