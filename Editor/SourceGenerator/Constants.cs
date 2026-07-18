namespace Sharq.Core.Editor
{
    /// <summary>
    /// Sharq template attribute constants shared across parser and generator.
    /// </summary>
    internal static class Constants
    {
        // Special attributes
        public const string MainElement = "$MainElement";
        public const string ScopedStyleAttr = "scoped";

        // Directives
        public const string VFor = "v-for";
        public const string VIf = "v-if";
        public const string VElseIf = "v-else-if";
        public const string VElse = "v-else";
        public const string VShow = "v-show";
        public const string VSlot = "v-slot";
        public const string Transition = "transition";

        // Bindings
        public const string BindPrefix = ":";
        public const string KeyBind = ":key";
        public const string EventPrefix = "@";
    }
}
