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
        [Header("Primary typeface")]
        [Tooltip("Default body text (regular weight). Required.")]
        public FontDefinition Regular;

        [Tooltip("Emphasis / labels (medium weight).")]
        public FontDefinition Medium;

        [Tooltip("Strong emphasis / headings (bold weight).")]
        public FontDefinition Bold;

        [Tooltip("Thin / captions (light weight).")]
        public FontDefinition Light;

        [Header("Special-purpose")]
        [Tooltip("Heading display font (optional — falls back to Bold).")]
        public FontDefinition Heading;

        [Tooltip("Monospaced font for code / stats (optional — falls back to Regular).")]
        public FontDefinition Mono;

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

        private static bool IsSet(FontDefinition fd) =>
            fd.fontAsset != null || fd.font != null;
    }
}
