using System;
using System.Reflection;
using UnityEditor;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Import defaults for SUS icon SVGs: keep the source viewBox instead of cropping the
    /// vector to its geometry bounds.
    ///
    /// Why: a VectorImage is painted as a stretched background, so a cropped vector loses its
    /// aspect — a sparse glyph like <c>minus</c> (an 18×1 bar in a 24×24 box) fills the whole
    /// element as a solid block, and glyphs like <c>check</c> or the carets come out visually
    /// larger than the rest of the set.
    ///
    /// Only applied on first import (<see cref="AssetImporter.importSettingsMissing"/>), so an
    /// explicit choice in an existing .meta — including a project's own icon folders — is kept.
    ///
    /// The SVG importer lives in a built-in editor module (no package dependency), and the type
    /// is reached by reflection so this file also compiles on Unity versions that lack it.
    /// </summary>
    public class SusIconImportDefaults : AssetPostprocessor
    {
        private const string IconsPathMarker = "/SusRuntime/Icons/";
        private const int PreserveViewport = 1; // Unity.VectorGraphics.ViewportOptions.PreserveViewport

        private static bool s_resolved;
        private static PropertyInfo s_viewportOptions;

        private void OnPreprocessAsset()
        {
            if (!assetPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return;
            if (assetPath.IndexOf(IconsPathMarker, StringComparison.OrdinalIgnoreCase) < 0) return;
            if (!assetImporter.importSettingsMissing) return;

            var prop = ResolveViewportOptions(assetImporter.GetType());
            if (prop == null) return;

            prop.SetValue(assetImporter, Enum.ToObject(prop.PropertyType, PreserveViewport));
        }

        private static PropertyInfo ResolveViewportOptions(Type importerType)
        {
            if (s_resolved) return s_viewportOptions;
            s_resolved = true;

            var prop = importerType.GetProperty("ViewportOptions",
                BindingFlags.Instance | BindingFlags.Public);
            if (prop != null && prop.CanWrite && prop.PropertyType.IsEnum)
                s_viewportOptions = prop;

            return s_viewportOptions;
        }
    }
}
