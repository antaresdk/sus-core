using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;

namespace Sharq.Core.Runtime.Tests
{
    public class BindListTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public VisualElement Container { get; } = new VisualElement { name = "container" };
            public void Bind(Prop<List<string>> prop)
            {
                BindList<string>(
                    Container,
                    () => prop.Value,
                    (item, _) => new Label(item) { name = item });
            }

            protected override void Build()
            {
                Add(Container);
            }
        }

        [UnityTest]
        public IEnumerator BindList_InitialItems_Rendered()
        {
            var p = new Prop<List<string>>(new List<string> { "a", "b", "c" });
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.AreEqual(3, comp.Container.childCount);
        }

        [UnityTest]
        public IEnumerator BindList_AddItem_AddsElement()
        {
            var p = new Prop<List<string>>(new List<string> { "a" });
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();
            Assert.AreEqual(1, comp.Container.childCount);

            p.Value = new List<string> { "a", "b" };
            yield return WaitFrame();
            Assert.AreEqual(2, comp.Container.childCount);
        }

        [UnityTest]
        public IEnumerator BindList_RemoveItem_RemovesElement()
        {
            var p = new Prop<List<string>>(new List<string> { "a", "b" });
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            p.Value = new List<string> { "a" };
            yield return WaitFrame();
            Assert.AreEqual(1, comp.Container.childCount);
        }
    }
}
