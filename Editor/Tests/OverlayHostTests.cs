using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Unit tests for OverlayHost — portal-style overlay container.
    /// Verifies stack management, DOM positioning, and category sorting.
    /// Run from Unity Test Runner: Window → General → Test Runner.
    /// </summary>
    public class OverlayHostTests
    {
        private OverlayHost _host;

        [SetUp]
        public void SetUp()
        {
            _host = new OverlayHost();
        }

        [TearDown]
        public void TearDown()
        {
            _host.ClearAll();
        }

        [Test]
        public void AddToOverlay_AppendsToStack()
        {
            var el = new VisualElement();
            var entry = _host.AddToOverlay(el, OverlayCategory.Tooltip);

            Assert.AreEqual(1, _host.Count);
            Assert.AreEqual(1, _host.Stack.Count);
            Assert.AreSame(el, entry.Element);
            Assert.AreEqual(OverlayCategory.Tooltip, entry.Category);
            Assert.IsFalse(entry.DismissOnClickOutside);
        }

        [Test]
        public void AddToOverlay_ReturnsNullForNullElement()
        {
            var entry = _host.AddToOverlay(null, OverlayCategory.Modal);

            Assert.IsNull(entry);
            Assert.AreEqual(0, _host.Count);
        }

        [Test]
        public void AddToOverlay_SetsDismissOnClickOutside()
        {
            var el = new VisualElement();
            var entry = _host.AddToOverlay(el, OverlayCategory.Modal, dismissOnClickOutside: true);

            Assert.IsTrue(entry.DismissOnClickOutside);
        }

        [Test]
        public void RemoveFromOverlay_RemovesElementAndStackEntry()
        {
            var el = new VisualElement();
            _host.AddToOverlay(el, OverlayCategory.Tooltip);
            Assert.AreEqual(1, _host.Count);

            _host.RemoveFromOverlay(el);

            Assert.AreEqual(0, _host.Count);
            Assert.AreEqual(0, _host.Stack.Count);
        }

        [Test]
        public void RemoveFromOverlay_DoesNotThrowOnNull()
        {
            Assert.DoesNotThrow(() => _host.RemoveFromOverlay((VisualElement)null));
            Assert.DoesNotThrow(() => _host.RemoveFromOverlay((OverlayEntry)null));
        }

        [Test]
        public void RemoveFromOverlay_InvokesOnDismiss()
        {
            var dismissed = false;
            var el = new VisualElement();
            _host.AddToOverlay(el, OverlayCategory.Tooltip, onDismiss: () => dismissed = true);

            _host.RemoveFromOverlay(el);

            Assert.IsTrue(dismissed);
        }

        [Test]
        public void RemoveFromOverlay_ByEntry_Works()
        {
            var el = new VisualElement();
            var entry = _host.AddToOverlay(el, OverlayCategory.Dropdown);
            Assert.AreEqual(1, _host.Count);

            _host.RemoveFromOverlay(entry);

            Assert.AreEqual(0, _host.Count);
        }

        [Test]
        public void RemoveFromOverlay_DoesNotAffectOtherEntries()
        {
            var el1 = new VisualElement();
            var el2 = new VisualElement();
            _host.AddToOverlay(el1, OverlayCategory.Tooltip);
            _host.AddToOverlay(el2, OverlayCategory.Modal);

            _host.RemoveFromOverlay(el1);

            Assert.AreEqual(1, _host.Count);
            Assert.AreSame(el2, _host.Stack[0].Element);
        }

        [Test]
        public void ClearAll_RemovesAllEntries()
        {
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Tooltip);
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Modal);
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Console);

            _host.ClearAll();

            Assert.AreEqual(0, _host.Count);
        }

        /// <summary>
        /// Card T-3168: "cleared" has to mean the host is EMPTY, not "the stack is empty". A host
        /// has no chrome of its own (its click guard is a callback, not an element), so a child
        /// nobody registered through <see cref="OverlayHost.AddToOverlay"/> is content whose owner
        /// is gone: an attractor particle layer, a ring cursor, a busy sheet, or — the live case —
        /// a Storybook story's trigger button, inserted beside a component that had already
        /// teleported itself in here. Those survived every teardown and were photographed on
        /// later stories (showcase-3, 2026-09-09).
        /// </summary>
        [Test]
        public void ClearAll_RemovesChildrenThatNeverWentThroughAddToOverlay()
        {
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Modal);
            var stray = new VisualElement { name = "stray" };
            _host.Add(stray);   // straight into the hierarchy, no stack entry

            Assert.AreEqual(2, _host.childCount, "sanity: both the overlay and the stray are here");

            _host.ClearAll();

            Assert.AreEqual(0, _host.Count, "no stack entries left");
            Assert.AreEqual(0, _host.childCount, "and no children left either");
            Assert.IsNull(stray.parent);
        }

        [Test]
        public void ClearCategory_LeavesUnregisteredChildrenAlone()
        {
            // The narrow sweep stays narrow: only ClearAll promises an empty host, because only
            // ClearAll is a teardown. ClearCategory dismisses one KIND of overlay while the rest
            // of the screen — including a service's own layer — keeps living.
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Tooltip);
            var layer = new VisualElement { name = "service-layer" };
            _host.Add(layer);

            _host.ClearCategory(OverlayCategory.Tooltip);

            Assert.AreEqual(0, _host.Count);
            Assert.AreSame(_host, layer.parent, "a category sweep is not a teardown");
        }

        [Test]
        public void ClearCategory_RemovesOnlyMatchingEntries()
        {
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Tooltip);
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Modal);
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Tooltip);

            _host.ClearCategory(OverlayCategory.Tooltip);

            Assert.AreEqual(1, _host.Count);
            Assert.AreEqual(OverlayCategory.Modal, _host.Stack[0].Category);
        }

        [Test]
        public void Stack_IsEmptyInitially()
        {
            Assert.AreEqual(0, _host.Count);
            Assert.AreEqual(0, _host.Stack.Count);
        }

        [Test]
        public void CategoryOrder_LowerCategoryBehind_HigherOnTop()
        {
            // Categories: World(0) < Transition(10) < Modal(20) < Tooltip(30) < Dropdown(40) < Console(50)
            // In DOM order: last child = topmost (USS no z-index)
            var tooltip = new VisualElement();
            var modal = new VisualElement();

            _host.AddToOverlay(tooltip, OverlayCategory.Tooltip);   // 30
            _host.AddToOverlay(modal, OverlayCategory.Modal);       // 20

            // Tooltip (30) > Modal (20) — tooltip inserted AFTER modal → tooltip on top
            Assert.AreEqual(2, _host.childCount);
            Assert.AreSame(modal, _host.Children().First());     // modal first (behind)
            Assert.AreSame(tooltip, _host.Children().Last());    // tooltip last (on top)
        }

        [Test]
        public void CategoryOrder_HigherCategoryStillOnTop_WhenAddedFirst()
        {
            var modal = new VisualElement();
            var tooltip = new VisualElement();

            // Add modal first (20), then tooltip (30 — higher category)
            _host.AddToOverlay(modal, OverlayCategory.Modal);      // 20
            _host.AddToOverlay(tooltip, OverlayCategory.Tooltip);  // 30

            // Tooltip (30) > Modal (20) — tooltip always after modal
            Assert.AreEqual(2, _host.childCount);
            Assert.AreSame(modal, _host.Children().First());
            Assert.AreSame(tooltip, _host.Children().Last());
        }

        [Test]
        public void CategoryOrder_SameCategory_MaintainsInsertionOrder()
        {
            var el1 = new VisualElement { name = "first" };
            var el2 = new VisualElement { name = "second" };
            var el3 = new VisualElement { name = "third" };

            _host.AddToOverlay(el1, OverlayCategory.Modal);
            _host.AddToOverlay(el2, OverlayCategory.Modal);
            _host.AddToOverlay(el3, OverlayCategory.Modal);

            Assert.AreEqual(3, _host.childCount);
            // Same category: later added = higher in DOM
            Assert.AreSame(el1, _host.Children().First());
            Assert.AreSame(el2, _host.Children().ElementAt(1));
            Assert.AreSame(el3, _host.Children().Last());
        }

        [Test]
        public void CategoryOrder_FullSpectrum_CorrectOrdering()
        {
            var world = new VisualElement();
            var transition = new VisualElement();
            var modal = new VisualElement();
            var tooltip = new VisualElement();
            var dropdown = new VisualElement();
            var console = new VisualElement();

            // Add in random order
            _host.AddToOverlay(modal, OverlayCategory.Modal);
            _host.AddToOverlay(tooltip, OverlayCategory.Tooltip);
            _host.AddToOverlay(console, OverlayCategory.Console);
            _host.AddToOverlay(world, OverlayCategory.World);
            _host.AddToOverlay(dropdown, OverlayCategory.Dropdown);
            _host.AddToOverlay(transition, OverlayCategory.Transition);

            // Expected DOM order: World(0), Transition(10), Modal(20), Tooltip(30), Dropdown(40), Console(50)
            Assert.AreEqual(6, _host.childCount);
            Assert.AreSame(world,      _host.Children().ElementAt(0));
            Assert.AreSame(transition, _host.Children().ElementAt(1));
            Assert.AreSame(modal,      _host.Children().ElementAt(2));
            Assert.AreSame(tooltip,    _host.Children().ElementAt(3));
            Assert.AreSame(dropdown,   _host.Children().ElementAt(4));
            Assert.AreSame(console,    _host.Children().ElementAt(5));
            // Console last = topmost
        }

        [Test]
        public void AddToOverlay_ReaddingSameElement_KeepsSingleChild()
        {
            var el = new VisualElement();
            _host.AddToOverlay(el, OverlayCategory.Tooltip);

            Assert.DoesNotThrow(() => _host.AddToOverlay(el, OverlayCategory.Tooltip));
            Assert.AreEqual(1, _host.Count);
            Assert.AreEqual(1, _host.childCount);
            Assert.AreSame(el, _host.Children().First());
        }

        [Test]
        public void AddToOverlay_AfterManualDetach_PrunesStaleStack()
        {
            // Simulate detach without RemoveFromOverlay (stack/DOM desync).
            var stale = new VisualElement();
            _host.AddToOverlay(stale, OverlayCategory.Modal);
            stale.RemoveFromHierarchy();
            Assert.AreEqual(0, _host.childCount);

            var el = new VisualElement();
            Assert.DoesNotThrow(() => _host.AddToOverlay(el, OverlayCategory.Tooltip));
            Assert.AreEqual(1, _host.Count);
            Assert.AreEqual(1, _host.childCount);
            Assert.AreSame(el, _host.Children().First());
        }

        [Test]
        public void DismissOnClickOutside_DefaultsToFalse()
        {
            var el = new VisualElement();
            var entry = _host.AddToOverlay(el, OverlayCategory.Modal);

            Assert.IsFalse(entry.DismissOnClickOutside);
        }

        [Test]
        public void Stack_IsReadOnly()
        {
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Tooltip);

            var stack = _host.Stack;
            Assert.AreEqual(1, stack.Count);
        }

        [Test]
        public void ClearAll_InvokesOnDismiss()
        {
            int dismissCount = 0;
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Tooltip, onDismiss: () => dismissCount++);
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Modal, onDismiss: () => dismissCount++);

            _host.ClearAll();

            Assert.AreEqual(2, dismissCount);
        }

        [Test]
        public void ClearAll_ReentrantRemoveFromOverlay_DoesNotDoubleDismissOrThrow()
        {
            // T-499 regression: a self-teleporting overlay (SusModal/SusSnackbar) closes
            // itself via Unmounted() -> CloseFromOverlay() -> UnmountSelfFromOverlay() ->
            // RemoveFromOverlay(), fired SYNCHRONOUSLY as a side effect of the
            // RemoveFromHierarchy() call ClearAll makes on its behalf — this test's OnDismiss
            // stands in for that chain. The old ClearAll() only cleared `_stack` once, after
            // its whole sweep finished, so a reentrant removal mid-sweep found its own entry
            // still present and removed/dismissed it a second time (index bookkeeping for the
            // REST of the sweep corrupted too — reproduced live as UIR "already being
            // modified" assertions across 9 subsequent ShotAll captures, see
            // 2026-08-13-showcase-4.md). ClearAll now routes through the same
            // RemoveFromOverlay(OverlayEntry) a single dismiss uses, which removes the stack
            // entry BEFORE touching the DOM/invoking OnDismiss, so a reentrant call finds
            // nothing left to remove and safely no-ops.
            int dismissCountA = 0, dismissCountB = 0;
            OverlayEntry entryA = null;
            var elA = new VisualElement();
            entryA = _host.AddToOverlay(elA, OverlayCategory.Modal, onDismiss: () =>
            {
                dismissCountA++;
                _host.RemoveFromOverlay(entryA); // reentrant — must be a safe no-op
            });
            _host.AddToOverlay(new VisualElement(), OverlayCategory.Tooltip, onDismiss: () => dismissCountB++);

            Assert.DoesNotThrow(() => _host.ClearAll());

            Assert.AreEqual(0, _host.Count);
            Assert.AreEqual(1, dismissCountA, "reentrant self-removal must not double-invoke its own OnDismiss");
            Assert.AreEqual(1, dismissCountB, "an unrelated entry must still be removed once during the same sweep");
        }
    }
}
