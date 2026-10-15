using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// Glyph fallback for the storybook's panel (POOL-01 R3, card T-3372).
    ///
    /// The shell's interface face, Instrument Sans, has no Cyrillic. In the Editor the OS fills the
    /// gap and nobody notices; a WebGL player has no OS fonts, so on the public site a value typed
    /// as "Hello" followed by a Russian word read only "Hello" in the panel field, and the Russian story descriptions came
    /// out as punctuation only. Montserrat carries Cyrillic and already ships under the package's
    /// Resources for the product chain, so falling back to it costs no bytes.
    ///
    /// Why not in the stylesheet: UI Toolkit builds a font asset from a .ttf named in USS without
    /// reading the importer's "Font Fallbacks" list (measured on 6000.3: the converted asset has no
    /// fallback table), and a font asset saved as a file with its own fallback table is dynamic -
    /// every glyph the Editor draws would be written back into the package. The panel's text
    /// settings are the one fallback list UI Toolkit consults that can live in memory only.
    /// </summary>
    public static class SusStorybookText
    {
        /// <summary>
        /// Resources paths of the faces a missing glyph is looked up in, in order. Regular only:
        /// a fallback face is chosen per glyph, not per weight, and one face keeps the cost at one
        /// atlas.
        /// </summary>
        public static readonly string[] GlyphFallbackFonts =
        {
            "SusRuntime/Fonts/Montserrat/Montserrat-Regular",
        };

        /// <summary>
        /// A copy of <paramref name="source"/> whose text settings carry
        /// <see cref="GlyphFallbackFonts"/>, or null when there is nothing to add (no source, no
        /// font found). The source asset is never written: a PanelSettings asset edited in Play
        /// mode keeps the edit - and a reference to a runtime-only object - after Play ends. The
        /// copy and its text settings are runtime objects; <see cref="Release"/> destroys them.
        /// </summary>
        public static PanelSettings WithGlyphFallback(PanelSettings source)
        {
            if (source == null) return null;

            var fallbacks = LoadFallbacks();
            if (fallbacks.Count == 0) return null;

            // A fresh PanelTextSettings is exactly what a panel with no text settings uses
            // (measured: the built-in default has no fallbacks, sprites or style sheet either).
            var text = source.textSettings != null
                ? Object.Instantiate(source.textSettings)
                : ScriptableObject.CreateInstance<PanelTextSettings>();
            text.name = "SUS storybook text settings (runtime)";
            var list = text.fallbackFontAssets != null
                ? new List<FontAsset>(text.fallbackFontAssets)
                : new List<FontAsset>();
            list.AddRange(fallbacks);
            text.fallbackFontAssets = list;

            var copy = Object.Instantiate(source);
            copy.name = source.name + " (storybook)";
            copy.textSettings = text;
            return copy;
        }

        /// <summary>
        /// Destroys a copy made by <see cref="WithGlyphFallback"/> and its text settings. The
        /// fallback font assets stay: TextCore registers a font asset by family name the first
        /// time it draws from it, and text elsewhere that later resolves that name would reach a
        /// destroyed atlas (measured: MissingReferenceException on Texture2D across the core
        /// suite). They are built once per domain and shared.
        /// </summary>
        public static void Release(PanelSettings copy)
        {
            if (copy == null) return;
            if (copy.textSettings != null) Destroy(copy.textSettings);
            Destroy(copy);
        }

        static readonly Dictionary<string, FontAsset> Built = new();

        static List<FontAsset> LoadFallbacks()
        {
            var result = new List<FontAsset>();
            foreach (var path in GlyphFallbackFonts)
            {
                if (Built.TryGetValue(path, out var built) && built != null)
                {
                    result.Add(built);
                    continue;
                }

                var font = Resources.Load<Font>(path);
                if (font == null)
                {
                    SusLog.Warn("[storybook] glyph fallback font not found at Resources/" + path);
                    continue;
                }

                var asset = FontAsset.CreateFontAsset(font);
                if (asset == null) continue;
                asset.name = font.name + " (storybook fallback)";
                asset.hideFlags = HideFlags.DontSave;
                Built[path] = asset;
                result.Add(asset);
            }
            return result;
        }

        static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
