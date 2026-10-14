using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Controls;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Card T-3374, UX pool POOL-01 U-032: the icon picker's popup of zone D opened as a bare
    /// Unity window below the field and ran off the bottom edge of the page.
    ///
    /// Two causes, one per test group. (1) The control panel sits BESIDE the stage, not inside
    /// it, so "the nearest overlay host above the control" resolved to none and the popup went to
    /// a host on the panel root - outside the shell subtree, outside the shell sheet. The panel
    /// now owns a host of its own. (2) Placement measured the list before it had a size, so a
    /// list taller than the room below was never flipped or pulled back inside the window.
    /// </summary>
    public class SusControlPanelOverlayTests
    {
        SusControlPanel _panel;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
        }

        SusIconPickerControl PickerInPanel(out VisualElement shell)
        {
            _panel = new SusControlPanel(new SusIntrospectionFixture(), "Fixture");
            shell = new VisualElement();
            shell.AddToClassList("sb-shell");
            shell.Add(_panel);
            var picker = _panel.Query<SusIconPickerControl>().First();
            Assert.IsNotNull(picker, "the fixture declares an Icon prop, so the panel builds a picker");
            return picker;
        }

        [Test]
        public void Panel_OwnsAnOverlayHost_AsItsLastChild()
        {
            PickerInPanel(out _);

            Assert.IsNotNull(_panel.Overlay);
            Assert.AreSame(_panel, _panel.Overlay.hierarchy.parent, "a direct child, where FindOverlayHost scans");
            Assert.AreSame(_panel.Overlay, _panel.hierarchy.ElementAt(_panel.hierarchy.childCount - 1),
                "last sibling paints on top");
        }

        [Test]
        public void IconPicker_ResolvesToThePanelsHost_NotToAPanelRootHost()
        {
            var picker = PickerInPanel(out _);

            Assert.AreSame(_panel.Overlay, SusBootstrap.FindOverlayHost(picker),
                "ancestor-first resolution (T-3032) now ends at the panel's own host");
        }

        [Test]
        public void IconPicker_Popup_LandsInsideTheShellSubtree()
        {
            var picker = PickerInPanel(out var shell);

            picker.Open();
            try
            {
                Assert.IsTrue(picker.IsOpen, "the popup opened");
                Assert.AreSame(_panel.Overlay, picker.Host);
                Assert.IsTrue(shell.Contains(picker.Popup),
                    "inside the shell subtree - that is what puts the shell sheet and --sb-* tokens on it");
            }
            finally
            {
                picker.Close();
            }
            Assert.IsFalse(picker.IsOpen);
        }

        // -- placement: pure geometry, no panel needed -----------------------------

        static readonly Rect Root = new Rect(0, 0, 1000, 600);

        [Test]
        public void Placement_ListFitsBelow_SitsUnderTheAnchor()
        {
            var anchor = new Rect(700, 100, 200, 30);
            var p = OverlayHost.PlaceDropdownWorld(anchor, new Vector2(200, 150), Root);
            Assert.AreEqual(new Vector2(700, 130), p);
        }

        [Test]
        public void Placement_NoRoomBelowRoomAbove_FlipsAbove()
        {
            var anchor = new Rect(700, 500, 200, 30);
            var p = OverlayHost.PlaceDropdownWorld(anchor, new Vector2(200, 300), Root);
            Assert.AreEqual(new Vector2(700, 200), p, "bottom of the list meets the top of the anchor");
        }

        [Test]
        public void Placement_RoomNowhere_IsPulledBackInsideTheWindow()
        {
            // The bug of U-032: anchor at y=320 of a 450 window, a 340 list - neither side fits.
            var root = new Rect(0, 0, 800, 450);
            var anchor = new Rect(620, 320, 170, 30);
            var p = OverlayHost.PlaceDropdownWorld(anchor, new Vector2(236, 340), root);
            Assert.LessOrEqual(p.y + 340, root.yMax, "the bottom edge is inside the window");
            Assert.GreaterOrEqual(p.y, root.y);
            Assert.LessOrEqual(p.x + 236, root.xMax, "so is the right edge");
        }

        [Test]
        public void Placement_UnmeasuredList_StaysAtTheAnchorsBottomLeft()
        {
            var anchor = new Rect(700, 100, 200, 30);
            Assert.AreEqual(new Vector2(700, 130),
                OverlayHost.PlaceDropdownWorld(anchor, new Vector2(float.NaN, float.NaN), Root));
            Assert.AreEqual(new Vector2(700, 130),
                OverlayHost.PlaceDropdownWorld(anchor, Vector2.zero, Root));
        }
    }
}
