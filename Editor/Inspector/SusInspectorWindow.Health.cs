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
        // ═══ Health — filterable issue triage ══════════════════════

        void DrawHealth()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run all checks", GUILayout.Width(110)))
                _health = SusHealthRunner.Run(echoToConsole: true, forceEcho: true);
            if (_health != null && GUILayout.Button("Copy report", GUILayout.Width(90)))
            {
                EditorGUIUtility.systemCopyBuffer = _health.FormatPlain();
                Debug.Log("[SUS Health] Report copied to clipboard.");
            }
            if (_health != null && GUILayout.Button("Echo to Console", GUILayout.Width(110)))
                SusHealthRunner.EchoToUnityConsole(_health, force: true);
            GUILayout.FlexibleSpace();
            var auto = EditorPrefs.GetBool(PrefAutoHealth, true);
            var auto2 = GUILayout.Toggle(auto, "Auto on focus", GUILayout.Width(100));
            if (auto2 != auto) EditorPrefs.SetBool(PrefAutoHealth, auto2);
            EditorGUILayout.EndHorizontal();

            if (_health == null)
            {
                EditorGUILayout.HelpBox("No report yet — click Run all checks.", MessageType.Info);
                return;
            }

            // Severity filter row with counts.
            var filter = EditorPrefs.GetInt(PrefHealthFilter, 0b0111); // errors+warnings visible, info hidden
            EditorGUILayout.BeginHorizontal();
            filter = FilterToggle(filter, 0, $"✕ Errors ({_health.Blocking + _health.Errors})", ColErr);
            filter = FilterToggle(filter, 1, $"▲ Warnings ({_health.Warnings})", ColWarn);
            filter = FilterToggle(filter, 2, $"● Info ({_health.Infos})", ColMuted);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"ran {_health.RanAtUtc.ToLocalTime():HH:mm:ss}",
                EditorStyles.miniLabel, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();
            EditorPrefs.SetInt(PrefHealthFilter, filter);

            if (_health.IsHealthy && _health.Warnings == 0)
                EditorGUILayout.HelpBox("● Healthy — no errors or warnings.", MessageType.Info);

            // Group by category prefix "[Cat] ...".
            var groups = _health.Issues
                .Where(i => SeverityVisible(i.Severity, filter))
                .GroupBy(CategoryOf);

            foreach (var g in groups)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(g.Key, EditorStyles.boldLabel);
                foreach (var issue in g)
                    DrawIssueCard(issue);
            }
        }

        static int FilterToggle(int mask, int bit, string label, Color color)
        {
            var on = (mask & (1 << bit)) != 0;
            var style = new GUIStyle(EditorStyles.miniButton);
            if (on) style.normal.textColor = color;
            var next = GUILayout.Toggle(on, label, style, GUILayout.Width(120));
            return next ? mask | (1 << bit) : mask & ~(1 << bit);
        }

        static bool SeverityVisible(SusIssueSeverity s, int mask) => s switch
        {
            SusIssueSeverity.Blocking or SusIssueSeverity.Error => (mask & 1) != 0,
            SusIssueSeverity.Warning => (mask & 2) != 0,
            _ => (mask & 4) != 0,
        };

        static string CategoryOf(SusHealthIssue i)
        {
            var t = i.Title ?? "";
            if (t.StartsWith("["))
            {
                var end = t.IndexOf(']');
                if (end > 1) return t.Substring(1, end - 1);
            }
            return i.Source ?? "Other";
        }

        void DrawIssueCard(SusHealthIssue issue)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            var (icon, color) = issue.Severity switch
            {
                SusIssueSeverity.Blocking or SusIssueSeverity.Error => ("✕", ColErr),
                SusIssueSeverity.Warning => ("▲", ColWarn),
                _ => ("●", ColMuted),
            };
            var iconStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = color } };
            GUILayout.Label(icon, iconStyle, GUILayout.Width(16));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(StripCategory(issue.Title), EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrEmpty(issue.Detail) && issue.Detail != issue.Title)
                EditorGUILayout.LabelField(issue.Detail, EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(issue.SuggestedFix))
            {
                var fixStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColAccent } };
                EditorGUILayout.LabelField("→ " + issue.SuggestedFix, fixStyle);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.Width(64));
            if (issue.FixAction != null && GUILayout.Button("Fix", GUILayout.Width(60)))
                issue.FixAction();
            if (GUILayout.Button("Copy", GUILayout.Width(60)))
            {
                var line = issue.Title;
                if (!string.IsNullOrEmpty(issue.SuggestedFix))
                    line += "\n→ " + issue.SuggestedFix;
                EditorGUIUtility.systemCopyBuffer = line;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        static string StripCategory(string title)
        {
            if (string.IsNullOrEmpty(title) || !title.StartsWith("[")) return title;
            var end = title.IndexOf(']');
            return end > 0 && end + 1 < title.Length ? title.Substring(end + 1).TrimStart() : title;
        }

    }
}
