using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Generic <see cref="ISusIconProvider"/> that serves SVG <see cref="VectorImage"/>s from a
    /// single Resources icon collection laid out as
    /// <c>Resources/SusRuntime/Icons/{collection}/{weight}/{name}.svg</c>.
    ///
    /// Parameterised by the owning package (for the editor asset-path fallback / .meta repair)
    /// and the collection folder, so multiple icon sets — the minimal built-in
    /// <see cref="CoreIconProvider"/> shipped in sus-core and the optional 9000-icon Phosphor
    /// package — reuse the exact same loading logic without duplication.
    /// </summary>
    public class ResourcesFolderIconProvider : ISusIconProvider
    {
        private readonly string _editorPackagePath; // e.g. "com.sharq-it.sus.core" (may be null)
        private readonly string _collection;        // e.g. "core" / "phosphor" / "app"

        private readonly Dictionary<string, VectorImage> _cache = new();
        private readonly Dictionary<string, Dictionary<SusIconWeight, string>> _iconPaths = new();
#if UNITY_EDITOR
        // resource-path (SusRuntime/Icons/{collection}/{weight}/{name}) → concrete AssetDatabase path,
        // populated by the editor scan so .meta repair works for icons in ANY Resources folder
        // (including a consumer's Customization/Icons/Resources/…), not just the owning package.
        private readonly Dictionary<string, string> _editorAssetPaths = new();
#endif
        private HashSet<string> _knownNames;
        private bool _triedRefresh;

        /// <param name="editorPackagePath">
        /// Owning UPM package id for the editor scan / .meta repair (e.g. "com.sharq-it.sus.core").
        /// Pass <c>null</c> for project-local icons that live under any <c>Assets/**/Resources</c>
        /// folder (e.g. the Setup wizard's <c>Customization/Icons/Resources</c>).
        /// </param>
        /// <param name="collection">Collection folder, e.g. "core" / "phosphor" / "app".</param>
        public ResourcesFolderIconProvider(string editorPackagePath, string collection)
        {
            _editorPackagePath = editorPackagePath;
            _collection = collection;
        }

        /// <summary>
        /// Project-local overload: icons served from any <c>Assets/**/Resources/SusRuntime/Icons/{collection}</c>
        /// folder, no owning package. Use this for a consumer app's own icons.
        /// </summary>
        public ResourcesFolderIconProvider(string collection) : this(null, collection) { }

        public IEnumerable<string> KnownNames => _knownNames ??= Scan();

        public void Invalidate()
        {
            _cache.Clear();
            _iconPaths.Clear();
            _knownNames = null;
            _triedRefresh = false;
        }

        public VectorImage Load(string name, SusIconWeight weight)
        {
            if (string.IsNullOrEmpty(name)) return null;

            _ = KnownNames; // ensure scan populated _iconPaths

            var folder = WeightFolder(weight);
            var key = $"{folder}/{name}";
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            string path;
            if (_iconPaths.TryGetValue(name, out var weightPaths) && weightPaths.TryGetValue(weight, out var scanned))
                path = scanned;
            else
            {
                var fileName = folder == "regular" ? name : $"{name}-{folder}";
                path = $"SusRuntime/Icons/{_collection}/{folder}/{fileName}";
            }

            var img = Resources.Load<VectorImage>(path);

#if UNITY_EDITOR
            if (img == null)
                img = LoadInEditorWithRepair(path);
#endif

            if (img != null)
                _cache[key] = img;
            return img;
        }

        private static string WeightFolder(SusIconWeight weight) => weight switch
        {
            SusIconWeight.Thin => "thin",
            SusIconWeight.Light => "light",
            SusIconWeight.Regular => "regular",
            SusIconWeight.Bold => "bold",
            SusIconWeight.Fill => "fill",
            SusIconWeight.Duotone => "duotone",
            _ => "regular",
        };

        private static SusIconWeight ParseWeight(string folder) => folder switch
        {
            "thin" => SusIconWeight.Thin,
            "light" => SusIconWeight.Light,
            "regular" => SusIconWeight.Regular,
            "bold" => SusIconWeight.Bold,
            "fill" => SusIconWeight.Fill,
            "duotone" => SusIconWeight.Duotone,
            _ => SusIconWeight.Regular,
        };

        private static string StripWeightSuffix(string rawName, string weightFolder)
        {
            if (weightFolder == "regular") return rawName;
            var suffix = $"-{weightFolder}";
            return rawName.EndsWith(suffix) ? rawName.Substring(0, rawName.Length - suffix.Length) : rawName;
        }

        private HashSet<string> Scan()
        {
            var set = new HashSet<string>();
            _iconPaths.Clear();

#if UNITY_EDITOR
            _editorAssetPaths.Clear();

            // 1) Owning package (fast path for core / phosphor). Skipped when no package id is given.
            if (!string.IsNullOrEmpty(_editorPackagePath))
            {
                var iconsRoot = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, $"../Packages/{_editorPackagePath}/Runtime/Resources/SusRuntime/Icons/{_collection}"));
                if (System.IO.Directory.Exists(iconsRoot))
                {
                    var prefixLen = iconsRoot.Length + 1;
                    foreach (var file in System.IO.Directory.GetFiles(iconsRoot, "*.svg", System.IO.SearchOption.AllDirectories))
                    {
                        var relative = file.Substring(prefixLen).Replace('\\', '/');
                        var slash = relative.LastIndexOf('/');
                        if (slash < 0) continue;
                        RegisterScanned(set, relative.Substring(0, slash), System.IO.Path.GetFileNameWithoutExtension(file), null);
                    }
                }
            }

            // 2) AssetDatabase fallback — discovers icons in ANY Resources folder (top-level
            // Assets/Resources AND nested Customization/Icons/Resources/…), matching the runtime
            // Resources.LoadAll behaviour so KnownNames / .meta repair work for consumer icons.
            var marker = $"/Resources/SusRuntime/Icons/{_collection}/";
            foreach (var assetPath in UnityEditor.AssetDatabase.GetAllAssetPaths())
            {
                if (!assetPath.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) continue;
                var idx = assetPath.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var after = assetPath.Substring(idx + marker.Length); // {weight}/{name}.svg
                var slash = after.IndexOf('/');
                if (slash < 0) continue;
                var weightFolder = after.Substring(0, slash);
                var rawName = System.IO.Path.GetFileNameWithoutExtension(after);
                RegisterScanned(set, weightFolder, rawName, assetPath);
            }
#else
            string[] weightDirs = { "thin", "light", "regular", "bold", "fill", "duotone" };
            foreach (var wd in weightDirs)
            {
                var icons = Resources.LoadAll<VectorImage>($"SusRuntime/Icons/{_collection}/{wd}");
                foreach (var icon in icons)
                    RegisterScanned(set, wd, icon.name, null);
            }
#endif
            return set;
        }

        private void RegisterScanned(HashSet<string> set, string weightFolder, string rawName, string editorAssetPath)
        {
            if (string.IsNullOrEmpty(weightFolder) || string.IsNullOrEmpty(rawName)) return;
            var weight = ParseWeight(weightFolder);
            var baseName = StripWeightSuffix(rawName, weightFolder);
            var resPath = $"SusRuntime/Icons/{_collection}/{weightFolder}/{rawName}";
            if (!_iconPaths.TryGetValue(baseName, out var wp))
                _iconPaths[baseName] = wp = new Dictionary<SusIconWeight, string>();
            wp[weight] = resPath;
            set.Add(baseName);
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(editorAssetPath))
                _editorAssetPaths[resPath] = editorAssetPath;
#endif
        }

#if UNITY_EDITOR
        private VectorImage LoadInEditorWithRepair(string path)
        {
            // Prefer the concrete asset path discovered during the scan (works for consumer icons
            // in any Resources folder); fall back to the owning package layout.
            string assetPath = _editorAssetPaths.TryGetValue(path, out var scanned)
                ? scanned
                : (!string.IsNullOrEmpty(_editorPackagePath)
                    ? $"Packages/{_editorPackagePath}/Runtime/Resources/{path}.svg"
                    : null);
            if (string.IsNullOrEmpty(assetPath)) return null;

            var img = UnityEditor.AssetDatabase.LoadAssetAtPath<VectorImage>(assetPath);
            if (img != null) return img;

            // Never Refresh / ForceImport during Play — that tears down UIDocuments mid-BuildUI
            // (empty icon → Refresh → empty panel).
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return null;

            if (!_triedRefresh)
            {
                _triedRefresh = true;
                UnityEditor.AssetDatabase.Refresh();
                img = UnityEditor.AssetDatabase.LoadAssetAtPath<VectorImage>(assetPath);
                if (img != null) return img;
            }

            var metaPath = assetPath + ".meta";
            if (System.IO.File.Exists(metaPath))
            {
                try
                {
                    var meta = System.IO.File.ReadAllText(metaPath);
                    meta = System.Text.RegularExpressions.Regex.Replace(meta, @"svgType:\s*\d+", "svgType: 3");
                    meta = System.Text.RegularExpressions.Regex.Replace(meta, @"tessellationMode:\s*\d+", "tessellationMode: 1");
                    meta = System.Text.RegularExpressions.Regex.Replace(meta, @"targetResolution:\s*\d+", "targetResolution: 2160");
                    meta = System.Text.RegularExpressions.Regex.Replace(meta, @"textureSize:\s*\d+", "textureSize: 512");
                    meta = System.Text.RegularExpressions.Regex.Replace(meta, @"\btextureWidth:\s*\d+", "textureWidth: 512");
                    meta = System.Text.RegularExpressions.Regex.Replace(meta, @"\btextureHeight:\s*\d+", "textureHeight: 512");
                    meta = System.Text.RegularExpressions.Regex.Replace(meta, @"sampleCount:\s*\d+", "sampleCount: 8");
                    System.IO.File.WriteAllText(metaPath, meta);
                    UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
                    img = UnityEditor.AssetDatabase.LoadAssetAtPath<VectorImage>(assetPath);
                }
                catch { }
            }

            if (img == null)
            {
                try { UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceSynchronousImport); }
                catch { }
                img = UnityEditor.AssetDatabase.LoadAssetAtPath<VectorImage>(assetPath);
            }
            return img;
        }
#endif
    }

    /// <summary>
    /// Minimal built-in icon set shipped in sus-core (<c>Resources/SusRuntime/Icons/core</c>) —
    /// the ~system icons components use by default (carets, check, x, magnifying-glass…).
    /// Guarantees SUS works without optional icon packages. Registered as the default
    /// provider in <see cref="SusIconRegistry"/>.
    /// </summary>
    public sealed class CoreIconProvider : ResourcesFolderIconProvider
    {
        public CoreIconProvider() : base("com.sharq-it.sus.core", "core") { }
    }
}
