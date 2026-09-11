using UnityEngine;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// Tells <see cref="SusLabelClassService"/> where the SHELL ends and the PRODUCT begins
    /// (card T-3388, plan ARCH-20260911-STORYBOOK-SHELL D2/D3/D20/D27).
    ///
    /// The shell was wearing the product's clothes. Nothing in the storybook code asked for it:
    /// the label service strips <c>unity-*</c> from every <see cref="UnityEngine.UIElements.Label"/>
    /// under the cascade root and adds <c>sus-label</c> instead, and the cascade root of the
    /// storybook scene is the UIDocument root - the shell included. The measured result was 314 of
    /// 517 shell elements carrying a kit/core class (report ux-reviewer 2026-09-11), which kept the
    /// kit and skin cascades physically able to reach the viewer even after the shell stylesheet
    /// had moved onto its own names. The owner's requirement is the opposite: the storybook is
    /// styled by its OWN sheet, apart from the kit defaults it is there to show.
    ///
    /// Registration lives here rather than in <c>SusStorybookHost</c> on purpose: the boundary
    /// then holds for EVERY way a shell comes into being - the scene behaviour, a WebGL player,
    /// and the twenty-odd EditMode tests that construct <c>new SusStorybookHost()</c> directly -
    /// instead of only for the one boot path that remembered to ask.
    ///
    /// Both attributes are needed, the same pair every downstream cascade registration uses:
    /// play/player start goes through <see cref="RuntimeInitializeOnLoadMethod"/>, pure edit mode
    /// (editor tooling and EditMode tests) through <c>InitializeOnLoadMethod</c>.
    /// </summary>
    public static class SusStorybookShellIsolation
    {
        /// <summary>
        /// Root class of the shell (<c>SusStorybookHost</c> constructor). Renaming the shell
        /// prefix (card T-3387, <c>sb-</c> to <c>sb-</c>) must rename this constant in the
        /// same wave, or the boundary stops matching and the shell is painted again.
        /// </summary>
        public const string ShellRootClass = "sb-shell";

        /// <summary>
        /// Zone C stage canvas - the product island inside the shell. What a story mounts there
        /// IS the component on display and must look exactly as it looks in an application, down
        /// to the raw labels a story puts around the instance as decoration (card T-3168).
        /// </summary>
        public const string StageCanvasClass = "sb-stage__canvas";

        /// <summary>
        /// Declares the boundary. Idempotent; safe (and cheap) to call from a test that needs the
        /// guarantee without relying on load order.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void EnsureRegistered()
        {
            SusLabelClassService.ExcludeSubtreeClass(ShellRootClass);
            SusLabelClassService.IncludeSubtreeClass(StageCanvasClass);
        }

#if UNITY_EDITOR
        // RuntimeInitialize only fires on entering play: edit-mode tooling and EditMode tests
        // build shells without ever entering play.
        [UnityEditor.InitializeOnLoadMethod]
        private static void EditorEnsureRegistered() => EnsureRegistered();
#endif
    }
}
