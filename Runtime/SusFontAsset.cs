using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Typed font set asset for SUS projects.
    /// Assign Font Assets (SDF or dynamic) per weight/style.
    /// Create via <c>Assets → Create → SUS → Font Set</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "SUS/Font Set", fileName = "SusFontSet", order = 200)]
    public class SusFontAsset : ScriptableObject
    {
        // T-2767 (D-069 / R120): this asset is an EDITOR-TIME declaration, not a runtime
        // applicator. Unity has no public API to set a USS custom property (--var) from C#, so
        // the old code path wrote each slot as an INLINE -unity-font-definition — which outranks
        // every USS rule and left the buyer unable to restyle the result. Export the set once via
        // Window > SUS > Fonts > Export Font Set to USS (SusFontUssExporter): it writes
        // Assets/Resources/SusRuntime/_font.uss with the --sus-font-family-* declarations that
        // the packaged sheet already reads, and from then on the whole cascade — component rules,
        // the .sus-font-* marker classes, skins, overlays — follows with no C# call at all.

        [Header("Primary typeface")]
        [Tooltip("Default body text (regular weight). Required. Exported to --sus-font-family-regular, which the packaged _font.uss makes the inherited panel default.")]
        public FontDefinition Regular;

        [Tooltip("Emphasis / labels (medium weight). Exported to the matching --sus-font-family-* USS hook; reaches elements tagged with SusFontService.MediumClassName.")]
        public FontDefinition Medium;

        [Tooltip("Strong emphasis / headings (bold weight). Exported to the matching --sus-font-family-* USS hook; reaches elements tagged with SusFontService.BoldClassName.")]
        public FontDefinition Bold;

        [Tooltip("Thin / captions (light weight). Exported to the matching --sus-font-family-* USS hook; reaches elements tagged with SusFontService.LightClassName.")]
        public FontDefinition Light;

        [Header("Special-purpose")]
        [Tooltip("Heading display font (optional — falls back to Bold, then Regular). Exported to the matching --sus-font-family-* USS hook; reaches elements tagged with SusFontService.HeadingClassName.")]
        public FontDefinition Heading;

        [Tooltip("Monospaced font for code / stats (optional — falls back to Regular). Exported to the matching --sus-font-family-* USS hook; reaches elements tagged with SusFontService.MonoClassName.")]
        public FontDefinition Mono;

        [Tooltip("Narrow / display typeface for large titles, e.g. a Condensed weight (optional — falls back to Heading, then Bold, then Regular). Exported to the matching --sus-font-family-* USS hook; reaches elements tagged with SusFontService.CondensedClassName.")]
        public FontDefinition Condensed;

        /// <summary>Returns Heading if set, Bold if set, otherwise Regular.</summary>
        public FontDefinition ResolveHeading() =>
            IsSet(Heading) ? Heading : IsSet(Bold) ? Bold : Regular;

        /// <summary>Returns Mono if set, otherwise Regular.</summary>
        public FontDefinition ResolveMono() =>
            IsSet(Mono) ? Mono : Regular;

        /// <summary>Returns Bold if set, otherwise Medium, otherwise Regular.</summary>
        public FontDefinition ResolveBold() =>
            IsSet(Bold) ? Bold : IsSet(Medium) ? Medium : Regular;

        /// <summary>Returns Medium if set, otherwise Regular.</summary>
        public FontDefinition ResolveMedium() =>
            IsSet(Medium) ? Medium : Regular;

        /// <summary>Returns Light if set, otherwise Regular.</summary>
        public FontDefinition ResolveLight() =>
            IsSet(Light) ? Light : Regular;

        /// <summary>Returns Condensed if set, otherwise falls through the Heading chain (Heading → Bold → Regular).</summary>
        public FontDefinition ResolveCondensed() =>
            IsSet(Condensed) ? Condensed : ResolveHeading();

        /// <summary>True when <paramref name="fd"/> carries either an SDF FontAsset or a legacy Font.</summary>
        public static bool HasFont(FontDefinition fd) => IsSet(fd);

        private static bool IsSet(FontDefinition fd) =>
            fd.fontAsset != null || fd.font != null;
    }
}
