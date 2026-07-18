using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Verify that Prop → Computed A → Computed B → BindText chain works reactively.
    /// </summary>
    public class ComputedChainTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public Label Label { get; } = new Label();
            public void Bind(Func<string> getter) => BindText(Label, getter);

            protected override void Build()
            {
                Add(Label);
            }
        }

        [UnityTest]
        public IEnumerator Chain_PropToComputedA_ToComputedB_ToLabel()
        {
            var p = new Prop<int>(5);
            var a = new Computed<int>(() => p.Value * 2);     // 10
            var b = new Computed<string>(() => $"value: {a.Value}");

            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(() => b.Value);
            yield return WaitFrame();

            Assert.AreEqual("value: 10", comp.Label.text);

            p.Value = 7;
            yield return WaitFrame();

            Assert.AreEqual("value: 14", comp.Label.text);
        }
    }
}
