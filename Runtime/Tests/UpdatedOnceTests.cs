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
    /// Uses WaitForSeconds to let UITK panel scheduler tick in PlayMode.
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
            // schedule.Execute().Every(16) needs real time for UITK panel to tick
            yield return new WaitForSeconds(0.1f);

            Assert.GreaterOrEqual(comp.UpdateCount, 2, "Updated should fire at least twice after 100ms");
        }

        [UnityTest]
        public IEnumerator Updated_DoesNotFireAfterDetach()
        {
            var comp = new TestComp();
            Root.Add(comp);
            yield return new WaitForSeconds(0.05f);
            int countBeforeDetach = comp.UpdateCount;

            comp.RemoveFromHierarchy();
            yield return new WaitForSeconds(0.05f);

            Assert.AreEqual(countBeforeDetach, comp.UpdateCount,
                "Updated should not fire after detach");
        }

        [UnityTest]
        public IEnumerator Updated_ResumesAfterReattach()
        {
            var comp = new TestComp();
            Root.Add(comp);
            yield return new WaitForSeconds(0.05f);
            comp.RemoveFromHierarchy();
            yield return new WaitForSeconds(0.05f);
            int countAfterDetach = comp.UpdateCount;

            Root.Add(comp);
            yield return new WaitForSeconds(0.05f);

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
            yield return new WaitForSeconds(0.1f);

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
            yield return new WaitForSeconds(0.1f);

            Assert.GreaterOrEqual(overriding.UpdateCount, 2);
            Assert.IsNull(s_updateItemField.GetValue(nonOverriding));
        }
    }
}
