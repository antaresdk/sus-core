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
    /// <summary>
    /// Unified SUS developer hub — Overview, Inspect, Health, Compile, Connect, Settings.
    /// Menu: <c>Window/SUS/Inspector</c>.
    /// Spec: docs/SUS_INSPECTOR_PANEL_DESIGN.md
    ///
    /// UX model (per usage scenario):
    ///  1. "Why is it broken?"      → Overview: big status + prioritized next steps with one-click fixes.
    ///  2. "Why does this UI look wrong?" → Inspect: collapsible tree, search, Game View highlight, live prop edit.
    ///  3. "Did my .sharq regenerate?"    → Compile tab badge (STALE), generate-stale-only, colored event log.
    ///  4. Issue triage             → Health: severity filters, Info collapsed, grouped by category.
    ///  5. Remote QA                → Connect: colored ping status with timestamp.
    /// </summary>
    public sealed partial class SusInspectorWindow : EditorWindow
    {
        const string PrefTab = "Sharq.Inspector.Tab";
        const string PrefAutoHealth = "Sharq.Inspector.AutoHealth";
        const string PrefAutoRefreshTree = "Sharq.Inspector.AutoRefreshTree";
        const string PrefHealthFilter = "Sharq.Inspector.HealthFilter";

        // ─── Colors (dark-skin friendly) ──────────────────────────
        static readonly Color ColOk = new(0.35f, 0.78f, 0.42f);
        static readonly Color ColWarn = new(0.95f, 0.75f, 0.25f);
        static readonly Color ColErr = new(0.93f, 0.36f, 0.36f);
        static readonly Color ColMuted = new(0.62f, 0.65f, 0.70f);
        static readonly Color ColAccent = new(0.36f, 0.62f, 0.95f);

        SusInspectorTab _tab;
        SusHealthReport _health;
        Vector2 _scroll;
        Vector2 _treeScroll;
        Vector2 _propsScroll;
        Vector2 _logScroll;

        // Inspect
        int _maxDepth = 14;
        string _filter = "";
        List<SusInspectorTree.Node> _nodes = new();
        VisualElement _selected;
        string _inspectStatus = "";
        readonly HashSet<VisualElement> _collapsed = new();
        bool _autoRefreshTree;
        double _nextTreeRefresh;
        VisualElement _highlighted;
        StyleColor _hlOldColor;
        StyleFloat _hlOldWidthT, _hlOldWidthB, _hlOldWidthL, _hlOldWidthR;
        double _hlClearAt;

        // Compile log
        static readonly List<SusCompileLogEntry> s_log = new();
        static bool s_logHooked;

        // Settings draft
        string _cfgSharq;
        string _cfgGen;
        string _cfgRes;
        bool _cfgValidation;
        bool _cfgStrictKey;
        bool _cfgLogGen;
        bool _cfgStatePreserve;
        bool _cfgDirty;
        bool _remoteEnabled;
        string _remoteUrl;
        string _connectStatus = "";
        int _connectState; // 0 unknown, 1 ok, 2 fail
        DateTime _lastPing;
        bool _pinging;

        // Cached stale info (avoid disk scan every OnGUI)
        int _staleCount;
        readonly Dictionary<string, bool> _staleByPackage = new();
        double _nextStaleScan;

        [MenuItem("Window/SUS/Inspector", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<SusInspectorWindow>(false, "SUS Inspector", true);
            w.minSize = new Vector2(680, 480);
            w.Show();
        }

        public static void OpenTab(SusInspectorTab tab)
        {
            Open();
            var w = GetWindow<SusInspectorWindow>();
            w._tab = tab;
            EditorPrefs.SetInt(PrefTab, (int)tab);
            w.Repaint();
        }

        void OnEnable()
        {
            _tab = (SusInspectorTab)EditorPrefs.GetInt(PrefTab, (int)SusInspectorTab.Overview);
            _autoRefreshTree = EditorPrefs.GetBool(PrefAutoRefreshTree, true);
            HookCompileLog();
            LoadSettingsDraft();
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += OnEditorUpdate;
            if (EditorPrefs.GetBool(PrefAutoHealth, true))
                _health = SusHealthRunner.Run();
            RescanStale();
        }

        void OnDisable()
        {
            ClearHighlight();
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnPlayMode(PlayModeStateChange change)
        {
            _selected = null;
            _highlighted = null;
            if (_tab == SusInspectorTab.Inspect && EditorApplication.isPlaying)
                RefreshTree();
            Repaint();
        }

        void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;

            if (_highlighted != null && now >= _hlClearAt)
            {
                ClearHighlight();
                Repaint();
            }

            if (_autoRefreshTree && _tab == SusInspectorTab.Inspect
                && EditorApplication.isPlaying && now >= _nextTreeRefresh)
            {
                _nextTreeRefresh = now + 1.0;
                RefreshTree(preserveSelection: true);
                Repaint();
            }

            if (now >= _nextStaleScan)
            {
                _nextStaleScan = now + 10.0;
                RescanStale();
            }
        }

        void OnFocus()
        {
            if (EditorPrefs.GetBool(PrefAutoHealth, true))
                _health = SusHealthRunner.Run();
            RescanStale();
            Repaint();
        }

        static void HookCompileLog()
        {
            if (s_logHooked) return;
            s_logHooked = true;
            SharqCompileEvents.OnUssGenerated += (cls, paths) =>
                PushLog("USS", $"{cls}  ({paths?.Length ?? 0} file(s))");
            SharqCompileEvents.OnTemplateChanged += (cls, _) =>
                PushLog("TMPL", cls);
            SharqCompileEvents.OnUssDeleted += cls =>
                PushLog("DEL", cls);
        }

        static void PushLog(string kind, string msg)
        {
            s_log.Add(new SusCompileLogEntry { Time = DateTime.Now, Kind = kind, Message = msg });
            while (s_log.Count > 80)
                s_log.RemoveAt(0);
        }

        void LoadSettingsDraft()
        {
            var c = SusConfig.Instance;
            _cfgSharq = c.SharqDirectory;
            _cfgGen = c.GeneratedDirectory;
            _cfgRes = c.ResourcesDirectory;
            _cfgValidation = c.EnableValidation;
            _cfgStrictKey = c.StrictVForKey;
            _cfgLogGen = c.LogGeneratedFiles;
            _cfgStatePreserve = c.HotReloadStatePreserve;
            _cfgDirty = false;
            _remoteEnabled = RemoteHotReloadPushService.IsEnabled;
            _remoteUrl = RemoteHotReloadPushService.SessionMcpUrl;
        }

        void RescanStale()
        {
            _staleByPackage.Clear();
            _staleCount = 0;
            foreach (var p in SusPackageRegistry.Packages)
            {
                var stale = IsPackageStale(p);
                _staleByPackage[p.PackageName] = stale;
                if (stale) _staleCount++;
            }
        }

        // ═══ Shell ═════════════════════════════════════════════════

        void OnGUI()
        {
            DrawHeader();
            DrawTabs();
            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case SusInspectorTab.Overview: DrawOverview(); break;
                case SusInspectorTab.Inspect: DrawInspect(); break;
                case SusInspectorTab.Health: DrawHealth(); break;
                case SusInspectorTab.Compile: DrawCompile(); break;
                case SusInspectorTab.Connect: DrawConnect(); break;
                case SusInspectorTab.Settings: DrawSettings(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("SUS Inspector", EditorStyles.boldLabel, GUILayout.Width(100));

            // Status pills — the "am I ok?" strip.
            Pill(EditorApplication.isPlaying ? "▶ Play" : "◼ Edit",
                 EditorApplication.isPlaying ? ColOk : ColMuted);

            if (_health == null)
                Pill("Health: —", ColMuted);
            else if (_health.Blocking + _health.Errors > 0)
                Pill($"✕ {_health.Blocking + _health.Errors} err", ColErr);
            else if (_health.Warnings > 0)
                Pill($"▲ {_health.Warnings} warn", ColWarn);
            else
                Pill("● Healthy", ColOk);

            if (_staleCount > 0)
                Pill($"⟳ {_staleCount} stale", ColWarn);

            Pill(RemoteHotReloadPushService.IsEnabled ? "HR ⇄ on" : "HR off",
                 RemoteHotReloadPushService.IsEnabled ? ColAccent : ColMuted);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _health = SusHealthRunner.Run();
                RescanStale();
                if (_tab == SusInspectorTab.Inspect) RefreshTree(preserveSelection: true);
            }
            EditorGUILayout.EndHorizontal();
        }

        static void Pill(string text, Color color)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = color },
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 2, 2),
            };
            GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
        }

        void DrawTabs()
        {
            // Badge counts baked into tab captions so problems are visible from any tab.
            var errBadge = _health != null && _health.Blocking + _health.Errors > 0
                ? $" ({_health.Blocking + _health.Errors})"
                : _health != null && _health.Warnings > 0 ? $" ({_health.Warnings}▲)" : "";
            var staleBadge = _staleCount > 0 ? $" ({_staleCount}⟳)" : "";
            var names = new[]
            {
                "Overview", "Inspect", $"Health{errBadge}", $"Compile{staleBadge}", "Connect", "Settings",
            };
            var next = (SusInspectorTab)GUILayout.Toolbar((int)_tab, names);
            if (next != _tab)
            {
                _tab = next;
                EditorPrefs.SetInt(PrefTab, (int)_tab);
                if (_tab == SusInspectorTab.Inspect) RefreshTree(preserveSelection: true);
                if (_tab == SusInspectorTab.Health && _health == null)
                    _health = SusHealthRunner.Run();
                if (_tab == SusInspectorTab.Settings) LoadSettingsDraft();
            }
        }

    }
}
