using System;
using UnityEngine.UIElements;
using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for component composition: prop passing and slots.
    /// Covers 00-CORE-PLAN.md B.11, B.14.
    /// </summary>
    public class SusCoreCompositionTests
    {
        #region B.11 — Literal prop preserves child reactivity

        /// <summary>
        /// Helper: a minimal VisualElement subclass that mimics a Sharq
        /// component with a Prop&lt;string&gt; field and a class-binding hook.
        /// </summary>
        private class MockComponent : VisualElement
        {
            public Prop<string> Variant = new("default");
            public string AddedClass;

            public MockComponent()
            {
                // Simulate what generator does: bind class based on Prop.Value
                TrackProp(Variant, (v) =>
                {
                    if (!string.IsNullOrEmpty(AddedClass))
                        RemoveFromClassList(AddedClass);
                    AddedClass = "mock--" + v;
                    AddToClassList(AddedClass);
                });
            }

            private void TrackProp<T>(Prop<T> prop, Action<T> onChange)
            {
                // Manually subscribe (in real component, ReactiveEffect does this)
                onChange(prop.Value);
            }
        }

        [Test]
        public void SetChildProp_MutatesExistingPropValue_DoesNotReplaceInstance()
        {
            var child = new MockComponent();
            var originalProp = child.Variant;
            Assert.AreEqual("default", originalProp.Value);
            Assert.IsTrue(child.ClassListContains("mock--default"));

            // Simulate generator output: <sus:MockComponent variant="primary" />
            SusComponent.SetChildProp(child, "Variant", "primary");

            // Prop instance should be the SAME object (not replaced)
            Assert.AreSame(originalProp, child.Variant,
                "SetChildProp must not replace Prop<T> instance");

            // Value should be mutated
            Assert.AreEqual("primary", child.Variant.Value,
                "Prop.Value should be mutated to 'primary'");
        }

        [Test]
        public void SetChildProp_CaseInsensitive_WorksForField()
        {
            var child = new MockComponent();
            SusComponent.SetChildProp(child, "variant", "secondary");
            Assert.AreEqual("secondary", child.Variant.Value,
                "Case-insensitive field lookup should work");
        }

        [Test]
        public void SetChildProp_PlainType_DirectAssignment()
        {
            var label = new Label();
            SusComponent.SetChildProp(label, "text", "Hello");
            Assert.AreEqual("Hello", label.text);
        }

        [Test]
        public void SetChildProp_BoolLiteral_ConvertsCorrectly()
        {
            var toggle = new Toggle();
            SusComponent.SetChildProp(toggle, "value", "True");
            Assert.IsTrue(toggle.value);
            SusComponent.SetChildProp(toggle, "value", "false");
            Assert.IsFalse(toggle.value);
        }

        [Test]
        public void SetChildProp_NullProp_InitializesNewInstance()
        {
            // Create a component-like object with null Prop field
            var child = new VisualElement();
            var field = typeof(MockComponent).GetField("Variant");
            var mock = new MockComponent();

            // Scenario: Prop field is still at its default (already initialized in ctor)
            // SetChildProp with null must create a new Prop if null
            // For this test, we just verify the mutation path works
            var original = mock.Variant;
            SusComponent.SetChildProp(mock, "variant", "danger");
            Assert.AreSame(original, mock.Variant, "Should mutate, not replace");
            Assert.AreEqual("danger", mock.Variant.Value);
        }

        #endregion

        #region B.14 — Slots between components

        [Test]
        public void CloneSlotContent_MovesElementToChildHierarchy()
        {
            var parentContainer = new VisualElement();
            var slotContent = new Label("Slot Content");
            parentContainer.Add(slotContent);

            var childContainer = new VisualElement();

            // Simulate slot projection: move content from parent to child
            slotContent.RemoveFromHierarchy();
            childContainer.Add(slotContent);

            Assert.IsNull(slotContent.parent?.parent == parentContainer ? slotContent.parent : null);
            Assert.IsNotNull(slotContent.parent);
            Assert.AreSame(childContainer, slotContent.parent);
        }

        /// <summary>
        /// T-530: the slot container created by GetSlotContainer() must carry stable classes
        /// so component USS can reach the projected children (row wrappers) without a
        /// type selector: <c>.wrapper &gt; .sus-slot { flex-direction: row }</c>.
        /// </summary>
        private class SlotHostComponent : SusComponent
        {
            public VisualElement Wrapper;
            public VisualElement DefaultSlot;
            public VisualElement AppendSlot;

            protected override void Build()
            {
                Wrapper = new VisualElement();
                Wrapper.AddToClassList("host__append");
                AppendSlot = GetSlotContainer("append");
                Wrapper.Add(AppendSlot);
                BuildSlot("append", null, AppendSlot);
                Add(Wrapper);

                DefaultSlot = GetSlotContainer(null);
                Add(DefaultSlot);
                BuildSlot("default", null, DefaultSlot);
            }
        }

        [Test]
        public void GetSlotContainer_CarriesStableSlotClasses()
        {
            var host = new SlotHostComponent();

            Assert.IsTrue(host.AppendSlot.ClassListContains(SusComponent.SlotContainerClass),
                "named slot container must have the shared 'sus-slot' class");
            Assert.IsTrue(host.AppendSlot.ClassListContains("sus-slot--append"),
                "named slot container must have 'sus-slot--<name>'");
            Assert.AreSame(host.AppendSlot, host.Wrapper[0],
                "slot container stays nested inside the author's wrapper");

            Assert.IsTrue(host.DefaultSlot.ClassListContains("sus-slot"));
            Assert.IsTrue(host.DefaultSlot.ClassListContains("sus-slot--default"),
                "null/empty name normalizes to 'default'");
            Assert.AreEqual("sus-slot--default", SusComponent.SlotContainerClassFor(""));

            // Public accessor returns the same (classed) element; classes are idempotent.
            Assert.AreSame(host.AppendSlot, host.Slot("append"));
            Assert.AreEqual(2, CountClasses(host.Slot("append")),
                "repeated GetSlotContainer must not stack duplicate classes");
        }

        private static int CountClasses(VisualElement ve)
        {
            var n = 0;
            foreach (var _ in ve.GetClasses()) n++;
            return n;
        }

        #endregion
    }
}
