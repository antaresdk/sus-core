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
        // ═══ Settings ══════════════════════════════════════════════

        void DrawSettings()
        {
            EditorGUILayout.LabelField("sus.config.json  (project)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                _cfgSharq = EditorGUILayout.TextField("SharqDirectory", _cfgSharq);
                _cfgGen = EditorGUILayout.TextField("GeneratedDirectory", _cfgGen);
                _cfgRes = EditorGUILayout.TextField("ResourcesDirectory", _cfgRes);
                _cfgValidation = EditorGUILayout.Toggle("EnableValidation", _cfgValidation);
                _cfgStrictKey = EditorGUILayout.Toggle("StrictVForKey", _cfgStrictKey);
                _cfgLogGen = EditorGUILayout.Toggle("LogGeneratedFiles", _cfgLogGen);
                _cfgStatePreserve = EditorGUILayout.Toggle("HotReloadStatePreserve", _cfgStatePreserve);
                if (EditorGUI.EndChangeCheck()) _cfgDirty = true;

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!_cfgDirty))
                {
                    if (GUILayout.Button("Save config"))
                    {
                        var c = new SusConfig
                        {
                            SharqDirectory = _cfgSharq,
                            GeneratedDirectory = _cfgGen,
                            ResourcesDirectory = _cfgRes,
                            EnableValidation = _cfgValidation,
                            StrictVForKey = _cfgStrictKey,
                            LogGeneratedFiles = _cfgLogGen,
                            HotReloadStatePreserve = _cfgStatePreserve,
                        };
                        SusConfig.Save(c);
                        _cfgDirty = false;
                        AssetDatabase.Refresh();
                    }
                }
                if (GUILayout.Button("Reload"))
                {
                    SusConfig.Reload();
                    LoadSettingsDraft();
                }
                if (GUILayout.Button("Reveal in Explorer"))
                {
                    var p = SusConfig.ConfigFilePath;
                    if (File.Exists(p)) EditorUtility.RevealInFinder(p);
                    else EditorUtility.DisplayDialog("Config", $"File not found:\n{p}", "OK");
                }
                EditorGUILayout.EndHorizontal();
                if (_cfgDirty)
                    EditorGUILayout.LabelField("● unsaved changes", new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = ColWarn },
                    });
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Remote hot reload  (this machine)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _remoteEnabled = EditorGUILayout.Toggle("Enabled", _remoteEnabled);
                _remoteUrl = EditorGUILayout.TextField("Session MCP URL", _remoteUrl);
                if (GUILayout.Button("Save", GUILayout.Width(80)))
                {
                    RemoteHotReloadPushService.IsEnabled = _remoteEnabled;
                    RemoteHotReloadPushService.SessionMcpUrl = _remoteUrl;
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Inspector behavior", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var auto = EditorPrefs.GetBool(PrefAutoHealth, true);
                var auto2 = EditorGUILayout.Toggle("Auto-run Health on focus", auto);
                if (auto2 != auto) EditorPrefs.SetBool(PrefAutoHealth, auto2);

                var echo = EditorPrefs.GetBool(SusHealthRunner.PrefEchoConsole, true);
                var echo2 = EditorGUILayout.Toggle("Echo Health issues to Console", echo);
                if (echo2 != echo) EditorPrefs.SetBool(SusHealthRunner.PrefEchoConsole, echo2);

                var echoInfo = EditorPrefs.GetBool(SusHealthRunner.PrefEchoInfo, false);
                var echoInfo2 = EditorGUILayout.Toggle("Also echo Info lines", echoInfo);
                if (echoInfo2 != echoInfo) EditorPrefs.SetBool(SusHealthRunner.PrefEchoInfo, echoInfo2);

                var autoTree = EditorGUILayout.Toggle("Auto-refresh Inspect tree (Play)", _autoRefreshTree);
                if (autoTree != _autoRefreshTree)
                {
                    _autoRefreshTree = autoTree;
                    EditorPrefs.SetBool(PrefAutoRefreshTree, autoTree);
                }
            }

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Toggle theme Dark/Light (Play)", GUILayout.Width(220)))
                    ToggleTheme();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "Docs: docs/SUS_INSPECTOR_PANEL_DESIGN.md · sus-core/Docs/09-compilation.md",
                MessageType.None);
        }

        static void ToggleTheme()
        {
            try
            {
                var root = SusInspectorTree.FindActiveRoot();
                if (root == null) return;
                var cur = SusThemeService.Current.Value;
                var next = cur == SusTheme.Light ? SusTheme.Dark : SusTheme.Light;
                SusThemeService.Instance.SetTheme(root, next);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SUS Inspector] Theme toggle: {ex.Message}");
            }
        }
    }
}
