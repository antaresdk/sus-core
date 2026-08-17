using NUnit.Framework;
using UnityEngine;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>M1 — SusEaseUtil endpoints and Quad* monotonicity (EditMode).</summary>
    public class SusEaseUtilTests
    {
        static readonly SusEase[] AllEases =
        {
            SusEase.Linear,
            SusEase.QuadIn, SusEase.QuadOut, SusEase.QuadInOut,
            SusEase.CubicIn, SusEase.CubicOut, SusEase.CubicInOut,
            SusEase.QuartIn, SusEase.QuartOut, SusEase.QuartInOut,
            SusEase.ExpoIn, SusEase.ExpoOut, SusEase.ExpoInOut,
            SusEase.BackIn, SusEase.BackOut, SusEase.BackInOut,
            SusEase.ElasticOut,
            SusEase.BounceOut,
        };

        [Test]
        public void Evaluate_Endpoints_ZeroAndOne()
        {
            foreach (var ease in AllEases)
            {
                Assert.AreEqual(0f, SusEaseUtil.Evaluate(ease, 0f), 1e-5f, ease + " t=0");
                Assert.AreEqual(1f, SusEaseUtil.Evaluate(ease, 1f), 1e-5f, ease + " t=1");
            }
        }

        [Test]
        public void Evaluate_QuadFamily_IsMonotonic()
        {
            AssertMonotonic(SusEase.QuadIn);
            AssertMonotonic(SusEase.QuadOut);
            AssertMonotonic(SusEase.QuadInOut);
            AssertMonotonic(SusEase.Linear);
        }

        [Test]
        public void Evaluate_BackAndElastic_MayOvershoot()
        {
            float back = SusEaseUtil.Evaluate(SusEase.BackOut, 0.8f);
            Assert.Greater(back, 1f, "BackOut should overshoot above 1");

            // ElasticOut oscillates around 1 near the end
            float elastic = SusEaseUtil.Evaluate(SusEase.ElasticOut, 0.5f);
            Assert.Greater(elastic, 0f);
        }

        static void AssertMonotonic(SusEase ease)
        {
            float prev = SusEaseUtil.Evaluate(ease, 0f);
            for (int i = 1; i <= 20; i++)
            {
                float t = i / 20f;
                float v = SusEaseUtil.Evaluate(ease, t);
                Assert.GreaterOrEqual(v, prev - 1e-5f, $"{ease} at t={t}");
                prev = v;
            }
        }
    }
}
