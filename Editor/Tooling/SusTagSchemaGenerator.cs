#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// F6: generates a JSON Schema describing all SusComponent UXML tags (props)
    /// for IDE autocomplete of <c>sus:*</c> in .sharq files.
    /// Menu: SUS → Generate Tag Schema.
    /// </summary>
    public static class SusTagSchemaGenerator
    {
        private const string DefaultOutPath = "Assets/SusModules/sus-tags.schema.json";
        // Optional: commit a copy next to a downstream UI package for IDE autocomplete.

        [MenuItem("Window/SUS/Sharq/Generate Tag Schema", false, 204)]
        public static void Generate()
        {
            var path = EditorUtility.SaveFilePanel(
                "Save SUS Tag Schema",
                Path.GetDirectoryName(DefaultOutPath),
                Path.GetFileName(DefaultOutPath),
                "json");
            if (string.IsNullOrEmpty(path)) return;

            var json = BuildSchemaJson();
            File.WriteAllText(path, json, Encoding.UTF8);
            if (path.Replace('\\', '/').Contains("/Assets/"))
                AssetDatabase.Refresh();
            Debug.Log($"[SusTagSchema] Wrote schema ({json.Length} chars) → {path}");
        }

        /// <summary>Build schema JSON without UI (for tests / CI).</summary>
        public static string BuildSchemaJson()
        {
            var components = DiscoverComponents();
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"$schema\": \"http://json-schema.org/draft-07/schema#\",");
            sb.AppendLine("  \"$id\": \"sus-tags.schema.json\",");
            sb.AppendLine("  \"title\": \"SUS / Sharq component tags\",");
            sb.AppendLine("  \"description\": \"Auto-generated from SusComponent Prop&lt;T&gt; fields. Regenerate via SUS → Generate Tag Schema.\",");
            sb.AppendLine("  \"type\": \"object\",");
            sb.AppendLine("  \"properties\": {");

            for (int i = 0; i < components.Count; i++)
            {
                var (tag, props) = components[i];
                sb.AppendLine($"    \"{Escape(tag)}\": {{");
                sb.AppendLine($"      \"type\": \"object\",");
                sb.AppendLine($"      \"description\": \"<{tag}>\",");
                sb.AppendLine("      \"properties\": {");
                for (int p = 0; p < props.Count; p++)
                {
                    var (name, typeName, enumHint) = props[p];
                    sb.Append($"        \"{Escape(name)}\": {{ \"type\": \"{JsonType(typeName)}\"");
                    if (!string.IsNullOrEmpty(enumHint))
                        sb.Append($", \"description\": \"{Escape(enumHint)}\"");
                    sb.Append(" }");
                    sb.AppendLine(p < props.Count - 1 ? "," : "");
                }
                sb.AppendLine("      }");
                sb.Append("    }");
                sb.AppendLine(i < components.Count - 1 ? "," : "");
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static List<(string tag, List<(string name, string type, string hint)> props)> DiscoverComponents()
        {
            var result = new List<(string, List<(string, string, string)>)>();
            var baseType = typeof(SusComponent);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || !baseType.IsAssignableFrom(t)) continue;
                    if (t == baseType) continue;

                    var tag = "sus:" + t.Name;
                    var props = new List<(string, string, string)>();

                    foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!field.FieldType.IsGenericType) continue;
                        if (field.FieldType.GetGenericTypeDefinition() != typeof(Prop<>)) continue;

                        var elem = field.FieldType.GetGenericArguments()[0];
                        var hint = GuessEnumHint(field.Name, elem);
                        props.Add((field.Name, elem.Name, hint));
                    }

                    result.Add((tag, props));
                }
            }

            return result.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
        }

        private static string GuessEnumHint(string propName, Type elemType)
        {
            if (elemType != typeof(string)) return null;
            // Common prop-name conventions — documented hints for IDE, not enforced
            return propName switch
            {
                "Variant" => "elevated|flat|tonal|outlined|text|plain|filled|underlined|solo",
                "Color" => "primary|secondary|success|danger|warning|info",
                "Size" => "x-small|small|default|large|x-large|xs|sm|md|lg|xl",
                "Density" => "default|comfortable|compact",
                "Transition" => "fade|slide|scale|none",
                "Rounded" => "0|xs|sm|md|lg|xl|pill|circle",
                _ => null
            };
        }

        private static string JsonType(string csharpType) => csharpType switch
        {
            "Boolean" or "bool" => "boolean",
            "Int32" or "Int64" or "UInt32" or "Single" or "Double" or "Decimal"
                or "int" or "long" or "float" or "double" => "number",
            _ => "string"
        };

        private static string Escape(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}
#endif
