using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sharq.Core.Editor.DesignImport
{
    /// <summary>
    /// Alias → canonical USS custom property. Downstream (--sk-*) rows are opt-in via ImportOptions.Downstream
    /// (or config target=downstream). Core never hardcodes paid package product names (R25).
    /// </summary>
    public sealed class AliasMap
    {
        readonly Dictionary<string, AliasEntry> _byAlias =
            new Dictionary<string, AliasEntry>(StringComparer.OrdinalIgnoreCase);

        readonly HashSet<string> _knownCss =
            new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, AliasEntry> Entries => _byAlias;
        public IReadOnlyCollection<string> KnownCssVars => _knownCss;

        public static AliasMap LoadFromJson(string json)
        {
            var root = DesignJson.Parse(json).AsObject()
                ?? throw new FormatException("alias-map root must be an object");
            var map = new AliasMap();
            if (!root.TryGet("aliases", out var aliasesNode) || aliasesNode.AsArray() == null)
                throw new FormatException("alias-map requires 'aliases' array");

            foreach (var item in aliasesNode.AsArray().Items)
            {
                var o = item.AsObject();
                if (o == null) continue;
                var alias = o.GetString("alias");
                var css = o.GetString("css");
                if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(css)) continue;
                var downstream = o.GetString("target") == "downstream"
                    || css.StartsWith("--sk-", StringComparison.Ordinal);
                var entry = new AliasEntry(alias, css, downstream);
                map._byAlias[NormalizeKey(alias)] = entry;
                map._knownCss.Add(css);
            }

            return map;
        }

        public static AliasMap LoadDefault(string explicitPath = null)
        {
            var path = ResolveAliasMapPath(explicitPath);
            if (path == null || !File.Exists(path))
                return LoadFromJson(DefaultAliasMapJson);
            return LoadFromJson(File.ReadAllText(path, Encoding.UTF8));
        }

        public static string ResolveAliasMapPath(string explicitPath = null)
        {
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
                return Path.GetFullPath(explicitPath);

            // Tools~/SusDesignImport/alias-map.json next to package root
            foreach (var candidate in CandidatePaths())
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        static IEnumerable<string> CandidatePaths()
        {
            var list = new List<string>();
            var cwd = Directory.GetCurrentDirectory();
            list.Add(Path.Combine(cwd, "alias-map.json"));
            list.Add(Path.Combine(cwd, "Tools~", "SusDesignImport", "alias-map.json"));

            // Walk up from CWD looking for sus-core package markers
            var dir = new DirectoryInfo(cwd);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                list.Add(Path.Combine(dir.FullName, "Tools~", "SusDesignImport", "alias-map.json"));
                list.Add(Path.Combine(dir.FullName, "sus-core", "Tools~", "SusDesignImport", "alias-map.json"));
            }

#if UNITY_EDITOR
            try
            {
                var asm = typeof(AliasMap).Assembly;
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(asm);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                {
                    list.Add(Path.Combine(pkg.resolvedPath, "Tools~", "SusDesignImport", "alias-map.json"));
                }
            }
            catch
            {
                // ignore — fall back to embedded default
            }
#endif
            return list;
        }

        public bool TryResolve(string aliasOrPath, bool allowDownstream, out AliasEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(aliasOrPath)) return false;

            var key = NormalizeKey(aliasOrPath);
            if (_byAlias.TryGetValue(key, out entry))
            {
                if (entry.Downstream && !allowDownstream) return false;
                return true;
            }

            // Already a known css var?
            if (aliasOrPath.StartsWith("--", StringComparison.Ordinal))
            {
                if (!_knownCss.Contains(aliasOrPath)) return false;
                if (aliasOrPath.StartsWith("--sk-", StringComparison.Ordinal) && !allowDownstream)
                    return false;
                entry = new AliasEntry(aliasOrPath, aliasOrPath,
                    aliasOrPath.StartsWith("--sk-", StringComparison.Ordinal));
                return true;
            }

            return false;
        }

        public bool IsKnownCssVar(string cssVar) =>
            !string.IsNullOrEmpty(cssVar) && _knownCss.Contains(cssVar);

        public bool LooksLikeGhostSusVar(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var v = name.StartsWith("--", StringComparison.Ordinal) ? name : "--" + name.TrimStart('-');
            if (!v.StartsWith("--sus-", StringComparison.Ordinal)) return false;
            return !_knownCss.Contains(v);
        }

        public IEnumerable<string> ListAliases(bool includeDownstream)
        {
            return _byAlias.Values
                .Where(e => includeDownstream || !e.Downstream)
                .OrderBy(e => e.Alias, StringComparer.OrdinalIgnoreCase)
                .Select(e => $"{e.Alias} → {e.CssVar}");
        }

        public static string NormalizeKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = s.Trim();
            // collapse spaces, unify separators to dots for lookup variants
            t = t.Replace('\\', '.').Replace('/', '.');
            while (t.Contains("..")) t = t.Replace("..", ".");
            return t.Trim('.');
        }

        /// <summary>MVP alias table (mirrors Tools~/SusDesignImport/alias-map.json).</summary>
        public const string DefaultAliasMapJson = @"{
  ""$schema"": ""sus-design-alias-map/v1"",
  ""aliases"": [
    { ""alias"": ""color.primary"", ""css"": ""--sus-primary"" },
    { ""alias"": ""primary"", ""css"": ""--sus-primary"" },
    { ""alias"": ""Primary"", ""css"": ""--sus-primary"" },
    { ""alias"": ""color.primary.hover"", ""css"": ""--sus-primary-hover"" },
    { ""alias"": ""primary.hover"", ""css"": ""--sus-primary-hover"" },
    { ""alias"": ""color.primary.pressed"", ""css"": ""--sus-primary-pressed"" },
    { ""alias"": ""color.bg.surface"", ""css"": ""--sus-bg-surface"" },
    { ""alias"": ""bg.surface"", ""css"": ""--sus-bg-surface"" },
    { ""alias"": ""surface"", ""css"": ""--sus-bg-surface"" },
    { ""alias"": ""color.bg.page"", ""css"": ""--sus-bg-page"" },
    { ""alias"": ""color.text.primary"", ""css"": ""--sus-text-primary"" },
    { ""alias"": ""text.primary"", ""css"": ""--sus-text-primary"" },
    { ""alias"": ""color.error"", ""css"": ""--sus-error"" },
    { ""alias"": ""error"", ""css"": ""--sus-error"" },
    { ""alias"": ""danger"", ""css"": ""--sus-error"" },
    { ""alias"": ""color.success"", ""css"": ""--sus-success"" },
    { ""alias"": ""color.warning"", ""css"": ""--sus-warning"" },
    { ""alias"": ""color.info"", ""css"": ""--sus-info"" },
    { ""alias"": ""color.secondary"", ""css"": ""--sus-secondary"" },
    { ""alias"": ""dimension.space.0"", ""css"": ""--sus-space-0"" },
    { ""alias"": ""space.0"", ""css"": ""--sus-space-0"" },
    { ""alias"": ""dimension.space.4"", ""css"": ""--sus-space-4"" },
    { ""alias"": ""space.4"", ""css"": ""--sus-space-4"" },
    { ""alias"": ""dimension.space.8"", ""css"": ""--sus-space-8"" },
    { ""alias"": ""space.8"", ""css"": ""--sus-space-8"" },
    { ""alias"": ""dimension.space.12"", ""css"": ""--sus-space-12"" },
    { ""alias"": ""space.12"", ""css"": ""--sus-space-12"" },
    { ""alias"": ""dimension.space.16"", ""css"": ""--sus-space-16"" },
    { ""alias"": ""space.16"", ""css"": ""--sus-space-16"" },
    { ""alias"": ""dimension.space.24"", ""css"": ""--sus-space-24"" },
    { ""alias"": ""space.24"", ""css"": ""--sus-space-24"" },
    { ""alias"": ""dimension.space.32"", ""css"": ""--sus-space-32"" },
    { ""alias"": ""space.32"", ""css"": ""--sus-space-32"" },
    { ""alias"": ""dimension.space.48"", ""css"": ""--sus-space-48"" },
    { ""alias"": ""space.48"", ""css"": ""--sus-space-48"" },
    { ""alias"": ""dimension.space.64"", ""css"": ""--sus-space-64"" },
    { ""alias"": ""space.64"", ""css"": ""--sus-space-64"" },
    { ""alias"": ""dimension.radius.sm"", ""css"": ""--sus-radius-sm"" },
    { ""alias"": ""radius.sm"", ""css"": ""--sus-radius-sm"" },
    { ""alias"": ""dimension.radius.md"", ""css"": ""--sus-radius-md"" },
    { ""alias"": ""radius.md"", ""css"": ""--sus-radius-md"" },
    { ""alias"": ""dimension.radius.lg"", ""css"": ""--sus-radius-lg"" },
    { ""alias"": ""radius.lg"", ""css"": ""--sus-radius-lg"" },
    { ""alias"": ""dimension.radius.xl"", ""css"": ""--sus-radius-xl"" },
    { ""alias"": ""radius.xl"", ""css"": ""--sus-radius-xl"" },
    { ""alias"": ""dimension.radius.full"", ""css"": ""--sus-radius-full"" },
    { ""alias"": ""radius.full"", ""css"": ""--sus-radius-full"" },
    { ""alias"": ""typography.fontSize.body"", ""css"": ""--sus-font-size-body"" },
    { ""alias"": ""fontSize.body"", ""css"": ""--sus-font-size-body"" },
    { ""alias"": ""sk.color.primary"", ""css"": ""--sk-color-primary"", ""target"": ""downstream"" }
  ]
}";
    }

    public sealed class AliasEntry
    {
        public string Alias { get; }
        public string CssVar { get; }
        public bool Downstream { get; }

        public AliasEntry(string alias, string cssVar, bool downstream)
        {
            Alias = alias;
            CssVar = cssVar;
            Downstream = downstream;
        }
    }
}
