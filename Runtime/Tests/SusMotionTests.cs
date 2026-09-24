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
            SusMotion.Reduce.Value = false;
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

        // Reduce motion (SusMotion.Reduce)

        [Test]
        public void Reduce_IsOffByDefault()
        {
            Assert.IsFalse(SusMotion.Reduce.Peek(),
                "reduce motion must be opt-in: existing applications keep their motion unchanged");
        }

        [Test]
        public void Reduce_Off_FinitePlayStillTicks()
        {
            var el = new VisualElement { name = "reduce-off" };
            _root.Add(el);

            bool completed = false;
            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.08f, SusEase.Linear)
                .Restore(SusRestoreMode.Keep);
            var handle = motion.Play(() => completed = true);

            Assert.IsTrue(handle.IsPlaying, "with Reduce off a finite play is scheduled as before");
            Assert.IsFalse(completed, "no synchronous completion with Reduce off");
            Assert.AreEqual(0f, el.style.opacity.value, 0.01f, "seed frame, not the end value");

            Advance(motion, 6);
            Assert.IsTrue(completed);
            Assert.AreEqual(1f, el.style.opacity.value, 0.05f);
        }

        [Test]
        public void Reduce_On_FinitePlaySnapsToEndAndCompletesWithoutTicks()
        {
            SusMotion.Reduce.Value = true;
            var el = new VisualElement { name = "reduce-finite" };
            _root.Add(el);

            int completions = 0;
            var handle = SusMotion.On(el)
                .FromOpacity(0f)
                .FromTranslate(new Vector2(0f, 40f))
                .Opacity(1f, 0.3f, SusEase.QuadOut)
                .Together()
                .Translate(Vector2.zero, 0.3f, SusEase.QuadOut)
                .Sequence()
                .Scale(1.2f, 0.2f)
                .Delay(0.5f)
                .Restore(SusRestoreMode.Keep)
                .Play(() => completions++);

            Assert.AreEqual(1, completions, "onComplete runs synchronously, exactly once");
            Assert.IsFalse(handle.IsPlaying, "nothing is left scheduled");
            Assert.AreEqual(1f, el.style.opacity.value, 0.0001f);
            Assert.AreEqual(0f, el.style.translate.value.y.value, 0.0001f);
            Assert.AreEqual(1.2f, el.style.scale.value.value.x, 0.0001f);
            Assert.AreEqual(0, GetActiveByTarget().Count, "a snapped play does not stay registered");
        }

        [Test]
        public void Reduce_On_FinitePlayStillAppliesRestoreMode()
        {
            SusMotion.Reduce.Value = true;
            var el = new VisualElement { name = "reduce-restore" };
            _root.Add(el);

            bool completed = false;
            SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.2f, SusEase.Linear)
                .Restore(SusRestoreMode.KeywordNull)
                .Play(() => completed = true);

            Assert.IsTrue(completed);
            Assert.AreEqual(StyleKeyword.Null, el.style.opacity.keyword,
                "the restore mode of the motion applies on snap exactly as on a normal finish");
        }

        [Test]
        public void Reduce_On_ForeverPlayDoesNotStartAndLeavesTargetUntouched()
        {
            SusMotion.Reduce.Value = true;
            var el = new VisualElement { name = "reduce-forever" };
            _root.Add(el);
            el.style.opacity = 0.5f;

            bool completed = false;
            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.5f, SusEase.Linear)
                .Repeat(0)
                .Restore(SusRestoreMode.KeywordNull);
            var handle = motion.Play(() => completed = true);

            Assert.IsFalse(handle.IsPlaying, "a forever motion does not start under reduce motion");
            Assert.IsFalse(completed, "a skipped forever motion does not report completion");
            Assert.AreEqual(0.5f, el.style.opacity.value, 0.0001f, "the seed was not written");
            Assert.AreEqual(0, GetActiveByTarget().Count);

            Advance(motion, 5);
            Assert.AreEqual(0.5f, el.style.opacity.value, 0.0001f, "no tick moves the target");
        }

        [Test]
        public void Reduce_SwitchedOn_StopsActiveForeverPlayWithRestore()
        {
            var el = new VisualElement { name = "reduce-switch-forever" };
            _root.Add(el);

            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.5f, SusEase.Linear)
                .Repeat(0)
                .Restore(SusRestoreMode.KeywordNull);
            var handle = motion.Play();
            Advance(motion, 3);
            Assert.IsTrue(handle.IsPlaying, "sanity: forever motion runs while Reduce is off");

            SusMotion.Reduce.Value = true;

            Assert.IsFalse(handle.IsPlaying, "switching Reduce on stops a running forever motion");
            Assert.AreEqual(StyleKeyword.Null, el.style.opacity.keyword, "its restore mode was applied");
            Assert.AreEqual(0, GetActiveByTarget().Count);
        }

        [Test]
        public void Reduce_SwitchedOn_LeavesRunningFinitePlayToFinish()
        {
            var el = new VisualElement { name = "reduce-switch-finite" };
            _root.Add(el);

            bool completed = false;
            var motion = SusMotion.On(el)
                .FromOpacity(0f)
                .Opacity(1f, 0.08f, SusEase.Linear)
                .Restore(SusRestoreMode.Keep);
            var handle = motion.Play(() => completed = true);
            Advance(motion, 1);

            SusMotion.Reduce.Value = true;

            Assert.IsTrue(handle.IsPlaying, "a finite motion already in flight is not cut short");
            Advance(motion, 6);
            Assert.IsTrue(completed);
            Assert.AreEqual(1f, el.style.opacity.value, 0.05f);
        }

        [Test]
        public void Reduce_On_StaggerCompletesAllChildrenAtOnce()
        {
            SusMotion.Reduce.Value = true;
            var parent = new VisualElement { name = "reduce-stagger" };
            _root.Add(parent);
            for (int i = 0; i < 3; i++)
                parent.Add(new VisualElement { name = "c" + i });

            var handle = SusMotionStagger.Children(
                parent,
                child => SusMotion.On(child).FromOpacity(0f).Opacity(1f, 0.1f, SusEase.Linear),
                delayStepS: 0.05f,
                restore: SusRestoreMode.Keep);

            Assert.IsFalse(handle.IsPlaying, "every child snapped, so the stagger is already done");
            foreach (var child in parent.Children())
                Assert.AreEqual(1f, child.style.opacity.value, 0.0001f, child.name);
        }

        [Test]
        public void Reduce_ResetStatics_TurnsFlagOffAndKeepsStopOnEnable()
        {
            SusMotion.Reduce.Value = true;
            var resetMethod = typeof(SusMotion).GetMethod("ResetStatics",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(resetMethod);
            resetMethod.Invoke(null, null);
            Assert.IsFalse(SusMotion.Reduce.Peek(), "a new Play session starts with Reduce off");

            var el = new VisualElement { name = "reduce-after-reset" };
            _root.Add(el);
            var motion = SusMotion.On(el).Opacity(0.2f, 0.5f, SusEase.Linear).Repeat(0);
            var handle = motion.Play();
            Assert.IsTrue(handle.IsPlaying);

            SusMotion.Reduce.Value = true;
            Assert.IsFalse(handle.IsPlaying,
                "the stop-on-enable handler survives the reset of the flag's subscribers");
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
