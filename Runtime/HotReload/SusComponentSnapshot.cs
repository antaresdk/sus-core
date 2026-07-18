using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Runtime snapshot/restore for Prop&lt;T&gt; values of active SusComponents.
    /// Used by the template hot-reload interpreter (E3) to preserve UI state
    /// across a template swap (Clear() → rebuild tree).
    ///
    /// Works in Editor, DEVELOPMENT_BUILD, and (if included) release builds.
    /// Only JSON-safe primitive Prop types and enums are captured; others are silently skipped.
    /// </summary>
    public static class SusComponentSnapshot
    {
        /// <summary>
        /// Captured state for a single component — its tree path + type + prop values.
        /// </summary>
        public sealed class Entry
        {
            public string TreePath;   // slash-separated index path from UIDocument root
            public string TypeName;   // component's C# type name (simple, not FQN)
            public Dictionary<string, string> Props = new();  // propName → JSON-encoded value
        }

        // ─── JSON-safe primitive types ───────────────────────────────────
        private static readonly HashSet<Type> SafeTypes = new()
        {
            typeof(bool),
            typeof(int), typeof(uint),
            typeof(long), typeof(ulong),
            typeof(float), typeof(double),
            typeof(string),
        };

        private static bool IsSerializablePropType(Type elemType)
            => elemType != null && (SafeTypes.Contains(elemType) || elemType.IsEnum);

        // ─── Snapshot ────────────────────────────────────────────────────

        /// <summary>
        /// Captures all serialisable Prop&lt;T&gt; values from every SusComponent
        /// reachable from <paramref name="root"/>.
        /// </summary>
        public static List<Entry> Capture(VisualElement root)
        {
            var list = new List<Entry>();
            if (root == null) return list;

            var stack = new Stack<(VisualElement el, string path)>();
            stack.Push((root, ""));

            while (stack.Count > 0)
            {
                var (el, path) = stack.Pop();

                if (el is SusComponent comp)
                {
                    var entry = CaptureComponent(comp, path);
                    if (entry != null) list.Add(entry);
                }

                // Children pushed in reverse so left-to-right order is preserved.
                for (int i = el.childCount - 1; i >= 0; i--)
                {
                    var childPath = string.IsNullOrEmpty(path) ? i.ToString() : $"{path}/{i}";
                    stack.Push((el[i], childPath));
                }
            }

            return list;
        }

        /// <summary>
        /// Convenience overload — captures from all UIDocuments currently active in the scene.
        /// Returns a flat list with paths relative to each document's rootVisualElement.
        /// </summary>
        public static List<Entry> CaptureAllDocuments()
        {
            var result = new List<Entry>();
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            foreach (var doc in docs)
            {
                if (doc?.rootVisualElement != null)
                    result.AddRange(Capture(doc.rootVisualElement));
            }
            return result;
        }

        /// <summary>
        /// Restore snapshot onto every active UIDocument root.
        /// </summary>
        public static void RestoreAllDocuments(List<Entry> snapshot)
        {
            if (snapshot == null || snapshot.Count == 0) return;
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            foreach (var doc in docs)
            {
                if (doc?.rootVisualElement != null)
                    Restore(doc.rootVisualElement, snapshot);
            }
        }

        // ─── SessionState / wire transport ───────────────────────────────

        /// <summary>Serialize entries to a compact JSON array for SessionState / MCP.</summary>
        public static string SerializeEntries(List<Entry> entries)
        {
            if (entries == null || entries.Count == 0) return "[]";
            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var e = entries[i];
                sb.Append("{\"TreePath\":").Append(Quote(e.TreePath ?? ""));
                sb.Append(",\"TypeName\":").Append(Quote(e.TypeName ?? ""));
                sb.Append(",\"Props\":{");
                var first = true;
                if (e.Props != null)
                {
                    foreach (var kv in e.Props)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        // Values are already JSON tokens from Serialize()
                        sb.Append(Quote(kv.Key)).Append(':').Append(kv.Value ?? "null");
                    }
                }
                sb.Append("}}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>Deserialize entries produced by <see cref="SerializeEntries"/>.</summary>
        public static List<Entry> DeserializeEntries(string json)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(json) || json == "[]") return list;

            // Minimal parser for our own format (no external JSON lib).
            var i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '[') return list;
            i++;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ']') break;
                if (i < json.Length && json[i] == ',') { i++; continue; }
                if (i >= json.Length || json[i] != '{') break;

                var entry = new Entry();
                i++; // {
                while (i < json.Length && json[i] != '}')
                {
                    SkipWs(json, ref i);
                    if (json[i] == ',') { i++; continue; }
                    var key = ReadQuoted(json, ref i);
                    SkipWs(json, ref i);
                    if (i < json.Length && json[i] == ':') i++;
                    SkipWs(json, ref i);
                    if (key == "TreePath")
                        entry.TreePath = ReadQuoted(json, ref i);
                    else if (key == "TypeName")
                        entry.TypeName = ReadQuoted(json, ref i);
                    else if (key == "Props")
                        entry.Props = ReadObjectMap(json, ref i);
                    else
                        SkipValue(json, ref i);
                }
                if (i < json.Length && json[i] == '}') i++;
                if (!string.IsNullOrEmpty(entry.TypeName))
                    list.Add(entry);
            }
            return list;
        }

        static string Quote(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static string ReadQuoted(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') return "";
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '\\' && i < s.Length)
                {
                    sb.Append(s[i++]);
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        static Dictionary<string, string> ReadObjectMap(string s, ref int i)
        {
            var map = new Dictionary<string, string>();
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '{') return map;
            i++;
            while (i < s.Length && s[i] != '}')
            {
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') break;
                var k = ReadQuoted(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                SkipWs(s, ref i);
                var v = ReadJsonToken(s, ref i);
                if (!string.IsNullOrEmpty(k)) map[k] = v;
            }
            if (i < s.Length && s[i] == '}') i++;
            return map;
        }

        static string ReadJsonToken(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return "";
            if (s[i] == '"')
            {
                // Keep surrounding quotes — Deserialize expects them for strings
                var start = i;
                i++;
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '\\' && i < s.Length) { i++; continue; }
                    if (c == '"') break;
                }
                return s.Substring(start, i - start);
            }
            var begin = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']')
                i++;
            return s.Substring(begin, i - begin).Trim();
        }

        static void SkipValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return;
            if (s[i] == '"') { ReadQuoted(s, ref i); return; }
            if (s[i] == '{') { ReadObjectMap(s, ref i); return; }
            if (s[i] == '[')
            {
                i++;
                while (i < s.Length && s[i] != ']')
                {
                    if (s[i] == '"') ReadQuoted(s, ref i);
                    else i++;
                }
                if (i < s.Length) i++;
                return;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;
        }

        // ─── Restore ─────────────────────────────────────────────────────

        /// <summary>
        /// Restores prop values from <paramref name="snapshot"/> onto the live tree
        /// under <paramref name="root"/>. Matching is by tree path + type name;
        /// unmatched entries are silently ignored.
        /// </summary>
        public static void Restore(VisualElement root, List<Entry> snapshot)
        {
            if (root == null || snapshot == null || snapshot.Count == 0) return;

            // Build a lookup: path → Entry (take first match per path, type checked at apply time)
            var byPath = new Dictionary<string, Entry>(snapshot.Count);
            foreach (var e in snapshot)
            {
                if (!byPath.ContainsKey(e.TreePath))
                    byPath[e.TreePath] = e;
            }

            WalkAndRestore(root, "", byPath);
        }

        private static void WalkAndRestore(VisualElement el, string path,
            Dictionary<string, Entry> byPath)
        {
            if (el is SusComponent comp)
            {
                if (byPath.TryGetValue(path, out var entry)
                    && entry.TypeName == comp.GetType().Name)
                {
                    ApplyEntry(comp, entry);
                }
            }

            for (int i = 0; i < el.childCount; i++)
            {
                var childPath = string.IsNullOrEmpty(path) ? i.ToString() : $"{path}/{i}";
                WalkAndRestore(el[i], childPath, byPath);
            }
        }

        // ─── Per-component capture/apply ─────────────────────────────────

        private static Entry CaptureComponent(SusComponent comp, string path)
        {
            var type = comp.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var entry = new Entry { TreePath = path, TypeName = type.Name };

            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(Prop<>))
                    continue;

                var elemType = fieldType.GetGenericArguments()[0];
                if (!IsSerializablePropType(elemType)) continue;

                var prop = field.GetValue(comp);
                if (prop == null) continue;

                var valueProp = fieldType.GetProperty("Value");
                if (valueProp == null) continue;

                var value = valueProp.GetValue(prop);
                entry.Props[field.Name] = Serialize(value, elemType);
            }

            return entry.Props.Count > 0 ? entry : null;
        }

        private static void ApplyEntry(SusComponent comp, Entry entry)
        {
            var type = comp.GetType();

            foreach (var kv in entry.Props)
            {
                var field = type.GetField(kv.Key,
                    BindingFlags.Public | BindingFlags.Instance);
                if (field == null) continue;

                var fieldType = field.FieldType;
                if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(Prop<>))
                    continue;

                var elemType = fieldType.GetGenericArguments()[0];
                if (!IsSerializablePropType(elemType)) continue;

                var deserialized = Deserialize(kv.Value, elemType);
                if (deserialized == null && elemType != typeof(string)) continue;

                var prop = field.GetValue(comp);
                if (prop == null) continue;

                var valueProp = fieldType.GetProperty("Value");
                valueProp?.SetValue(prop, deserialized);
            }
        }

        // ─── Minimal serialization (no external deps) ────────────────────

        private static string Serialize(object value, Type type)
        {
            if (value == null) return "null";
            if (type == typeof(string)) return $"\"{((string)value).Replace("\"", "\\\"")}\"";
            if (type == typeof(bool)) return (bool)value ? "true" : "false";
            if (type.IsEnum) return $"\"{value}\"";
            return value.ToString();
        }

        private static object Deserialize(string raw, Type type)
        {
            if (raw == "null") return null;
            try
            {
                if (type == typeof(string))
                {
                    if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
                        return raw.Substring(1, raw.Length - 2).Replace("\\\"", "\"");
                    return raw;
                }
                if (type == typeof(bool)) return raw == "true";
                if (type == typeof(int)) return int.Parse(raw);
                if (type == typeof(uint)) return uint.Parse(raw);
                if (type == typeof(long)) return long.Parse(raw);
                if (type == typeof(ulong)) return ulong.Parse(raw);
                if (type == typeof(float)) return float.Parse(raw,
                    System.Globalization.CultureInfo.InvariantCulture);
                if (type == typeof(double)) return double.Parse(raw,
                    System.Globalization.CultureInfo.InvariantCulture);
                if (type.IsEnum)
                {
                    var name = raw;
                    if (name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"')
                        name = name.Substring(1, name.Length - 2);
                    return Enum.Parse(type, name, ignoreCase: true);
                }
            }
            catch
            {
                // Deserialization failed — return null, caller uses prop default.
            }
            return null;
        }
    }
}
