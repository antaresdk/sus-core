using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// v-show. T-3141 (plan ARCH-20260909-SUS-HIDDEN, D4): every assertion here reads
    /// <c>resolvedStyle.display</c> - the OUTCOME of the cascade - not
    /// <c>style.display</c>, the inline FIELD. A test that asserts the field asserts the
    /// MECHANISM: it goes red when the implementation moves with the behaviour unchanged
    /// (which is exactly what happened to these three when v-show moved from inline
    /// display to the `sus-hidden` class), and stays green on broken behaviour as long as
    /// the mechanism matches. The single legitimate reading of the inline field is the
    /// lift test at the bottom, whose subject IS the inline write.
    /// </summary>
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

        /// <summary>Binds v-show onto an element it does NOT own - see the bare-element test.</summary>
        private class ExternalBinder : SusComponent
        {
            public void BindExternal(VisualElement el, Prop<bool> prop) => BindShow(el, () => prop.Value);
            protected override void Build() { }
        }

        [UnityTest]
        public IEnumerator BindShow_True_ShowsElement()
        {
            var p = new Prop<bool>(true);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.AreEqual(DisplayStyle.Flex, comp.Content.resolvedStyle.display);
        }

        [UnityTest]
        public IEnumerator BindShow_False_HidesElement()
        {
            var p = new Prop<bool>(false);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            Assert.AreEqual(DisplayStyle.None, comp.Content.resolvedStyle.display);
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
            Assert.AreEqual(DisplayStyle.None, comp.Content.resolvedStyle.display);

            p.Value = true;
            yield return WaitFrame();
            Assert.AreEqual(DisplayStyle.Flex, comp.Content.resolvedStyle.display);
        }

        /// <summary>
        /// T-3141 risk 1 (plan ARCH-20260909-SUS-HIDDEN section 5): v-show may be bound to
        /// ANY VisualElement, including a bare one with no companion sheet of its own. There
        /// the global `.sus-hidden { display: none }` from _global.uss has no competitor -
        /// but that has to be PROVEN, not reasoned about: this element is a direct child of
        /// the panel root, outside any SusComponent subtree.
        /// </summary>
        [UnityTest]
        public IEnumerator BindShow_BareElement_NoCompanionSheet_HidesAndShows()
        {
            var p = new Prop<bool>(true);
            var bare = new VisualElement { name = "bare-no-sheet" };
            Root.Add(bare);

            var binder = new ExternalBinder();
            Root.Add(binder);
            binder.BindExternal(bare, p);
            yield return WaitFrame();

            Assert.AreEqual(DisplayStyle.Flex, bare.resolvedStyle.display,
                "sanity: a bare element with no sheet resolves to flex before v-show hides it");

            p.Value = false;
            yield return WaitFrame();
            Assert.AreEqual(DisplayStyle.None, bare.resolvedStyle.display,
                "the global .sus-hidden must hide an element that carries no sheet of its own");

            p.Value = true;
            yield return WaitFrame();
            Assert.AreEqual(DisplayStyle.Flex, bare.resolvedStyle.display);
        }

        /// <summary>
        /// T-3141 risk 2 (plan section 5): showing removes the class AND lifts a FOREIGN
        /// inline display:none - the one write of <c>style.display</c> v-show still makes.
        /// Removing only one of the two reproduces the PreAttachBindFlushTests
        /// Two/ThreeSiblings regression, so this is the one place where reading the inline
        /// FIELD is the point.
        /// </summary>
        [UnityTest]
        public IEnumerator BindShow_True_LiftsForeignInlineDisplayNone()
        {
            var p = new Prop<bool>(false);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();

            // Legacy/foreign writer (an older build, a caller outside SUS) pins the inline field.
            comp.Content.style.display = DisplayStyle.None;
            yield return WaitFrame();

            p.Value = true;
            yield return WaitFrame();

            Assert.AreEqual(StyleKeyword.Null, comp.Content.style.display.keyword,
                "showing must LIFT the foreign inline display:none - the class alone cannot " +
                "outrank an inline write");
            Assert.AreEqual(DisplayStyle.Flex, comp.Content.resolvedStyle.display);
        }
    }
}
