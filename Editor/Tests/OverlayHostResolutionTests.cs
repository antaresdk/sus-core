using NUnit.Framework;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-3032 - overlay host resolution is ancestor-first in EVERY channel.
    ///
    /// Two of the three channels used to jump straight to <c>panel.visualTree</c>
    /// (SusComponent.AddToOverlay, SusOverlayService.GetOverlayHost), so a popup opened
    /// inside a container that owns its OWN OverlayHost (a Storybook story stage) landed at
    /// the panel root instead - outside the story canvas. All three now go through
    /// <see cref="SusBootstrap.FindOverlayHost"/> / <see cref="SusBootstrap.ResolveOverlayHost"/>:
    /// nearest host wins, panel root stays the fallback.
    ///
    /// EditMode (Editor test assembly): no panel is needed - resolution is a pure
    /// visual-tree walk.
    /// </summary>
    public class OverlayHostResolutionTests
    {
        private class Probe : SusComponent
        {
            public OverlayEntry Mount(VisualElement element, OverlayCategory category = OverlayCategory.Dropdown)
                => AddToOverlay(element, category);

            public void Unmount(VisualElement element) => RemoveFromOverlay(element);

            protected override void Build() { }
        }

        private class SelfMounting : SusOverlayComponent
        {
            protected override OverlayCategory Layer => OverlayCategory.Dropdown;

            public bool MountSelf() => MountSelfInOverlay();

            protected override void Build() { }
        }

        private static OverlayHost NewHost() =>
            new OverlayHost { name = OverlayHost.OverlayHostName };

        /// <summary>root [ stage [ content [ probe ], stageHost ], rootHost ]</summary>
        private static (VisualElement root, OverlayHost rootHost, VisualElement stage, OverlayHost stageHost, VisualElement content)
            BuildTree(bool withStageHost)
        {
            var root = new VisualElement { name = "root" };
            var stage = new VisualElement { name = "stage" };
            var content = new VisualElement { name = "content" };
            stage.Add(content);
            root.Add(stage);

            OverlayHost stageHost = null;
            if (withStageHost)
            {
                stageHost = NewHost();
                stage.Add(stageHost);
            }

            var rootHost = NewHost();
            root.Add(rootHost);
            return (root, rootHost, stage, stageHost, content);
        }

        // --- resolution itself -------------------------------------------------

        [Test]
        public void FindOverlayHost_NearestHostWins_OverRootHost()
        {
            var t = BuildTree(withStageHost: true);
            var probe = new Probe();
            t.content.Add(probe);

            Assert.AreSame(t.stageHost, SusBootstrap.FindOverlayHost(probe),
                "nearest host is the stage's own, not the panel root's");
        }

        [Test]
        public void FindOverlayHost_FallsBackToRootHost_WithoutNearerOne()
        {
            var t = BuildTree(withStageHost: false);
            var probe = new Probe();
            t.content.Add(probe);

            Assert.AreSame(t.rootHost, SusBootstrap.FindOverlayHost(probe),
                "no nearer host - the walk must still reach the root host (app behaviour)");
        }

        [Test]
        public void FindOverlayHost_ReturnsHostItself_WhenAlreadyMounted()
        {
            var t = BuildTree(withStageHost: true);
            var probe = new Probe();
            t.stageHost.Add(probe);

            Assert.AreSame(t.stageHost, SusBootstrap.FindOverlayHost(probe));
        }

        [Test]
        public void FindOverlayHost_ReturnsNull_WhenTreeHasNoHost()
        {
            var root = new VisualElement();
            var probe = new Probe();
            root.Add(probe);

            Assert.IsNull(SusBootstrap.FindOverlayHost(probe));
            Assert.IsNull(SusBootstrap.FindOverlayHost(null));
        }

        [Test]
        public void ResolveOverlayHost_KeepsHostLastSibling()
        {
            var t = BuildTree(withStageHost: true);
            var probe = new Probe();
            t.content.Add(probe);
            var trailing = new VisualElement { name = "after-host" };
            t.stage.Add(trailing);

            var host = SusBootstrap.ResolveOverlayHost(probe);

            Assert.AreSame(t.stageHost, host);
            Assert.AreSame(host, t.stage.ElementAt(t.stage.childCount - 1),
                "resolved host is brought to front so it paints above the stage content");
        }

        [Test]
        public void ResolveOverlayHost_ReturnsNull_WithoutHostAndWithoutPanel()
        {
            var probe = new Probe();
            new VisualElement().Add(probe);

            Assert.IsNull(SusBootstrap.ResolveOverlayHost(probe));
        }

        // --- channel 1: SusComponent.AddToOverlay ------------------------------

        [Test]
        public void AddToOverlay_MountsIntoNearestHost()
        {
            var t = BuildTree(withStageHost: true);
            var probe = new Probe();
            t.content.Add(probe);

            var popup = new VisualElement { name = "popup" };
            var entry = probe.Mount(popup);

            Assert.IsNotNull(entry);
            Assert.AreSame(t.stageHost, popup.parent, "popup stays inside the story stage");
            Assert.AreEqual(0, t.rootHost.childCount, "root host must not receive it");
        }

        [Test]
        public void AddToOverlay_MountsIntoRootHost_WithoutNearerOne()
        {
            var t = BuildTree(withStageHost: false);
            var probe = new Probe();
            t.content.Add(probe);

            var popup = new VisualElement { name = "popup" };
            probe.Mount(popup);

            Assert.AreSame(t.rootHost, popup.parent, "app behaviour is unchanged");
        }

        [Test]
        public void RemoveFromOverlay_RemovesFromTheHostThatOwnsIt()
        {
            var t = BuildTree(withStageHost: true);
            var probe = new Probe();
            t.content.Add(probe);

            var popup = new VisualElement { name = "popup" };
            probe.Mount(popup);
            Assert.AreSame(t.stageHost, popup.parent);

            probe.Unmount(popup);
            Assert.IsNull(popup.parent, "popup is detached from the stage host");
            Assert.AreEqual(0, t.stageHost.childCount);
        }

        // --- channel 3: SusOverlayComponent.MountSelfInOverlay -----------------

        [Test]
        public void MountSelfInOverlay_TeleportsIntoNearestHost()
        {
            var t = BuildTree(withStageHost: true);
            var self = new SelfMounting();
            t.content.Add(self);

            Assert.IsTrue(self.MountSelf());
            Assert.AreSame(t.stageHost, self.parent);
            Assert.AreEqual(0, t.rootHost.childCount);
        }

        [Test]
        public void MountSelfInOverlay_FallsBackToRootHost_WithoutNearerOne()
        {
            var t = BuildTree(withStageHost: false);
            var self = new SelfMounting();
            t.content.Add(self);

            Assert.IsTrue(self.MountSelf());
            Assert.AreSame(t.rootHost, self.parent);
        }
    }
}
