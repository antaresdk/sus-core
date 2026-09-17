using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Declarative Sharq generation descriptor for a UPM package.
    /// Loaded from <c>{packageRoot}/sharq.gen.json</c> (next to package.json).
    ///
    /// The package declares WHAT to generate; sus-core knows HOW
    /// (<see cref="SharqBatchCompiler"/>). Example:
    /// <code>
    /// {
    ///   "displayName": "MyUI",
    ///   "sources":   ["Components"],
    ///   "generated": "Runtime/Generated",
    ///   "resources": "Runtime/Resources/SusRuntime",
    ///   "uss": "resources",
    ///   "watch": true,
    ///   "namespace": "MyCompany.UI"
    /// }
    /// </code>
    /// </summary>
    [Serializable]
    public sealed class SusPackageDescriptor
    {
        private static readonly Regex NamespacePattern = new(
            @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$",
            RegexOptions.Compiled);

        internal const string UssModeGenerated = "generated";
        internal const string UssModeResources = "resources";

        // ─── JSON fields (JsonUtility) ───────────────────────────────
        public string displayName;
        public string[] sources;
        public string generated;
        public string resources;   // optional — empty string when the package has no runtime USS
        /// <summary>
        /// (ARCH-20260917-PKG-REFACTOR-I §5 S1) Where the generator writes its compiled
        /// <c>.g.uss</c> output. <c>"generated"</c> (default, omitted ⇒ this) — legacy
        /// behavior: USS lands in <see cref="generated"/> and is then mirrored into
        /// <see cref="resources"/> by <c>SharqCompilePipeline.SyncUssToResources</c>.
        /// <c>"resources"</c> — the generator writes <c>.g.uss</c> straight into
        /// <see cref="resources"/> (which must be non-empty); no second copy is ever
        /// produced, and a stale copy left in <see cref="generated"/> by a prior
        /// <c>"generated"</c>-mode run is pruned on the next compile.
        /// </summary>
        public string uss;
        public bool watch = true;
        /// <summary>
        /// Optional C# namespace for generated <c>.g.cs</c> types.
        /// Empty / omitted → global namespace (legacy). Downstream UI packages
        /// set this to their package root namespace (e.g. <c>MyCompany.UI</c>).
        /// </summary>
        public string @namespace;
        /// <summary>
        /// Optional extra <c>using</c> namespaces merged into every generated
        /// <c>.g.cs</c> (e.g. a package that composes types from another UI package).
        /// </summary>
        public string[] usings;

        // ─── Resolved at load time by the registry ──────────────────
        [NonSerialized] public string PackageName;   // e.g. com.example.my-ui
        [NonSerialized] public string PackageRoot;   // absolute on-disk resolvedPath

        public IReadOnlyList<string> AbsSourceDirs =>
            (sources ?? Array.Empty<string>()).Select(Abs).ToArray();

        public string AbsGeneratedDir => Abs(generated);

        /// <summary>Null when the descriptor declares no resources mirror.</summary>
        public string AbsResourcesDir => string.IsNullOrEmpty(resources) ? null : Abs(resources);

        /// <summary>
        /// (§5 S1) True when <see cref="uss"/> selects the <c>"resources"</c> mode — the
        /// generator writes <c>.g.uss</c> straight into <see cref="AbsResourcesDir"/>, no
        /// second copy in <see cref="AbsGeneratedDir"/>. False (default) is the legacy
        /// <c>"generated"</c> mode.
        /// </summary>
        public bool UssInResourcesOnly => uss == UssModeResources;

        /// <summary>
        /// Directory the generator writes <c>.g.uss</c> into: <see cref="AbsResourcesDir"/>
        /// when <see cref="UssInResourcesOnly"/>, otherwise <see cref="AbsGeneratedDir"/>
        /// (byte-identical to pre-§5-S1 behavior).
        /// </summary>
        public string AbsUssDir => UssInResourcesOnly ? AbsResourcesDir : AbsGeneratedDir;

        private string Abs(string rel) =>
            Path.GetFullPath(Path.Combine(PackageRoot ?? "", rel ?? "")).Replace('\\', '/');

        /// <summary>
        /// Parses and validates a descriptor. Returns null (with a single LogError)
        /// on malformed JSON or an invalid configuration — a broken descriptor must
        /// never break domain reload.
        /// </summary>
        public static SusPackageDescriptor Load(string jsonPath, string packageName, string packageRoot)
        {
            SusPackageDescriptor d;
            try
            {
                d = JsonUtility.FromJson<SusPackageDescriptor>(File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SusPackages] Failed to parse '{jsonPath}': {ex.Message}");
                return null;
            }

            if (d == null)
            {
                Debug.LogError($"[SusPackages] '{jsonPath}' parsed to null (empty file?)");
                return null;
            }

            d.PackageName = packageName;
            d.PackageRoot = packageRoot.Replace('\\', '/');
            if (string.IsNullOrEmpty(d.displayName)) d.displayName = packageName;

            return d.Validate(jsonPath) ? d : null;
        }

        private bool Validate(string jsonPath)
        {
            if (sources == null || sources.Length == 0 || sources.All(string.IsNullOrEmpty))
            {
                Debug.LogError($"[SusPackages] '{jsonPath}': \"sources\" must be a non-empty array.");
                return false;
            }

            if (string.IsNullOrEmpty(generated))
            {
                Debug.LogError($"[SusPackages] '{jsonPath}': \"generated\" is required.");
                return false;
            }

            if (!string.IsNullOrEmpty(@namespace) && !NamespacePattern.IsMatch(@namespace))
            {
                Debug.LogError(
                    $"[SusPackages] '{jsonPath}': \"namespace\" must be a dotted C# identifier " +
                    $"(e.g. MyCompany.UI); got '{@namespace}'.");
                return false;
            }

            if (!string.IsNullOrEmpty(uss) && uss != UssModeGenerated && uss != UssModeResources)
            {
                Debug.LogError(
                    $"[SusPackages] '{jsonPath}': \"uss\" must be \"{UssModeGenerated}\" or " +
                    $"\"{UssModeResources}\" (or omitted); got '{uss}'.");
                return false;
            }

            if (uss == UssModeResources && string.IsNullOrEmpty(resources))
            {
                Debug.LogError(
                    $"[SusPackages] '{jsonPath}': \"uss\": \"{UssModeResources}\" requires a " +
                    "non-empty \"resources\" — there would be nowhere to write .g.uss.");
                return false;
            }

            foreach (var src in AbsSourceDirs)
            {
                if (!Directory.Exists(src))
                {
                    Debug.LogError($"[SusPackages] '{jsonPath}': source directory not found: {src}");
                    return false;
                }
            }

            // Guard against self-compilation: generated must not live inside any source
            // (mirrors SharqFileImporter.IsUnderGenerated / SharqBatchCompiler's own skip).
            var gen = AbsGeneratedDir.TrimEnd('/') + "/";
            foreach (var src in AbsSourceDirs)
            {
                var s = src.TrimEnd('/') + "/";
                if (gen.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError(
                        $"[SusPackages] '{jsonPath}': \"generated\" ({generated}) must not be " +
                        $"nested inside a source directory ({src}).");
                    return false;
                }
            }

            return true;
        }
    }
}
