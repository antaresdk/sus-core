using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>T-1267: shared TouchMinPt math + USS tier class mapping.</summary>
    public class SusTouchMinTests
    {
        [Test]
        public void ScreenPx_IsFortyFour()
        {
            Assert.AreEqual(44f, SusTouchMin.ScreenPx);
            Assert.AreEqual("--sus-touch-min", SusTouchMin.CssVar);
        }

        [Test]
        public void ComputePt_AtUnityScale_IsFortyFour()
        {
            Assert.AreEqual(44f, SusTouchMin.ComputePt(1f));
            Assert.AreEqual(44f, SusTouchMin.ComputePt(0f)); // clamped spp floor
            Assert.AreEqual(44f, SusTouchMin.ComputePt(-1f));
        }

        [Test]
        public void ComputePt_ScalesWithInverseSpp()
        {
            Assert.AreEqual(88f, SusTouchMin.ComputePt(0.5f));
            Assert.AreEqual(59f, SusTouchMin.ComputePt(0.75f)); // ceil(58.666…)
            Assert.AreEqual(22f, SusTouchMin.ComputePt(2f));    // ceil(22)
        }

        [Test]
        public void ApplyTierClass_MapsBands()
        {
            var el = new VisualElement();

            SusTouchMin.ApplyTierClass(el, 44f);
            Assert.IsFalse(el.ClassListContains(SusTouchMin.Class48));
            Assert.IsFalse(el.ClassListContains(SusTouchMin.Class56));
            Assert.IsFalse(el.ClassListContains(SusTouchMin.Class64));
            Assert.IsFalse(el.ClassListContains(SusTouchMin.Class88));

            SusTouchMin.ApplyTierClass(el, 48f);
            Assert.IsTrue(el.ClassListContains(SusTouchMin.Class48));
            Assert.IsFalse(el.ClassListContains(SusTouchMin.Class56));

            SusTouchMin.ApplyTierClass(el, 56f);
            Assert.IsFalse(el.ClassListContains(SusTouchMin.Class48));
            Assert.IsTrue(el.ClassListContains(SusTouchMin.Class56));

            SusTouchMin.ApplyTierClass(el, 64f);
            Assert.IsTrue(el.ClassListContains(SusTouchMin.Class64));

            SusTouchMin.ApplyTierClass(el, 88f);
            Assert.IsFalse(el.ClassListContains(SusTouchMin.Class64));
            Assert.IsTrue(el.ClassListContains(SusTouchMin.Class88));
        }
    }
}
