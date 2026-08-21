namespace Sharq.Core
{
    /// <summary>
    /// Optional glyph dictionary override (skins / platforms).
    /// Return false to fall back to built-in defaults.
    /// </summary>
    public interface ISusInputGlyphProvider
    {
        bool TryResolve(SusInputActionId id, SusInputDeviceKind kind, out string glyph);
        bool TryResolve(string customId, SusInputDeviceKind kind, out string glyph);
    }
}
