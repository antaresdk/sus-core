using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sharq.Core.Editor.DesignImport
{
    /// <summary>
    /// Editor UX for ARCH-DESIGN-IMPORT §5.2 / §7.1c:
    /// file picker → dry-run unified diff → Apply. Same parser as CLI Tools~/SusDesignImport.
    /// No network / secrets without an explicit user token (this window never calls the network).
    /// </summary>
    public sealed class SusDesignImportWindow : EditorWindow
    {
        const string MenuPath = "Window/SUS/Import Design Tokens…";
        const int MenuPriority = 55;

        string _jsonPath = "";
        string _outDir = DesignImportPreview.DefaultOutDirAssets;
        bool _downstream;
        bool _emitUnknown;
        Vector2 _diffScroll;
        string _diffText = "";
        string _status = "";
        MessageType _statusType = MessageType.None;
        DesignImportPreview.PreviewResult _lastPreview;
        string _cachedJson;
        GUIStyle _monoStyle;

        [MenuItem(MenuPath, false, MenuPriority)]
        public static void Open()
        {
            var w = GetWindow<SusDesignImportWindow>(true, "SUS — Import Design Tokens", true);
            w.minSize = new Vector2(560, 480);
            w.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Import Design Tokens", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Imports a Pixso/Figma-like JSON export into an override USS sheet " +
                "(imported-tokens.uss). Does not patch shipped design-tokens.uss. " +
                "Same pipeline as the SusDesignImport CLI. Local files only — no network.",
                MessageType.Info);

            DrawSource();
            EditorGUILayout.Space(4);
            DrawOptions();
            EditorGUILayout.Space(8);
            DrawActions();
            EditorGUILayout.Space(6);
            DrawStatus();
            EditorGUILayout.Space(4);
            DrawDiff();
        }

        void DrawSource()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _jsonPath = EditorGUILayout.TextField(
                    new GUIContent("Export JSON", "Local sus-design/v1, DTCG, or Tokens Studio JSON."),
                    _jsonPath);
                if (GUILayout.Button("Browse…", GUILayout.Width(80)))
                {
                    var picked = EditorUtility.OpenFilePanel(
                        "Select design export JSON",
                        string.IsNullOrEmpty(_jsonPath) ? "" : Path.GetDirectoryName(_jsonPath),
                        "json");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        _jsonPath = picked.Replace('\\', '/');
                        _cachedJson = null;
                        _lastPreview = null;
                        _diffText = "";
                        ClearStatus();
                    }
                }
            }
        }

        void DrawOptions()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _outDir = EditorGUILayout.TextField(
                    new GUIContent("Out folder", "Project folder for imported-tokens.uss + .sus-design-meta.json."),
                    _outDir);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    var start = Directory.Exists(_outDir)
                        ? Path.GetFullPath(_outDir)
                        : Application.dataPath;
                    var picked = EditorUtility.OpenFolderPanel("Import output folder", start, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        _outDir = ToProjectRelative(picked);
                        _lastPreview = null;
                        _diffText = "";
                    }
                }
            }

            _downstream = EditorGUILayout.Toggle(
                new GUIContent("Include downstream aliases",
                    "Opt-in --sk-* alias rows from the map (no paid package names in core)."),
                _downstream);
            _emitUnknown = EditorGUILayout.Toggle(
                new GUIContent("Emit unknown as --app-*",
                    "Allow aliases missing from the map (writes --app-* vars)."),
                _emitUnknown);
        }

        void DrawActions()
        {
            var canRun = !string.IsNullOrWhiteSpace(_jsonPath) && File.Exists(_jsonPath)
                         && !string.IsNullOrWhiteSpace(_outDir);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canRun))
                {
                    if (GUILayout.Button("Preview (dry-run)", GUILayout.Height(28)))
                        RunPreview();
                }

                var canApply = canRun && _lastPreview != null && _lastPreview.Ok;
                using (new EditorGUI.DisabledScope(!canApply))
                {
                    if (GUILayout.Button("Apply", GUILayout.Height(28)))
                        RunApply();
                }
            }
        }

        void DrawStatus()
        {
            if (_statusType == MessageType.None && string.IsNullOrEmpty(_status))
                return;
            EditorGUILayout.HelpBox(_status, _statusType == MessageType.None ? MessageType.Info : _statusType);
        }

        void DrawDiff()
        {
            EditorGUILayout.LabelField("Unified diff", EditorStyles.boldLabel);
            if (_monoStyle == null)
            {
                _monoStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = false,
                    richText = false
                };
            }

            _diffScroll = EditorGUILayout.BeginScrollView(_diffScroll, GUILayout.ExpandHeight(true));
            var display = string.IsNullOrEmpty(_diffText)
                ? "(run Preview to see dry-run diff against existing imported-tokens.uss)"
                : _diffText;
            // Read-only preview: SelectableLabel keeps selection without inviting edits.
            EditorGUILayout.SelectableLabel(display, _monoStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void RunPreview()
        {
            try
            {
                var json = ReadJson();
                var opts = BuildOptions(dryRun: true);
                _lastPreview = DesignImportPreview.Preview(json, opts);

                if (!_lastPreview.Ok)
                {
                    var errs = _lastPreview.Import != null
                        ? string.Join("\n", _lastPreview.Import.Errors)
                        : "import failed";
                    SetStatus("Preview failed:\n" + errs, MessageType.Error);
                    _diffText = "";
                    return;
                }

                if (_lastPreview.Unchanged)
                {
                    _diffText = "(no USS changes — re-import would be identical)";
                    SetStatus(
                        $"Dry-run OK — {_lastPreview.Import.Mapped.Count} mapped, " +
                        $"{_lastPreview.Import.Skipped.Count} skipped. USS unchanged.",
                        MessageType.Info);
                }
                else
                {
                    _diffText = _lastPreview.UnifiedDiff;
                    var kind = _lastPreview.HasExisting ? "update" : "create";
                    SetStatus(
                        $"Dry-run OK ({kind}) — {_lastPreview.Import.Mapped.Count} mapped, " +
                        $"{_lastPreview.Import.Skipped.Count} skipped. Review diff, then Apply.",
                        MessageType.Info);
                }

                foreach (var w in _lastPreview.Import.Warnings)
                    Debug.LogWarning("[SusDesignImport] " + w);
            }
            catch (Exception ex)
            {
                SetStatus("Preview error: " + ex.Message, MessageType.Error);
                Debug.LogException(ex);
            }
        }

        void RunApply()
        {
            try
            {
                // Re-preview to ensure diff matches what we write
                var json = ReadJson();
                var previewOpts = BuildOptions(dryRun: true);
                var preview = DesignImportPreview.Preview(json, previewOpts);
                if (!preview.Ok)
                {
                    SetStatus("Apply blocked — validation failed:\n" +
                              string.Join("\n", preview.Import.Errors), MessageType.Error);
                    return;
                }

                if (!EditorUtility.DisplayDialog(
                        "Apply design import",
                        $"Write override USS to:\n{preview.UssPath}\n\n" +
                        "Shipped design-tokens.uss will NOT be modified.",
                        "Apply", "Cancel"))
                    return;

                var applyOpts = BuildOptions(dryRun: false);
                var result = DesignImportPreview.Apply(json, applyOpts);
                if (!result.Ok)
                {
                    SetStatus("Apply failed:\n" + string.Join("\n", result.Errors), MessageType.Error);
                    return;
                }

                AssetDatabase.Refresh();
                _lastPreview = preview;
                _diffText = preview.Unchanged
                    ? "(no USS changes — files refreshed)"
                    : preview.UnifiedDiff;

                SetStatus(
                    $"Applied — wrote {applyOpts.UssFileName} + {applyOpts.MetaFileName} under {_outDir}.\n" +
                    $"Wire with SusApp.UseCustomStyles(\"…\") pointing at the USS (without extension).",
                    MessageType.Info);
                Debug.Log($"[SusDesignImport] wrote {preview.UssPath}");
            }
            catch (Exception ex)
            {
                SetStatus("Apply error: " + ex.Message, MessageType.Error);
                Debug.LogException(ex);
            }
        }

        ImportOptions BuildOptions(bool dryRun)
        {
            return new ImportOptions
            {
                OutDir = DesignImportPreview.ResolveOutDir(_outDir),
                DryRun = dryRun,
                Downstream = _downstream,
                EmitUnknown = _emitUnknown,
                AliasMapPath = AliasMap.ResolveAliasMapPath()
            };
        }

        string ReadJson()
        {
            if (_cachedJson != null && File.Exists(_jsonPath))
            {
                // Re-read if file may have changed — always fresh for Apply safety
            }
            _cachedJson = File.ReadAllText(_jsonPath, Encoding.UTF8);
            return _cachedJson;
        }

        void SetStatus(string text, MessageType type)
        {
            _status = text;
            _statusType = type;
        }

        void ClearStatus()
        {
            _status = "";
            _statusType = MessageType.None;
        }

        static string ToProjectRelative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return DesignImportPreview.DefaultOutDirAssets;
            absolutePath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            var data = Application.dataPath.Replace('\\', '/');
            var project = data.EndsWith("/Assets", StringComparison.OrdinalIgnoreCase)
                ? data.Substring(0, data.Length - "/Assets".Length)
                : Directory.GetCurrentDirectory().Replace('\\', '/');

            if (absolutePath.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase))
                return absolutePath.Substring(project.Length + 1);

            // Outside project — keep absolute (Import still writes via File API)
            return absolutePath;
        }
    }
}
