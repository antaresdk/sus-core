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
        // ═══ Overview — "what's my status and what do I do next" ═══

        void DrawOverview()
        {
            DrawHeroStatus();

            // Next steps — the top actionable items, not a wall of labels.
            var steps = BuildNextSteps();
            if (steps.Count > 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Next steps", EditorStyles.boldLabel);
                foreach (var (title, detail, severity, action, actionLabel) in steps)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    var c = severity == SusIssueSeverity.Warning ? ColWarn : ColErr;
                    var st = new GUIStyle(EditorStyles.label) { normal = { textColor = c }, fontStyle = FontStyle.Bold };
                    GUILayout.Label(severity == SusIssueSeverity.Warning ? "▲" : "✕", st, GUILayout.Width(16));
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(title, EditorStyles.wordWrappedLabel);
                    if (!string.IsNullOrEmpty(detail))
                        EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.EndVertical();
                    if (action != null && GUILayout.Button(actionLabel, GUILayout.Width(130), GUILayout.Height(22)))
                        action();
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Project", EditorStyles.boldLabel);
            var cfg = SusConfig.Instance;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PathRow("Sharq sources", cfg.SharqDirectory);
                PathRow("Generated", cfg.GeneratedDirectory);
                PathRow("Resources", cfg.ResourcesDirectory);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var p in SusPackageRegistry.Packages)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{p.displayName}", GUILayout.Width(180));
                    EditorGUILayout.LabelField(p.PackageName, EditorStyles.miniLabel);
                    var stale = _staleByPackage.TryGetValue(p.PackageName, out var s) && s;
                    if (stale)
                    {
                        Pill("STALE", ColWarn);
                        if (GUILayout.Button("Generate", GUILayout.Width(80)))
                        {
                            SusPackageGenerator.Generate(p);
                            RescanStale();
                        }
                    }
                    else
                    {
                        Pill(p.watch ? "watch" : "fresh", ColOk);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (SusPackageRegistry.Packages.Count == 0)
                    EditorGUILayout.LabelField("(no Sharq packages registered)", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Quick actions", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Setup Wizard", GUILayout.Height(24)))
                SusSetupWizard.Open();
            if (GUILayout.Button("Validate Setup", GUILayout.Height(24)))
            {
                _health = SusHealthRunner.Run();
                OpenTab(SusInspectorTab.Health);
            }
            if (GUILayout.Button("Generate All", GUILayout.Height(24)))
            {
                SusPackageGenerator.GenerateAll();
                RescanStale();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Theme Editor", GUILayout.Height(24)))
                SusThemeEditorWindow.Open();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Copy UI tree", GUILayout.Height(24)))
                    CopyTreeToClipboard();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawHeroStatus()
        {
            string text;
            Color color;
            if (_health == null)
            {
                text = "Health not checked yet";
                color = ColMuted;
            }
            else if (_health.Blocking + _health.Errors > 0)
            {
                text = $"✕  {_health.Blocking + _health.Errors} error(s), {_health.Warnings} warning(s)";
                color = ColErr;
            }
            else if (_health.Warnings > 0)
            {
                text = $"▲  Working, {_health.Warnings} warning(s)";
                color = ColWarn;
            }
            else
            {
                text = "●  All good — SUS is healthy";
                color = ColOk;
            }

            var style = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = color },
                padding = new RectOffset(12, 12, 10, 10),
            };
            GUILayout.Label(text, style, GUILayout.MinHeight(38));
        }

        List<(string title, string detail, SusIssueSeverity sev, Action action, string label)> BuildNextSteps()
        {
            var steps = new List<(string, string, SusIssueSeverity, Action, string)>();
            if (_health != null)
            {
                foreach (var issue in _health.Issues)
                {
                    if (issue.Severity == SusIssueSeverity.Info) continue;
                    steps.Add((issue.Title, issue.SuggestedFix, issue.Severity,
                        issue.FixAction, "Fix"));
                    if (steps.Count >= 4) break;
                }
            }
            if (_staleCount > 0 && steps.Count < 5)
            {
                steps.Add(($"{_staleCount} package(s) have stale generated code",
                    ".sharq sources are newer than .g.cs — regenerate to apply changes",
                    SusIssueSeverity.Warning,
                    () => { SusPackageGenerator.GenerateAll(); RescanStale(); },
                    "Generate stale"));
            }
            return steps;
        }

        static void PathRow(string label, string path)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110));
            EditorGUILayout.LabelField(path ?? "(not set)", EditorStyles.miniLabel);
            var abs = AbsProjectPath(path);
            using (new EditorGUI.DisabledScope(abs == null))
            {
                if (GUILayout.Button("Open", GUILayout.Width(46)))
                    EditorUtility.RevealInFinder(abs);
            }
            EditorGUILayout.EndHorizontal();
        }

        static string AbsProjectPath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            try
            {
                var abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
                return Directory.Exists(abs) || File.Exists(abs) ? abs : null;
            }
            catch { return null; }
        }

        static bool IsPackageStale(SusPackageDescriptor d)
        {
            try
            {
                var genDir = d.AbsGeneratedDir;
                foreach (var srcDir in d.AbsSourceDirs)
                {
                    if (!Directory.Exists(srcDir)) continue;
                    foreach (var sharq in Directory.GetFiles(srcDir, "*.sharq", SearchOption.AllDirectories))
                    {
                        var genCs = Path.Combine(genDir, Path.GetFileNameWithoutExtension(sharq) + ".g.cs");
                        if (!File.Exists(genCs)) return true;
                        if (File.GetLastWriteTimeUtc(sharq) > File.GetLastWriteTimeUtc(genCs)) return true;
                    }
                }
            }
            catch { /* ignore */ }
            return false;
        }

        void CopyTreeToClipboard()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Inspect", "Enter Play mode to dump the live UI tree.", "OK");
                return;
            }
            var root = SusInspectorTree.FindActiveRoot();
            if (root == null)
            {
                EditorUtility.DisplayDialog("Inspect", "No UIDocument root found.", "OK");
                return;
            }
            EditorGUIUtility.systemCopyBuffer = SusInspectorTree.DumpTreeText(root, _maxDepth);
            _inspectStatus = "Tree copied to clipboard";
        }

    }
}
