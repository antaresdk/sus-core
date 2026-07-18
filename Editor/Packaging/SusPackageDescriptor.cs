using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    ///   "displayName": "DownstreamLib",
    ///   "sources":   ["Components"],
    ///   "generated": "Runtime/Generated",
    ///   "resources": "Runtime/Resources/SusRuntime",
    ///   "watch": true
    /// }
    /// </code>
    /// </summary>
    [Serializable]
    public sealed class SusPackageDescriptor
    {
        // ─── JSON fields (JsonUtility) ───────────────────────────────
        public string displayName;
        public string[] sources;
        public string generated;
        public string resources;   // optional — empty string when the package has no runtime USS
        public bool watch = true;

        // ─── Resolved at load time by the registry ──────────────────
        [NonSerialized] public string PackageName;   // e.g. com.sharq-it.sus.game
        [NonSerialized] public string PackageRoot;   // absolute on-disk resolvedPath

        public IReadOnlyList<string> AbsSourceDirs =>
            (sources ?? Array.Empty<string>()).Select(Abs).ToArray();

        public string AbsGeneratedDir => Abs(generated);

        /// <summary>Null when the descriptor declares no resources mirror.</summary>
        public string AbsResourcesDir => string.IsNullOrEmpty(resources) ? null : Abs(resources);

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
