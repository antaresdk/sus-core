namespace Sharq.Core
{
    /// <summary>
    /// Semantic input actions for glyph resolution.
    /// Skins may also use string custom ids via <see cref="SusInputGlyph.Resolve(string, SusInputDeviceKind)"/>.
    /// </summary>
    public enum SusInputActionId
    {
        Submit,
        Cancel,
        Navigate,
        Alt,
        Menu
    }
}
