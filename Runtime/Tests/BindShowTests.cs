using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    public class BindShowTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public VisualElement Content { get; } = new VisualElement();
            public void Bind(Prop<bool> prop) => BindShow(Content, () => prop.Value);

            protected override void Build()
            {
                Add(Content);
            }
        }

        [UnityTest]
        public IEnumerator BindShow_True_ShowsElement()
        {
            var p = new Prop<bool>(true);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.AreEqual(DisplayStyle.Flex, comp.Content.style.display.value);
        }

        [UnityTest]
        public IEnumerator BindShow_False_HidesElement()
        {
            var p = new Prop<bool>(false);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.AreEqual(DisplayStyle.None, comp.Content.style.display.value);
        }

        [UnityTest]
        public IEnumerator BindShow_Toggle_UpdatesDisplay()
        {
            var p = new Prop<bool>(true);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            p.Value = false;
            yield return WaitFrame();
            Assert.AreEqual(DisplayStyle.None, comp.Content.style.display.value);

            p.Value = true;
            yield return WaitFrame();
            Assert.AreEqual(DisplayStyle.Flex, comp.Content.style.display.value);
        }
    }
}
