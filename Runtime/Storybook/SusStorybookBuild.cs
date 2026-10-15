using UnityEngine;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// Which reader a storybook build serves (UX pool POOL-01 R11, owner decision Q-158 2a). The
    /// engine's diagnostics - "range not declared", "not read", "dead props: ...", "cell budget",
    /// "heavy story", the "no state twins" note - are a developer's instrument and stay out of a
    /// public build. Captions that explain a value to a buyer (range, step, unit, the chips of
    /// special values) show in both.
    /// </summary>
    public static class SusStorybookBuild
    {
        /// <summary>
        /// True in the Editor and in a development player build; false in a release player build,
        /// which is what the public site serves. Settable, so a test or a project can force either
        /// view; read when a story is shown, so a change applies from the next story.
        /// </summary>
        public static bool Diagnostics { get; set; } = Application.isEditor || Debug.isDebugBuild;
    }
}
