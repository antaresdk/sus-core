using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    public class BindClassTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public VisualElement Content { get; } = new VisualElement { name = "content" };
            public void Bind(Prop<bool> prop) => BindClass(Content, "active", () => prop.Value);

            protected override void Build()
            {
                Add(Content);
            }
        }

        [UnityTest]
        public IEnumerator BindClass_True_AddsClass()
        {
            var p = new Prop<bool>(true);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.IsTrue(comp.Content.ClassListContains("active"));
        }

        [UnityTest]
        public IEnumerator BindClass_False_RemovesClass()
        {
            var p = new Prop<bool>(false);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.IsFalse(comp.Content.ClassListContains("active"));
        }

        [UnityTest]
        public IEnumerator BindClass_Toggle_AddsAndRemoves()
        {
            var p = new Prop<bool>(true);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();
            Assert.IsTrue(comp.Content.ClassListContains("active"));

            p.Value = false;
            yield return WaitFrame();
            Assert.IsFalse(comp.Content.ClassListContains("active"));
        }
    }
}
