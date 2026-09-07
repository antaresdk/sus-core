namespace Sharq.Core
{
    /// <summary>
    /// Phosphor Icons provider — serves the full 1512×6 set from any
    /// <c>Resources/SusRuntime/Icons/phosphor/{weight}/{name}.svg</c> folder (MIT license).
    ///
    /// The set ships as the optional <c>Phosphor Icon Set</c> sample rather than inside the
    /// package, so a project only pays the ~19 MB of build size when it opts in. Without the
    /// sample this provider simply resolves nothing and the built-in
    /// <see cref="CoreIconProvider"/> subset covers the icons components use by default.
    ///
    /// Thin subclass of <see cref="ResourcesFolderIconProvider"/>; registered automatically
    /// by <see cref="PhosphorIconBootstrap"/>. Kept as a named type so callers can reference
    /// it explicitly (e.g. <c>SusApp.UseIcons(new PhosphorIconProvider())</c>).
    /// </summary>
    public sealed class PhosphorIconProvider : ResourcesFolderIconProvider
    {
        /// <summary>
        /// SVG files the full set ships: 1512 names × 6 weights. Declared, not measured — the
        /// number is what a project GETS by importing the sample, and it has to be nameable
        /// exactly when the sample is absent and <see cref="ResourcesFolderIconProvider.KnownNames"/>
        /// is therefore empty (the storybook's icon picker prints it as the "not imported" hint).
        /// </summary>
        public const int DeclaredSvgCount = 9072;

        public PhosphorIconProvider() : base("phosphor") { }
    }
}
