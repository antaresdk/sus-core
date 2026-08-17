using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// M2–M5 — SusMotion acceptance (ARCH-LUNA-JUICE-A §2.3).
    /// EditMode via <see cref="SusMotion.AdvanceFixedTickForTests"/> (+0.016, same as Every(16)).
    /// </summary>
    public class SusMotionTests
    {
        GameObject _go;
        UIDocument _doc;
        VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestMotionUI", typeof(UIDocument));
            _doc = _go.GetComponent<UIDocument>();
            _doc.panelSettings = UIDocumentTestHelper.CreateTestPanelSettings();
            _root = _doc.rootVisualElement;
            Assert.IsNotNull(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            _go = null;
            _doc = null;
            _root = null;
        }

        static void Advance(SusMotion motion, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                motion.AdvanceFixedTickForTests();
        }

        [Test]
        public void M2_FadeIn_OpacityRisesAndCompletes()
        {
            var el = new VisualElement { name = "fade-in" };
            _root.Add(el);

            bool completed = false;
            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.08f, SusEase.Linear)
                .Restore(SusRestoreMode.Keep);
            var handle = motion.Play(() => completed = true);

            Assert.IsTrue(handle.IsPlaying);
            Assert.AreEqual(0f, el.style.opacity.value, 0.01f);

            // 0.08 / 0.016 = 5 ticks
            Advance(motion, 6);

            Assert.IsTrue(completed, "complete callback");
            Assert.IsFalse(handle.IsPlaying);
            Assert.AreEqual(1f, el.style.opacity.value, 0.05f);
        }

        [Test]
        public void M3_Restore_KeywordNull_ClearsInlineOpacity()
        {
            var el = new VisualElement { name = "restore-null" };
            _root.Add(el);

            bool completed = false;
            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.048f, SusEase.Linear)
                .Restore(SusRestoreMode.KeywordNull);
            motion.Play(() => completed = true);

            Advance(motion, 5);

            Assert.IsTrue(completed);
            Assert.AreEqual(StyleKeyword.Null, el.style.opacity.keyword);
        }

        [Test]
        public void M4_Stagger_SecondChildStartsAfterDelayStep()
        {
            var parent = new VisualElement { name = "stagger-parent" };
            var a = new VisualElement { name = "child-a" };
            var b = new VisualElement { name = "child-b" };
            parent.Add(a);
            parent.Add(b);
            _root.Add(parent);

            const float delayStep = 0.08f; // 5 ticks
            SusMotion motionA = null;
            SusMotion motionB = null;
            int idx = 0;

            SusMotionStagger.Children(
                parent,
                child =>
                {
                    var m = SusMotion.On(child)
                        .FromOpacity(0f)
                        .Opacity(1f, 0.2f, SusEase.Linear);
                    if (idx == 0) motionA = m;
                    else motionB = m;
                    idx++;
                    return m;
                },
                delayStepS: delayStep,
                restore: SusRestoreMode.Keep);

            Assert.IsNotNull(motionA);
            Assert.IsNotNull(motionB);

            // 2 ticks: first moving, second still delayed (needs 5 ticks)
            Advance(motionA, 2);
            Advance(motionB, 2);

            Assert.Greater(a.style.opacity.value, 0.01f, "first child should have started");
            Assert.AreEqual(0f, b.style.opacity.value, 0.01f, "second child still delayed");

            // Advance both through delayStep
            Advance(motionA, 5);
            Advance(motionB, 5);

            Assert.Greater(b.style.opacity.value, 0.01f, "second child started after delayStep");
        }

        [Test]
        public void M5_Repeat2_PlaysGroupTwiceThenStops()
        {
            var el = new VisualElement { name = "repeat" };
            _root.Add(el);

            bool completed = false;
            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.048f, SusEase.Linear)
                .Repeat(2)
                .Restore(SusRestoreMode.Keep);
            var handle = motion.Play(() => completed = true);

            // One cycle ≈ 0.048s → 3 ticks; must still be playing (second cycle pending)
            Advance(motion, 3);
            Assert.IsFalse(completed, "must not complete after a single cycle");
            Assert.IsTrue(handle.IsPlaying);

            // Second cycle + headroom
            for (int i = 0; i < 8 && !completed; i++)
                motion.AdvanceFixedTickForTests();

            Assert.IsTrue(completed, "should stop after Repeat(2)");
            Assert.IsFalse(handle.IsPlaying);
        }

        [Test]
        public void Presets_ReturnPlayingHandles()
        {
            var el = new VisualElement();
            _root.Add(el);
            var h = SusMotionPresets.PunchScale(el, 1.08f, 0.1f);
            Assert.IsTrue(h.IsPlaying);
            h.Stop(applyRestore: true);
            Assert.IsFalse(h.IsPlaying);
        }
    }
}
