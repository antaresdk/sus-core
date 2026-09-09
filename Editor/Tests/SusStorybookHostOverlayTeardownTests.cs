using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-3131: the demount contract — "Unmount = empty canvas + empty overlays" — checked on the
    /// registry, not on the symptom. <see cref="SusStorybookHost.Unmount"/> used to clear only
    /// <c>_canvasOverlay</c>; a story that opens its overlay from
    /// <see cref="AttachToPanelEvent"/> (exactly like the real <c>SusModal</c>, see
    /// <see cref="CoreRootLeakDemo"/>'s own doc) resolves to an OverlayHost created lazily on
    /// <c>panel.visualTree</c> instead, which sat outside the reach of that clear — 37 of 95 kit
    /// showcase frames were shot with the previous story's popup still on screen (found by the
    /// showcase sweep, T-3045/T-3038 zone C).
    ///
    /// Needs a REAL panel (<c>SusBootstrap.ResolveOverlayHost</c> only falls back to
    /// <c>panel.visualTree</c> when one exists) — an <see cref="EditorWindow"/> gives one without
    /// Play, same as <c>SusSkinsWindowTests.Window_Opens_DoesNotThrow</c>. Under -batchmode
    /// -nographics there is no graphics device to init the window's view (T-1731), so this is
    /// Inconclusive there rather than a false red.
    /// </summary>
    public class SusStorybookHostOverlayTeardownTests
    {
        EditorWindow _window;
        SusStorybookHost _host;

        [SetUp]
        public void SetUp()
        {
            Assume.That(!Application.isBatchMode,
                "needs a real graphics device to init an EditorWindow view (T-1731 pattern)");

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            _window = EditorWindow.CreateInstance<EditorWindow>();
            _window.Show();
            _host = new SusStorybookHost();
            _window.rootVisualElement.Add(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;
            if (_window != null) _window.Close();
            _window = null;

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        static int LeakedLabelCount(VisualElement anyElementInThePanel) =>
            anyElementInThePanel.panel.visualTree
                .Query<Label>(className: CoreRootLeakDemo.MarkerClass).ToList().Count;

        [Test]
        public void Switching_away_clears_an_overlay_that_escaped_to_the_panel_root()
        {
            Assert.IsTrue(_host.ShowStoryById("core/overlay/root-leak"));

            // Sanity: the bug actually reproduced — the leaked label is NOT a descendant of the
            // host (it escaped past it, to panel.visualTree) and IS sitting somewhere under the
            // panel. A test that skipped this check could pass for the wrong reason if a future
            // refactor made ResolveOverlayHost stop escaping at all.
            Assert.That(_host.Query<Label>(className: CoreRootLeakDemo.MarkerClass).ToList(),
                Is.Empty, "sanity: this story's popup must land OUTSIDE the host to exercise T-3131");
            // >= 1, not == 1: AttachToPanelEvent firing more than once for one element while an
            // EditorWindow's panel is still settling is a real, separately-observed quirk of this
            // harness (not the T-3131 bug) — the CONTRACT under test is "Unmount drives this back
            // to zero", which holds regardless of how many times it fired going in.
            Assert.That(LeakedLabelCount(_host), Is.GreaterThanOrEqualTo(1), "sanity: the leak must reproduce");

            Assert.IsTrue(_host.ShowStoryById("core/primitives/counter"), "switch away -> Unmount()");

            Assert.That(LeakedLabelCount(_host), Is.Zero,
                "T-3131: Unmount must clear the panel-root overlay too, not only _canvasOverlay");
        }

        [Test]
        public void Switching_away_leaves_the_canvas_and_its_own_overlay_empty()
        {
            Assert.IsTrue(_host.ShowStoryById("core/overlay/root-leak"));
            Assert.IsTrue(_host.ShowStoryById("core/primitives/counter"));

            Assert.That(_host.CanvasOverlay, Is.Not.Null);
            Assert.That(_host.CanvasOverlay.Count, Is.Zero, "the canvas's own overlay is empty");
            // The canvas now holds the CURRENT story only — none of the previous one's elements.
            Assert.That(_host.QaCanvas.Query<Label>(className: CoreRootLeakDemo.MarkerClass).ToList(),
                Is.Empty);
        }
    }
}
