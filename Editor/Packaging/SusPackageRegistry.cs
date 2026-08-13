using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Discovers Sharq-generation descriptors (<c>sharq.gen.json</c>) across all
    /// resolved UPM packages. Only mutable packages (Embedded / Local, i.e.
    /// <c>file:</c> references) are considered — registry/git/tarball packages ship
    /// read-only <c>Generated/</c> artifacts and never need regeneration on the
    /// consumer side.
    ///
    /// Also discovers descriptors that live directly under <c>Assets/</c> with no UPM
    /// registration at all — the classic .unitypackage channel ships modules as plain
    /// asset folders (<c>Assets/Sharq/&lt;Module&gt;/sharq.gen.json</c>, no
    /// <c>package.json</c>, see ARCH-PACK-CLASSIC.md §3 T6). Every path inside a
    /// descriptor is already relative to its own folder, so the module root is simply
    /// "wherever the descriptor file was found".
    ///
    /// The list is cached per domain reload; call <see cref="Refresh"/> after
    /// adding or editing a descriptor.
    /// </summary>
    public static class SusPackageRegistry
    {
        private static List<SusPackageDescriptor> s_packages;

        public const string DescriptorFileName = "sharq.gen.json";

        /// <summary>AssetDatabase name filter for <see cref="DescriptorFileName"/> — Unity's
        /// plain-string search matches the asset name without its last extension.</summary>
        private const string DescriptorSearchTerm = "sharq.gen";

        /// <summary>All valid descriptors from mutable packages (lazy, cached).</summary>
        public static IReadOnlyList<SusPackageDescriptor> Packages
        {
            get
            {
                if (s_packages == null) Refresh();
                return s_packages;
            }
        }

        /// <summary>Finds a descriptor by UPM package name or displayName (case-insensitive).</summary>
        public static SusPackageDescriptor Find(string nameOrDisplayName)
        {
            foreach (var d in Packages)
            {
                if (string.Equals(d.PackageName, nameOrDisplayName, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(d.displayName, nameOrDisplayName, System.StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            return null;
        }

        /// <summary>Re-scans all resolved packages (and, for the classic channel, plain
        /// <c>Assets/</c> folders) for descriptors.</summary>
        public static void Refresh()
        {
            var list = new List<SusPackageDescriptor>();
            var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var info in PackageInfo.GetAllRegisteredPackages())
            {
                // Mutable on-disk packages only.
                if (info.source != PackageSource.Embedded && info.source != PackageSource.Local)
                    continue;

                var jsonPath = Path.Combine(info.resolvedPath, DescriptorFileName);
                if (!File.Exists(jsonPath)) continue;

                var d = SusPackageDescriptor.Load(jsonPath, info.name, info.resolvedPath);
                if (d == null) continue;

                list.Add(d);
                seenRoots.Add(NormalizeRoot(info.resolvedPath));
            }

            foreach (var moduleRoot in FindAssetsDescriptorRoots())
            {
                var normalizedRoot = NormalizeRoot(moduleRoot);
                if (!seenRoots.Add(normalizedRoot)) continue; // already covered by a UPM package

                var jsonPath = Path.Combine(moduleRoot, DescriptorFileName);
                // No UPM registration to name the module — fall back to the folder name
                // (e.g. "Kit"); Find() also matches on the descriptor's own displayName.
                var packageName = Path.GetFileName(normalizedRoot);
                var d = SusPackageDescriptor.Load(jsonPath, packageName, moduleRoot);
                if (d != null) list.Add(d);
            }

            s_packages = list;
        }

        /// <summary>Scans <c>Assets/</c> for <c>sharq.gen.json</c> files not covered by any
        /// registered UPM package, returning each descriptor's containing folder (absolute path).</summary>
        private static IEnumerable<string> FindAssetsDescriptorRoots()
        {
            foreach (var guid in AssetDatabase.FindAssets(DescriptorSearchTerm))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath) || !IsDescriptorAssetPath(assetPath))
                    continue; // e.g. an unrelated "sharq.gen.something" asset

                var moduleRoot = ModuleRootFromDescriptorPath(Path.GetFullPath(assetPath));
                if (!string.IsNullOrEmpty(moduleRoot))
                    yield return moduleRoot;
            }
        }

        /// <summary>True when an AssetDatabase-relative path is exactly a
        /// <see cref="DescriptorFileName"/> (case-insensitive; guards against
        /// <see cref="DescriptorSearchTerm"/> matching an unrelated "sharq.gen.*" asset).</summary>
        internal static bool IsDescriptorAssetPath(string assetPath) =>
            !string.IsNullOrEmpty(assetPath)
            && string.Equals(Path.GetFileName(assetPath), DescriptorFileName, StringComparison.OrdinalIgnoreCase);

        /// <summary>The module root (containing folder) for an absolute descriptor path.</summary>
        internal static string ModuleRootFromDescriptorPath(string absJsonPath) =>
            Path.GetDirectoryName(absJsonPath);

        internal static string NormalizeRoot(string path) =>
            path.Replace('\\', '/').TrimEnd('/');
    }
}
