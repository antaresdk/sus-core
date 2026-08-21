using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sharq.Core.Editor.DesignImport
{
    /// <summary>
    /// Normalizes sus-design/v1, raw W3C DTCG, and Tokens Studio-ish JSON into <see cref="DesignDocument"/>.
    /// </summary>
    public static class DesignNormalizer
    {
        static readonly HashSet<string> AllowedBreakpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "breakpoint-sm", "breakpoint-md", "breakpoint-lg", "breakpoint-xl", "breakpoint-2xl"
        };

        public static DesignDocument Normalize(string jsonText)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
                throw new FormatException("empty design JSON");

            var rootNode = DesignJson.Parse(jsonText);
            var root = rootNode.AsObject()
                ?? throw new FormatException("design JSON root must be an object");

            var doc = new DesignDocument();

            var schema = root.GetString("$schema");
            if (string.IsNullOrEmpty(schema))
                schema = root.GetString("schema");

            if (root.TryGet("source", out var srcNode) && srcNode.AsObject() != null)
            {
                var src = srcNode.AsObject();
                doc.Source.Tool = src.GetString("tool", "unknown");
                doc.Source.File = src.GetString("file");
            }

            // Tokens Studio often uses "global" / theme sets at top level without $schema
            if (LooksLikeTokensStudio(root))
            {
                doc.Schema = "sus-design/v1";
                if (doc.Source.Tool == "unknown") doc.Source.Tool = "tokens-studio";
                FlattenTokensStudio(root, doc);
            }
            else if (root.TryGet("tokens", out var tokensNode) && tokensNode.AsObject() != null)
            {
                doc.Schema = string.IsNullOrEmpty(schema) ? "sus-design/v1" : schema;
                FlattenTokenGroups(tokensNode.AsObject(), "", doc.Tokens);
            }
            else if (HasDtcgLeaves(root))
            {
                // raw DTCG: groups with $value/$type at leaves
                doc.Schema = "sus-design/v1";
                if (doc.Source.Tool == "unknown") doc.Source.Tool = "raw-dtcg";
                FlattenTokenGroups(root, "", doc.Tokens, skipMetaKeys: true);
            }
            else
            {
                throw new FormatException(
                    "unrecognized design JSON: need sus-design/v1 'tokens', raw DTCG leaves, or Tokens Studio sets");
            }

            if (root.TryGet("modes", out var modesNode) && modesNode.AsObject() != null)
            {
                foreach (var kv in modesNode.AsObject().Props)
                {
                    var modeObj = kv.Value.AsObject();
                    if (modeObj == null) continue;
                    var mode = new DesignMode { Name = kv.Key };
                    var applies = modeObj.GetString("appliesTo");
                    if (string.IsNullOrEmpty(applies) &&
                        kv.Key.Equals("mobile", StringComparison.OrdinalIgnoreCase))
                        applies = "breakpoint-sm";
                    if (string.IsNullOrEmpty(applies))
                        applies = "breakpoint-sm";
                    if (!AllowedBreakpoints.Contains(applies))
                    {
                        doc.Warnings.Add($"mode '{kv.Key}': unknown appliesTo '{applies}' (kept, phase 1b)");
                    }
                    mode.AppliesTo = applies;
                    if (modeObj.TryGet("tokens", out var mt) && mt.AsObject() != null)
                        FlattenTokenGroups(mt.AsObject(), "", mode.Tokens);
                    doc.Modes.Add(mode);
                    doc.Warnings.Add(
                        $"mode '{kv.Key}' parsed but breakpoint emit is phase 1b — tokens recorded only");
                }
            }

            if (root.TryGet("components", out var comps) && comps.AsArray() != null &&
                comps.AsArray().Items.Count > 0)
            {
                doc.Warnings.Add($"components[] ({comps.AsArray().Items.Count}) ignored in MVP (phase 2)");
            }

            if (root.TryGet("assets", out var assets) && assets.AsArray() != null &&
                assets.AsArray().Items.Count > 0)
            {
                doc.Warnings.Add($"assets[] ({assets.AsArray().Items.Count}) ignored in MVP (phase 2)");
            }

            // Stable order
            doc.Tokens = doc.Tokens
                .OrderBy(t => t.Path, StringComparer.Ordinal)
                .ToList();
            foreach (var m in doc.Modes)
            {
                m.Tokens = m.Tokens.OrderBy(t => t.Path, StringComparer.Ordinal).ToList();
            }

            return doc;
        }

        static bool LooksLikeTokensStudio(JsonObject root)
        {
            // Heuristic: has "global" object with nested $value, and no "tokens" wrapper
            if (root.Props.ContainsKey("tokens")) return false;
            if (root.TryGet("global", out var g) && g.AsObject() != null && HasDtcgLeaves(g.AsObject()))
                return true;
            // $themes / $metadata Markers from Tokens Studio
            if (root.Props.ContainsKey("$themes") || root.Props.ContainsKey("$metadata"))
                return true;
            return false;
        }

        static bool HasDtcgLeaves(JsonObject obj)
        {
            foreach (var kv in obj.Props)
            {
                if (IsMetaKey(kv.Key)) continue;
                var child = kv.Value.AsObject();
                if (child == null) continue;
                if (child.Props.ContainsKey("$value") || child.Props.ContainsKey("value"))
                    return true;
                if (HasDtcgLeaves(child)) return true;
            }
            return false;
        }

        static void FlattenTokensStudio(JsonObject root, DesignDocument doc)
        {
            foreach (var kv in root.Props)
            {
                if (IsMetaKey(kv.Key)) continue;
                var set = kv.Value.AsObject();
                if (set == null) continue;
                // set name as optional prefix only when not "global"
                var prefix = kv.Key.Equals("global", StringComparison.OrdinalIgnoreCase) ? "" : kv.Key;
                FlattenTokenGroups(set, prefix, doc.Tokens);
            }
        }

        static void FlattenTokenGroups(
            JsonObject obj,
            string prefix,
            List<DesignToken> sink,
            bool skipMetaKeys = false)
        {
            foreach (var kv in obj.Props)
            {
                if (skipMetaKeys && IsTopLevelMeta(kv.Key)) continue;
                if (IsMetaKey(kv.Key) && !(kv.Key == "$value" || kv.Key == "$type" || kv.Key == "value" || kv.Key == "type"))
                {
                    // nested $description etc. on a leaf handled below
                }

                var child = kv.Value;
                var path = string.IsNullOrEmpty(prefix) ? kv.Key : prefix + "." + kv.Key;

                var childObj = child.AsObject();
                if (childObj != null && IsTokenLeaf(childObj))
                {
                    sink.Add(ReadLeaf(path, childObj));
                    continue;
                }

                if (childObj != null)
                {
                    FlattenTokenGroups(childObj, path, sink, skipMetaKeys: false);
                    continue;
                }

                // Primitive leaf (Tokens Studio sometimes stores bare values)
                if (child is JsonString || child is JsonNumber || child is JsonBool)
                {
                    sink.Add(new DesignToken
                    {
                        Path = path,
                        Type = InferType(path, child.AsString()),
                        Value = FormatPrimitive(child)
                    });
                }
            }
        }

        static bool IsTokenLeaf(JsonObject obj)
        {
            return obj.Props.ContainsKey("$value") || obj.Props.ContainsKey("value");
        }

        static DesignToken ReadLeaf(string path, JsonObject obj)
        {
            var valueNode = obj.Get("$value") ?? obj.Get("value");
            var type = obj.GetString("$type");
            if (string.IsNullOrEmpty(type)) type = obj.GetString("type");
            var desc = obj.GetString("$description");
            if (string.IsNullOrEmpty(desc)) desc = obj.GetString("description");

            var value = FormatValue(valueNode);
            if (string.IsNullOrEmpty(type))
                type = InferType(path, value);

            return new DesignToken
            {
                Path = path,
                Type = type,
                Value = value,
                Description = desc
            };
        }

        static string FormatValue(JsonNode node)
        {
            if (node == null || node.IsNull) return "";
            var obj = node.AsObject();
            if (obj != null)
            {
                // DTCG color object { colorSpace, components, alpha } — best-effort
                if (obj.TryGet("hex", out var hex) && hex.AsString() != null)
                    return hex.AsString();
                if (obj.TryGet("components", out var comps) && comps.AsArray() != null)
                {
                    var a = comps.AsArray().Items;
                    if (a.Count >= 3)
                    {
                        var r = (int)Math.Round((a[0].AsNumber() ?? 0) * (LooksNormalized(a[0]) ? 255 : 1));
                        var g = (int)Math.Round((a[1].AsNumber() ?? 0) * (LooksNormalized(a[1]) ? 255 : 1));
                        var b = (int)Math.Round((a[2].AsNumber() ?? 0) * (LooksNormalized(a[2]) ? 255 : 1));
                        double? alpha = null;
                        if (obj.TryGet("alpha", out var al) && al.AsNumber() != null)
                            alpha = al.AsNumber();
                        if (alpha != null && alpha.Value < 1.0)
                            return string.Format(CultureInfo.InvariantCulture,
                                "rgba({0}, {1}, {2}, {3})", r, g, b, alpha.Value);
                        return string.Format(CultureInfo.InvariantCulture, "rgb({0}, {1}, {2})", r, g, b);
                    }
                }
                // dimension { value, unit }
                if (obj.TryGet("value", out var v) && obj.TryGet("unit", out var u))
                {
                    return FormatPrimitive(v) + (u.AsString() ?? "px");
                }
            }
            return FormatPrimitive(node);
        }

        static bool LooksNormalized(JsonNode n)
        {
            var num = n.AsNumber();
            return num != null && num.Value <= 1.0;
        }

        static string FormatPrimitive(JsonNode node)
        {
            if (node == null || node.IsNull) return "";
            if (node is JsonString js) return js.Value;
            if (node is JsonNumber jn)
                return jn.Value.ToString(CultureInfo.InvariantCulture);
            if (node is JsonBool jb) return jb.Value ? "true" : "false";
            return node.AsString() ?? "";
        }

        static string InferType(string path, string value)
        {
            var p = path.ToLowerInvariant();
            if (p.Contains("color") || p.Contains("primary") || p.Contains("surface") ||
                p.Contains("error") || p.Contains("danger") || (value != null && value.StartsWith("#")))
                return "color";
            if (p.Contains("space") || p.Contains("radius") || p.Contains("fontsize") ||
                p.Contains("font-size") || p.Contains("fontSize") ||
                (value != null && (value.EndsWith("px") || value.EndsWith("rem"))))
                return "dimension";
            return "unknown";
        }

        static bool IsMetaKey(string key) =>
            key.StartsWith("$", StringComparison.Ordinal) &&
            key != "$value" && key != "$type" && key != "$description";

        static bool IsTopLevelMeta(string key) =>
            key == "$schema" || key == "schema" || key == "source" ||
            key == "modes" || key == "components" || key == "assets" ||
            key == "$themes" || key == "$metadata";
    }
}
