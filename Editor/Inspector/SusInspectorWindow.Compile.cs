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
        // ═══ Compile — packages + hot reload log ═══════════════════

        void DrawCompile()
        {
            // Summary strip
            if (_staleCount > 0)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                var st = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColWarn } };
                GUILayout.Label($"⟳ {_staleCount} package(s) stale — sources newer than generated code", st);
                if (GUILayout.Button("Generate stale", GUILayout.Width(110)))
                {
                    foreach (var p in SusPackageRegistry.Packages)
                        if (_staleByPackage.TryGetValue(p.PackageName, out var s) && s)
                            SusPackageGenerator.Generate(p);
                    RescanStale();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh registry", GUILayout.Width(110)))
            {
                SusPackageRegistry.Refresh();
                RescanStale();
            }
            if (GUILayout.Button("Generate All", GUILayout.Width(100)))
            {
                SusPackageGenerator.GenerateAll();
                RescanStale();
            }
            EditorGUILayout.EndHorizontal();

            foreach (var p in SusPackageRegistry.Packages)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(p.displayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(p.PackageName, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                var stale = _staleByPackage.TryGetValue(p.PackageName, out var s) && s;
                Pill(stale ? "STALE" : "fresh", stale ? ColWarn : ColOk);
                Pill(p.watch ? "watch" : "manual", p.watch ? ColAccent : ColMuted);
                if (GUILayout.Button("Generate", GUILayout.Width(80)))
                {
                    SusPackageGenerator.Generate(p);
                    RescanStale();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Hot reload", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            Pill(RemoteHotReloadPushService.IsEnabled ? "Remote push ON" : "Remote push OFF",
                 RemoteHotReloadPushService.IsEnabled ? ColOk : ColMuted);
            Pill(SusConfig.Instance.HotReloadStatePreserve ? "State preserve ON" : "State preserve OFF",
                 SusConfig.Instance.HotReloadStatePreserve ? ColOk : ColMuted);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Play + style/template edit in a watched package → hot reload, no domain reload. " +
                "<script> changes defer until you exit Play.",
                MessageType.None);

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Event log", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
                s_log.Clear();
            EditorGUILayout.EndHorizontal();

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(180));
            for (int i = s_log.Count - 1; i >= 0; i--)
            {
                var e = s_log[i];
                var color = e.Kind switch
                {
                    "USS" => ColAccent,
                    "TMPL" => ColOk,
                    "DEL" => ColErr,
                    _ => ColMuted,
                };
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(e.Time.ToString("HH:mm:ss"), EditorStyles.miniLabel, GUILayout.Width(56));
                var kindStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = color } };
                GUILayout.Label(e.Kind, kindStyle, GUILayout.Width(40));
                GUILayout.Label(e.Message, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            if (s_log.Count == 0)
                EditorGUILayout.LabelField("(empty — save a .sharq style/template to see events)",
                    EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Generate Tag Schema"))
                EditorApplication.ExecuteMenuItem("Window/SUS/Sharq/Generate Tag Schema");
        }

    }
}
