using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// T-2174 — regression coverage for the same-frame Cancel()+Start() remount loop.
    ///
    /// A downstream overlay-driven step-change handler can call
    /// <c>Cancel()</c> then <c>Start()</c> in the SAME frame. Cancel
    /// goes through <see cref="SusOverlayComponent.UnmountSelfFromOverlay"/>, which schedules
    /// a deferred restore-to-original-parent for the NEXT frame (required — see the
    /// reentrancy comments in that method). Start immediately remounts into the overlay via
    /// <see cref="SusOverlayComponent.MountSelfInOverlay"/>. Before the T-2174 fix, the
    /// deferred restore from Cancel() didn't know the element had been remounted by Start()
    /// and unconditionally reparented it back out of the overlay a frame later — a spurious
    /// detach/attach that PlayMode's RemountLoopAudit catches as "attached N times in 1s"
    /// once repeated.
    /// </summary>
    public class SusOverlayComponentRemountTests : UIDocumentTestHelper
    {
        private class TestOverlayComp : SusOverlayComponent
        {
            protected override OverlayCategory Layer => OverlayCategory.Modal;

            public bool MountInOverlay() => MountSelfInOverlay();
            public void UnmountFromOverlay() => UnmountSelfFromOverlay();
            public void DismissFromOverlay() => DismissSelfFromOverlay();

            protected override void Build()
            {
            }
        }

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
        }

        [UnityTest]
        public IEnumerator CancelThenStart_SameFrame_StaysMountedInOverlay()
        {
            var host = SusBootstrap.GetOrCreateOverlay(Root);
            var inlineParent = new VisualElement { name = "inline-parent" };
            Root.Add(inlineParent);

            var comp = new TestOverlayComp();
            inlineParent.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(comp.MountInOverlay(), "initial mount should find the host");
            Assert.AreSame(host, comp.parent, "component should be reparented into the overlay host");

            // Same-frame Cancel()+Start(), exactly like SusTutorialModal's step-change handler.
            comp.UnmountFromOverlay();
            Assert.IsTrue(comp.MountInOverlay(), "re-mount right after cancel should succeed");
            Assert.AreSame(host, comp.parent, "component should be back in the overlay immediately after Start()");

            // Let the deferred restore scheduled by UnmountFromOverlay() fire.
            yield return WaitFrame();
            yield return WaitFrame();

            Assert.AreSame(host, comp.parent,
                "stale deferred restore from Cancel() must not evict the element " +
                "the subsequent Start() already remounted into the overlay (T-2174)");
            Assert.AreNotEqual(inlineParent, comp.parent,
                "component must not have been silently reparented back to its inline slot");
        }

        [UnityTest]
        public IEnumerator PlainClose_NoRemount_StillRestoresNextFrame()
        {
            // Guard against over-fixing: a normal single Cancel() (no same-frame Start())
            // must still restore to the inline parent on the next frame, as before.
            var host = SusBootstrap.GetOrCreateOverlay(Root);
            var inlineParent = new VisualElement { name = "inline-parent" };
            Root.Add(inlineParent);

            var comp = new TestOverlayComp();
            inlineParent.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(comp.MountInOverlay());
            Assert.AreSame(host, comp.parent);

            comp.UnmountFromOverlay();
            yield return WaitFrame();
            yield return WaitFrame();

            Assert.AreSame(inlineParent, comp.parent,
                "a plain close with no remount should still restore to the original parent");
        }

        [UnityTest]
        public IEnumerator Dismiss_TrackedEntry_DoesNotRestore()
        {
            // A dismissed toast must leave the tree: restoring it to its inline slot left an
            // invisible (opacity 0) but pickable box that swallowed clicks under it.
            var host = SusBootstrap.GetOrCreateOverlay(Root);
            var inlineParent = new VisualElement { name = "inline-parent" };
            Root.Add(inlineParent);

            var comp = new TestOverlayComp();
            inlineParent.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(comp.MountInOverlay());
            Assert.AreSame(host, comp.parent);

            comp.DismissFromOverlay();
            yield return WaitFrame();
            yield return WaitFrame();

            Assert.IsNull(comp.parent, "a dismissed overlay must not be restored to its original parent");
            Assert.AreEqual(0, host.Count, "the host stack entry must be gone");
        }

        [UnityTest]
        public IEnumerator Dismiss_AddedStraightIntoHost_Detaches()
        {
            // An element added directly into the host has no stack entry (MountSelfInOverlay
            // treats "already on host" as mounted); dismiss must still take it out.
            var host = SusBootstrap.GetOrCreateOverlay(Root);

            var comp = new TestOverlayComp();
            host.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(comp.MountInOverlay());
            Assert.AreSame(host, comp.parent);

            comp.DismissFromOverlay();
            yield return WaitFrame();

            Assert.IsNull(comp.parent, "dismiss must detach an element parented straight to the host");
        }

        [UnityTest]
        public IEnumerator Dismiss_AfterUnmount_CancelsPendingRestore()
        {
            var host = SusBootstrap.GetOrCreateOverlay(Root);
            var inlineParent = new VisualElement { name = "inline-parent" };
            Root.Add(inlineParent);

            var comp = new TestOverlayComp();
            inlineParent.Add(comp);
            yield return WaitFrame();

            Assert.IsTrue(comp.MountInOverlay());
            comp.UnmountFromOverlay();   // schedules a restore for the next frame
            comp.DismissFromOverlay();   // ...which must now no-op
            yield return WaitFrame();
            yield return WaitFrame();

            Assert.IsNull(comp.parent, "a restore scheduled before dismiss must not bring the element back");
        }
    }
}
