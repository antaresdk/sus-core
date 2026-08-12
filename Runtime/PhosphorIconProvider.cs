namespace Sharq.Core
{
    /// <summary>
    /// Phosphor Icons provider — serves the full 1512×6 set from
    /// <c>Resources/SusRuntime/Icons/phosphor/{weight}/{name}.svg</c> (MIT license).
    /// That folder ships as the optional <c>PhosphorIcons</c> sample, not in Runtime: without
    /// it the provider simply resolves nothing and <c>CoreIconProvider</c> covers the built-ins.
    ///
    /// Thin subclass of <see cref="ResourcesFolderIconProvider"/>; registered automatically
    /// by <see cref="PhosphorIconBootstrap"/>. Kept as a named type so callers can reference
    /// it explicitly (e.g. <c>SusApp.UseIcons(new PhosphorIconProvider())</c>).
    /// </summary>
    public sealed class PhosphorIconProvider : ResourcesFolderIconProvider
    {
        public PhosphorIconProvider() : base("com.sharq-it.sus.core", "phosphor") { }
    }
}
