using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Shared touch-target floor (T-1267): owner threshold is ≥ 44 <b>screen</b> px, so
    /// panel points = <c>ceil(44 / scaledPixelsPerPoint)</c>. USS consumers read
    /// <c>var(--sus-touch-min)</c>; UITK cannot set custom properties from C#, so
    /// <see cref="ApplyTierClass"/> lifts the value via <c>.sus-touch-min--*</c> classes
    /// defined in <c>design-tokens.uss</c>.
    /// </summary>
    public static class SusTouchMin
    {
        /// <summary>Owner threshold in screen pixels (not panel points).</summary>
        public const float ScreenPx = 44f;

        /// <summary>USS custom property name (L3 semantic token).</summary>
        public const string CssVar = "--sus-touch-min";

        public const string Class48 = "sus-touch-min--48";
        public const string Class56 = "sus-touch-min--56";
        public const string Class64 = "sus-touch-min--64";
        public const string Class88 = "sus-touch-min--88";

        /// <summary>
        /// Panel-point minimum for a touch target at the given panel scale.
        /// Non-positive / NaN spp is treated as 1 (same as inventory SyncContainerMode).
        /// </summary>
        public static float ComputePt(float scaledPixelsPerPoint)
        {
            var spp = scaledPixelsPerPoint;
            if (float.IsNaN(spp) || spp <= 0f) spp = 1f;
            return Mathf.Ceil(ScreenPx / Mathf.Max(spp, 0.01f));
        }

        /// <summary>
        /// Resolve <c>scaledPixelsPerPoint</c> from an element (falls back to 1).
        /// </summary>
        public static float ResolveScaledPixelsPerPoint(VisualElement element)
        {
            if (element == null) return 1f;
            var spp = element.scaledPixelsPerPoint;
            if (float.IsNaN(spp) || spp <= 0f) return 1f;
            return spp;
        }

        /// <summary>
        /// Enable the matching <c>.sus-touch-min--*</c> tier class so USS
        /// <c>--sus-touch-min</c> rises above the 44 default when spp &lt; 1.
        /// </summary>
        public static void ApplyTierClass(VisualElement element, float touchMinPt)
        {
            if (element == null) return;
            var t = touchMinPt;
            element.EnableInClassList(Class48, t > ScreenPx && t <= 48f);
            element.EnableInClassList(Class56, t > 48f && t <= 56f);
            element.EnableInClassList(Class64, t > 56f && t <= 64f);
            element.EnableInClassList(Class88, t > 64f);
        }

        /// <summary>
        /// Compute TouchMinPt from the element's panel scale and apply the USS tier class.
        /// Returns the panel-point minimum.
        /// </summary>
        public static float Sync(VisualElement element)
        {
            var pt = ComputePt(ResolveScaledPixelsPerPoint(element));
            ApplyTierClass(element, pt);
            return pt;
        }
    }
}
