using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Verify that subscriptions are cleaned up after component detach
    /// (prop changes after detach should NOT trigger apply).
    /// </summary>
    public class DetachCleanupTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public Label Label { get; } = new Label();
            public void Bind(Prop<string> prop) => BindText(Label, () => prop.Value);

            protected override void Build()
            {
                Add(Label);
            }
        }

        [UnityTest]
        public IEnumerator AfterDetach_PropChange_DoesNotUpdateLabel()
        {
            var p = new Prop<string>("before");
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();
            Assert.AreEqual("before", comp.Label.text);

            // Detach — should clean up bindings
            comp.RemoveFromHierarchy();
            yield return WaitFrame();

            p.Value = "after detach";
            yield return WaitFrame();

            // Label text should NOT be updated after detach
            Assert.AreEqual("before", comp.Label.text);
        }
    }
}
