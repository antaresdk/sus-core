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
        // T-2216: every slot below is dispatched by SusFontService.ApplyFonts to elements that
        // carry the matching marker USS class (SusFontService.<Role>ClassName) — NOT to the
        // whole tree. Unity has no public API to set USS custom properties (--var) from C#, so
        // the inherited-root trick only ever worked for Regular; the other roles need markup to
        // opt in via that class. A filled slot with no marked element in the tree logs a warning
        // instead of silently doing nothing (see SusFontService.ApplyFonts).

        [Header("Primary typeface")]
        [Tooltip("Default body text (regular weight). Required. Applied to the root (and inherited by default) via SusApp.UseFonts / SusFontService.ApplyFonts.")]
        public FontDefinition Regular;

        [Tooltip("Emphasis / labels (medium weight). Applied only to elements with the USS class SusFontService.MediumClassName.")]
        public FontDefinition Medium;

        [Tooltip("Strong emphasis / headings (bold weight). Applied only to elements with the USS class SusFontService.BoldClassName.")]
        public FontDefinition Bold;

        [Tooltip("Thin / captions (light weight). Applied only to elements with the USS class SusFontService.LightClassName.")]
        public FontDefinition Light;

        [Header("Special-purpose")]
        [Tooltip("Heading display font (optional — falls back to Bold, then Regular). Applied only to elements with the USS class SusFontService.HeadingClassName.")]
        public FontDefinition Heading;

        [Tooltip("Monospaced font for code / stats (optional — falls back to Regular). Applied only to elements with the USS class SusFontService.MonoClassName.")]
        public FontDefinition Mono;

        [Tooltip("Narrow / display typeface for large titles, e.g. a Condensed weight (optional — falls back to Heading, then Bold, then Regular). Applied only to elements with the USS class SusFontService.CondensedClassName.")]
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
