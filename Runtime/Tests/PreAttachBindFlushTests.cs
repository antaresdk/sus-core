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

        /// <summary>
        /// Mimics SusChip: one Prop (Color) feeds SEVERAL independent BindClass
        /// ReactiveEffects on the same element (like the Vuetify-parity :class object
        /// literal), plus a Label with BOTH BindText and BindVisibility bound to a
        /// second Prop (Label) — the exact shape SusProfileScreenContent.SetFriends
        /// builds per friend row (T-578/T-587).
        /// </summary>
        private class MultiClassComp : SusComponent
        {
            public Prop<string> Color { get; } = new("primary");
            public Prop<string> Label { get; } = new("");
            public VisualElement Content { get; } = new VisualElement { name = "content" };
            public Label LabelEl { get; } = new Label { name = "label" };

            protected override void Build()
            {
                BindClass(Content, "is-primary", () => Color.Value == "primary");
                BindClass(Content, "is-secondary", () => Color.Value == "secondary");
                BindClass(Content, "is-success", () => Color.Value == "success");
                BindClass(Content, "is-danger", () => Color.Value == "danger");
                BindClass(Content, "is-warning", () => Color.Value == "warning");
                BindClass(Content, "is-info", () => Color.Value == "info");
                BindVisibility(LabelEl, () => !string.IsNullOrEmpty(Label.Value));
                BindText(LabelEl, () => Label.Value);
                Add(Content);
                Add(LabelEl);
            }
        }

        /// <summary>
        /// Mimics the OWNING component (e.g. SusProfileScreenContent's friend-row
        /// container) also freshly mounting in the SAME synchronous attach batch as
        /// its reactive children — not just a plain, already-inert VisualElement host.
        /// </summary>
        private class HostComp : SusComponent
        {
            public VisualElement Content { get; } = new VisualElement { name = "host-content" };

            protected override void Build() => Add(Content);
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

        // ─── T-587: sibling attach-flush race ──────────────────────────────
        // Regression for the SusProfileScreenContent.SetFriends bug (T-578): a
        // freshly-built reactive child, Props set before Add(), that attaches
        // SYNCHRONOUSLY alongside OTHER freshly-attached siblings (all part of the
        // same originating Add() cascade) must still receive its full initial
        // bind flush — not just a component attached alone (already covered above).

        [UnityTest]
        public IEnumerator TwoSiblings_PlainHost_PropsSetBeforeAdd_BothApplyOnSharedAttachBatch()
        {
            var host = new VisualElement();

            var chip1 = new MultiClassComp();
            chip1.Color.Value = "success";
            chip1.Label.Value = "online";

            var chip2 = new MultiClassComp();
            chip2.Color.Value = "danger";
            chip2.Label.Value = "away";

            host.Add(chip1);
            host.Add(chip2);

            // Single synchronous attach cascade: host + chip1 + chip2 all attach here.
            Root.Add(host);
            yield return WaitFrame();

            Assert.IsTrue(chip1.Content.ClassListContains("is-success"),
                "chip1 should reflect Color=success after shared attach-batch flush");
            Assert.AreEqual("online", chip1.LabelEl.text, "chip1 label text");
            Assert.AreNotEqual(DisplayStyle.None, chip1.LabelEl.resolvedStyle.display, "chip1 label visible");

            Assert.IsTrue(chip2.Content.ClassListContains("is-danger"),
                "chip2 should reflect Color=danger after shared attach-batch flush");
            Assert.AreEqual("away", chip2.LabelEl.text, "chip2 label text");
            Assert.AreNotEqual(DisplayStyle.None, chip2.LabelEl.resolvedStyle.display, "chip2 label visible");
        }

        [UnityTest]
        public IEnumerator ThreeSiblings_SusComponentHost_PropsSetBeforeAdd_AllApplyOnSharedAttachBatch()
        {
            // HostComp is itself a SusComponent undergoing its OWN first mount in the
            // same batch — matches SetFriends() building rows into an already-mounted
            // list container while sibling stat chips (Wins/Matches/K-D) are ALSO
            // mid-flush from the same originating host attach (T-578 live repro).
            var host = new HostComp();

            var chip1 = new MultiClassComp();
            chip1.Color.Value = "success";
            chip1.Label.Value = "online";

            var chip2 = new MultiClassComp();
            chip2.Color.Value = "secondary";
            chip2.Label.Value = "away";

            var chip3 = new MultiClassComp();
            chip3.Color.Value = "info";
            chip3.Label.Value = "offline";

            host.Content.Add(chip1);
            host.Content.Add(chip2);
            host.Content.Add(chip3);

            // Single synchronous attach cascade: host + all three chips attach here,
            // while host's own OnAttachToPanelHandler is also mid-flush.
            Root.Add(host);
            yield return WaitFrame();

            Assert.IsTrue(chip1.Content.ClassListContains("is-success"), "chip1 color class");
            Assert.AreEqual("online", chip1.LabelEl.text, "chip1 label text");
            Assert.AreNotEqual(DisplayStyle.None, chip1.LabelEl.resolvedStyle.display, "chip1 label visible");

            Assert.IsTrue(chip2.Content.ClassListContains("is-secondary"), "chip2 color class");
            Assert.AreEqual("away", chip2.LabelEl.text, "chip2 label text");
            Assert.AreNotEqual(DisplayStyle.None, chip2.LabelEl.resolvedStyle.display, "chip2 label visible");

            Assert.IsTrue(chip3.Content.ClassListContains("is-info"), "chip3 color class");
            Assert.AreEqual("offline", chip3.LabelEl.text, "chip3 label text");
            Assert.AreNotEqual(DisplayStyle.None, chip3.LabelEl.resolvedStyle.display, "chip3 label visible");
        }
    }
}
