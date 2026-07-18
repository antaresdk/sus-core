using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    public class BindTextPlaymodeTests : UIDocumentTestHelper
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
        public IEnumerator BindText_InitialValue_IsApplied()
        {
            var p = new Prop<string>("hello");
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.AreEqual("hello", comp.Label.text);
        }

        [UnityTest]
        public IEnumerator BindText_PropChange_UpdatesLabel()
        {
            var p = new Prop<string>("initial");
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            p.Value = "updated";
            yield return WaitFrame();

            Assert.AreEqual("updated", comp.Label.text);
        }

        [UnityTest]
        public IEnumerator BindText_NullGetter_ReturnsEmptyString()
        {
            var p = new Prop<string>(null);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.AreEqual(string.Empty, comp.Label.text);
        }
    }
}
