using System.Collections.Generic;
using System.IO;
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
    /// The list is cached per domain reload; call <see cref="Refresh"/> after
    /// adding or editing a descriptor.
    /// </summary>
    public static class SusPackageRegistry
    {
        private static List<SusPackageDescriptor> s_packages;

        public const string DescriptorFileName = "sharq.gen.json";

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

        /// <summary>Re-scans all resolved packages for descriptors.</summary>
        public static void Refresh()
        {
            var list = new List<SusPackageDescriptor>();

            foreach (var info in PackageInfo.GetAllRegisteredPackages())
            {
                // Mutable on-disk packages only.
                if (info.source != PackageSource.Embedded && info.source != PackageSource.Local)
                    continue;

                var jsonPath = Path.Combine(info.resolvedPath, DescriptorFileName);
                if (!File.Exists(jsonPath)) continue;

                var d = SusPackageDescriptor.Load(jsonPath, info.name, info.resolvedPath);
                if (d != null) list.Add(d);
            }

            s_packages = list;
        }
    }
}
