#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Console commands for inspecting SusComponent tree and Props at runtime.
    ///
    /// Usage in Unity Console or immediate window:
    /// <code>
    /// DevConsole.Inspect();           // prints full SusComponent tree from root
    /// DevConsole.Inspect(someNode);   // prints tree from a specific node
    /// DevConsole.Set("MainScreen.score", 42);  // set a prop by path
    /// DevConsole.Overlays();          // print overlay stack
    /// DevConsole.Trace("SusButton");  // enable lifecycle tracing for SusButton
    /// DevConsole.TraceOff();          // disable all tracing
    /// </code>
    /// </summary>
    public static class DevConsole
    {
        private static readonly HashSet<string> TraceFilters = new();

        /// <summary>
        /// Print the SusComponent tree starting from the first UIDocument root.
        /// </summary>
        public static void Inspect(VisualElement root = null)
        {
            if (root == null)
            {
                var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(UnityEngine.FindObjectsSortMode.None);
                if (docs.Length == 0)
                {
                    Debug.Log("[sus.inspect] No UIDocument found in scene.");
                    return;
                }
                root = docs[0].rootVisualElement;
            }

            Debug.Log($"[sus.inspect] SusComponent tree:");
            WalkAndPrint(root, 0);
        }

        private static void WalkAndPrint(VisualElement el, int depth)
        {
            if (el is SusComponent sc)
            {
                var indent = new string(' ', depth * 2);
                var name = string.IsNullOrEmpty(el.name) ? sc.GetType().Name : el.name;
                var bindingCount = CountWatchHandles(sc);

                // List public Prop<T> fields
                var props = GetPropSummaries(sc);
                var propsStr = props.Count > 0
                    ? $" [{string.Join(", ", props.Select(p => $"{p.Key}={p.Value}"))}]"
                    : "";
                var bindingsStr = bindingCount > 0 ? $" ({bindingCount} bindings)" : "";

                Debug.Log($"{indent}⚙ {name}{propsStr}{bindingsStr}");
            }

            foreach (var child in el.Children())
                WalkAndPrint(child, depth + 1);
        }

        /// <summary>
        /// Set a Prop&lt;T&gt; value by component name and prop name.
        /// Example: DevConsole.Set("MainScreen.score", 42);
        /// </summary>
        public static void Set(string path, object value)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[sus.set] Usage: DevConsole.Set(\"ComponentName.propName\", value)");
                return;
            }

            var parts = path.Split('.');
            if (parts.Length != 2)
            {
                Debug.LogWarning($"[sus.set] Invalid path '{path}'. Expected 'ComponentName.propName'.");
                return;
            }

            var compName = parts[0];
            var propName = parts[1];

            // Find component by name or type name
            var sc = FindSusComponent(compName);
            if (sc == null)
            {
                Debug.LogWarning($"[sus.set] Component '{compName}' not found in scene.");
                return;
            }

            // Find Prop<T> field
            var field = sc.GetType().GetField(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                Debug.LogWarning($"[sus.set] Prop '{propName}' not found on '{compName}'.");
                return;
            }

            var propObj = field.GetValue(sc);
            if (propObj == null)
            {
                Debug.LogWarning($"[sus.set] Prop '{propName}' is null.");
                return;
            }

            var valueProp = propObj.GetType().GetProperty("Value");
            if (valueProp == null || !valueProp.CanWrite)
            {
                Debug.LogWarning($"[sus.set] Cannot set Value on '{propName}'.");
                return;
            }

            try
            {
                var targetType = valueProp.PropertyType;
                var converted = targetType == typeof(string) || !(value is IConvertible)
                    ? value
                    : Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
                valueProp.SetValue(propObj, converted);
                Debug.Log($"[sus.set] ✓ {compName}.{propName} = {converted}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[sus.set] Failed: {e.Message}");
            }
        }

        /// <summary>
        /// Print the overlay stack from OverlayHost.
        /// </summary>
        public static void Overlays()
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(UnityEngine.FindObjectsSortMode.None);
            if (docs.Length == 0)
            {
                Debug.Log("[sus.overlays] No UIDocument found.");
                return;
            }

            var root = docs[0].rootVisualElement;
            var overlayHost = root.Q<OverlayHost>();
            if (overlayHost == null)
            {
                Debug.Log("[sus.overlays] No OverlayHost found. Call SusBootstrap to mount first.");
                return;
            }

            var stack = overlayHost.Stack;
            if (stack.Count == 0)
            {
                Debug.Log("[sus.overlays] Stack is empty.");
                return;
            }

            Debug.Log($"[sus.overlays] Stack ({stack.Count} entries):");
            for (int i = 0; i < stack.Count; i++)
            {
                var e = stack[i];
                var size = e.Element?.resolvedStyle;
                var w = size?.width ?? 0;
                var h = size?.height ?? 0;
                Debug.Log($"  [{i}] cat={e.Category} name='{e.Element?.GetType().Name}' " +
                    $"size={w:F0}x{h:F0} dismissOnClick={e.DismissOnClickOutside}");
            }
        }

        /// <summary>
        /// Enable lifecycle tracing for components matching a type name filter.
        /// Example: DevConsole.Trace("SusButton") — logs Created/Mounted/Updated/Unmounted.
        /// Call TraceOff() to disable.
        /// </summary>
        public static void Trace(string typeFilter)
        {
            if (!TraceFilters.Contains(typeFilter))
            {
                TraceFilters.Add(typeFilter);
                Debug.Log($"[sus.trace] Tracing lifecycle events for '{typeFilter}'.");
            }
        }

        /// <summary>Disable all lifecycle tracing.</summary>
        public static void TraceOff()
        {
            if (TraceFilters.Count > 0)
            {
                Debug.Log($"[sus.trace] Tracing disabled ({TraceFilters.Count} filter(s) cleared).");
                TraceFilters.Clear();
            }
        }

        /// <summary>Called by SusComponent lifecycle hooks. Internal use only.</summary>
        internal static bool ShouldTrace(Type componentType, string hook, string details)
        {
            if (TraceFilters.Count == 0) return false;

            var typeName = componentType.Name;
            foreach (var filter in TraceFilters)
            {
                if (typeName.Contains(filter))
                {
                    Debug.Log($"[sus.trace] {typeName}.{hook}: {details}");
                    return true;
                }
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════

        private static SusComponent FindSusComponent(string name)
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(UnityEngine.FindObjectsSortMode.None);
            foreach (var doc in docs)
            {
                var match = doc.rootVisualElement.Q<SusComponent>(name);
                if (match != null) return match;

                // Also try by type name
                var all = doc.rootVisualElement.Query<SusComponent>().Build().ToList();
                foreach (var sc in all)
                {
                    if (sc.GetType().Name == name || sc.name == name)
                        return sc;
                }
            }
            return null;
        }

        private static int CountWatchHandles(SusComponent sc)
        {
            var field = typeof(SusComponent).GetField("_bindings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(sc) is List<WatchHandle> bindings)
                return bindings.Count;
            return 0;
        }

        private static Dictionary<string, string> GetPropSummaries(SusComponent sc)
        {
            var result = new Dictionary<string, string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in sc.GetType().GetFields(flags))
            {
                if (!field.FieldType.IsGenericType) continue;
                if (field.FieldType.GetGenericTypeDefinition() != typeof(Prop<>)) continue;

                try
                {
                    var propObj = field.GetValue(sc);
                    if (propObj == null) continue;
                    var valueProp = propObj.GetType().GetProperty("Value");
                    var val = valueProp?.GetValue(propObj);
                    result[field.Name] = val?.ToString() ?? "null";
                }
                catch
                {
                    result[field.Name] = "?";
                }
            }

            return result;
        }
    }
}
#endif
