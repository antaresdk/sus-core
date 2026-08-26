using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;
using System.Reflection;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Verify that Updated() fires once per frame and correctly pauses on detach.
    /// Frame-count / condition polls (T-1123) — no WaitForSeconds (flaky under batch load).
    /// </summary>
    public class UpdatedOnceTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public int UpdateCount { get; private set; }

            protected override void Updated()
            {
                base.Updated();
                UpdateCount++;
            }

            protected override void Build()
            {
                Add(new Label("test"));
            }
        }

        [UnityTest]
        public IEnumerator Updated_FiresOncePerFrame()
        {
            var comp = new TestComp();
            Root.Add(comp);
            // schedule.Execute().Every(16) is time-based — wall-clock poll (T-1123)
            yield return WaitUntil(() => comp.UpdateCount >= 2, timeoutSeconds: 0.5f);

            Assert.GreaterOrEqual(comp.UpdateCount, 2, "Updated should fire at least twice");
        }

        [UnityTest]
        public IEnumerator Updated_DoesNotFireAfterDetach()
        {
            var comp = new TestComp();
            Root.Add(comp);
            yield return WaitUntil(() => comp.UpdateCount >= 1, timeoutSeconds: 0.5f);
            int countBeforeDetach = comp.UpdateCount;

            comp.RemoveFromHierarchy();
            // hold wall-clock while detached so a flaky scheduler tick would have time to fire
            float holdUntil = Time.realtimeSinceStartup + 0.15f;
            while (Time.realtimeSinceStartup < holdUntil)
                yield return new WaitForSecondsRealtime(0.016f);

            Assert.AreEqual(countBeforeDetach, comp.UpdateCount,
                "Updated should not fire after detach");
        }

        [UnityTest]
        public IEnumerator Updated_ResumesAfterReattach()
        {
            var comp = new TestComp();
            Root.Add(comp);
            yield return WaitUntil(() => comp.UpdateCount >= 1, timeoutSeconds: 0.5f);
            comp.RemoveFromHierarchy();
            yield return WaitFrames(5);
            int countAfterDetach = comp.UpdateCount;

            Root.Add(comp);
            yield return WaitUntil(() => comp.UpdateCount > countAfterDetach, timeoutSeconds: 0.5f);

            Assert.Greater(comp.UpdateCount, countAfterDetach,
                "Updated should resume after reattach");
        }

        // ─── T-1102: components that never override Updated() must not be scheduled ──────

        private class NoUpdateOverrideComp : SusComponent
        {
            protected override void Build()
            {
                Add(new Label("no-update-override"));
            }
        }

        private static readonly FieldInfo s_updateItemField =
            typeof(SusComponent).GetField("_updateItem", BindingFlags.NonPublic | BindingFlags.Instance);

        [UnityTest]
        public IEnumerator Updated_NotScheduled_WhenComponentNeverOverridesIt()
        {
            var comp = new NoUpdateOverrideComp();
            Root.Add(comp);
            // Give the mount path time to (not) schedule — condition-poll hold, not fixed WaitForSeconds
            float holdUntil = Time.realtimeSinceStartup + 0.15f;
            while (Time.realtimeSinceStartup < holdUntil)
                yield return new WaitForSecondsRealtime(0.016f);

            var updateItem = s_updateItemField.GetValue(comp);
            Assert.IsNull(updateItem,
                "T-1102: a component whose Updated() is never overridden must not get an " +
                "Every(16) schedule item at all — it would tick 60 times/sec calling a no-op.");
        }

        [UnityTest]
        public IEnumerator Updated_StillScheduled_WhenOverridden_SideBySideWithNonOverriding()
        {
            // Regression guard: adding the T-1102 skip must not accidentally suppress
            // scheduling for a sibling component that DOES override Updated().
            var overriding = new TestComp();
            var nonOverriding = new NoUpdateOverrideComp();
            Root.Add(overriding);
            Root.Add(nonOverriding);
            yield return WaitUntil(() => overriding.UpdateCount >= 2, timeoutSeconds: 0.5f);

            Assert.GreaterOrEqual(overriding.UpdateCount, 2);
            Assert.IsNull(s_updateItemField.GetValue(nonOverriding));
        }
    }
}
