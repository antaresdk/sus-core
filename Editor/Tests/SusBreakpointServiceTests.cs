using UnityEngine;
using UnityEngine.UIElements;
using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for SusBreakpointService. Covers THEME_SYSTEM_WIRING.md C.5
    /// and TEST_PLAN.md §1.22 (SusBreakpointService_UpdatesOnWidth).
    /// Each test uses an isolated service instance to avoid shared state.
    /// </summary>
    public class SusBreakpointServiceTests
    {
        [Test]
        public void Update_MapsWidthToBreakpoint()
        {
            var svc = new SusBreakpointService();

            svc.Update(500);
            Assert.AreEqual(Breakpoint.Sm, svc.Current.Value);

            svc.Update(800);
            Assert.AreEqual(Breakpoint.Md, svc.Current.Value);

            svc.Update(1300);
            Assert.AreEqual(Breakpoint.Lg, svc.Current.Value);

            svc.Update(1800);
            Assert.AreEqual(Breakpoint.Xl, svc.Current.Value);

            svc.Update(3000);
            Assert.AreEqual(Breakpoint.Xxl, svc.Current.Value);
        }

        [Test]
        public void Update_UpdatesReactiveFlags_Mobile()
        {
            var svc = new SusBreakpointService();
            svc.Update(500);
            Assert.IsTrue(svc.IsMobile.Value, "Sm should be mobile");
            Assert.IsFalse(svc.IsTablet.Value);
            Assert.IsFalse(svc.IsDesktop.Value);
        }

        [Test]
        public void Update_UpdatesReactiveFlags_Tablet()
        {
            var svc = new SusBreakpointService();
            svc.Update(1300); // Lg
            Assert.IsTrue(svc.IsTablet.Value, "Lg should be tablet");
            Assert.IsFalse(svc.IsMobile.Value);
            Assert.IsFalse(svc.IsDesktop.Value);
        }

        [Test]
        public void Update_UpdatesReactiveFlags_Desktop()
        {
            var svc = new SusBreakpointService();
            svc.Update(2000); // Xl
            Assert.IsTrue(svc.IsDesktop.Value, "Xl should be desktop");
            Assert.IsFalse(svc.IsMobile.Value);
            Assert.IsFalse(svc.IsTablet.Value);
        }

        [Test]
        public void Attach_SwapsBreakpointClassOnRoot()
        {
            // Attach -> Bind -> RefreshFromRoot() probes the Editor GameView width
            // (TryGetEditorGameViewWidth), which needs a real graphics device. Under
            // -batchmode -nographics that logs '[Error] No graphic device is available
            // to initialize the view.', which Unity's test runner treats as a failure
            // even though no assertion fires (T-1731). Inconclusive, not a false red.
            Assume.That(!Application.isBatchMode,
                "Attach() probes GameView width via TryGetEditorGameViewWidth, which needs a graphics device unavailable in -nographics batchmode");

            var root = new VisualElement();
            var svc = SusBreakpointService.Attach(root);

            svc.Update(500);
            Assert.IsTrue(root.ClassListContains("breakpoint-sm"));
            Assert.IsFalse(root.ClassListContains("breakpoint-2xl"));

            svc.Update(2500);
            Assert.IsTrue(root.ClassListContains("breakpoint-2xl"));
            Assert.IsFalse(root.ClassListContains("breakpoint-sm"));

            SusBreakpointService.Detach(root);
        }

        [Test]
        public void For_ReturnsSharedInstancePerRoot()
        {
            var root = new VisualElement();
            var a = SusBreakpointService.For(root);
            var b = SusBreakpointService.For(root);
            Assert.AreSame(a, b, "Same root must yield the same cached service");
            SusBreakpointService.Detach(root);
        }

        [Test]
        public void Detach_StopsUpdatingRootClass()
        {
            var root = new VisualElement();
            var svc = SusBreakpointService.Attach(root);
            svc.Update(500);
            Assert.IsTrue(root.ClassListContains("breakpoint-sm"));

            SusBreakpointService.Detach(root);
            svc.Update(2000);
            // After Detach the root reference is cleared → class no longer synced.
            Assert.IsTrue(root.ClassListContains("breakpoint-sm"));
            Assert.IsFalse(root.ClassListContains("breakpoint-xl"));
        }

        [Test]
        public void ClassFor_UsesTailwind2xlName()
        {
            Assert.AreEqual("breakpoint-2xl", SusBreakpointService.ClassFor(Breakpoint.Xxl));
            Assert.AreEqual("breakpoint-sm", SusBreakpointService.ClassFor(Breakpoint.Sm));
        }
    }
}
