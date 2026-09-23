using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Characterization coverage for the <c>Attached()</c>/<c>Detached()</c> pair added by
    /// T-3998 (docs-canon/plans/impl/ARCH-20260923-MOUNT-LIFECYCLE.md step 1). Confirms the
    /// asymmetry the plan documents rather than "fixes": <c>Mounted()</c> stays ONE-SHOT per
    /// instance, <c>Unmounted()</c> fires on EVERY detach, and the new pair is symmetric with
    /// attach/detach counts.
    /// </summary>
    public class SusComponentAttachDetachLifecycleTests : UIDocumentTestHelper
    {
        private sealed class CountingComp : SusComponent
        {
            public int Mounted_Count;
            public int Unmounted_Count;
            public int Attached_Count;
            public int Detached_Count;

            protected override void Build() { }

            protected override void Mounted() => Mounted_Count++;
            protected override void Unmounted() => Unmounted_Count++;
            protected override void Attached() => Attached_Count++;
            protected override void Detached() => Detached_Count++;
        }

        [UnityTest]
        public IEnumerator AttachFrameDetachAttach_MountedOnce_UnmountedOnce_AttachedTwice_DetachedOnce()
        {
            var comp = new CountingComp();

            // First attach.
            Root.Add(comp);
            Assert.AreEqual(1, comp.Attached_Count, "Attached() fires on the first attach too, same frame");
            Assert.AreEqual(0, comp.Mounted_Count, "Mounted() is deferred to next frame");

            yield return WaitFrame();
            yield return WaitFrame();
            Assert.AreEqual(1, comp.Mounted_Count, "Mounted() ran once after the deferred frame");

            // Detach.
            comp.RemoveFromHierarchy();
            Assert.AreEqual(1, comp.Detached_Count, "Detached() fires on the detach");
            Assert.AreEqual(1, comp.Unmounted_Count, "Unmounted() fires on the detach");

            // Re-attach the SAME instance.
            Root.Add(comp);
            Assert.AreEqual(2, comp.Attached_Count, "Attached() fires again on the second attach");

            yield return WaitFrame();
            yield return WaitFrame();

            Assert.AreEqual(1, comp.Mounted_Count,
                "Mounted() is one-shot: a re-attach after the first Mounted() does NOT run it again");
            Assert.AreEqual(1, comp.Unmounted_Count, "no further detach happened, so Unmounted() stays at 1");
            Assert.AreEqual(2, comp.Attached_Count, "final: attach ran twice");
            Assert.AreEqual(1, comp.Detached_Count, "final: detach ran once — DoD numbers from the plan");
        }
    }
}
