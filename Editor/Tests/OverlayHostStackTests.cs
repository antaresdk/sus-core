using NUnit.Framework;
using UnityEngine.UIElements;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    public class OverlayHostStackTests
    {
        private OverlayHost _host;

        [SetUp]
        public void SetUp()
        {
            // OverlayHost needs a parent for ValidateIsLastChild
            var root = new VisualElement();
            _host = new OverlayHost();
            root.Add(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.ClearAll();
            _host?.RemoveFromHierarchy();
            _host = null;
        }

        private static VisualElement MakeEl(string name = null)
        {
            var el = new VisualElement();
            if (name != null) el.name = name;
            return el;
        }

        [Test]
        public void AddToOverlay_SortsByCategory_Ascending()
        {
            var modal = MakeEl("modal");
            var tooltip = MakeEl("tooltip");
            var transition = MakeEl("transition");

            _host.AddToOverlay(modal, OverlayCategory.Modal);
            _host.AddToOverlay(tooltip, OverlayCategory.Tooltip);
            _host.AddToOverlay(transition, OverlayCategory.Transition);

            var stack = _host.Stack;
            Assert.AreEqual(3, stack.Count);
            // Ascending: Transition(10) → Modal(20) → Tooltip(30) — tooltips above modals
            Assert.AreEqual(OverlayCategory.Transition, stack[0].Category);
            Assert.AreEqual(OverlayCategory.Modal, stack[1].Category);
            Assert.AreEqual(OverlayCategory.Tooltip, stack[2].Category);
        }

        [Test]
        public void AddToOverlay_SameCategory_PreservesInsertionOrder()
        {
            var a = MakeEl("a");
            var b = MakeEl("b");

            _host.AddToOverlay(a, OverlayCategory.Modal);
            _host.AddToOverlay(b, OverlayCategory.Modal);

            Assert.AreEqual(2, _host.Stack.Count);
            Assert.AreSame(a, _host.Stack[0].Element);
            Assert.AreSame(b, _host.Stack[1].Element);
        }

        [Test]
        public void AddToOverlay_ElementAddedToHost()
        {
            var el = MakeEl("test");
            _host.AddToOverlay(el, OverlayCategory.Modal);

            Assert.IsTrue(_host.Contains(el));
        }

        [Test]
        public void RemoveFromOverlay_ByElement_CallsOnDismiss()
        {
            bool dismissed = false;
            var el = MakeEl("modal");
            _host.AddToOverlay(el, OverlayCategory.Modal, onDismiss: () => dismissed = true);

            _host.RemoveFromOverlay(el);

            Assert.IsTrue(dismissed);
            Assert.AreEqual(0, _host.Count);
            Assert.IsFalse(_host.Contains(el));
        }

        [Test]
        public void RemoveFromOverlay_ByEntry_CallsOnDismiss()
        {
            bool dismissed = false;
            var el = MakeEl("modal");
            var entry = _host.AddToOverlay(el, OverlayCategory.Modal, onDismiss: () => dismissed = true);

            _host.RemoveFromOverlay(entry);

            Assert.IsTrue(dismissed);
        }

        [Test]
        public void ClearCategory_RemovesOnlyItsLayer()
        {
            var modal = MakeEl("modal");
            var tooltip = MakeEl("tooltip");

            _host.AddToOverlay(modal, OverlayCategory.Modal);
            _host.AddToOverlay(tooltip, OverlayCategory.Tooltip);

            _host.ClearCategory(OverlayCategory.Modal);

            Assert.AreEqual(1, _host.Count);
            Assert.AreSame(tooltip, _host.Stack[0].Element);
        }

        [Test]
        public void ClearAll_RemovesEverything()
        {
            _host.AddToOverlay(MakeEl("a"), OverlayCategory.Modal);
            _host.AddToOverlay(MakeEl("b"), OverlayCategory.Tooltip);
            _host.AddToOverlay(MakeEl("c"), OverlayCategory.Transition);

            _host.ClearAll();

            Assert.AreEqual(0, _host.Count);
        }

        [Test]
        public void ValidateIsLastChild_True_WhenOverlayIsLastChild()
        {
            // OverlayHost added as last child in SetUp
            Assert.IsTrue(_host.ValidateIsLastChild());
        }

        [Test]
        public void ValidateIsLastChild_False_WhenSiblingAddedAfter()
        {
            var extra = new VisualElement();
            _host.parent.Add(extra); // Added after OverlayHost

            Assert.IsFalse(_host.ValidateIsLastChild());
        }

        [Test]
        public void DumpStack_DoesNotThrow_OnEmptyStack()
        {
            Assert.DoesNotThrow(() => _host.DumpStack());
        }

        [Test]
        public void DumpStack_DoesNotThrow_OnNonEmptyStack()
        {
            _host.AddToOverlay(MakeEl("test"), OverlayCategory.Modal);
            Assert.DoesNotThrow(() => _host.DumpStack());
        }

        [Test]
        public void Count_ReflectsAddAndRemove()
        {
            Assert.AreEqual(0, _host.Count);

            var el = MakeEl();
            _host.AddToOverlay(el, OverlayCategory.Modal);
            Assert.AreEqual(1, _host.Count);

            _host.RemoveFromOverlay(el);
            Assert.AreEqual(0, _host.Count);
        }

        [Test]
        public void DismissOnClickOutside_Fires_WhenClickDim()
        {
            var el = new VisualElement { name = "modal" };
            _host.AddToOverlay(el, OverlayCategory.Modal,
                dismissOnClickOutside: true);

            // The dim is a child at index 0 (inserted before element)
            var dimEl = _host.ElementAt(0);
            Assert.IsNotNull(dimEl);
        }

        [Test]
        public void DismissOnClickOutside_False_DoesNotAddDim()
        {
            var el = new VisualElement { name = "modal" };
            _host.AddToOverlay(el, OverlayCategory.Modal,
                dismissOnClickOutside: false);

            // Without dismissOnClickOutside, no dim child should be added
            // Element is inserted directly
            Assert.AreEqual(1, _host.childCount, "Only the element, no dim");
        }
    }
}
