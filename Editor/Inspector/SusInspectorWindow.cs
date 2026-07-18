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
    public sealed class SusInspectorWindow : EditorWindow
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
