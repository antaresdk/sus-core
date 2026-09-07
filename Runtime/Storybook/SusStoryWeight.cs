namespace Sharq.Core.Storybook
{
    /// <summary>
    /// How expensive ONE instance of the component a story is about is to build
    /// (plan ARCH-20260907-STORYBOOK-ENGINE.md §0.3, card T-3038).
    ///
    /// The state matrix builds an instance PER CELL, so weight is the story's own answer to
    /// "may the shell build two dozen of me at once". It is declared, not measured: a build-time
    /// probe would have to build the very instance we are trying not to build.
    /// </summary>
    public enum SusStoryWeight
    {
        /// <summary>An instance is cheap; the matrix is bound only by the cell budget.</summary>
        Normal = 0,

        /// <summary>
        /// An instance is expensive (a data table, an inventory, a battle grid, a minimap):
        /// the matrix stays collapsed until the reader asks for it, whatever the budget says.
        /// </summary>
        Heavy = 1,
    }
}
