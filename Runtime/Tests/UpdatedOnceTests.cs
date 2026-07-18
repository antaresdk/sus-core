using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

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
    }
}
