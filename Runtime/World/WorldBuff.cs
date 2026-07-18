namespace Sharq.Core.World
{
    /// <summary>
    /// A single buff/debuff entry for world-space display.
    /// Part of the generic marker framework (referenced by <see cref="WorldMarkerData"/>);
    /// the content contract <c>IWorldBuffable</c> lives in downstream library.
    /// </summary>
    public struct WorldBuff
    {
        public string Icon;
        public int Stacks;
        public float Duration;
    }
}
