using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>Common motion recipes built on <see cref="SusMotion"/>.</summary>
    public static class SusMotionPresets
    {
        public static SusMotionHandle FadeIn(
            VisualElement el,
            float durationS = 0.2f,
            SusRestoreMode restore = SusRestoreMode.KeywordNull) =>
            SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, durationS, SusEase.QuadOut)
                .Restore(restore)
                .Play();

        public static SusMotionHandle FadeOut(
            VisualElement el,
            float durationS = 0.2f,
            SusRestoreMode restore = SusRestoreMode.KeywordNull) =>
            SusMotion.On(el)
                .Opacity(0f, durationS, SusEase.QuadIn)
                .Restore(restore)
                .Play();

        public static SusMotionHandle SlideIn(
            VisualElement el,
            Vector2 fromPx,
            float durationS = 0.3f,
            SusRestoreMode restore = SusRestoreMode.KeywordNull) =>
            SusMotion.On(el)
                .FromOpacity(0f)
                .FromTranslate(fromPx)
                .Opacity(1f, durationS, SusEase.QuadOut)
                .Translate(Vector2.zero, durationS, SusEase.QuadOut)
                .Restore(restore)
                .Play();

        public static SusMotionHandle SlideOut(
            VisualElement el,
            Vector2 toPx,
            float durationS = 0.3f,
            SusRestoreMode restore = SusRestoreMode.KeywordNull) =>
            SusMotion.On(el)
                .Opacity(0f, durationS, SusEase.QuadInOut)
                .Translate(toPx, durationS, SusEase.QuadInOut)
                .Restore(restore)
                .Play();

        public static SusMotionHandle Bounce(VisualElement el, float durationS = 0.45f) =>
            SusMotion.On(el)
                .FromScale(Vector2.zero)
                .Scale(1f, durationS, SusEase.BounceOut)
                .Restore(SusRestoreMode.KeywordNull)
                .Play();

        public static SusMotionHandle PunchScale(
            VisualElement el,
            float peak = 1.08f,
            float durationS = 0.25f)
        {
            float half = durationS * 0.5f;
            return SusMotion.On(el)
                .Scale(peak, half, SusEase.QuadOut)
                .Sequence()
                .Scale(1f, half, SusEase.QuadIn)
                .Restore(SusRestoreMode.KeywordNull)
                .Play();
        }

        public static SusMotionHandle Shake(
            VisualElement el,
            float amplitudePx = 6f,
            float durationS = 0.35f)
        {
            float q = durationS * 0.25f;
            return SusMotion.On(el)
                .FromTranslate(Vector2.zero)
                .Translate(new Vector2(amplitudePx, 0f), q, SusEase.QuadOut)
                .Sequence()
                .Translate(new Vector2(-amplitudePx, 0f), q, SusEase.Linear)
                .Sequence()
                .Translate(new Vector2(amplitudePx * 0.6f, 0f), q, SusEase.Linear)
                .Sequence()
                .Translate(Vector2.zero, q, SusEase.QuadIn)
                .Restore(SusRestoreMode.KeywordNull)
                .Play();
        }
    }
}
