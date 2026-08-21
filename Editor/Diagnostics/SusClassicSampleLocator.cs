using System;
using System.IO;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>
    /// Shared classic-set (Asset Store) detection for per-module sample Setup menus.
    ///
    /// A UPM install and a classic-set install put a module's sample at two structurally
    /// different places: <c>Assets/Samples/&lt;displayName&gt;/&lt;version&gt;/&lt;sample&gt;</c>
    /// for UPM, <c>Assets/&lt;root&gt;/Samples/&lt;ModuleDir&gt;/&lt;sample&gt;</c> for a classic set
    /// (ARCH-PACK-CLASSIC.md §2.1/§3.T2-samples S2). Each Setup menu already knows how to find the
    /// UPM shape; this class adds the classic one.
    ///
    /// Since D7 this reads the asking module's OWN <c>sus-module.json</c> directly (by
    /// module id) instead of going through a shared <c>sus-set.json</c>'s module list (the
    /// original approach): the old approach broke exactly the scenario D7 exists to fix — after
    /// importing kit-set on top of game-set, the single shared manifest no longer mentioned
    /// "game" at all, so this locator (and Set Doctor) lost track of the Game sample even though
    /// its files, and its own manifest, were still sitting untouched on disk. Reading the
    /// module's own manifest by id sidesteps that: it doesn't matter which set descriptor(s) are
    /// present, only whether THIS module's own manifest is.
    /// </summary>
    public static class SusClassicSampleLocator
    {
        /// <summary>
        /// True when any classic-set manifest (a module's <c>sus-module.json</c> or a set's
        /// <c>sus-set.&lt;set&gt;.json</c>) is present anywhere in the project — i.e. this
        /// project has SOME classic-set install, not necessarily one containing the module asking.
        /// </summary>
        public static bool IsClassicSetInstalled()
        {
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("sus-module"))
            {
                if (string.Equals(Path.GetFileName(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)),
                        SusSetDoctor.ModuleManifestFileName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("sus-set"))
            {
                if (SusSetDoctor.IsSetDescriptorFileName(Path.GetFileName(UnityEditor.AssetDatabase.GUIDToAssetPath(guid))))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the sample folder for <paramref name="moduleId"/> (manifest key, e.g. "kit",
        /// "game") under the installed classic module, if any. Returns false — never throws —
        /// when this module isn't present as a classic module (no manifest), or the sample
        /// folder isn't on disk (residual manifest with the sample removed by the purchaser).
        /// </summary>
        public static bool TryFindClassicSample(
            string moduleId, string sampleSubfolder, out string assetsFolder, out SusModuleManifest manifest)
        {
            assetsFolder = null;
            manifest = null;

            if (!TryFindModuleManifest(moduleId, out _, out var json))
                return false;

            manifest = SusModuleManifest.Parse(json);
            if (manifest == null || string.IsNullOrEmpty(manifest.root) || string.IsNullOrEmpty(manifest.dir))
                return false;

            var candidate = string.IsNullOrEmpty(sampleSubfolder)
                ? $"Assets/{manifest.root}/Samples/{manifest.dir}"
                : $"Assets/{manifest.root}/Samples/{manifest.dir}/{sampleSubfolder}";
            if (!Directory.Exists(candidate))
                return false;

            assetsFolder = candidate;
            return true;
        }

        private static bool TryFindModuleManifest(string moduleId, out string assetPath, out string json)
        {
            assetPath = null;
            json = null;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("sus-module"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileName(path), SusSetDoctor.ModuleManifestFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; } // transient (mid-import) — try the next candidate

                var parsed = SusModuleManifest.Parse(text);
                if (parsed == null || !string.Equals(parsed.id, moduleId, StringComparison.OrdinalIgnoreCase))
                    continue;

                assetPath = path;
                json = text;
                return true;
            }
            return false;
        }
    }
}
