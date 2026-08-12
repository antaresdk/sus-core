using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    public enum ConsoleFilter { All, Log, Warning, Error }

    public struct SusLogEntry
    {
        public LogType Type;
        public string Message;
        public string StackTrace;
        public float Time;

        public override string ToString() => $"[{Type}] {Message}";
    }

    /// <summary>
    /// Dev console overlay — intercepts Unity logs, displays them above all UI,
    /// and provides a command input with a registered command registry.
    ///
    /// Category: OverlayCategory.Console = 50 (topmost layer).
    /// Toggle by hotkey (~) via SusConsoleDriver.
    ///
    /// Wrap in #if DEVELOPMENT_BUILD || UNITY_EDITOR for release builds.
    /// </summary>
    public class SusConsoleService
    {
        public static SusConsoleService Instance { get; private set; }

        public OverlayHost OverlayHost { get; set; }
        public KeyCode ToggleKey = KeyCode.BackQuote;
        public int MaxEntries = 500;

        public bool IsOpen { get; private set; }

        private readonly List<SusLogEntry> _buffer = new();
        private readonly Queue<SusLogEntry> _pendingEntries = new(); // thread-safe queue
        private readonly object _lock = new();

        // Filters
        private ConsoleFilter _filter = ConsoleFilter.All;
        private string _searchText = string.Empty;
        private readonly Dictionary<string, (Action<string[]> handler, string help)> _commands = new();

        // UI elements (lazy, built on first Show)
        private VisualElement _root;
        private ScrollView _scrollView;
        private Label _filterLabel;
        private bool _userScrolledUp;
        private TextField _cmdField; // captured for tab-completion

        // ─── Attach / Detach ────────────────────────────────────────────────

        /// <summary>
        /// Starts log interception + hotkey polling.
        /// Creates a SusConsoleDriver GameObject if one doesn't exist.
        /// </summary>
        public void Attach()
        {
            Application.logMessageReceivedThreaded += OnLogReceived;

            // Ensure driver exists
            var driverObj = GameObject.Find("__SusConsoleDriver__");
            if (driverObj == null)
            {
                driverObj = new GameObject("__SusConsoleDriver__");
                driverObj.hideFlags = HideFlags.HideAndDontSave;
                driverObj.AddComponent<SusConsoleDriver>().Service = this;
            }
            else
            {
                var driver = driverObj.GetComponent<SusConsoleDriver>();
                if (driver == null)
                    driver = driverObj.AddComponent<SusConsoleDriver>();
                driver.Service = this;
            }

            RegisterBuiltinCommands();
            Instance = this;
        }

        public void Detach()
        {
            Application.logMessageReceivedThreaded -= OnLogReceived;

            if (IsOpen)
                Hide();

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        // ─── Log interception (thread-safe) ─────────────────────────────────

        private void OnLogReceived(string message, string stackTrace, LogType type)
        {
            lock (_lock)
            {
                _pendingEntries.Enqueue(new SusLogEntry
                {
                    Type = type,
                    Message = message,
                    StackTrace = stackTrace,
                    Time = Time.unscaledTime
                });
            }
        }

        /// <summary>
        /// Called from SusConsoleDriver.Update() — drains the thread-safe queue
        /// into the buffer and updates UI if visible.
        /// </summary>
        public void DrainPendingEntries()
        {
            bool hadNew = false;

            lock (_lock)
            {
                while (_pendingEntries.Count > 0)
                {
                    var entry = _pendingEntries.Dequeue();

                    if (_buffer.Count >= MaxEntries)
                        _buffer.RemoveAt(0);

                    _buffer.Add(entry);
                    hadNew = true;
                }
            }

            if (hadNew && IsOpen)
                UpdateLogList();
        }

        // ─── Show / Hide / Toggle ───────────────────────────────────────────

        public void Show()
        {
            if (IsOpen) return;
            if (OverlayHost == null) return;

            BuildUI();
            OverlayHost.AddToOverlay(_root, OverlayCategory.Console);
            IsOpen = true;
            UpdateLogList();
        }

        public void Hide()
        {
            if (!IsOpen) return;
            if (OverlayHost != null && _root != null)
                OverlayHost.RemoveFromOverlay(_root);
            IsOpen = false;
        }

        public void Toggle()
        {
            if (IsOpen) Hide();
            else Show();
        }

        public void Clear()
        {
            _buffer.Clear();
            if (IsOpen)
                UpdateLogList();
        }

        // ─── Filters ────────────────────────────────────────────────────────

        public void SetFilter(ConsoleFilter filter)
        {
            _filter = filter;
            if (IsOpen)
                UpdateLogList();
        }

        public void SetSearch(string text)
        {
            _searchText = text ?? string.Empty;
            if (IsOpen)
                UpdateLogList();
        }

        private bool MatchesFilter(SusLogEntry entry)
        {
            if (_filter == ConsoleFilter.All) return true;
            if (_filter == ConsoleFilter.Log && entry.Type == LogType.Log) return true;
            if (_filter == ConsoleFilter.Warning && entry.Type == LogType.Warning) return true;
            if (_filter == ConsoleFilter.Error &&
                (entry.Type == LogType.Error || entry.Type == LogType.Exception || entry.Type == LogType.Assert))
                return true;
            return false;
        }

        // ─── Commands ───────────────────────────────────────────────────────

        /// <summary>
        /// Optional fail-fast when RegisterCommand overwrites an existing name without overwrite:true.
        /// </summary>
        public static Action<string> OnDuplicateCommand;

        public void RegisterCommand(string name, Action<string[]> handler, string help = null, bool overwrite = false)
        {
            var key = name.ToLowerInvariant();
            if (!overwrite && _commands.ContainsKey(key))
                OnDuplicateCommand?.Invoke(key);
            _commands[key] = (handler, help);
        }

        public bool ExecuteCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            var parts = input.Trim().Split(' ');
            var name = parts[0].ToLowerInvariant();
            var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

            if (_commands.TryGetValue(name, out var cmd))
            {
                cmd.handler(args);
                return true;
            }

            SusLog.Warn($"[Console] Unknown command: {name}. Type 'help' for a list.");
            return false;
        }

        private IEnumerable<string> GetCommandList()
        {
            foreach (var kv in _commands)
            {
                var help = kv.Value.help ?? "(no description)";
                yield return $"  {kv.Key} — {help}";
            }
        }

        private void RegisterBuiltinCommands()
        {
            RegisterCommand("clear", _ => Clear(), "Clear the console.");
            RegisterCommand("help", _ =>
            {
                SusLog.Verbose("[Console] Commands:");
                foreach (var line in GetCommandList())
                    SusLog.Verbose(line);
            }, "List all commands.");
            RegisterCommand("filter", args =>
            {
                if (args.Length == 0) return;
                var f = args[0].ToLowerInvariant();
                if (f == "all") SetFilter(ConsoleFilter.All);
                else if (f == "log") SetFilter(ConsoleFilter.Log);
                else if (f == "warn" || f == "warning") SetFilter(ConsoleFilter.Warning);
                else if (f == "error") SetFilter(ConsoleFilter.Error);
            }, "filter <all|log|warn|error> — Filter by log type.");
        }

        // ─── UI Building (lazy, first Show) ─────────────────────────────────

        private void BuildUI()
        {
            if (_root != null) return;

            _root = new VisualElement
            {
                name = "sus-console",
                pickingMode = PickingMode.Position,
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    bottom = 0,
                    height = Length.Percent(40),
                    backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.92f),
                    flexDirection = FlexDirection.Column,
                    paddingTop = 4,
                    paddingBottom = 4,
                    paddingLeft = 8,
                    paddingRight = 8,
                }
            };

            // ── Toolbar ──
            var toolbar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    height = 24,
                    marginBottom = 4,
                }
            };

            // Filter buttons
            foreach (var (label, filter) in new[]
            {
                ("All", ConsoleFilter.All), ("Log", ConsoleFilter.Log),
                ("Warn", ConsoleFilter.Warning), ("Err", ConsoleFilter.Error)
            })
            {
                var btn = new Button(() =>
                {
                    SetFilter(filter);
                    UpdateFilterLabel();
                })
                {
                    text = label,
                    style =
                    {
                        marginRight = 4,
                        height = 22,
                        fontSize = 11,
                    }
                };
                toolbar.Add(btn);
            }

            // Search field
            var searchField = new TextField
            {
                style =
                {
                    flexGrow = 1,
                    height = 22,
                    marginLeft = 8,
                    fontSize = 11,
                }
            };
            searchField.RegisterValueChangedCallback(evt => SetSearch(evt.newValue));
            toolbar.Add(searchField);

            // Command input
            _cmdField = new TextField
            {
                style =
                {
                    width = 200,
                    height = 22,
                    marginLeft = 8,
                    fontSize = 11,
                }
            };
            _cmdField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    ExecuteCommand(_cmdField.value);
                    _cmdField.value = string.Empty;
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Tab)
                {
                    TryCompleteCommand(_cmdField);
                    evt.StopPropagation();
                }
            });
            toolbar.Add(_cmdField);

            // Close button
            var closeBtn = new Button(Hide) { text = "✕",
                style = { marginLeft = 4, height = 22, fontSize = 11, width = 22 }
            };
            toolbar.Add(closeBtn);

            _root.Add(toolbar);

            // ── Scroll view ──
            _scrollView = new ScrollView
            {
                style = { flexGrow = 1 },
            };
            _scrollView.verticalScroller.valueChanged += v =>
                _userScrolledUp = v < _scrollView.verticalScroller.highValue - 0.01f;

            _root.Add(_scrollView);

            // Filter status label
            _filterLabel = new Label
            {
                style =
                {
                    fontSize = 10,
                    color = new Color(0.5f, 0.5f, 0.5f),
                    unityTextAlign = TextAnchor.MiddleRight,
                    height = 16,
                }
            };
            _root.Add(_filterLabel);
        }

        private void UpdateLogList()
        {
            if (_scrollView == null) return;

            _scrollView.Clear();
            int shown = 0;

            for (int i = _buffer.Count - 1; i >= 0; i--)
            {
                var entry = _buffer[i];
                if (!MatchesFilter(entry)) continue;
                if (!string.IsNullOrEmpty(_searchText) &&
                    !entry.Message.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    continue;

                var color = entry.Type switch
                {
                    LogType.Warning => new Color(0.9f, 0.8f, 0.1f),
                    LogType.Error or LogType.Exception or LogType.Assert => new Color(0.95f, 0.3f, 0.3f),
                    _ => new Color(0.65f, 0.65f, 0.65f),
                };

                var line = new Label
                {
                    text = entry.Message,
                    style =
                    {
                        color = color,
                        fontSize = 11,
                        unityFontStyleAndWeight = FontStyle.Normal,
                        whiteSpace = WhiteSpace.Normal,
                        paddingBottom = 1,
                        paddingTop = 1,
                    }
                };
                _scrollView.Add(line);
                shown++;
            }

            _filterLabel.text = $"{shown}/{_buffer.Count} entries";

            if (!_userScrolledUp)
                _scrollView.schedule.Execute(() => _scrollView.verticalScroller.value = 0).StartingIn(0);
        }

        private void UpdateFilterLabel()
        {
            if (_filterLabel != null)
                _filterLabel.text = $"Filter: {_filter}";
        }

        // ─── C4.4: Tab-completion ────────────────────────────────────────────

        /// <summary>
        /// Autocomplete the current command input on Tab press.
        /// Finds the first registered command matching the current prefix.
        /// </summary>
        private void TryCompleteCommand(TextField field)
        {
            if (field == null || string.IsNullOrEmpty(field.value))
                return;

            var prefix = field.value.Trim().ToLowerInvariant();
            foreach (var name in _commands.Keys)
            {
                if (name.StartsWith(prefix) && name != prefix)
                {
                    field.value = name + " ";
                    // Move caret to end
                    field.Q("unity-text-input")?.Focus();
                    field.SelectRange(field.value.Length, 0);
                    return;
                }
            }
        }
    }
}
