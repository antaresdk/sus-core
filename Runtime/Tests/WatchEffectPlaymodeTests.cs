using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    public class WatchEffectPlaymodeTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public int RunCount { get; private set; }
            public Label Label { get; } = new Label();

            public WatchHandle StartWatching(Prop<int> p)
            {
                return WatchEffect(() =>
                {
                    Label.text = p.Value.ToString();
                    RunCount++;
                });
            }

            protected override void Build()
            {
                Add(Label);
            }
        }

        [UnityTest]
        public IEnumerator WatchEffect_RunsOnRegistration()
        {
            var p = new Prop<int>(42);
            var comp = new TestComp();
            Root.Add(comp);
            var handle = comp.StartWatching(p);
            yield return WaitFrame();

            Assert.GreaterOrEqual(comp.RunCount, 1);
            Assert.AreEqual("42", comp.Label.text);
        }

        [UnityTest]
        public IEnumerator WatchEffect_ReRunsOnPropChange()
        {
            var p = new Prop<int>(0);
            var comp = new TestComp();
            Root.Add(comp);
            comp.StartWatching(p);
            yield return WaitFrame();
            int runsBefore = comp.RunCount;

            p.Value = 99;
            yield return WaitFrame();

            Assert.Greater(comp.RunCount, runsBefore);
            Assert.AreEqual("99", comp.Label.text);
        }

        [UnityTest]
        public IEnumerator WatchEffect_Dispose_StopsObservation()
        {
            var p = new Prop<int>(0);
            var comp = new TestComp();
            Root.Add(comp);
            var handle = comp.StartWatching(p);
            yield return WaitFrame();
            int runsBefore = comp.RunCount;

            handle.Dispose();
            p.Value = 999;
            yield return WaitFrame();

            Assert.AreEqual(runsBefore, comp.RunCount, "Should not re-run after dispose");
        }
    }
}
