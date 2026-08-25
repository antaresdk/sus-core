using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Theme authoring tool over the EXISTING plain-text token USS (roadmap 4.4).
    /// Menu: <c>Window → SUS → Theme Editor</c>.
    ///
    /// Design constraint (explicit): it introduces NO new format. It reads/edits the
    /// existing <c>_palette.uss</c> (L1 <c>--base-*</c>) and <c>_theme.uss</c> (L2
    /// <c>--thm-*</c>) — or any hand-authored override <c>.uss</c> — and writes back
    /// <b>surgically</b>: only the lines whose value the user actually changed are
    /// rewritten. Every other line (comments, alignment, ordering) is preserved
    /// byte-for-byte, so the file stays fully hand-editable and produces minimal diffs.
    ///
    /// Features: swatch preview + colour picker for <c>rgb/rgba/#hex/var()</c> values,
    /// var() chain resolution (across palette+theme), WCAG contrast checker, and a
    /// hover/pressed variant generator that just appends normal <c>--*: …;</c> lines.
    /// </summary>
    public sealed class SusThemeEditorWindow : EditorWindow
    {
        private const string PaletteName = "_palette.uss";
        private const string ThemeName = "_theme.uss";

        private string _path;
        private string _newline = "\n";
        private List<string> _lines = new();
        private readonly List<UssVar> _vars = new();
        private readonly Dictionary<string, string> _resolveMap = new(StringComparer.Ordinal);
        private bool _dirty;
        private Vector2 _scroll;
        private string _filter = "";

        // Contrast / preview pickers (variable names).
        private string _fgVar = "";
        private string _bgVar = "";
        private string _accentVar = "";

        // Hover/pressed generator.
        private string _hpSource = "";
        private float _hpAmount = 0.15f;

        [MenuItem("Window/SUS/Theme Editor", priority = 2)]
        public static void Open()
        {
            var w = GetWindow<SusThemeEditorWindow>(false, "SUS Theme", true);
            w.minSize = new Vector2(520, 620);
            if (string.IsNullOrEmpty(w._path))
                w.TryOpenDefault(PaletteName);
            w.Show();
        }

        // ─── File open / load ────────────────────────────────────────────

        private void TryOpenDefault(string fileName)
        {
            var found = FindTokenFile(fileName);
            if (found != null) Load(found);
        }

        private static string FindTokenFile(string fileName)
        {
            // Prefer a project Assets copy, else the package source.
            var guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(fileName));
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(p);
            }
            // Fallback: scan the packages/submodule folder on disk.
            var root = Directory.GetCurrentDirectory();
            foreach (var baseDir in new[] { "Packages", "Assets", ".." })
            {
                var dir = Path.Combine(root, baseDir);
                if (!Directory.Exists(dir)) continue;
                var hit = Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault(f => f.Replace("\\", "/").Contains("Resources/SusRuntime"));
                if (hit != null) return Path.GetFullPath(hit);
            }
            return null;
        }

        private void Load(string fullPath)
        {
            if (_dirty && !EditorUtility.DisplayDialog("Unsaved changes",
                    "Discard unsaved theme changes?", "Discard", "Cancel"))
                return;

            _path = fullPath;
            var text = File.ReadAllText(fullPath);
            _newline = text.Contains("\r\n") ? "\r\n" : "\n";
            _lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            Parse();
            BuildResolveMap();
            _dirty = false;
        }

        private void Parse()
        {
            _vars.Clear();
            string scope = "";
            var pendingSelector = new StringBuilder();
            bool inBlockComment = false;

            var declRe = new Regex(@"^(?<prefix>\s*(?<name>--[A-Za-z0-9_-]+)\s*:\s*)(?<value>[^;]*?)(?<suffix>\s*;.*)$");

            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var trimmed = line.Trim();

                if (inBlockComment)
                {
                    if (trimmed.Contains("*/")) inBlockComment = false;
                    continue;
                }
                if (trimmed.StartsWith("/*") && !trimmed.Contains("*/")) { inBlockComment = true; continue; }
                if (trimmed.StartsWith("/*") && trimmed.EndsWith("*/")) continue;
                if (trimmed.Length == 0) continue;

                if (trimmed.Contains("{"))
                {
                    var before = trimmed.Substring(0, trimmed.IndexOf('{')).Trim();
                    pendingSelector.Append(before);
                    scope = pendingSelector.ToString().Trim();
                    pendingSelector.Clear();
                    continue;
                }
                if (trimmed.Contains("}")) { scope = ""; pendingSelector.Clear(); continue; }

                var m = declRe.Match(line);
                if (m.Success)
                {
                    _vars.Add(new UssVar
                    {
                        Name = m.Groups["name"].Value,
                        Prefix = m.Groups["prefix"].Value,
                        Suffix = m.Groups["suffix"].Value,
                        Original = m.Groups["value"].Value.Trim(),
                        Edited = m.Groups["value"].Value.Trim(),
                        LineIndex = i,
                        Scope = string.IsNullOrEmpty(scope) ? ":root" : scope,
                    });
                }
                else
                {
                    pendingSelector.Append(trimmed).Append(' ');
                }
            }
        }

        // Resolution map merges THIS file with palette+theme so var() chains resolve.
        private void BuildResolveMap()
        {
            _resolveMap.Clear();
            foreach (var v in _vars) _resolveMap[v.Name] = v.Edited;

            foreach (var fn in new[] { PaletteName, ThemeName })
            {
                var p = FindTokenFile(fn);
                if (p == null || p == _path) continue;
                try
                {
                    foreach (var raw in File.ReadAllLines(p))
                    {
                        var m = Regex.Match(raw, @"^\s*(--[A-Za-z0-9_-]+)\s*:\s*([^;]*?)\s*;");
                        if (m.Success && !_resolveMap.ContainsKey(m.Groups[1].Value))
                            _resolveMap[m.Groups[1].Value] = m.Groups[2].Value.Trim();
                    }
                }
                catch { /* ignore unreadable */ }
            }
        }

        // ─── GUI ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            DrawToolbar();

            if (_vars.Count == 0)
            {
                EditorGUILayout.HelpBox("Open a token USS file (_palette.uss, _theme.uss) or any " +
                    "hand-authored override .uss with --var declarations.", MessageType.Info);
                return;
            }

            DrawPreviewAndContrast();
            DrawHoverPressed();

            EditorGUILayout.Space(4);
            _filter = EditorGUILayout.TextField("Filter", _filter);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var group in _vars.GroupBy(v => v.Scope))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);
                foreach (var v in group)
                {
                    if (!string.IsNullOrEmpty(_filter) &&
                        v.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    DrawVarRow(v);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Palette", EditorStyles.toolbarButton, GUILayout.Width(64)))
                    TryOpenDefault(PaletteName);
                if (GUILayout.Button("Theme", EditorStyles.toolbarButton, GUILayout.Width(64)))
                    TryOpenDefault(ThemeName);
                if (GUILayout.Button("Open…", EditorStyles.toolbarButton, GUILayout.Width(64)))
                {
                    var p = EditorUtility.OpenFilePanel("Open token USS", Application.dataPath, "uss");
                    if (!string.IsNullOrEmpty(p)) Load(p);
                }
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(64)) && _path != null)
                    Load(_path);

                GUILayout.FlexibleSpace();
                var label = _path == null ? "(no file)" : Path.GetFileName(_path) + (_dirty ? " *" : "");
                GUILayout.Label(label, EditorStyles.miniLabel);

                using (new EditorGUI.DisabledScope(!_dirty))
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(64)))
                        Save();
            }
        }

        private void DrawVarRow(UssVar v)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(v.Name, GUILayout.Width(220));

                if (TryResolveColor(v.Edited, out var color, out var fmtHadAlpha))
                {
                    // Swatch (resolved, follows var() chains).
                    var rect = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18));
                    EditorGUI.DrawRect(rect, color);

                    bool isDirectColor = IsDirectColor(v.Edited);
                    if (isDirectColor)
                    {
                        EditorGUI.BeginChangeCheck();
                        var picked = EditorGUILayout.ColorField(GUIContent.none, color, true, true, false,
                            GUILayout.Width(60));
                        if (EditorGUI.EndChangeCheck())
                        {
                            v.Edited = EmitColor(picked, v.Edited, fmtHadAlpha);
                            OnEdited();
                        }
                    }
                    else
                    {
                        GUILayout.Label("→ var()", EditorStyles.miniLabel, GUILayout.Width(60));
                    }

                    EditorGUI.BeginChangeCheck();
                    var txt = EditorGUILayout.TextField(v.Edited);
                    if (EditorGUI.EndChangeCheck()) { v.Edited = txt; OnEdited(); }
                }
                else
                {
                    GUILayout.Space(22);
                    EditorGUI.BeginChangeCheck();
                    var txt = EditorGUILayout.TextField(v.Edited);
                    if (EditorGUI.EndChangeCheck()) { v.Edited = txt; OnEdited(); }
                }

                using (new EditorGUI.DisabledScope(v.Edited == v.Original))
                    if (GUILayout.Button("↺", GUILayout.Width(22)))
                    {
                        v.Edited = v.Original;
                        OnEdited();
                    }
            }
        }

        private void DrawPreviewAndContrast()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Preview & Contrast", EditorStyles.boldLabel);
                var names = _vars.Select(v => v.Name).ToArray();

                _bgVar = PickerRow("Background", _bgVar, names);
                _fgVar = PickerRow("Text", _fgVar, names);
                _accentVar = PickerRow("Accent", _accentVar, names);

                bool hasBg = TryResolveColor(GetVarValue(_bgVar), out var bg, out _);
                bool hasFg = TryResolveColor(GetVarValue(_fgVar), out var fg, out _);
                bool hasAccent = TryResolveColor(GetVarValue(_accentVar), out var accent, out _);

                if (hasBg)
                {
                    var box = GUILayoutUtility.GetRect(0, 54, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(box, bg);
                    if (hasAccent)
                    {
                        var btn = new Rect(box.x + 12, box.y + 14, 96, 26);
                        EditorGUI.DrawRect(btn, accent);
                    }
                    if (hasFg)
                    {
                        var style = new GUIStyle(EditorStyles.label) { normal = { textColor = fg } };
                        GUI.Label(new Rect(box.x + 120, box.y + 8, box.width - 130, 20), "The quick brown fox", style);
                        GUI.Label(new Rect(box.x + 120, box.y + 28, box.width - 130, 20), "0123456789", style);
                    }
                }

                if (hasBg && hasFg)
                {
                    float ratio = ContrastRatio(fg, bg);
                    string verdict = ratio >= 7f ? "AAA" : ratio >= 4.5f ? "AA" : ratio >= 3f ? "AA Large" : "FAIL";
                    var prev = GUI.color;
                    GUI.color = ratio >= 4.5f ? new Color(0.4f, 0.8f, 0.4f)
                              : ratio >= 3f ? new Color(0.9f, 0.8f, 0.3f) : new Color(0.9f, 0.4f, 0.4f);
                    EditorGUILayout.LabelField($"Contrast {ratio:0.00}:1  →  {verdict}", EditorStyles.boldLabel);
                    GUI.color = prev;
                }
            }
        }

        private void DrawHoverPressed()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Generate hover / pressed", EditorStyles.boldLabel);
                var colorVars = _vars.Where(v => IsDirectColor(v.Edited)).Select(v => v.Name).ToArray();
                if (colorVars.Length == 0)
                {
                    EditorGUILayout.LabelField("No direct rgb/rgba/#hex variables in this file " +
                        "(alias files hold var() references).", EditorStyles.miniLabel);
                    return;
                }

                _hpSource = PickerRow("Source colour", _hpSource, colorVars);
                _hpAmount = EditorGUILayout.Slider("Amount", _hpAmount, 0.02f, 0.5f);

                var src = _vars.FirstOrDefault(v => v.Name == _hpSource);
                if (src != null && TryResolveColor(src.Edited, out var c, out var hadAlpha))
                {
                    var hover = Lerp(c, Color.white, _hpAmount);
                    var pressed = Lerp(c, Color.black, _hpAmount);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Swatch("hover", hover);
                        Swatch("pressed", pressed);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Append " + src.Name + "Hover/Pressed", GUILayout.Height(20)))
                            AppendHoverPressed(src, hover, pressed, hadAlpha);
                    }
                }
            }
        }

        private static void Swatch(string label, Color c)
        {
            GUILayout.Label(label, GUILayout.Width(52));
            var r = GUILayoutUtility.GetRect(20, 16, GUILayout.Width(20));
            EditorGUI.DrawRect(r, c);
        }

        private string PickerRow(string label, string current, string[] names)
        {
            int idx = Math.Max(0, Array.IndexOf(names, current));
            int picked = EditorGUILayout.Popup(label, idx, names);
            return names.Length > 0 ? names[Mathf.Clamp(picked, 0, names.Length - 1)] : "";
        }

        // ─── Edit / save ─────────────────────────────────────────────────

        private void OnEdited()
        {
            _dirty = _vars.Any(v => v.Edited != v.Original);
            BuildResolveMap();
            Repaint();
        }

        private void AppendHoverPressed(UssVar src, Color hover, Color pressed, bool hadAlpha)
        {
            var indent = new string(' ', src.Prefix.Length - src.Prefix.TrimStart().Length);
            var hoverLine = $"{indent}{src.Name}Hover: {EmitColor(hover, src.Edited, hadAlpha)};";
            var pressedLine = $"{indent}{src.Name}Pressed: {EmitColor(pressed, src.Edited, hadAlpha)};";

            // Insert right after the source line, then re-parse so indices/rows update.
            _lines.Insert(src.LineIndex + 1, pressedLine);
            _lines.Insert(src.LineIndex + 1, hoverLine);
            Parse();
            BuildResolveMap();
            _dirty = true;
            Repaint();
        }

        private void Save()
        {
            if (_path == null) return;

            // Surgical: rewrite ONLY changed lines; everything else stays byte-identical.
            foreach (var v in _vars)
            {
                if (v.Edited == v.Original) continue;
                _lines[v.LineIndex] = v.Prefix + v.Edited + v.Suffix;
                v.Original = v.Edited;
            }

            File.WriteAllText(_path, string.Join(_newline, _lines), new UTF8Encoding(false));
            _dirty = false;

            // Rescan so Unity reimports the changed USS (works for Assets/ and file: packages
            // alike — no fragile full-path → AssetDatabase-path mapping needed).
            AssetDatabase.Refresh();

            Debug.Log($"[SusTheme] Saved {Path.GetFileName(_path)} (changed lines only).");
        }

        // ─── Colour helpers ──────────────────────────────────────────────

        private string GetVarValue(string name) =>
            _vars.FirstOrDefault(v => v.Name == name)?.Edited ?? "";

        private static bool IsDirectColor(string value)
        {
            value = value.Trim();
            return value.StartsWith("rgb(") || value.StartsWith("rgba(") || value.StartsWith("#");
        }

        private bool TryResolveColor(string value, out Color color, out bool hadAlpha)
            => TryResolveColor(value, out color, out hadAlpha, 0);

        private bool TryResolveColor(string value, out Color color, out bool hadAlpha, int depth)
        {
            color = Color.magenta; hadAlpha = false;
            if (string.IsNullOrEmpty(value) || depth > 8) return false;
            value = value.Trim();

            var varM = Regex.Match(value, @"^var\(\s*(--[A-Za-z0-9_-]+)");
            if (varM.Success)
                return _resolveMap.TryGetValue(varM.Groups[1].Value, out var inner)
                    && TryResolveColor(inner, out color, out hadAlpha, depth + 1);

            return TryParseDirectColor(value, out color, out hadAlpha);
        }

        private static bool TryParseDirectColor(string value, out Color color, out bool hadAlpha)
        {
            color = Color.magenta; hadAlpha = false;
            value = value.Trim();

            var rgb = Regex.Match(value, @"rgba?\(\s*([0-9.]+)\s*,\s*([0-9.]+)\s*,\s*([0-9.]+)\s*(?:,\s*([0-9.]+)\s*)?\)");
            if (rgb.Success)
            {
                float r = ParseF(rgb.Groups[1].Value) / 255f;
                float g = ParseF(rgb.Groups[2].Value) / 255f;
                float b = ParseF(rgb.Groups[3].Value) / 255f;
                float a = rgb.Groups[4].Success ? ParseF(rgb.Groups[4].Value) : 1f;
                hadAlpha = rgb.Groups[4].Success;
                color = new Color(r, g, b, a);
                return true;
            }

            if (value.StartsWith("#") && ColorUtility.TryParseHtmlString(value, out color))
            {
                hadAlpha = value.Length == 9 || value.Length == 5;
                return true;
            }
            return false;
        }

        private static string EmitColor(Color c, string originalValue, bool hadAlpha)
        {
            originalValue = originalValue.Trim();
            int r = Mathf.RoundToInt(c.r * 255f);
            int g = Mathf.RoundToInt(c.g * 255f);
            int b = Mathf.RoundToInt(c.b * 255f);

            if (originalValue.StartsWith("#"))
                return "#" + ColorUtility.ToHtmlStringRGB(c);

            bool useAlpha = hadAlpha || c.a < 0.999f || originalValue.StartsWith("rgba(");
            if (useAlpha)
                return $"rgba({r}, {g}, {b}, {c.a.ToString("0.###", CultureInfo.InvariantCulture)})";
            return $"rgb({r}, {g}, {b})";
        }

        private static float ParseF(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

        private static Color Lerp(Color a, Color b, float t) =>
            new Color(Mathf.Lerp(a.r, b.r, t), Mathf.Lerp(a.g, b.g, t), Mathf.Lerp(a.b, b.b, t), a.a);

        // WCAG 2.1 relative-luminance contrast ratio.
        private static float ContrastRatio(Color a, Color b)
        {
            float la = Luminance(a) + 0.05f;
            float lb = Luminance(b) + 0.05f;
            return la > lb ? la / lb : lb / la;
        }

        private static float Luminance(Color c)
        {
            float R = Channel(c.r), G = Channel(c.g), B = Channel(c.b);
            return 0.2126f * R + 0.7152f * G + 0.0722f * B;
        }

        private static float Channel(float c) =>
            c <= 0.03928f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        // ─── Model ───────────────────────────────────────────────────────

        private sealed class UssVar
        {
            public string Name;
            public string Prefix;   // indentation + "name:" + alignment spaces
            public string Suffix;   // ";" + optional trailing comment
            public string Original; // trimmed value as loaded
            public string Edited;   // trimmed value being edited
            public int LineIndex;
            public string Scope;
        }
    }
}
