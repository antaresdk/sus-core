using System.Collections;
using System.Reflection;
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

        // ─── T-1103 — ActiveByTarget FEP-reset + detach cleanup (R-A4) ────────────────────

        [Test]
        public void T1103_ForeverRepeat_StopsWhenTargetDetaches()
        {
            var el = new VisualElement { name = "forever" };
            _root.Add(el);

            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.1f, SusEase.Linear)
                .Repeat(0) // <=0 == forever, per SusMotion.Group.Repeat contract
                .Restore(SusRestoreMode.Keep);
            var handle = motion.Play();

            Assert.IsTrue(handle.IsPlaying, "sanity: forever motion should be playing after Play()");

            el.RemoveFromHierarchy(); // synchronously dispatches DetachFromPanelEvent

            Assert.IsFalse(handle.IsPlaying,
                "T-1103: a forever-Repeat motion never reaches CompleteInternal() on its own — " +
                "it must be stopped when its target leaves the panel, or it ticks forever.");
        }

        [Test]
        public void T1103_ForeverRepeat_DetachRemovesFromActiveByTarget()
        {
            var el = new VisualElement { name = "forever-registry" };
            _root.Add(el);

            SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.1f, SusEase.Linear)
                .Repeat(0)
                .Restore(SusRestoreMode.Keep)
                .Play();

            var active = GetActiveByTarget();
            Assert.IsTrue(active.Contains(el), "sanity: Play() registers the target in ActiveByTarget");

            el.RemoveFromHierarchy();

            Assert.IsFalse(active.Contains(el),
                "T-1103: detach must remove the target from the static ActiveByTarget map, " +
                "or it keeps a VisualElement from a dead panel pinned alive forever.");
        }

        [Test]
        public void T1103_StopThenDetach_DoesNotThrow()
        {
            // A motion that already completed/stopped naturally must not blow up when its
            // (now inert) detach handler fires later — defensive against double-unregister bugs.
            var el = new VisualElement { name = "stop-then-detach" };
            _root.Add(el);

            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.02f, SusEase.Linear)
                .Restore(SusRestoreMode.Keep);
            var handle = motion.Play();
            handle.Stop(applyRestore: false);

            Assert.DoesNotThrow(() => el.RemoveFromHierarchy());
        }

        [Test]
        public void T1103_ResetStatics_ClearsActiveByTarget()
        {
            var el = new VisualElement { name = "fep-reset" };
            _root.Add(el);

            SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 1f, SusEase.Linear)
                .Repeat(0)
                .Restore(SusRestoreMode.Keep)
                .Play();

            var active = GetActiveByTarget();
            Assert.Greater(active.Count, 0, "sanity: Play() should populate ActiveByTarget");

            var resetMethod = typeof(SusMotion).GetMethod("ResetStatics",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(resetMethod,
                "T-1103 requires a static FEP-reset method (RuntimeInitializeOnLoadMethod " +
                "pattern used by the other 15 statics in sus-core, e.g. ClickAuditService)");
            resetMethod.Invoke(null, null);

            Assert.AreEqual(0, active.Count,
                "ResetStatics() must clear ActiveByTarget so it doesn't hold VisualElements " +
                "from a previous Play session (T-1103, Domain Reload disabled scenario)");
        }

        private static IDictionary GetActiveByTarget()
        {
            var field = typeof(SusMotion).GetField("ActiveByTarget",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "SusMotion.ActiveByTarget field not found — test needs updating");
            return (IDictionary)field.GetValue(null);
        }
    }
}
