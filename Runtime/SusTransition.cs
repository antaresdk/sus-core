using System;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Transition presets for <c>v-if</c> / <see cref="SusComponent.BindTransitionVisibility"/>.
    /// Class names match <c>_transitions.uss</c> (loaded via SusDefault.tss / cascade).
    /// </summary>
    public static class SusTransition
    {
        public const string Fade = "fade";
        public const string Slide = "slide";
        public const string Scale = "scale";
        public const string None = "none";

        public const string EnterFrom = "sus-transition-enter-from";
        public const string EnterActive = "sus-transition-enter-active";
        public const string EnterTo = "sus-transition-enter-to";
        public const string LeaveFrom = "sus-transition-leave-from";
        public const string LeaveActive = "sus-transition-leave-active";
        public const string LeaveTo = "sus-transition-leave-to";

        /// <summary>Default duration in ms — must match USS transition-duration.</summary>
        public const long DefaultDurationMs = 200;

        public static string PresetClass(string name) =>
            string.IsNullOrEmpty(name) || name == None ? null : $"sus-transition--{name}";
    }
}
