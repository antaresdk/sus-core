using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-3160: the demount contract of zone C's state MATRIX, not of the single mounted instance
    /// (that one is T-3131). Every matrix cell is a real component built by the story's own
    /// <c>Create()</c> + <c>Configure()</c> — so a story whose <c>Configure()</c> opens the
    /// component (the modal stories do: <c>Model = true</c>) builds four more self-teleporting
    /// overlays besides the one on the canvas, and those escape to the panel root exactly the way
    /// the single instance does.
    ///
    /// The showcase sweep shot 4 ghost modals carrying <c>sus-sb-matrix__item</c> in the ROOT
    /// overlay host, on frames of stories that came LATER (showcase-3, 2026-09-09) — after
    /// T-3131 had already made <c>Unmount</c> clear the root host. Everything here is therefore a
    /// <see cref="UnityTest"/>, not a <see cref="Test"/>: the leak is one FRAME wide.
    /// <c>SusOverlayComponent.UnmountSelfFromOverlay</c> answers a host-initiated
    /// <c>ClearAll()</c> by scheduling a restore of the element back into its original parent —
    /// so a synchronous test sees an empty host, and the frame after it the modal is back.
    /// </summary>
    public class SusStoryMatrixOverlayTeardownTests
    {
        const string MatrixLeak = "core/overlay/matrix-modal";
        const string Counter = "core/primitives/counter";

        EditorWindow _window;
        SusStorybookHost _host;
        OverlayHost _appOverlay;

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
            // The rig the live storybook actually runs in: an app root that ALREADY carries an
            // OverlayHost (SusBootstrap.Mount puts one on the UIDocument root, and the sweep's
            // dump shows it at 0;0;1280;720). Without it the fallback in
            // SusOverlayComponent.MountSelfInOverlay finds nothing above the matrix and every
            // cell stays inline — i.e. the corpus configuration would not be reproduced at all.
            _appOverlay = SusBootstrap.GetOrCreateOverlay(_window.rootVisualElement);
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

        int MarkersInPanel() => _window.rootVisualElement.panel.visualTree
            .Query<VisualElement>(className: CoreMatrixModalDemo.MarkerClass).ToList().Count;

        int MarkersOutsideHost() => MarkersInPanel() -
            _host.Query<VisualElement>(className: CoreMatrixModalDemo.MarkerClass).ToList().Count;

        static string Dump(VisualElement root) => string.Join(" · ",
            root.Query<VisualElement>(className: CoreMatrixModalDemo.MarkerClass).ToList()
                .Select(e => (e.parent?.GetType().Name ?? "<null>") + "/" +
                             (string.IsNullOrEmpty(e.parent?.name) ? "-" : e.parent.name)));

        [UnityTest]
        public IEnumerator Matrix_cells_do_not_escape_to_the_panel_root()
        {
            Assert.IsTrue(_host.ShowStoryById(MatrixLeak));
            yield return null;
            yield return null;

            Assert.That(_host.Matrix.BuiltCellCount, Is.GreaterThan(0), "sanity: the matrix built cells");
            Assert.That(MarkersInPanel(), Is.GreaterThan(1), "sanity: the cells are real instances");
            Assert.That(_host.Matrix.Overlay.Count, Is.EqualTo(_host.Matrix.BuiltCellCount),
                "T-3160: every cell that opens itself lands in the MATRIX's own host");
            Assert.That(_appOverlay.Count, Is.Zero,
                "T-3160: no cell may end up in the application's root overlay host");
            Assert.That(MarkersOutsideHost(), Is.Zero,
                "T-3160: a matrix cell's overlay belongs to the cell, not to panel.visualTree — " +
                "found at: " + Dump(_window.rootVisualElement.panel.visualTree));
        }

        [UnityTest]
        public IEnumerator Switching_away_leaves_no_matrix_overlay_a_frame_later()
        {
            Assert.IsTrue(_host.ShowStoryById(MatrixLeak));
            yield return null;
            yield return null;

            Assert.IsTrue(_host.ShowStoryById(Counter), "switch away -> Unmount()");
            yield return null;
            yield return null;
            yield return null;

            Assert.That(MarkersInPanel(), Is.Zero,
                "T-3160: nothing the previous story's matrix built may survive the switch — " +
                "found at: " + Dump(_window.rootVisualElement.panel.visualTree));
            Assert.That(_host.CanvasOverlay, Is.Not.Null);
            Assert.That(_host.CanvasOverlay.Count, Is.Zero, "the canvas's own overlay is empty");
        }

        [UnityTest]
        public IEnumerator Switching_back_and_forth_does_not_accumulate_overlays()
        {
            Assert.IsTrue(_host.ShowStoryById(MatrixLeak));
            yield return null;
            int first = MarkersInPanel();

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(_host.ShowStoryById(Counter));
                yield return null;
                Assert.IsTrue(_host.ShowStoryById(MatrixLeak));
                yield return null;
            }
            yield return null;

            Assert.That(MarkersInPanel(), Is.EqualTo(first),
                "T-3160: three round trips must cost nothing — " +
                "found at: " + Dump(_window.rootVisualElement.panel.visualTree));
        }
    }
}
