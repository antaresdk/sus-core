using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sharq.Core.Storybook.Probe
{
    /// <summary>
    /// How zone C is showing the states of the current story (cards T-3482 / T-3483, plan
    /// ARCH-20260907-STORYBOOK-ENGINE.md §0.3, decision d:360869).
    ///
    /// The mode is a PUBLIC fact and not an internal branch: a buyer looking at two stories, one
    /// with a grid and one with a single instance and a row of state buttons, has to be able to
    /// read why they differ — and the cell judge has to be able to tell "no cells because the
    /// component is too big for a grid" from "no cells because the matrix is broken".
    /// </summary>
    public enum SusStoryMatrixMode
    {
        /// <summary>No matrix at all — the component's role declares no state (§4.6).</summary>
        None = 0,

        /// <summary>Rows × states, one live instance per cell.</summary>
        Grid = 1,

        /// <summary>
        /// One natural-size instance plus a row of state switches: the component is larger than
        /// <c>SusStoryMatrix.CellMaxWidth</c> × <c>CellMaxHeight</c>, so a grid of it would be a
        /// wall rather than a comparison.
        /// </summary>
        Switcher = 2,

        /// <summary>The matrix exists but starts folded (heavy story, or over the cell budget).</summary>
        Collapsed = 3,
    }

    /// <summary>
    /// One cell of the state matrix, measured (cards T-3481 / T-3483, decision d:3750a2 — "a cell
    /// promises the instance WHOLE").
    ///
    /// Four numbers, and they are four because one of them alone cannot catch the defect this
    /// record exists for. <see cref="Item"/> is where the instance actually ended up,
    /// <see cref="Cell"/> is the box that promised to hold it, <see cref="Stage"/> is the size the
    /// SAME instance takes with nothing around it, and <see cref="Scale"/> is the transform chain
    /// between them. The T-3355 crop passed acceptance precisely because the report carried none
    /// of them: an instance can sit inside its cell and still be a lie, if the cell was smaller
    /// than what the instance asked for (<see cref="Stage"/> over <see cref="Cell"/>) or if
    /// something shrank it (<see cref="Scale"/> other than 1).
    ///
    /// Everything here is read from LAYOUT, never from the rendered frame, so
    /// <c>overflow: hidden</c> cannot hide a violation from it — USS is not read at all.
    /// </summary>
    public readonly struct SusStoryCellGeometry
    {
        public SusStoryCellGeometry(
            string row, string state, Rect cell, Rect item, Vector2 stage, float scale, string violation)
        {
            Row = row;
            State = state;
            Cell = cell;
            Item = item;
            Stage = stage;
            Scale = scale;
            Violation = violation;
        }

        /// <summary>Value of the axis this cell's row stands for.</summary>
        public string Row { get; }

        /// <summary>State forced on this cell's instance.</summary>
        public string State { get; }

        /// <summary>The cell's content box in panel coordinates (padding and border removed).</summary>
        public Rect Cell { get; }

        /// <summary>The instance's <c>worldBound</c> — where it really is.</summary>
        public Rect Item { get; }

        /// <summary>The natural size of the same instance measured free of any cell.</summary>
        public Vector2 Stage { get; }

        /// <summary>Effective scale of the chain from the cell down to the instance; 1 when honest.</summary>
        public float Scale { get; }

        /// <summary>Why this cell breaks the promise, or null when it keeps it.</summary>
        public string Violation { get; }

        /// <summary>True when this cell does not show its instance whole.</summary>
        public bool Cropped => !string.IsNullOrEmpty(Violation);

        /// <summary>Tolerance of every comparison, in pixels (DoD of T-3481).</summary>
        public const float Tolerance = 1f;

        /// <summary>
        /// Judges the three points of the promise and returns the violation text, or null.
        /// Static and pure so a test states the rule without a panel.
        ///
        /// The stage size is read as "what the instance ASKED for", and it is compared against
        /// the CELL rather than against the instance. The difference matters, and it cost a live
        /// sweep to find out: comparing it against the instance accused
        /// <c>game/hud/weapon-panel</c> of being squeezed to 312 in a 339 cell that had asked to
        /// be 339 - a component that reflows when given more room is not a component that was
        /// cut. What IS a crop is a cell smaller than what its instance needs, which is exactly
        /// what the 64x28 of T-3355 was.
        /// </summary>
        public static string Judge(Rect cell, Rect item, Vector2 stage, float scale)
        {
            var faults = new List<string>();
            if (item.width > cell.width + Tolerance || item.height > cell.height + Tolerance)
                faults.Add("item " + Size(item.width, item.height) +
                           " does not fit the cell " + Size(cell.width, cell.height));
            else if (item.xMin < cell.xMin - Tolerance || item.yMin < cell.yMin - Tolerance ||
                     item.xMax > cell.xMax + Tolerance || item.yMax > cell.yMax + Tolerance)
                faults.Add("item hangs out of its cell");
            if (stage.x > 0f && stage.x > cell.width + Tolerance)
                faults.Add("cell " + N(cell.width) + " narrower than the instance needs (" +
                           N(stage.x) + ")");
            if (stage.y > 0f && stage.y > cell.height + Tolerance)
                faults.Add("cell " + N(cell.height) + " shorter than the instance needs (" +
                           N(stage.y) + ")");
            if (Mathf.Abs(scale - 1f) > 0.01f)
                faults.Add("scale " + N(scale));
            return faults.Count == 0 ? null : string.Join("; ", faults);
        }

        static string Size(float w, float h) => N(w) + "x" + N(h);

        static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What zone E asks zone C for when it builds the session report (card T-3483). An interface
    /// rather than a direct reference to <c>SusStoryMatrix</c>: the probe describes what it needs
    /// to know, and a test can hand it a stub without a matrix, a panel and a layout pass.
    /// </summary>
    public interface ISusStoryCellSource
    {
        /// <summary>How zone C is showing the states right now.</summary>
        SusStoryMatrixMode MatrixMode { get; }

        /// <summary>Why that mode and not another — named, because the buyer sees the difference.</summary>
        string MatrixModeReason { get; }

        /// <summary>Natural size of one instance of the story, measured live; zero until measured.</summary>
        Vector2 NaturalCellSize { get; }

        /// <summary>Live instances zone C built for this story — 24 in a grid, 1 in a switcher.</summary>
        int MatrixInstanceCount { get; }

        /// <summary>
        /// Instances zone C CREATED since the story was shown, the measuring probe included. The
        /// live count alone cannot prove the saving of T-3482: a switcher that reached one
        /// instance by building twenty-four and dropping them has paid the whole bill.
        /// </summary>
        int MatrixInstancesCreated { get; }

        /// <summary>Measured geometry of every cell; empty while the layout has not converged.</summary>
        IReadOnlyList<SusStoryCellGeometry> Cells { get; }

        /// <summary>
        /// How much the widest cell of the grid falls short of one instance measured free of any
        /// cell, per axis; zero when it does not. The only witness of a grid that makes its own
        /// instances small - see the property of the same name on the matrix.
        /// </summary>
        Vector2 CellShortfall { get; }
    }
}
