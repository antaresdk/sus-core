using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// T-415 (2026-08-13): a v-if element with a `transition` (e.g. SusExpansionPanel's
    /// body, <c>transition="fade"</c>) that starts HIDDEN must not be visible at all on
    /// initial mount. Before the fix, the generator's Add-then-bind emission order meant
    /// the element was already parented when BindTransitionVisibility's effect first ran,
    /// so an initial `getter() == false` played a full Leave() animation — fully opaque
    /// (`.sus-transition-leave-from` = opacity:1) for ~200ms before the scheduled removal
    /// fired. A collapsed SusExpansionPanel therefore showed its body fully readable at
    /// t=0 (screenshot pipelines and fast clicks both raced the pending removal job).
    /// </summary>
    public class BindTransitionVisibilityTests : UIDocumentTestHelper
    {
        private class TestComp : SusComponent
        {
            public VisualElement Content { get; } = new VisualElement { name = "content" };
            public void Bind(Prop<bool> prop) => BindTransitionVisibility(Content, () => prop.Value, "fade");

            protected override void Build()
            {
                Add(Content);
            }
        }

        [UnityTest]
        public IEnumerator StartsHidden_NoLeaveFlash_RemovedSynchronously()
        {
            var p = new Prop<bool>(false);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);

            // No frame wait at all — must already be gone on the very same tick, not
            // merely scheduled for later removal via a Leave() animation.
            Assert.IsFalse(comp.Contains(comp.Content),
                "content bound to a false getter must not be in the hierarchy at mount");
            Assert.IsFalse(comp.Content.ClassListContains(SusTransition.LeaveFrom),
                "no leave-from (opacity:1) flash should ever be applied on initial mount");

            yield return WaitFrame();
            Assert.IsFalse(comp.Contains(comp.Content));
        }

        [UnityTest]
        public IEnumerator StartsVisible_NoEnterAnimation_PresentSynchronously()
        {
            var p = new Prop<bool>(true);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);

            Assert.IsTrue(comp.Contains(comp.Content),
                "content bound to a true getter must be in the hierarchy at mount");
            yield return WaitFrame();
            Assert.IsTrue(comp.Contains(comp.Content));
        }

        [UnityTest]
        public IEnumerator ToggleAfterMount_StillAnimatesNormally()
        {
            var p = new Prop<bool>(false);
            var comp = new TestComp();
            Root.Add(comp);
            comp.Bind(p);
            yield return WaitFrame();
            Assert.IsFalse(comp.Contains(comp.Content));

            // A REAL user-triggered toggle (not the initial mount) should still play the
            // Enter() animation and result in the content being present.
            p.Value = true;
            yield return WaitFrame();
            Assert.IsTrue(comp.Contains(comp.Content), "Enter() must re-parent on toggle");

            p.Value = false;
            yield return WaitFrame();
            // Leave() just started — element is still present mid-animation (by design,
            // fade-out plays before the delayed removal), so this must NOT be synchronous.
            Assert.IsTrue(comp.Contains(comp.Content),
                "a real toggle-to-hidden should still animate (stay parented briefly), unlike the initial-mount case");
        }
    }
}
