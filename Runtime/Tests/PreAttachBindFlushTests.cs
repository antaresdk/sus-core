using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Regression: ScheduleBindUpdate before panel attach is a no-op — Prop changes
    /// during Build / SetChildProp before parent.Add() must still reach Bind*
    /// after attach (flush on AttachToPanel).
    /// </summary>
    public class PreAttachBindFlushTests : UIDocumentTestHelper
    {
        private class ShowComp : SusComponent
        {
            public Prop<bool> Visible { get; } = new(false);
            public VisualElement Content { get; } = new VisualElement { name = "content" };

            protected override void Build()
            {
                BindShow(Content, () => Visible.Value);
                Add(Content);
            }
        }

        private class VisibilityComp : SusComponent
        {
            public Prop<bool> Visible { get; } = new(false);
            public VisualElement Content { get; } = new VisualElement { name = "content" };

            protected override void Build()
            {
                BindVisibility(Content, () => Visible.Value);
                Add(Content);
            }
        }

        private class TextComp : SusComponent
        {
            public Prop<string> Title { get; } = new("");
            public Label Label { get; } = new Label { name = "title" };

            protected override void Build()
            {
                BindText(Label, () => Title.Value);
                Add(Label);
            }
        }

        private class ClassComp : SusComponent
        {
            public Prop<bool> Active { get; } = new(false);
            public VisualElement Content { get; } = new VisualElement { name = "content" };

            protected override void Build()
            {
                BindClass(Content, "is-active", () => Active.Value);
                Add(Content);
            }
        }

        [UnityTest]
        public IEnumerator BindShow_PropChangeBeforeAdd_AppliesOnAttach()
        {
            var comp = new ShowComp();
            // Mimic SetChildProp / parent wiring before hierarchy attach
            comp.Visible.Value = true;
            Assert.AreEqual(DisplayStyle.None, comp.Content.style.display.value,
                "pre-attach schedule must not have applied yet (or initial false still showing)");

            Root.Add(comp);
            yield return WaitFrame();

            Assert.AreEqual(DisplayStyle.Flex, comp.Content.style.display.value);
        }

        [UnityTest]
        public IEnumerator BindVisibility_PropChangeBeforeAdd_AppliesOnAttach()
        {
            var comp = new VisibilityComp();
            // Build: BindVisibility(false) before Add → display:None, then Add parents Content
            Assert.AreEqual(DisplayStyle.None, comp.Content.style.display.value);

            comp.Visible.Value = true;
            Root.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(comp.Contains(comp.Content),
                "v-if true queued before Add must keep Content in hierarchy after attach flush");
            Assert.AreNotEqual(DisplayStyle.None, comp.Content.resolvedStyle.display);
        }

        [UnityTest]
        public IEnumerator BindText_PropChangeBeforeAdd_AppliesOnAttach()
        {
            var comp = new TextComp();
            comp.Title.Value = "Quick form";

            Root.Add(comp);
            yield return WaitFrame();

            Assert.AreEqual("Quick form", comp.Label.text);
        }

        [UnityTest]
        public IEnumerator BindClass_PropChangeBeforeAdd_AppliesOnAttach()
        {
            var comp = new ClassComp();
            comp.Active.Value = true;

            Root.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(comp.Content.ClassListContains("is-active"));
        }

        [UnityTest]
        public IEnumerator BindShow_PropChangeBeforeAdd_FalseStaysHidden()
        {
            var comp = new ShowComp();
            comp.Visible.Value = true;
            comp.Visible.Value = false;

            Root.Add(comp);
            yield return WaitFrame();

            Assert.AreEqual(DisplayStyle.None, comp.Content.style.display.value);
        }
    }
}
