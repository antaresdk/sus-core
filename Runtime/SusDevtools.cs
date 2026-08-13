using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace Sharq.Core
{
    /// <summary>
    /// Runtime devtools panel for inspecting SusComponent tree and Prop&lt;T&gt; values.
    /// Toggle with F12 key when attached. Supports editing primitive props in-place.
    ///
    /// Usage:
    /// <code>
    /// SusDevtools.Attach(uiDocument.rootVisualElement);
    /// // Press F12 to toggle
    /// </code>
    /// </summary>
    public static class SusDevtools
    {
        private const float PanelWidth = 380f;
        private const string PanelName = "sus-devtools";
        private const string PanelClass = "sus-devtools";

        private static VisualElement _root;
        private static VisualElement _panel;
        private static ScrollView _propsScroll;
        private static VisualElement _selectedElement;

        // Cache reflected Prop<T> info per SusComponent (field name → getter/setter)
        private static readonly Dictionary<Type, List<PropFieldInfo>> s_propCache = new();

        /// <summary>
        /// Attaches devtools to a root VisualElement. Creates the debug panel
        /// (initially hidden) and registers F12 toggle.
        /// Idempotent — call once at startup.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (_panel != null) return; // already attached

            _root = root;
            BuildPanel();
            RegisterToggle(root);
        }

        /// <summary>
        /// Detaches devtools — removes panel and keyboard listener.
        /// </summary>
        public static void Detach()
        {
            if (_panel != null)
            {
                _panel.RemoveFromHierarchy();
                _panel = null;
            }
            if (_root != null)
            {
                _root.UnregisterCallback<KeyDownEvent>(OnKeyDown);
                _root = null;
            }
        }

        /// <summary>
        /// Returns true if the devtools panel is currently visible.
        /// </summary>
        public static bool IsVisible => _panel?.style.display == DisplayStyle.Flex;

#if UNITY_EDITOR
        // With Domain Reload disabled these survive leaving Play Mode: _panel/_root/_selectedElement
        // would point at VisualElements from the destroyed previous panel, and Attach() would see
        // _panel != null and skip re-registering the F12 toggle on the new root.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _root = null;
            _panel = null;
            _propsScroll = null;
            _selectedElement = null;
        }
#endif

        // ════════════════════════════════════════════════════════════════
        //  Panel construction
        // ════════════════════════════════════════════════════════════════

        private static void BuildPanel()
        {
            _panel = new VisualElement { name = PanelName };
            _panel.AddToClassList(PanelClass);
            _panel.style.position = Position.Absolute;
            _panel.style.top = 0;
            _panel.style.right = 0;
            _panel.style.width = PanelWidth;
            _panel.style.height = Length.Percent(100);
            _panel.style.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.95f);
            _panel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.paddingLeft = 12;
            _panel.style.paddingRight = 12;
            _panel.style.display = DisplayStyle.None;
            _panel.pickingMode = PickingMode.Position;

            // Header
            var header = new Label("SUS Devtools")
            {
                style =
                {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = new Color(0.4f, 0.8f, 1f),
                    marginBottom = 8
                }
            };
            _panel.Add(header);

            // Refresh button
            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 8 } };
            var refreshBtn = new Button(() => RefreshProps()) { text = "⟳ Refresh" };
            refreshBtn.style.flexGrow = 1f;
            refreshBtn.style.height = 22;
            refreshBtn.style.fontSize = 11;

            var scanBtn = new Button(() => ScanAndShowTree()) { text = "🌳 Tree" };
            scanBtn.style.flexGrow = 1f;
            scanBtn.style.height = 22;
            scanBtn.style.fontSize = 11;
            scanBtn.style.marginLeft = 4;

            btnRow.Add(refreshBtn);
            btnRow.Add(scanBtn);
            _panel.Add(btnRow);

            // Tree view (scrollable)
            var treeScroll = new ScrollView
            {
                name = "devtools-tree",
                style =
                {
                    flexGrow = 1f,
                    marginBottom = 8,
                    maxHeight = Length.Percent(50)
                }
            };
            _panel.Add(treeScroll);

            // Props label
            var propsLabel = new Label("Props")
            {
                style =
                {
                    fontSize = 12,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = new Color(0.6f, 0.9f, 0.6f),
                    marginBottom = 4
                }
            };
            _panel.Add(propsLabel);

            // Props scroll
            _propsScroll = new ScrollView
            {
                name = "devtools-props",
                style = { flexGrow = 1f, maxHeight = Length.Percent(40) }
            };
            _panel.Add(_propsScroll);

            // Add as last child → renders on top
            _root.Add(_panel);
        }

        private static void RegisterToggle(VisualElement root)
        {
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            root.focusable = true;
            root.Focus();
        }

        private static void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.F12)
            {
                Toggle();
                evt.StopPropagation();
            }
        }

        /// <summary>
        /// Toggle devtools panel visibility.
        /// </summary>
        public static void Toggle()
        {
            if (_panel == null) return;
            var visible = _panel.style.display == DisplayStyle.Flex;
            _panel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            if (!visible)
                ScanAndShowTree();
        }

        // ════════════════════════════════════════════════════════════════
        //  Tree scanning
        // ════════════════════════════════════════════════════════════════

        private static void ScanAndShowTree()
        {
            if (_root == null || _panel == null) return;
            var treeScroll = _panel.Q<ScrollView>("devtools-tree");
            if (treeScroll == null) return;

            treeScroll.Clear();

            WalkTree(_root, 0, treeScroll);
        }

        private static void WalkTree(VisualElement el, int depth, ScrollView parent)
        {
            if (el == _panel) return; // skip the devtools panel itself

            var indent = new string(' ', depth * 2);
            var typeLabel = el.GetType().Name;
            var icon = el is SusComponent ? "⚙ " : "■ ";
            var nameSuffix = string.IsNullOrEmpty(el.name) ? "" : $"  [{el.name}]";

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 1 } };
            var label = new Label($"{indent}{icon}{typeLabel}{nameSuffix}")
            {
                style = { fontSize = 11, color = el is SusComponent ? new Color(0.5f, 0.9f, 1f) : new Color(0.7f, 0.7f, 0.7f) }
            };

            if (el is SusComponent sc)
            {
                label.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedElement = el;
                    RefreshProps();
                });
            }

            row.Add(label);
            parent.Add(row);

            // Recurse children
            foreach (var child in el.Children())
                WalkTree(child, depth + 1, parent);
        }

        // ════════════════════════════════════════════════════════════════
        //  Props inspection
        // ════════════════════════════════════════════════════════════════

        private static void RefreshProps()
        {
            if (_propsScroll == null) return;
            _propsScroll.Clear();

            if (_selectedElement == null)
            {
                _propsScroll.Add(new Label("Select a ⚙ component in the tree above to inspect its Props.")
                {
                    style = { fontSize = 11, color = new Color(0.5f, 0.5f, 0.5f), whiteSpace = WhiteSpace.Normal }
                });
                return;
            }

            var header = new Label($"{(string.IsNullOrEmpty(_selectedElement.name) ? _selectedElement.GetType().Name : _selectedElement.name)}")
            {
                style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
            };
            _propsScroll.Add(header);

            var propFields = GetPropFields(_selectedElement.GetType());

            foreach (var pf in propFields)
            {
                try
                {
                    var propObj = pf.Getter(_selectedElement);
                    if (propObj == null) continue;

                    var valueObj = pf.ValueGetter(propObj);
                    var valueStr = valueObj?.ToString() ?? "null";

                    var row = new VisualElement
                    {
                        style =
                        {
                            flexDirection = FlexDirection.Row,
                            marginBottom = 3,
                            alignItems = Align.Center
                        }
                    };

                    var nameLbl = new Label(pf.Name)
                    {
                        style = { width = 100, fontSize = 11, color = new Color(0.8f, 0.8f, 0.5f) }
                    };
                    row.Add(nameLbl);

                    var valLbl = new Label(valueStr)
                    {
                        style = { fontSize = 11, flexGrow = 1f, color = new Color(0.9f, 0.9f, 0.9f) }
                    };
                    row.Add(valLbl);

                    // Edit button
                    var editBtn = new Button(() => EditPropValue(pf, _selectedElement, propObj))
                    {
                        text = "…", style = { width = 22, height = 18, fontSize = 10 }
                    };
                    row.Add(editBtn);

                    _propsScroll.Add(row);
                }
                catch (Exception e)
                {
                    _propsScroll.Add(new Label($"err: {e.Message}")
                    {
                        style = { fontSize = 10, color = Color.red }
                    });
                }
            }
        }

        private static void EditPropValue(PropFieldInfo pf, object target, object propObj)
        {
            // Create a simple inline editor
            var currentValue = pf.ValueGetter(propObj)?.ToString() ?? "";

            var editorRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, marginTop = 2, marginBottom = 4 }
            };
            editorRow.AddToClassList("sus-devtools__editor-row");

            // Remove any existing editor row before adding a new one
            var existingEditor = _propsScroll.Q<VisualElement>(className: "sus-devtools__editor-row");
            existingEditor?.RemoveFromHierarchy();

            var input = new TextField { value = currentValue, style = { flexGrow = 1f, fontSize = 11, height = 20 } };

            var applyBtn = new Button(() =>
            {
                try
                {
                    var converted = Convert.ChangeType(input.value, pf.ValueType, System.Globalization.CultureInfo.InvariantCulture);
                    var setter = pf.ValueSetter;
                    if (setter != null)
                    {
                        setter(propObj, converted);
                    }
                    else
                    {
                        // Fallback: set via reflection on Prop<T>.Value
                        var valueProp = propObj.GetType().GetProperty("Value");
                        valueProp?.SetValue(propObj, converted);
                    }
                    RefreshProps();
                }
                catch (Exception e)
                {
                    SusLog.Warn($"[SusDevtools] Edit failed: {e.Message}");
                }
            })
            { text = "✓", style = { width = 24, height = 20, fontSize = 11, marginLeft = 4 } };

            editorRow.Add(input);
            editorRow.Add(applyBtn);

            // Insert editor after the current row — find the last child before refresh was triggered
            _propsScroll.Add(editorRow);
        }

        // ════════════════════════════════════════════════════════════════
        //  Reflection cache
        // ════════════════════════════════════════════════════════════════

        private static List<PropFieldInfo> GetPropFields(Type type)
        {
            if (s_propCache.TryGetValue(type, out var cached))
                return cached;

            var result = new List<PropFieldInfo>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in type.GetFields(flags))
            {
                if (!field.FieldType.IsGenericType) continue;
                if (field.FieldType.GetGenericTypeDefinition() != typeof(Prop<>)) continue;

                var valueType = field.FieldType.GetGenericArguments()[0];
                Func<object, object> getter = field.GetValue;
                MethodInfo propValueGetter = field.FieldType.GetProperty("Value")?.GetMethod;

                // Build a fast setter delegate via reflection
                Action<object, object> valueSetter = null;
                var valueProp = field.FieldType.GetProperty("Value");
                if (valueProp?.SetMethod != null)
                {
                    // Prefer direct set — but we can't cache typed delegates easily.
                    // Use setter lambda that does reflection per-call (devtools is not hot path).
                    valueSetter = (obj, val) =>
                    {
                        var converted = val is IConvertible && valueType != typeof(string)
                            ? Convert.ChangeType(val, valueType, System.Globalization.CultureInfo.InvariantCulture)
                            : val;
                        valueProp.SetValue(obj, converted);
                    };
                }

                result.Add(new PropFieldInfo
                {
                    Name = field.Name,
                    ValueType = valueType,
                    Getter = target => field.GetValue(target),
                    ValueGetter = obj => propValueGetter?.Invoke(obj, null),
                    ValueSetter = valueSetter
                });
            }

            s_propCache[type] = result;
            return result;
        }

        // ════════════════════════════════════════════════════════════════
        //  Helper types
        // ════════════════════════════════════════════════════════════════

        private sealed class PropFieldInfo
        {
            public string Name;
            public Type ValueType;
            public Func<object, object> Getter;       // SusComponent → Prop<T>
            public Func<object, object> ValueGetter;   // Prop<T> → T
            public Action<object, object> ValueSetter; // (Prop<T>, newValue) → set Value
        }
    }
}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
