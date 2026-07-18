namespace Sharq.Core
{
    /// <summary>
    /// Phosphor Icons provider — serves the full 1512×6 set shipped in sus-core at
    /// <c>Resources/SusRuntime/Icons/phosphor/{weight}/{name}.svg</c> (MIT license).
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
