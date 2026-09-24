using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core;
using Sharq.Core.Storybook;
using Sharq.Core.Editor.TestSupport;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-4101: the "theme" chip (zone B) promises to repaint the SUBJECT (plan
    /// ARCH-20260911-STORYBOOK-SHELL.md, decision D27: "the environment axis applies to the STAGE
    /// SUBTREE") — and the subject is not only the canvas's single mounted instance, it is also
    /// every cell of zone C's state matrix. A live measurement (ux-reviewer, 2026-09-24,
    /// kit/atoms/dropdown) found the promise broken for the matrix: 14 of 14 cells stayed on the
    /// dark background while the chip read "light" — <see cref="SusStorybookHost"/> was binding
    /// <c>SusStoryEnvBar</c>'s <c>previewRoot</c> and <c>SusThemeService.MarkScopedCascadeRoot</c>
    /// to the canvas alone, and the matrix is a SIBLING of the canvas under the stage, not a
    /// descendant of it — no axis bound to the canvas could ever reach a matrix cell.
    ///
    /// This is a structural ancestor check (an element between a matrix cell and the stage root
    /// carries <c>theme-light</c>/<c>theme-dark</c>), not a background-colour readback: the
    /// story-specific USS a buyer component paints its own background with is not this repo's
    /// contract, but the SCOPE the chip's class lands in is — the exact thing that drifted.
    ///
    /// <see cref="UnityTest"/> and the <see cref="EditorWindow"/> rig are the same pattern as
    /// <see cref="SusStoryMatrixOverlayTeardownTests"/>: the matrix's grid arrives over several
    /// scheduled passes that wait for layout to settle (card T-3482,
    /// <c>SusStoryMatrix.SettleFrames</c>), so a synchronous <see cref="Test"/> would see zero
    /// cells regardless of whether the fix works.
    /// </summary>
    public class SusStorybookHostThemeAxisMatrixTests
    {
        const string Swatch = "enginetests/primitives/swatch";

        /// <summary>Frames the matrix needs before its grid has settled (see class doc).</summary>
        const int Settle = 20;

        // T-4145: ONE window for the whole fixture (was one per test — see
        // SusEditorWindowTestHost's doc for why that flickered the owner's desktop).
        static EditorWindow s_window;
        SusStorybookHost _host;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assume.That(!Application.isBatchMode,
                "needs a real graphics device to init an EditorWindow view (T-1731 pattern)");

            s_window = SusEditorWindowTestHost.CreateAndShow();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (s_window != null) s_window.Close();
            s_window = null;
        }

        [SetUp]
        public void SetUp()
        {
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            _host = new SusStorybookHost();
            s_window.rootVisualElement.Add(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host?.RemoveFromHierarchy();
            _host = null;

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();

            // Theme is a process-global PROP (SusThemeService.Current) behind a scoped class —
            // leaving it on "light" here would leak into the next fixture's resting-state
            // assumptions (same reset SusStoryEnvBarTests.TearDown uses).
            var scrap = new VisualElement();
            SusThemeService.Instance.SetTheme(scrap, SusTheme.Dark);
        }

        /// <summary>Whether <paramref name="from"/> or an ancestor up to (and including)
        /// <paramref name="stopAt"/> carries <paramref name="cls"/>.</summary>
        static bool AncestryHasClass(VisualElement from, string cls, VisualElement stopAt)
        {
            for (var e = from; e != null; e = e.parent)
            {
                if (e.ClassListContains(cls)) return true;
                if (e == stopAt) break;
            }
            return false;
        }

        [UnityTest]
        public IEnumerator Theme_chip_reaches_a_matrix_cell_not_only_the_canvas()
        {
            Assert.IsTrue(_host.ShowStoryById(Swatch));
            for (int f = 0; f < Settle; f++) yield return null;

            Assert.That(_host.Matrix.BuiltCellCount, Is.GreaterThan(0), "sanity: the matrix built cells");

            var axis = _host.Env.ActiveAxes().First(a => a.Id == "theme");

            axis.Apply("light");
            for (int f = 0; f < Settle; f++) yield return null;

            var cellLight = _host.Matrix.Query<VisualElement>(className: "sb-matrix__item").First();
            Assert.That(AncestryHasClass(cellLight, "theme-light", _host), Is.True,
                "T-4101: the theme chip must reach zone C's matrix, not only the canvas instance " +
                "(ux-reviewer 2026-09-24: 14 of 14 matrix cells stayed dark while the chip read " +
                "light on kit/atoms/dropdown)");
            Assert.That(AncestryHasClass(cellLight, "theme-dark", _host), Is.False,
                "the resting theme class must be REPLACED, not left stacked alongside the new one");
            // Control: the canvas instance must still track the axis too (T-3394 must not regress
            // while T-4101 widens the scope from canvas to stage).
            Assert.That(AncestryHasClass(_host.QaCanvas, "theme-light", _host), Is.True);

            axis.Apply("dark");
            for (int f = 0; f < Settle; f++) yield return null;

            var cellDark = _host.Matrix.Query<VisualElement>(className: "sb-matrix__item").First();
            Assert.That(AncestryHasClass(cellDark, "theme-dark", _host), Is.True,
                "T-4101: switching back must reach the matrix too, not only the canvas");
            Assert.That(AncestryHasClass(cellDark, "theme-light", _host), Is.False);
        }
    }
}
