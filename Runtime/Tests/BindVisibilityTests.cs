using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    public class BindVisibilityTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public VisualElement Content { get; } = new VisualElement { name = "content" };
            public void Bind(Prop<bool> prop) => BindVisibility(Content, () => prop.Value);

            protected override void Build()
            {
                Add(Content);
            }
        }

        [UnityTest]
        public IEnumerator BindVisibility_True_ElementInHierarchy()
        {
            var p = new Prop<bool>(true);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.IsTrue(comp.Contains(comp.Content));
        }

        [UnityTest]
        public IEnumerator BindVisibility_False_RemovesFromHierarchy()
        {
            var p = new Prop<bool>(false);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.IsFalse(comp.Contains(comp.Content));
        }

        [UnityTest]
        public IEnumerator BindVisibility_Toggle_AddsAndRemoves()
        {
            var p = new Prop<bool>(true);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();
            Assert.IsTrue(comp.Contains(comp.Content));

            p.Value = false;
            yield return WaitFrame();
            Assert.IsFalse(comp.Contains(comp.Content));

            p.Value = true;
            yield return WaitFrame();
            Assert.IsTrue(comp.Contains(comp.Content));
        }
    }
}
