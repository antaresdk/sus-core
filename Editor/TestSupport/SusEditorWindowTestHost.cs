using System;
#if UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif
using UnityEditor;
using UnityEngine;

namespace Sharq.Core.Editor.TestSupport
{
    /// <summary>
    /// One place EditMode fixtures get a real <see cref="EditorWindow"/> from (T-4145): before
    /// this, nine fixtures across this repo and a downstream package each called
    /// <c>EditorWindow.CreateInstance&lt;EditorWindow&gt;(); Show();</c> straight in their own
    /// per-TEST <c>[SetUp]</c> — a floating native OS window popping open and closing on every
    /// single test. T-1731 is why a real window is needed at all: a resolvedStyle number needs a
    /// real layout pass, which needs a real graphics device, which -batchmode -nographics does
    /// not have. At suite speed that is a new window roughly every 0.3s — read from the owner's
    /// desktop as constant flicker through whatever else had focus (owner complaint 2026-09-24,
    /// C:/Temp/sus-focus.log 18:45:31-35: foreground window title cycling "Untitled" / "" /
    /// "UnityEditor.EditorWindow" every ~0.3s).
    ///
    /// Two independent fixes, both applied by <see cref="CreateAndShow"/>:
    /// 1. Off-screen (<see cref="OffscreenRect"/>): every assertion in these fixtures reads
    ///    <c>VisualElement.resolvedStyle</c> — layout relative to the window's OWN client rect —
    ///    never <c>EditorWindow.position</c> itself (grep-checked across all nine call sites,
    ///    T-4145). Moving the window off the virtual desktop cannot change a single resolved
    ///    number, but it does mean the native handle never paints a visible pixel — nothing to
    ///    flicker, regardless of how often a fixture creates or resizes it.
    /// 2. <see cref="ShowWithoutStealingFocus"/>: <c>Show()</c> still has to activate the window
    ///    once — Unity only initializes a real view/panel for a window that has been shown — so
    ///    hand the OS foreground straight back to whoever had it. Legal unconditionally: the
    ///    instant <c>Show()</c> returns, THIS process is the foreground process (the call just
    ///    fired from its own main thread), which is the one precondition <c>SetForegroundWindow</c>
    ///    enforces — no AttachThreadInput dance needed (same precondition
    ///    sus-dev/Assets/Editor/SusFocusGuard.cs relies on for the Play-mode steal-back case).
    ///
    /// Callers that need ONE window shared by every test in a fixture create it once in
    /// <c>[OneTimeSetUp]</c> and close it in <c>[OneTimeTearDown]</c> instead of per-test
    /// <c>[SetUp]</c>/<c>[TearDown]</c> — that is the OTHER half of the T-4145 fix (window count
    /// per suite run drops from one-per-test to one-per-fixture) and is a call-site concern, not
    /// this class's; remember to detach and rebuild whatever content a test adds to
    /// <c>Window.rootVisualElement</c> in per-test <c>[SetUp]</c>/<c>[TearDown]</c> instead, or
    /// state leaks between tests that used to be cleaned up by closing the window.
    /// </summary>
    public static class SusEditorWindowTestHost
    {
        /// <summary>
        /// Far outside any real monitor's bounds — large enough that no plausible multi-monitor
        /// virtual-desktop layout reaches it, small enough to stay inside Windows' signed 32-bit
        /// screen-coordinate range.
        /// </summary>
        const float OffscreenX = -32000f;
        const float OffscreenY = -32000f;

        /// <summary>A window rect at (<see cref="OffscreenX"/>, <see cref="OffscreenY"/>) with
        /// the given size — see the class doc for why off-screen cannot affect any of these
        /// fixtures' assertions.</summary>
        public static Rect OffscreenRect(float width, float height) => new Rect(OffscreenX, OffscreenY, width, height);

        /// <summary>
        /// Creates a plain <see cref="EditorWindow"/>, positions it off-screen and shows it
        /// without taking the OS foreground away from whatever the owner is doing. Same rig every
        /// T-4145 fixture used to build inline (<c>EditorWindow.CreateInstance&lt;EditorWindow&gt;();
        /// Show();</c>) — call this instead.
        /// </summary>
        public static EditorWindow CreateAndShow(float width = 1400f, float height = 900f)
        {
            var window = EditorWindow.CreateInstance<EditorWindow>();
            window.position = OffscreenRect(width, height);
            ShowWithoutStealingFocus(window);
            return window;
        }

        /// <summary>
        /// <see cref="EditorWindow.Show()"/>, capturing the OS foreground immediately before and
        /// restoring it immediately after — see the class doc, mechanism 2. Also usable around a
        /// window opened through its own production entry point (e.g. a <c>[MenuItem]</c> static
        /// <c>Open()</c> that calls <c>Show()</c> internally) by capturing before the call and
        /// restoring after it returns — that call site must not itself lose the focus-stealing
        /// behaviour, since real menu-driven opens SHOULD take focus; only the test invocation
        /// should not.
        /// </summary>
        public static void ShowWithoutStealingFocus(EditorWindow window)
        {
#if UNITY_EDITOR_WIN
            var previousForeground = CaptureForeground();
#endif
            window.Show();
#if UNITY_EDITOR_WIN
            RestoreForeground(previousForeground);
#endif
        }

#if UNITY_EDITOR_WIN
        /// <summary>The OS foreground window handle right now, or <see cref="IntPtr.Zero"/>.</summary>
        public static IntPtr CaptureForeground() => GetForegroundWindow();

        /// <summary>Hands the OS foreground back to <paramref name="previousForeground"/>, a
        /// handle earlier returned by <see cref="CaptureForeground"/>. No-op for
        /// <see cref="IntPtr.Zero"/>.</summary>
        public static void RestoreForeground(IntPtr previousForeground)
        {
            if (previousForeground != IntPtr.Zero) SetForegroundWindow(previousForeground);
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetForegroundWindow(IntPtr hWnd);
#else
        public static IntPtr CaptureForeground() => IntPtr.Zero;
        public static void RestoreForeground(IntPtr previousForeground) { }
#endif
    }
}
