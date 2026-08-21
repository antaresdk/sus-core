using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sharq.Core.Editor.DesignImport
{
    /// <summary>
    /// Shared pipeline for CLI and Editor: normalize → map → emit override USS + meta sidecar.
    /// Never patches shipped design-tokens.uss (ARCH-DESIGN-IMPORT D2).
    /// </summary>
    public static class DesignImporter
    {
        public static DesignDocument Parse(string jsonText) => DesignNormalizer.Normalize(jsonText);

        public static ValidateResult Validate(string jsonText, ImportOptions options = null)
        {
            options = options ?? new ImportOptions();
            var result = new ValidateResult();
            DesignDocument doc;
            try
            {
                doc = DesignNormalizer.Normalize(jsonText);
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Errors.Add(ex.Message);
                return result;
            }

            result.Warnings.AddRange(doc.Warnings);
            var map = AliasMap.LoadDefault(options.AliasMapPath);

            // Ghost names appearing as token paths or values that look like --sus-*
            foreach (var t in doc.Tokens)
            {
                CheckGhost(t.Path, map, result);
                CheckGhost(t.Value, map, result);

                if (!TryMapToken(t, map, options, out _, out var skipReason))
                {
                    if (skipReason == "unknown")
                        result.UnknownAliases.Add(t.Path);
                }
            }

            // Explicit reject for classic ghost examples
            foreach (var ghost in new[] { "--sus-fail", "sus-fail", "--sus-btn-primary", "--sus-font-16" })
            {
                if (jsonText.IndexOf(ghost, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    map.LooksLikeGhostSusVar(ghost.StartsWith("--") ? ghost : "--" + ghost.TrimStart('-')))
                {
                    var normalized = ghost.StartsWith("--") ? ghost : "--" + ghost.TrimStart('-');
                    if (!result.GhostCssVars.Contains(normalized))
                        result.GhostCssVars.Add(normalized);
                }
            }

            if (result.GhostCssVars.Count > 0)
            {
                result.Ok = false;
                foreach (var g in result.GhostCssVars)
                    result.Errors.Add($"ghost CSS var rejected: {g}");
            }

            if (result.UnknownAliases.Count > 0 && !options.EmitUnknown)
            {
                result.Ok = false;
                foreach (var u in result.UnknownAliases.Distinct().OrderBy(x => x))
                    result.Errors.Add($"unknown token alias (not in map): {u}");
            }

            if (result.Errors.Count == 0)
                result.Ok = true;

            return result;
        }

        public static ImportResult Import(string jsonText, ImportOptions options = null)
        {
            options = options ?? new ImportOptions();
            var result = new ImportResult
            {
                InputSha256 = Sha256Hex(jsonText)
            };

            DesignDocument doc;
            try
            {
                doc = DesignNormalizer.Normalize(jsonText);
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Errors.Add(ex.Message);
                return result;
            }

            result.Warnings.AddRange(doc.Warnings);
            var map = AliasMap.LoadDefault(options.AliasMapPath);

            // Validate ghosts first
            var validation = Validate(jsonText, options);
            if (!validation.Ok)
            {
                result.Ok = false;
                result.Errors.AddRange(validation.Errors);
                result.Warnings.AddRange(validation.Warnings.Where(w => !result.Warnings.Contains(w)));
                return result;
            }

            foreach (var t in doc.Tokens)
            {
                if (TryMapToken(t, map, options, out var mapped, out var skipReason))
                {
                    result.Mapped.Add(mapped);
                }
                else
                {
                    result.Skipped.Add($"{t.Path} ({skipReason})");
                    if (skipReason == "unknown" && options.EmitUnknown)
                    {
                        var appVar = "--app-" + SanitizeAppName(t.Path);
                        result.Mapped.Add(new MappedToken
                        {
                            AliasPath = t.Path,
                            CssVar = appVar,
                            Value = t.Value,
                            IsDownstream = false
                        });
                        result.Warnings.Add($"emit-unknown: {t.Path} → {appVar}");
                    }
                }
            }

            var r22 = new List<string>();
            result.Uss = UssEmitter.Emit(result.Mapped, r22);
            if (r22.Count > 0)
            {
                result.Ok = false;
                result.Errors.AddRange(r22);
                return result;
            }

            var ts = options.TimestampUtc ?? DateTime.UtcNow;
            result.MetaJson = BuildMeta(result, doc, ts);

            if (!options.DryRun && !string.IsNullOrEmpty(options.OutDir))
            {
                Directory.CreateDirectory(options.OutDir);
                var ussPath = Path.Combine(options.OutDir, options.UssFileName);
                var metaPath = Path.Combine(options.OutDir, options.MetaFileName);
                // Idempotent USS write (stable content)
                File.WriteAllText(ussPath, result.Uss, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(metaPath, result.MetaJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            result.Ok = result.Errors.Count == 0;
            return result;
        }

        public static string MapList(ImportOptions options = null)
        {
            options = options ?? new ImportOptions();
            var map = AliasMap.LoadDefault(options.AliasMapPath);
            var sb = new StringBuilder();
            sb.AppendLine("alias → css-var");
            foreach (var line in map.ListAliases(options.Downstream))
                sb.AppendLine(line);
            return sb.ToString();
        }

        static bool TryMapToken(
            DesignToken token,
            AliasMap map,
            ImportOptions options,
            out MappedToken mapped,
            out string skipReason)
        {
            mapped = null;
            skipReason = "unknown";

            var candidates = ExpandAliasCandidates(token.Path);
            foreach (var c in candidates)
            {
                if (map.TryResolve(c, options.Downstream, out var entry))
                {
                    if (entry.Downstream && !options.Downstream)
                    {
                        skipReason = "downstream-disabled";
                        return false;
                    }
                    mapped = new MappedToken
                    {
                        AliasPath = token.Path,
                        CssVar = entry.CssVar,
                        Value = token.Value,
                        IsDownstream = entry.Downstream
                    };
                    skipReason = null;
                    return true;
                }
            }

            // Ghost --sus-* in path
            if (map.LooksLikeGhostSusVar(token.Path) || map.LooksLikeGhostSusVar(token.Value))
            {
                skipReason = "ghost";
                return false;
            }

            skipReason = "unknown";
            return false;
        }

        static IEnumerable<string> ExpandAliasCandidates(string path)
        {
            if (string.IsNullOrEmpty(path)) yield break;
            yield return path;

            var key = AliasMap.NormalizeKey(path);
            yield return key;

            // color.primary.hover already full; also try without first group if duplicated
            var parts = key.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                yield return string.Join(".", parts.Skip(1));
                yield return parts[parts.Length - 1];
            }

            // Tokens Studio sometimes uses "Primary" under Colors
            if (parts.Length >= 1)
                yield return parts[parts.Length - 1];
        }

        static void CheckGhost(string text, AliasMap map, ValidateResult result)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Find --sus-* substrings
            var idx = 0;
            while (idx < text.Length)
            {
                var at = text.IndexOf("--sus-", idx, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;
                var end = at + 6;
                while (end < text.Length)
                {
                    var c = text[end];
                    if (char.IsLetterOrDigit(c) || c == '-' || c == '_') end++;
                    else break;
                }
                var css = text.Substring(at, end - at);
                if (map.LooksLikeGhostSusVar(css) && !result.GhostCssVars.Contains(css))
                    result.GhostCssVars.Add(css);
                idx = end;
            }

            // Bare ghost path like color referencing sus-fail
            if (map.LooksLikeGhostSusVar("--" + text.TrimStart('-')) && text.IndexOf("sus-", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var g = text.StartsWith("--") ? text : "--" + text.TrimStart('-');
                if (g.StartsWith("--sus-", StringComparison.OrdinalIgnoreCase) && !map.IsKnownCssVar(g))
                {
                    if (!result.GhostCssVars.Contains(g))
                        result.GhostCssVars.Add(g);
                }
            }
        }

        static string SanitizeAppName(string path)
        {
            var sb = new StringBuilder();
            foreach (var c in path.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == '.' || c == '-' || c == '_' || c == '/') sb.Append('-');
            }
            var s = sb.ToString().Trim('-');
            while (s.Contains("--")) s = s.Replace("--", "-");
            return string.IsNullOrEmpty(s) ? "token" : s;
        }

        static string BuildMeta(ImportResult result, DesignDocument doc, DateTime utc)
        {
            var ordered = result.Mapped.OrderBy(x => x.CssVar, StringComparer.Ordinal).ToList();
            var skipped = result.Skipped.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"$schema\": \"sus-design-meta/v1\",");
            sb.Append("  \"inputSha256\": \"").Append(result.InputSha256).AppendLine("\",");
            sb.Append("  \"tool\": \"").Append(DesignJson.Escape(doc.Source.Tool)).AppendLine("\",");
            sb.Append("  \"sourceFile\": \"").Append(DesignJson.Escape(doc.Source.File)).AppendLine("\",");
            sb.Append("  \"timestampUtc\": \"").Append(utc.ToString("o", CultureInfo.InvariantCulture)).AppendLine("\",");
            sb.AppendLine("  \"mapped\": [");
            for (var i = 0; i < ordered.Count; i++)
            {
                var m = ordered[i];
                sb.Append("    { \"alias\": \"");
                sb.Append(DesignJson.Escape(m.AliasPath));
                sb.Append("\", \"css\": \"");
                sb.Append(DesignJson.Escape(m.CssVar));
                sb.Append("\", \"value\": \"");
                sb.Append(DesignJson.Escape(m.Value));
                sb.Append("\" }");
                sb.AppendLine(i < ordered.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"skipped\": [");
            for (var i = 0; i < skipped.Count; i++)
            {
                sb.Append("    \"").Append(DesignJson.Escape(skipped[i])).Append("\"");
                sb.AppendLine(i < skipped.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Idempotency helper: USS content must match for same input (meta timestamp excluded).
        /// </summary>
        public static bool UssEquals(string a, string b) =>
            string.Equals(a?.Replace("\r\n", "\n"), b?.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }
}
