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
        // ═══ Connect ═══════════════════════════════════════════════

        void DrawConnect()
        {
            EditorGUILayout.LabelField("Session MCP", EditorStyles.boldLabel);

            // Connection status strip — always visible on top.
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            var (txt, col) = _connectState switch
            {
                1 => ($"● Connected  (checked {_lastPing:HH:mm:ss})", ColOk),
                2 => ($"✕ Unreachable  (checked {_lastPing:HH:mm:ss})", ColErr),
                _ => ("? Not checked yet", ColMuted),
            };
            var stStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = col } };
            GUILayout.Label(txt, stStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_pinging ? "Pinging…" : "Ping", GUILayout.Width(70)))
                _ = PingMcpAsync();
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_connectStatus))
                EditorGUILayout.LabelField(_connectStatus, EditorStyles.miniLabel);

            _remoteEnabled = EditorGUILayout.Toggle("Remote hot reload", _remoteEnabled);
            _remoteUrl = EditorGUILayout.TextField("URL", _remoteUrl ?? "");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply", GUILayout.Width(80)))
            {
                RemoteHotReloadPushService.IsEnabled = _remoteEnabled;
                RemoteHotReloadPushService.SessionMcpUrl = _remoteUrl;
                _connectStatus = "Prefs saved";
            }
            if (GUILayout.Button("Reset URL", GUILayout.Width(80)))
                _remoteUrl = RemoteHotReloadPushService.DefaultUrl;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime MCP agent capabilities", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "snapshot · ui.dispatch · probe.call · ui.hotreload.template\n" +
                "ui.hotreload.uss — Editor-only (StyleSheetFromUss factory)\n" +
                "Tunnel: _mcp_tunnel_session.ps1 → localhost:7711",
                MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Quick local probes (Play)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("UI tree → clipboard"))
                    CopyTreeToClipboard();
                if (GUILayout.Button("Selected props → clipboard"))
                {
                    if (_selected is SusComponent sc)
                        EditorGUIUtility.systemCopyBuffer = SusInspectorTree.DumpProps(sc);
                    else
                        _connectStatus = "Select a SusComponent in Inspect first";
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        async Task PingMcpAsync()
        {
            _pinging = true;
            _connectStatus = "";
            Repaint();
            try
            {
                var url = string.IsNullOrWhiteSpace(_remoteUrl)
                    ? RemoteHotReloadPushService.DefaultUrl
                    : _remoteUrl.Trim();
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}";
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp = await http.PostAsync(url, content);
                var text = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    _connectState = 1;
                    _connectStatus = $"HTTP {(int)resp.StatusCode}, {text.Length} bytes";
                }
                else
                {
                    _connectState = 2;
                    _connectStatus = $"HTTP {(int)resp.StatusCode}: {Truncate(text, 120)}";
                }
            }
            catch (Exception ex)
            {
                _connectState = 2;
                _connectStatus = ex.Message;
            }
            finally
            {
                _lastPing = DateTime.Now;
                _pinging = false;
                Repaint();
            }
        }

        static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= n) return s ?? "";
            return s.Substring(0, n - 1) + "…";
        }

    }
}
