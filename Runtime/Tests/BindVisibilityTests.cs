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

        /// <summary>
        /// T-421 (2026-08-13): a leading v-if element (e.g. a caption Label) that starts
        /// hidden and is revealed later must return to its ORIGINAL template index, not jump
        /// to the end of the parent. Regression for SusWedgeSlider label rendering below the
        /// control instead of above it.
        /// </summary>
        private class OrderComp : SusComponent
        {
            public Label Leading { get; } = new Label { name = "leading", text = "CAPTION" };
            public VisualElement Trailing { get; } = new VisualElement { name = "trailing" };
            public void Bind(Prop<bool> prop) => BindVisibility(Leading, () => prop.Value);

            protected override void Build()
            {
                Add(Leading);
                Add(Trailing);
            }
        }

        [UnityTest]
        public IEnumerator BindVisibility_RevealAfterHiddenStart_KeepsOriginalIndex()
        {
            var p = new Prop<bool>(false); // starts hidden, like SusWedgeSlider.Label ("")
            var comp = new OrderComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();
            Assert.IsFalse(comp.Contains(comp.Leading), "leading should start removed while hidden");
            Assert.AreEqual(0, comp.IndexOf(comp.Trailing));

            p.Value = true; // reveal — must re-insert BEFORE Trailing, not append after it
            yield return WaitFrame();
            Assert.IsTrue(comp.Contains(comp.Leading));
            Assert.AreEqual(0, comp.IndexOf(comp.Leading), "leading must return to index 0, not jump to the end");
            Assert.AreEqual(1, comp.IndexOf(comp.Trailing), "trailing must stay after leading");
        }
    }
}
