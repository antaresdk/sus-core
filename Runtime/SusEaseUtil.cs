using UnityEngine;

namespace Sharq.Core
{
    /// <summary>Pure easing evaluators for <see cref="SusEase"/> (t in [0,1]).</summary>
    public static class SusEaseUtil
    {
        const float BackC1 = 1.70158f;
        const float BackC2 = BackC1 * 1.525f;
        const float BackC3 = BackC1 + 1f;
        const float ElasticC4 = (2f * Mathf.PI) / 3f;

        /// <summary>Map normalized time <paramref name="t01"/> through <paramref name="ease"/>.</summary>
        public static float Evaluate(SusEase ease, float t01)
        {
            float t = Mathf.Clamp01(t01);
            switch (ease)
            {
                case SusEase.Linear: return t;
                case SusEase.QuadIn: return t * t;
                case SusEase.QuadOut: return 1f - (1f - t) * (1f - t);
                case SusEase.QuadInOut: return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
                case SusEase.CubicIn: return t * t * t;
                case SusEase.CubicOut: return 1f - Mathf.Pow(1f - t, 3f);
                case SusEase.CubicInOut: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                case SusEase.QuartIn: return t * t * t * t;
                case SusEase.QuartOut: return 1f - Mathf.Pow(1f - t, 4f);
                case SusEase.QuartInOut: return t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) / 2f;
                case SusEase.ExpoIn: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case SusEase.ExpoOut: return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case SusEase.ExpoInOut:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f
                        ? Mathf.Pow(2f, 20f * t - 10f) / 2f
                        : (2f - Mathf.Pow(2f, -20f * t + 10f)) / 2f;
                case SusEase.BackIn: return BackC3 * t * t * t - BackC1 * t * t;
                case SusEase.BackOut: return 1f + BackC3 * Mathf.Pow(t - 1f, 3f) + BackC1 * Mathf.Pow(t - 1f, 2f);
                case SusEase.BackInOut:
                    return t < 0.5f
                        ? (Mathf.Pow(2f * t, 2f) * ((BackC2 + 1f) * 2f * t - BackC2)) / 2f
                        : (Mathf.Pow(2f * t - 2f, 2f) * ((BackC2 + 1f) * (t * 2f - 2f) + BackC2) + 2f) / 2f;
                case SusEase.ElasticOut:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * ElasticC4) + 1f;
                case SusEase.BounceOut: return BounceOut(t);
                default: return t;
            }
        }

        static float BounceOut(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}
