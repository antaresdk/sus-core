using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// R-D8 / T-1123 — SusOverlayService (ShowFloating / Hide / CloseAll / owner group).
    /// PlayMode: needs a live panel so GetOrCreateOverlay resolves.
    /// </summary>
    public class SusOverlayServiceTests : UIDocumentTestHelper
    {
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            SusBootstrap.GetOrCreateOverlay(Root);
            SusOverlayService.CloseAllFloatings();
        }

        [TearDown]
        public override void TearDown()
        {
            SusOverlayService.CloseAllFloatings();
            SusOverlayService.HideTooltip(null, null);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator ShowFloating_NullArgs_ReturnsNull()
        {
            var overlay = new VisualElement();
            var anchor = new VisualElement();
            Root.Add(anchor);
            yield return WaitFrame();

            Assert.IsNull(SusOverlayService.ShowFloating(null, anchor, OverlayCategory.Dropdown));
            Assert.IsNull(SusOverlayService.ShowFloating(overlay, null, OverlayCategory.Dropdown));
        }

        [UnityTest]
        public IEnumerator ShowFloating_ThenHide_RemovesFromHost()
        {
            var host = SusBootstrap.GetOrCreateOverlay(Root);
            var anchor = new VisualElement { name = "anchor" };
            var popup = new VisualElement { name = "popup" };
            Root.Add(anchor);
            yield return WaitFrame();

            var closed = false;
            var entry = SusOverlayService.ShowFloating(
                popup, anchor, OverlayCategory.Dropdown,
                onClose: () => closed = true, closeOthers: false);

            Assert.IsNotNull(entry);
            Assert.AreEqual(1, host.Count);
            Assert.AreSame(popup, entry.Element);

            SusOverlayService.HideFloating(popup);
            yield return WaitFrame();

            Assert.AreEqual(0, host.Count);
            Assert.IsTrue(closed);
        }

        [UnityTest]
        public IEnumerator CloseAllFloatings_ClosesActive()
        {
            var host = SusBootstrap.GetOrCreateOverlay(Root);
            var anchor = new VisualElement();
            Root.Add(anchor);
            yield return WaitFrame();

            SusOverlayService.ShowFloating(
                new VisualElement(), anchor, OverlayCategory.Dropdown, closeOthers: false);
            SusOverlayService.ShowFloating(
                new VisualElement(), anchor, OverlayCategory.Tooltip, closeOthers: false);
            Assert.GreaterOrEqual(host.Count, 2);

            SusOverlayService.CloseAllFloatings();
            yield return WaitFrame();

            Assert.AreEqual(0, host.Count);
        }

        [UnityTest]
        public IEnumerator HideFloatingsByOwner_ClosesOnlyThatGroup()
        {
            var host = SusBootstrap.GetOrCreateOverlay(Root);
            var anchor = new VisualElement();
            Root.Add(anchor);
            yield return WaitFrame();

            var ownerA = new object();
            var ownerB = new object();
            var keep = new VisualElement { name = "keep" };

            SusOverlayService.ShowFloating(
                new VisualElement(), anchor, OverlayCategory.Dropdown,
                closeOthers: false, owner: ownerA);
            SusOverlayService.ShowFloating(
                keep, anchor, OverlayCategory.Dropdown,
                closeOthers: false, owner: ownerB);

            SusOverlayService.HideFloatingsByOwner(ownerA);
            yield return WaitFrame();

            Assert.AreEqual(1, host.Count);
            Assert.AreSame(keep, host.Stack[0].Element);

            SusOverlayService.HideFloatingsByOwner(ownerB);
            yield return WaitFrame();
            Assert.AreEqual(0, host.Count);
        }

        [UnityTest]
        public IEnumerator Show_CloseOthers_ReplacesPrevious()
        {
            var host = SusBootstrap.GetOrCreateOverlay(Root);
            var anchor = new VisualElement();
            Root.Add(anchor);
            yield return WaitFrame();

            SusOverlayService.Show(new VisualElement { name = "first" }, anchor);
            Assert.AreEqual(1, host.Count);

            SusOverlayService.Show(new VisualElement { name = "second" }, anchor);
            yield return WaitFrame();

            Assert.AreEqual(1, host.Count);
            Assert.AreEqual("second", host.Stack[0].Element.name);
        }
    }
}
