#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// E4: on SharqCompileEvents, push regenerated USS / template XML to connected
    /// Runtime MCP clients via Session MCP HTTP (localhost tunnel).
    ///
    /// Also applies locally via <see cref="SusRuntimeHotReload"/> when Play Mode is active.
    /// Opt-out: EditorPrefs key <c>Sharq.RemoteHotReload.Enabled</c> = 0.
    /// </summary>
    [InitializeOnLoad]
    public static class RemoteHotReloadPushService
    {
        const string PrefsEnabled = "Sharq.RemoteHotReload.Enabled";
        const string PrefsSessionUrl = "Sharq.RemoteHotReload.SessionMcpUrl";
        const string DefaultSessionUrl = "http://127.0.0.1:7711/mcp";

        static readonly Dictionary<string, (string suffix, string path)> PendingUss = new();
        static readonly Dictionary<string, string> PendingTemplates = new();
        static double _nextProcess;
        const double DebounceSeconds = 0.3;
        static readonly HttpClient Http = new();

        static RemoteHotReloadPushService()
        {
            SharqCompileEvents.OnUssGenerated += OnUssGenerated;
            SharqCompileEvents.OnTemplateChanged += OnTemplateChanged;
        }

        public static bool IsEnabled
        {
            get => EditorPrefs.GetInt(PrefsEnabled, 1) != 0;
            set => EditorPrefs.SetInt(PrefsEnabled, value ? 1 : 0);
        }

        public static string SessionMcpUrl
        {
            get => EditorPrefs.GetString(PrefsSessionUrl, DefaultSessionUrl);
            set => EditorPrefs.SetString(PrefsSessionUrl,
                string.IsNullOrWhiteSpace(value) ? DefaultSessionUrl : value.Trim());
        }

        public static string DefaultUrl => DefaultSessionUrl;

        static void OnUssGenerated(string className, string[] ussPaths)
        {
            if (string.IsNullOrEmpty(className) || ussPaths == null) return;
            foreach (var path in ussPaths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                var suf = SuffixOf(className, path) ?? ".g";
                PendingUss[$"{className}|{suf}"] = (suf, path);
            }
            Schedule();
        }

        static void OnTemplateChanged(string className, string templateXml)
        {
            if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(templateXml)) return;
            PendingTemplates[className] = templateXml;
            Schedule();
        }

        static void Schedule()
        {
            _nextProcess = EditorApplication.timeSinceStartup + DebounceSeconds;
            EditorApplication.update -= ProcessPending;
            EditorApplication.update += ProcessPending;
        }

        static void ProcessPending()
        {
            if (EditorApplication.timeSinceStartup < _nextProcess) return;
            EditorApplication.update -= ProcessPending;

            var uss = new Dictionary<string, (string suffix, string path)>(PendingUss);
            var templates = new Dictionary<string, string>(PendingTemplates);
            PendingUss.Clear();
            PendingTemplates.Clear();

            foreach (var kv in uss)
            {
                var parts = kv.Key.Split('|');
                var className = parts[0];
                var (suffix, path) = kv.Value;
                string text;
                try { text = File.ReadAllText(path); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RemoteHotReload] read {path}: {ex.Message}");
                    continue;
                }

                // Local Play Mode
                if (EditorApplication.isPlaying)
                    SusRuntimeHotReload.ApplyUss(className, suffix, text);

                if (IsEnabled)
                    _ = PushAllAsync("client.hotreload_uss", new
                    {
                        className,
                        suffix,
                        ussText = text,
                    });
            }

            foreach (var kv in templates)
            {
                if (EditorApplication.isPlaying)
                    SusRuntimeHotReload.ApplyTemplate(kv.Key, kv.Value);

                if (IsEnabled)
                    _ = PushAllAsync("client.hotreload_template", new
                    {
                        className = kv.Key,
                        templateXml = kv.Value,
                    });
            }
        }

        static async Task PushAllAsync(string tool, object argsBody)
        {
            try
            {
                var baseUrl = SessionMcpUrl;
                // Resolve roster then fan-out — best-effort; failures are silent warnings.
                var rosterJson = await CallMcpAsync(baseUrl, "session.client_roster", new { });
                if (string.IsNullOrEmpty(rosterJson) || !rosterJson.Contains("playerGuid"))
                    return;

                // Lightweight guid scrape (avoid System.Text.Json dependency in Editor asm)
                foreach (var guid in ExtractGuids(rosterJson))
                {
                    var payload = MergeGuid(argsBody, guid);
                    await CallMcpAsync(baseUrl, tool, payload);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteHotReload] push {tool}: {ex.Message}");
            }
        }

        static object MergeGuid(object argsBody, string guid)
        {
            // Serialize manually as JSON object with playerGuid
            var json = EditorJsonUtility.ToJson(Wrap(argsBody));
            // EditorJsonUtility needs [Serializable] — use simple dictionary-like string build instead
            return new HotReloadArgs
            {
                playerGuid = guid,
                className = GetProp(argsBody, "className"),
                suffix = GetProp(argsBody, "suffix"),
                ussText = GetProp(argsBody, "ussText"),
                templateXml = GetProp(argsBody, "templateXml"),
            };
        }

        static string GetProp(object o, string name)
        {
            var p = o.GetType().GetProperty(name);
            return p?.GetValue(o) as string ?? "";
        }

        [Serializable]
        class HotReloadArgs
        {
            public string playerGuid;
            public string className;
            public string suffix;
            public string ussText;
            public string templateXml;
        }

        static object Wrap(object o) => o;

        static async Task<string> CallMcpAsync(string url, string method, object args)
        {
            // JSON-RPC 2.0 tools/call — Session MCP accepts this shape
            var argsJson = JsonUtility.ToJson(args is HotReloadArgs ha ? ha : new HotReloadArgs
            {
                className = GetProp(args, "className"),
                suffix = GetProp(args, "suffix"),
                ussText = GetProp(args, "ussText"),
                templateXml = GetProp(args, "templateXml"),
            });

            // For roster with empty args
            if (method == "session.client_roster")
                argsJson = "{}";

            var body = $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"{method}\",\"arguments\":{argsJson}}}}}";
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content);
            return await resp.Content.ReadAsStringAsync();
        }

        static IEnumerable<string> ExtractGuids(string json)
        {
            const string key = "\"playerGuid\"";
            var idx = 0;
            while (true)
            {
                idx = json.IndexOf(key, idx, StringComparison.Ordinal);
                if (idx < 0) yield break;
                idx = json.IndexOf(':', idx);
                if (idx < 0) yield break;
                idx++;
                while (idx < json.Length && (json[idx] == ' ' || json[idx] == '"'))
                {
                    if (json[idx] == '"') { idx++; break; }
                    idx++;
                }
                var start = idx;
                while (idx < json.Length && json[idx] != '"' && json[idx] != ',' && json[idx] != '}')
                    idx++;
                var guid = json.Substring(start, idx - start).Trim();
                if (!string.IsNullOrEmpty(guid))
                    yield return guid;
            }
        }

        static string SuffixOf(string className, string ussPath)
        {
            var file = Path.GetFileNameWithoutExtension(ussPath);
            if (string.IsNullOrEmpty(file) || !file.StartsWith(className, StringComparison.Ordinal))
                return null;
            var rest = file.Substring(className.Length);
            if (rest == ".g" || rest == "_scoped.g" || rest == "_static.g")
                return rest;
            if (rest == "" || rest == ".g")
                return ".g";
            // file may be ClassName.g already stripped of .uss → ClassName.g
            if (file.EndsWith("_scoped.g", StringComparison.Ordinal)) return "_scoped.g";
            if (file.EndsWith("_static.g", StringComparison.Ordinal)) return "_static.g";
            if (file.EndsWith(".g", StringComparison.Ordinal)) return ".g";
            return ".g";
        }
    }
}
#endif
