namespace Sharq.Core.World
{
    /// <summary>
    /// Edge side for off-screen marker projection.
    /// Determines which screen edge the marker clamps to when the tracked object is outside the camera frustum.
    /// </summary>
    public enum EdgeSide
    {
        /// <summary>Object is on-screen, no edge clamping needed.</summary>
        None = 0,

        Top,
        Bottom,
        Left,
        Right,

        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
