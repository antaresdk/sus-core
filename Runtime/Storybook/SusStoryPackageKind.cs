namespace Sharq.Core.Storybook
{
    /// <summary>
    /// What a story package IS for (plan ARCH-20260907-STORYBOOK-ENGINE §4.1b, decisions D21–D23,
    /// card T-3409).
    ///
    /// The storybook used to have exactly one kind of package, so a TEST assembly that named
    /// itself <c>core</c> got a product tab next to <c>kit</c> and <c>game</c> — fourteen engine
    /// fixtures, four of them deliberately broken, shown to a buyer as if core shipped components.
    /// The fix is not a name and not a sort order: both are text a rule has to guess at. It is a
    /// DECLARED field, so "this package is a test bench" is a fact the engine can read.
    ///
    /// The default is <see cref="Product"/> on purpose: a package that says nothing is the real
    /// thing, and only a test bench has to say so.
    /// </summary>
    public enum SusStoryPackageKind
    {
        /// <summary>Ships to the buyer: it gets a tab, a tree, search hits and sweep records.</summary>
        Product = 0,

        /// <summary>
        /// A test bench of the engine or of a package suite. It is NOT listed (no tab, no tree
        /// row, no search hit, no sweep record) and it IS addressable: a direct
        /// <c>ShowStoryById</c> and a deep link open it always, because the suites that drive the
        /// engine cite these addresses by the hundred.
        /// </summary>
        Fixture = 1,
    }
}
