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
        public PhosphorIconProvider() : base("phosphor") { }
    }
}
