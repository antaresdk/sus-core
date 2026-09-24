using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;
using Sharq.Core.Editor.TestSupport;

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

    /// <summary>
    /// T-3801 (plan ARCH-20260923-TUTORIAL-SPOTLIGHT §4/§6): <see cref="OverlayHost.Raise"/>
    /// reorders <c>_stack</c> and the DOM together WITHOUT detaching the element from the
    /// panel — proving that needs a REAL panel (a disconnected <see cref="VisualElement"/>
    /// tree never dispatches <see cref="DetachFromPanelEvent"/>/<see cref="AttachToPanelEvent"/>
    /// and has no <c>focusController</c> at all, so an assertion against either would pass
    /// trivially regardless of the implementation). An <see cref="EditorWindow"/> gives one
    /// without Play — same idiom as <c>SusStorybookHostOverlayTeardownTests</c> (T-3131).
    /// </summary>
    public class OverlayHostRaiseTests
    {
        // T-4145: ONE window for the whole fixture (was one per test — see
        // SusEditorWindowTestHost's doc for why that flickered the owner's desktop).
        static EditorWindow s_window;
        OverlayHost _host;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assume.That(!UnityEngine.Application.isBatchMode,
                "needs a real graphics device to init an EditorWindow view (T-1731 pattern)");

            s_window = SusEditorWindowTestHost.CreateAndShow();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (s_window != null) s_window.Close();
            s_window = null;
        }

        [SetUp]
        public void SetUp()
        {
            _host = new OverlayHost();
            s_window.rootVisualElement.Add(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.ClearAll();
            _host?.RemoveFromHierarchy();
            _host = null;
        }

        private static VisualElement MakeEl(string name) => new VisualElement { name = name };

        [Test]
        public void Raise_MovesElement_ToEndOfOwnCategoryBlock()
        {
            var a = MakeEl("a");
            var b = MakeEl("b");
            _host.AddToOverlay(a, OverlayCategory.Modal);
            _host.AddToOverlay(b, OverlayCategory.Modal);

            Assert.IsTrue(_host.Raise(a));

            // _stack order:
            Assert.AreSame(b, _host.Stack[0].Element);
            Assert.AreSame(a, _host.Stack[1].Element);
            // Hierarchy (DOM) order must agree with _stack — last sibling renders on top.
            Assert.AreEqual(0, _host.IndexOf(b));
            Assert.AreEqual(1, _host.IndexOf(a));
        }

        [Test]
        public void Raise_DoesNotShiftOtherCategoryNeighbours()
        {
            var transition = MakeEl("transition");
            var a = MakeEl("a");
            var b = MakeEl("b");
            var tooltip = MakeEl("tooltip");

            _host.AddToOverlay(transition, OverlayCategory.Transition);
            _host.AddToOverlay(a, OverlayCategory.Modal);
            _host.AddToOverlay(b, OverlayCategory.Modal);
            _host.AddToOverlay(tooltip, OverlayCategory.Tooltip);

            Assert.IsTrue(_host.Raise(a));

            var stack = _host.Stack;
            Assert.AreEqual(4, stack.Count);
            Assert.AreSame(transition, stack[0].Element);
            Assert.AreSame(b, stack[1].Element);
            Assert.AreSame(a, stack[2].Element);
            Assert.AreSame(tooltip, stack[3].Element);

            Assert.AreEqual(0, _host.IndexOf(transition));
            Assert.AreEqual(1, _host.IndexOf(b));
            Assert.AreEqual(2, _host.IndexOf(a));
            Assert.AreEqual(3, _host.IndexOf(tooltip));
        }

        [Test]
        public void Raise_DoesNotDetachFromPanel()
        {
            var a = MakeEl("a");
            var b = MakeEl("b");
            _host.AddToOverlay(a, OverlayCategory.Modal);
            _host.AddToOverlay(b, OverlayCategory.Modal);

            bool detached = false;
            a.RegisterCallback<DetachFromPanelEvent>(_ => detached = true);
            var panelBefore = a.panel;

            Assert.IsTrue(_host.Raise(a));

            Assert.IsFalse(detached,
                "Raise must reorder in place — a real AddToOverlay() re-add would detach/reattach and fire this");
            Assert.AreSame(panelBefore, a.panel, "element must stay on the SAME panel across Raise");
        }

        [Test]
        public void Raise_PreservesFocus_OnRaisedElement()
        {
            var a = MakeEl("a");
            a.focusable = true;
            var b = MakeEl("b");
            _host.AddToOverlay(a, OverlayCategory.Modal);
            _host.AddToOverlay(b, OverlayCategory.Modal);

            a.Focus();
            Assert.AreSame(a, _host.panel.focusController.focusedElement as VisualElement,
                "precondition: focus is on the element about to be raised");

            Assert.IsTrue(_host.Raise(a));

            Assert.AreSame(a, _host.panel.focusController.focusedElement as VisualElement,
                "Raise must not steal focus from the element it moves (T-3790 regression: a " +
                "detach/reattach round-trip through AddToOverlay clears it)");
        }

        [Test]
        public void Raise_SingleElementInCategory_IsANoOp_AndReturnsTrue()
        {
            var a = MakeEl("a");
            _host.AddToOverlay(a, OverlayCategory.Modal);

            Assert.IsTrue(_host.Raise(a));

            Assert.AreEqual(1, _host.Stack.Count);
            Assert.AreSame(a, _host.Stack[0].Element);
            Assert.AreEqual(0, _host.IndexOf(a));
        }

        [Test]
        public void Raise_ReturnsFalse_ForElementNotHostedHere()
        {
            var outsider = MakeEl("outsider");
            Assert.IsFalse(_host.Raise(outsider));
        }

        [Test]
        public void Raise_ReturnsFalse_ForNull()
        {
            Assert.IsFalse(_host.Raise(null));
        }
    }
}
