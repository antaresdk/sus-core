namespace Sharq.Core.World
{
    /// <summary>
    /// Controls when a world-space marker is automatically removed.
    /// </summary>
    public enum MarkerLifetime
    {
        /// <summary>Remove when the tracked Target (Transform) is destroyed or becomes null.</summary>
        TrackTarget = 0,

        /// <summary>Only remove via explicit Dispose() call — marker persists even if target is gone.</summary>
        Manual,

        /// <summary>Remove when DataSource reports IsAlive == false (e.g. unit dies in ECS).</summary>
        WhenDead
    }
}
