using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Sharq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Inspector
{
    /// <summary>
    /// Lightweight UI tree dump for the Inspector (no downstream library dependency).
    /// Mirrors SusDiagnostics.GetUITreeFlat / DumpProps for Editor use.
    /// </summary>
    public static class SusInspectorTree
    {
        public sealed class Node
        {
            public int Depth;
            public string TypeName;
            public string Name;
            public string Classes;
            public int ChildCount;
            public bool IsSusComponent;
            public float Width, Height, X, Y;
            public string Text;
            public bool Hidden;
            public VisualElement Element;
        }

        public static VisualElement FindActiveRoot()
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            foreach (var doc in docs)
            {
                if (doc != null && doc.rootVisualElement != null)
                    return doc.rootVisualElement;
            }
            return null;
        }

        public static List<Node> Flatten(VisualElement root, int maxDepth = 12)
        {
            var list = new List<Node>();
            Walk(root, list, 0, maxDepth);
            return list;
        }

        static void Walk(VisualElement el, List<Node> list, int depth, int maxDepth)
        {
            if (el == null || depth > maxDepth) return;
            var rect = el.worldBound;
            list.Add(new Node
            {
                Depth = depth,
                TypeName = el.GetType().Name,
                Name = el.name ?? "",
                Classes = string.Join(" ", el.GetClasses()),
                ChildCount = el.childCount,
                IsSusComponent = el is SusComponent,
                Width = rect.width,
                Height = rect.height,
                X = rect.x,
                Y = rect.y,
                Text = GetText(el),
                Hidden = el.resolvedStyle.display == DisplayStyle.None,
                Element = el,
            });
            foreach (var child in el.Children())
                Walk(child, list, depth + 1, maxDepth);
        }

        public static string DumpProps(SusComponent component)
        {
            if (component == null) return "(no SusComponent selected)";
            var sb = new StringBuilder();
            sb.AppendLine($"=== {component.GetType().Name} ===");
            foreach (var field in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.FieldType.IsGenericType) continue;
                if (field.FieldType.GetGenericTypeDefinition() != typeof(Prop<>)) continue;
                var val = field.GetValue(component);
                string shown = "?";
                if (val != null)
                {
                    var valueProp = val.GetType().GetProperty("Value");
                    shown = valueProp?.GetValue(val)?.ToString() ?? val.ToString();
                }
                sb.AppendLine($"  {field.Name} = {shown}");
            }

            // Visual state if present
            try
            {
                var vsProp = component.GetType().GetProperty("VisualState");
                if (vsProp != null)
                    sb.AppendLine($"  visualState = {vsProp.GetValue(component)}");
            }
            catch { /* ignore */ }

            return sb.ToString();
        }

        public static string DumpTreeText(VisualElement root, int maxDepth = 12)
        {
            var nodes = Flatten(root, maxDepth);
            var sb = new StringBuilder();
            foreach (var n in nodes)
            {
                sb.Append(new string(' ', n.Depth * 2));
                sb.Append(n.IsSusComponent ? "◆ " : "· ");
                sb.Append(n.TypeName);
                if (!string.IsNullOrEmpty(n.Name)) sb.Append($" #{n.Name}");
                if (n.Width > 0 || n.Height > 0)
                    sb.Append($" ({n.Width:F0}×{n.Height:F0})");
                if (n.Hidden) sb.Append(" [hidden]");
                if (!string.IsNullOrEmpty(n.Text)) sb.Append($" \"{Truncate(n.Text, 40)}\"");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string GetText(VisualElement el)
        {
            if (el is TextElement te) return te.text;
            if (el is Label lb) return lb.text;
            if (el is Button btn) return btn.text;
            return null;
        }

        static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= n) return s;
            return s.Substring(0, n - 1) + "…";
        }

        public static (int elements, int components, int maxDepth) Stats(VisualElement root)
        {
            int el = 0, comp = 0, maxD = 0;
            void Rec(VisualElement v, int d)
            {
                if (v == null) return;
                el++;
                if (v is SusComponent) comp++;
                if (d > maxD) maxD = d;
                foreach (var c in v.Children())
                    Rec(c, d + 1);
            }
            Rec(root, 0);
            return (el, comp, maxD);
        }
    }
}
