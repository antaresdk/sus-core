using System;
using System.IO;
using System.Linq;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>
    /// Shared classic-set (Asset Store) detection for per-module sample Setup menus (T-532).
    ///
    /// A UPM install and a classic-set install put a module's sample at two structurally
    /// different places: <c>Assets/Samples/&lt;displayName&gt;/&lt;version&gt;/&lt;sample&gt;</c>
    /// for UPM, <c>Assets/&lt;root&gt;/Samples/&lt;ModuleDir&gt;/&lt;sample&gt;</c> for a classic set
    /// (ARCH-PACK-CLASSIC.md §2.1/§3.T2-samples S2). Each Setup menu already knows how to find the
    /// UPM shape; this class adds the classic one by reading <c>sus-set.json</c> — the same manifest
    /// <see cref="SusSetDoctor"/> uses — instead of guessing at path substrings (T-532: the previous
    /// code only searched the UPM sample path and the package cache, so a classic install printed a
    /// misleading "check that the package is installed" message about a package the purchaser, who
    /// bought a classic asset and has no such package, was never going to have).
    /// </summary>
    public static class SusClassicSampleLocator
    {
        /// <summary>
        /// True when a <c>sus-set.json</c> manifest is present anywhere in the project — i.e. this
        /// project has a classic-set install of *some* набор (not necessarily one containing the
        /// module asking).
        /// </summary>
        public static bool IsClassicSetInstalled() => TryFindManifest(out _, out _);

        /// <summary>
        /// Resolves the sample folder for <paramref name="moduleId"/> (manifest key, e.g. "kit",
        /// "game") under the installed classic set, if any. Returns false — never throws — when no
        /// set is installed, the manifest doesn't list this module, or the folder isn't on disk
        /// (residual manifest with the module removed by the purchaser).
        /// </summary>
        public static bool TryFindClassicSample(
            string moduleId, string sampleSubfolder, out string assetsFolder, out SusSetManifest manifest)
        {
            assetsFolder = null;
            manifest = null;

            if (!TryFindManifest(out var manifestPath, out var json))
                return false;

            manifest = SusSetManifest.Parse(json);
            if (manifest?.modules == null)
                return false;

            var mod = manifest.modules.FirstOrDefault(m =>
                string.Equals(m.id, moduleId, StringComparison.OrdinalIgnoreCase));
            if (mod == null || string.IsNullOrEmpty(mod.dir))
                return false;

            var candidate = string.IsNullOrEmpty(sampleSubfolder)
                ? $"Assets/{manifest.root}/Samples/{mod.dir}"
                : $"Assets/{manifest.root}/Samples/{mod.dir}/{sampleSubfolder}";
            if (!Directory.Exists(candidate))
                return false;

            assetsFolder = candidate;
            return true;
        }

        static bool TryFindManifest(out string assetPath, out string json)
        {
            assetPath = null;
            json = null;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("sus-set"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileName(path), SusSetDoctor.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { json = File.ReadAllText(path); }
                catch (IOException) { return false; } // transient (mid-import)
                assetPath = path;
                return true;
            }
            return false;
        }
    }
}
