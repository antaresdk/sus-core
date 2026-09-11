using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Probe;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The promise of a matrix cell, stated as arithmetic (cards T-3481 / T-3483, decisions
    /// d:3750a2 and d:ef1a8a).
    ///
    /// These are EditMode tests and therefore have no layout, which is precisely why the judging
    /// half of the contract is a pure function: the numbers come from a Play sweep, the VERDICT
    /// over them must be assertable without one. The measuring half — which numbers zone C reads
    /// and when — is a Play matter and lives in the sweep.
    /// </summary>
    public class SusStoryCellGeometryTests
    {
        static readonly Rect Cell = new(0f, 0f, 100f, 40f);

        [Test]
        public void A_cell_that_shows_its_instance_whole_has_no_violation()
        {
            var item = new Rect(0f, 0f, 81f, 32f);

            Assert.That(SusStoryCellGeometry.Judge(Cell, item, new Vector2(81f, 32f), 1f), Is.Null);
        }

        [Test]
        public void An_instance_wider_than_its_cell_names_both_boxes()
        {
            // The witness of the ux-reviewer verdict of 2026-09-11: on kit/atoms/button the cell
            // was 64x28 and the instance 64x40, so the caption lost 53 of its 81 pixels — and the
            // widget reported itself healthy, because nothing anywhere compared these two boxes.
            var verdict = SusStoryCellGeometry.Judge(
                new Rect(0f, 0f, 64f, 28f), new Rect(0f, 0f, 64f, 40f), new Vector2(81f, 40f), 1f);

            Assert.That(verdict, Is.Not.Null);
            Assert.That(verdict, Does.Contain("64x40").And.Contain("64x28"));
        }

        [Test]
        public void A_cell_smaller_than_what_its_instance_needs_is_a_violation()
        {
            // The subtle half of d:3750a2: containment alone would call this healthy. The
            // instance sits inside its cell - because the cell made it smaller than it is. This
            // is the exact shape of T-3355: a 64x28 cell over a component that needs 117x40.
            var verdict = SusStoryCellGeometry.Judge(
                new Rect(0f, 0f, 64f, 28f), new Rect(0f, 0f, 64f, 28f), new Vector2(117f, 40f), 1f);

            Assert.That(verdict, Does.Contain("64").And.Contain("117"));
            Assert.That(verdict, Does.Contain("28").And.Contain("40"));
        }

        [Test]
        public void An_instance_that_reflowed_smaller_in_a_roomy_cell_is_not_a_violation()
        {
            // game/hud/weapon-panel, measured live: it asked for 339 and settled at 312 once the
            // column gave it 339. A component that reflows when given MORE room has not been cut,
            // and the first version of this judge called all four of its cells cropped.
            Assert.That(
                SusStoryCellGeometry.Judge(
                    new Rect(0f, 0f, 339f, 57f), new Rect(0f, 0f, 312f, 57f), new Vector2(339f, 57f), 1f),
                Is.Null);
        }

        [Test]
        public void A_scaled_down_miniature_is_a_violation_even_when_it_fits()
        {
            // Decision d:360869 refused scaling by arithmetic: a shrunk component shows what no
            // buyer will ever see. So a scale other than 1 is reported, not tolerated.
            var verdict = SusStoryCellGeometry.Judge(Cell, new Rect(0f, 0f, 81f, 32f), new Vector2(81f, 32f), 0.5f);

            Assert.That(verdict, Does.Contain("scale"));
        }

        [Test]
        public void A_pixel_of_rounding_is_not_a_violation()
        {
            Assert.That(
                SusStoryCellGeometry.Judge(Cell, new Rect(0f, 0f, 81.4f, 32f), new Vector2(81f, 32f), 1f),
                Is.Null, "the tolerance of the DoD is one pixel");
        }

        [Test]
        public void An_unmeasured_stage_size_does_not_invent_a_violation()
        {
            Assert.That(SusStoryCellGeometry.Judge(Cell, new Rect(0f, 0f, 81f, 32f), Vector2.zero, 1f),
                Is.Null, "zero means 'not measured', never 'measured as nothing'");
        }

        // ── the mode, and what the buyer is told about it (card T-3482) ──────

        [Test]
        public void The_grid_threshold_is_named_by_what_fits_in_zone_C()
        {
            // 777px stage − 68px of row labels ≈ 709; two natural cells across, 709 / 2 ≈ 354,
            // rounded down. The same arithmetic on the 280px scroll box gives 140.
            Assert.That(SusStoryMatrix.CellMaxWidth, Is.EqualTo(340f));
            Assert.That(SusStoryMatrix.CellMaxHeight, Is.EqualTo(140f));
        }

        [Test]
        public void A_matrix_with_no_story_is_in_no_mode_at_all()
        {
            var matrix = new SusStoryMatrix();

            Assert.That(matrix.MatrixMode, Is.EqualTo(SusStoryMatrixMode.None));
            Assert.That(matrix.MatrixModeReason, Is.Null);
            Assert.That(matrix.Cells, Is.Empty);
            Assert.That(matrix.MatrixInstancesCreated, Is.Zero);
        }

        [Test]
        public void The_report_carries_no_cells_when_nothing_measured_them()
        {
            // Card T-3483: "no cell source" and "a grid whose cells measured zero" must not look
            // the same to a rule. A probe without a matrix reports mode none.
            var report = new SusStoryProbe().BuildReport();

            Assert.That(report.MatrixMode, Is.EqualTo(SusStoryMatrixMode.None));
            Assert.That(report.Cells, Is.Empty);
            Assert.That(report.NaturalCell, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void The_report_carries_whatever_zone_C_measured()
        {
            var probe = new SusStoryProbe
            {
                CellSource = new StubSource
                {
                    MatrixMode = SusStoryMatrixMode.Switcher,
                    MatrixModeReason = "one instance is 600x400, over the 340x140 a grid cell may take",
                    NaturalCellSize = new Vector2(600f, 400f),
                    MatrixInstanceCount = 1,
                    MatrixInstancesCreated = 2,
                },
            };

            var report = probe.BuildReport();

            Assert.That(report.MatrixMode, Is.EqualTo(SusStoryMatrixMode.Switcher));
            Assert.That(report.MatrixModeReason, Does.Contain("600x400"));
            Assert.That(report.NaturalCell, Is.EqualTo(new Vector2(600f, 400f)));
            Assert.That(report.MatrixInstances, Is.EqualTo(1));
            Assert.That(report.MatrixInstancesCreated, Is.EqualTo(2),
                "a switcher that built a grid first has paid the grid's price anyway");
            Assert.That(report.Cells, Is.Empty, "a switcher has no cells");
        }

        [Test]
        public void A_cell_record_survives_the_trip_into_the_report()
        {
            var cell = new SusStoryCellGeometry(
                "primary", "disabled", Cell, new Rect(0f, 0f, 81f, 32f), new Vector2(81f, 32f), 1f, null);
            var probe = new SusStoryProbe
            {
                CellSource = new StubSource
                {
                    MatrixMode = SusStoryMatrixMode.Grid,
                    Cells = new[] { cell },
                },
            };

            var report = probe.BuildReport();

            Assert.That(report.Cells.Count, Is.EqualTo(1));
            Assert.That(report.Cells[0].Row, Is.EqualTo("primary"));
            Assert.That(report.Cells[0].State, Is.EqualTo("disabled"));
            Assert.That(report.Cells[0].Cropped, Is.False);
            Assert.That(report.Cells[0].Stage, Is.EqualTo(new Vector2(81f, 32f)));
        }

        sealed class StubSource : ISusStoryCellSource
        {
            public SusStoryMatrixMode MatrixMode { get; set; }
            public string MatrixModeReason { get; set; }
            public Vector2 NaturalCellSize { get; set; }
            public int MatrixInstanceCount { get; set; }
            public int MatrixInstancesCreated { get; set; }
            public System.Collections.Generic.IReadOnlyList<SusStoryCellGeometry> Cells { get; set; } =
                Array.Empty<SusStoryCellGeometry>();
        }
    }
}
