namespace Sharq.Core.World
{
    /// <summary>
    /// A single buff/debuff entry for world-space display.
    /// Part of the generic marker framework (referenced by <see cref="WorldMarkerData"/>);
    /// richer display contracts live in downstream UI packages.
    /// </summary>
    public struct WorldBuff
    {
        public string Icon;
        public int Stacks;
        public float Duration;
    }
}
