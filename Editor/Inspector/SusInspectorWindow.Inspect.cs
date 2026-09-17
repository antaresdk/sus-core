using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Sharq.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Inspector
{
    public partial class SusInspectorWindow
    {
        // ═══ Inspect — collapsible live tree + editable props ══════

        void DrawInspect()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Inspect is live in Play mode. Enter Play with a UIDocument / SusApp scene.",
                    MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                RefreshTree(preserveSelection: true);
            var newAuto = GUILayout.Toggle(_autoRefreshTree, "Auto (1s)", "Button", GUILayout.Width(70));
            if (newAuto != _autoRefreshTree)
            {
                _autoRefreshTree = newAuto;
                EditorPrefs.SetBool(PrefAutoRefreshTree, newAuto);
            }
            GUILayout.Space(8);
            GUILayout.Label("Search", GUILayout.Width(44));
            var newFilter = EditorGUILayout.TextField(_filter);
            if (newFilter != _filter)
                _filter = newFilter;
            if (GUILayout.Button("✕", GUILayout.Width(22)))
                _filter = "";
            GUILayout.Space(8);
            _maxDepth = EditorGUILayout.IntSlider(_maxDepth, 2, 24, GUILayout.Width(160));
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_inspectStatus))
                EditorGUILayout.LabelField(_inspectStatus, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();

            // Tree pane
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.55f));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("UI Tree", EditorStyles.boldLabel, GUILayout.Width(60));
            if (GUILayout.Button("Expand all", EditorStyles.miniButton, GUILayout.Width(72)))
                _collapsed.Clear();
            if (GUILayout.Button("Collapse to 2", EditorStyles.miniButton, GUILayout.Width(86)))
                CollapseToDepth(2);
            EditorGUILayout.EndHorizontal();

            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll,
                GUILayout.Height(Mathf.Max(260, position.height - 200)));
            DrawTreeRows();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // Selection pane
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
            _propsScroll = EditorGUILayout.BeginScrollView(_propsScroll,
                GUILayout.Height(Mathf.Max(260, position.height - 200)));
            DrawSelectionPane();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        void CollapseToDepth(int depth)
        {
            _collapsed.Clear();
            foreach (var n in _nodes)
                if (n.Depth >= depth && n.ChildCount > 0 && n.Element != null)
                    _collapsed.Add(n.Element);
        }

        void DrawTreeRows()
        {
            if (_nodes.Count == 0)
            {
                EditorGUILayout.LabelField(
                    EditorApplication.isPlaying ? "(empty — press Refresh)" : "(enter Play mode)",
                    EditorStyles.miniLabel);
                return;
            }

            var searching = !string.IsNullOrEmpty(_filter);
            HashSet<VisualElement> visible = null;
            if (searching)
            {
                // Matches + all their ancestors stay visible so hierarchy context is kept.
                visible = new HashSet<VisualElement>();
                var stack = new List<SusInspectorTree.Node>();
                foreach (var n in _nodes)
                {
                    while (stack.Count > 0 && stack[^1].Depth >= n.Depth)
                        stack.RemoveAt(stack.Count - 1);
                    stack.Add(n);
                    if (Matches(n, _filter))
                        foreach (var a in stack)
                            visible.Add(a.Element);
                }
            }

            int hiddenBelowDepth = int.MaxValue;
            foreach (var n in _nodes)
            {
                if (n.Depth >= hiddenBelowDepth) continue;
                hiddenBelowDepth = int.MaxValue;

                if (searching && !visible.Contains(n.Element)) continue;

                var isCollapsed = !searching && n.Element != null && _collapsed.Contains(n.Element);
                if (isCollapsed)
                    hiddenBelowDepth = n.Depth + 1;

                DrawTreeRow(n, isCollapsed, searching);
            }
        }

        static bool Matches(SusInspectorTree.Node n, string filter) =>
            $"{n.TypeName} {n.Name} {n.Classes} {n.Text}"
                .IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        void DrawTreeRow(SusInspectorTree.Node n, bool isCollapsed, bool searching)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(n.Depth * 14);

            // Fold arrow (only when node has children and not searching)
            if (!searching && n.ChildCount > 0 && n.Element != null)
            {
                if (GUILayout.Button(isCollapsed ? "▸" : "▾", EditorStyles.label, GUILayout.Width(14)))
                {
                    if (isCollapsed) _collapsed.Remove(n.Element);
                    else _collapsed.Add(n.Element);
                }
            }
            else
            {
                GUILayout.Space(17);
            }

            var isSelected = _selected != null && ReferenceEquals(n.Element, _selected);
            var label = $"{(n.IsSusComponent ? "◆ " : "")}{n.TypeName}"
                        + (string.IsNullOrEmpty(n.Name) ? "" : $"  #{n.Name}")
                        + (isCollapsed ? $"  (+{n.ChildCount})" : "");

            var style = new GUIStyle(EditorStyles.label);
            if (isSelected)
            {
                style.fontStyle = FontStyle.Bold;
                style.normal.textColor = ColAccent;
            }
            else if (n.Hidden)
            {
                style.normal.textColor = ColMuted;
            }
            else if (n.IsSusComponent)
            {
                style.normal.textColor = new Color(0.85f, 0.78f, 0.45f);
            }

            if (GUILayout.Button(label, style))
            {
                _selected = n.Element;
                HighlightElement(n.Element);
            }

            // Right-side mini info: size + hidden flag
            var info = n.Hidden ? "[hidden]" : $"{n.Width:F0}×{n.Height:F0}";
            GUILayout.Label(info, EditorStyles.miniLabel, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();
        }

        void DrawSelectionPane()
        {
            if (_selected == null)
            {
                EditorGUILayout.LabelField("Click a node in the tree.", EditorStyles.miniLabel);
                return;
            }

            var el = _selected;
            EditorGUILayout.LabelField(el.GetType().Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("name", string.IsNullOrEmpty(el.name) ? "—" : el.name);
            EditorGUILayout.LabelField("classes", string.Join(" ", el.GetClasses()));
            var wb = el.worldBound;
            EditorGUILayout.LabelField("bounds", $"{wb.width:F0}×{wb.height:F0} @ {wb.x:F0},{wb.y:F0}");
            EditorGUILayout.LabelField("display",
                el.resolvedStyle.display == DisplayStyle.None ? "None (hidden)" : "Flex");
            EditorGUILayout.LabelField("children", el.childCount.ToString());
            EditorGUILayout.LabelField("path", ElementPath(el), EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Highlight", GUILayout.Width(80)))
                HighlightElement(el);
            if (GUILayout.Button("Copy path", GUILayout.Width(80)))
                EditorGUIUtility.systemCopyBuffer = ElementPath(el);
            if (GUILayout.Button("Copy info", GUILayout.Width(80)))
                EditorGUIUtility.systemCopyBuffer = SelectionInfoText(el);
            EditorGUILayout.EndHorizontal();

            if (el is SusComponent sc)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Props (live, editable)", EditorStyles.boldLabel);
                DrawEditableProps(sc);
            }
        }

        static string SelectionInfoText(VisualElement el)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Type: {el.GetType().Name}");
            sb.AppendLine($"Name: {el.name}");
            sb.AppendLine($"Classes: {string.Join(" ", el.GetClasses())}");
            var wb = el.worldBound;
            sb.AppendLine($"Bounds: {wb.width:F0}×{wb.height:F0} @ {wb.x:F0},{wb.y:F0}");
            sb.AppendLine($"Path: {ElementPath(el)}");
            if (el is SusComponent sc)
                sb.Append(SusInspectorTree.DumpProps(sc));
            return sb.ToString();
        }

        static string ElementPath(VisualElement el)
        {
            var parts = new List<string>();
            var cur = el;
            while (cur != null)
            {
                parts.Add(string.IsNullOrEmpty(cur.name) ? cur.GetType().Name : $"#{cur.name}");
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Prop&lt;T&gt; live editing for primitives — bool, string, int, float.</summary>
        void DrawEditableProps(SusComponent component)
        {
            foreach (var field in component.GetType()
                         .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.FieldType.IsGenericType) continue;
                if (field.FieldType.GetGenericTypeDefinition() != typeof(Prop<>)) continue;

                var prop = field.GetValue(component);
                if (prop == null) continue;
                var valueProp = prop.GetType().GetProperty("Value");
                if (valueProp == null) continue;

                var t = field.FieldType.GetGenericArguments()[0];
                var val = valueProp.GetValue(prop);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(field.Name, GUILayout.Width(120));
                try
                {
                    if (t == typeof(bool))
                    {
                        var b = (bool)(val ?? false);
                        var nb = EditorGUILayout.Toggle(b);
                        if (nb != b) valueProp.SetValue(prop, nb);
                    }
                    else if (t == typeof(string))
                    {
                        var s = (string)val ?? "";
                        var ns = EditorGUILayout.TextField(s);
                        if (ns != s) valueProp.SetValue(prop, ns);
                    }
                    else if (t == typeof(int))
                    {
                        var i = (int)(val ?? 0);
                        var ni = EditorGUILayout.IntField(i);
                        if (ni != i) valueProp.SetValue(prop, ni);
                    }
                    else if (t == typeof(float))
                    {
                        var f = (float)(val ?? 0f);
                        var nf = EditorGUILayout.FloatField(f);
                        if (!Mathf.Approximately(nf, f)) valueProp.SetValue(prop, nf);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(val?.ToString() ?? "null", EditorStyles.miniLabel);
                    }
                }
                catch (Exception ex)
                {
                    EditorGUILayout.LabelField($"(error: {ex.Message})", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        void HighlightElement(VisualElement el)
        {
            if (el == null) return;
            ClearHighlight();
            _highlighted = el;
            _hlOldColor = el.style.borderTopColor;
            _hlOldWidthT = el.style.borderTopWidth;
            _hlOldWidthB = el.style.borderBottomWidth;
            _hlOldWidthL = el.style.borderLeftWidth;
            _hlOldWidthR = el.style.borderRightWidth;
            var c = new Color(1f, 0.45f, 0.1f);
            el.style.borderTopColor = c;
            el.style.borderBottomColor = c;
            el.style.borderLeftColor = c;
            el.style.borderRightColor = c;
            el.style.borderTopWidth = 2;
            el.style.borderBottomWidth = 2;
            el.style.borderLeftWidth = 2;
            el.style.borderRightWidth = 2;
            _hlClearAt = EditorApplication.timeSinceStartup + 1.6;
        }

        void ClearHighlight()
        {
            if (_highlighted == null) return;
            try
            {
                _highlighted.style.borderTopColor = _hlOldColor;
                _highlighted.style.borderBottomColor = _hlOldColor;
                _highlighted.style.borderLeftColor = _hlOldColor;
                _highlighted.style.borderRightColor = _hlOldColor;
                _highlighted.style.borderTopWidth = _hlOldWidthT;
                _highlighted.style.borderBottomWidth = _hlOldWidthB;
                _highlighted.style.borderLeftWidth = _hlOldWidthL;
                _highlighted.style.borderRightWidth = _hlOldWidthR;
            }
            catch { /* element may be dead */ }
            _highlighted = null;
        }

        void RefreshTree(bool preserveSelection = false)
        {
            var root = SusInspectorTree.FindActiveRoot();
            if (root == null)
            {
                _nodes = new List<SusInspectorTree.Node>();
                _selected = null;
                _inspectStatus = EditorApplication.isPlaying
                    ? "No UIDocument root in scene"
                    : "Not in Play — no live tree";
                return;
            }
            var oldSelected = preserveSelection ? _selected : null;
            _nodes = SusInspectorTree.Flatten(root, _maxDepth);
            _selected = oldSelected != null && _nodes.Any(n => ReferenceEquals(n.Element, oldSelected))
                ? oldSelected
                : null;
            var (el, comp, depth) = SusInspectorTree.Stats(root);
            _inspectStatus = $"{el} elements · {comp} SusComponents · depth {depth}";
        }

    }
}
