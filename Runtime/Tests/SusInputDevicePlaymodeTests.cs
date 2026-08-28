using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// PlayMode coverage for SusInputDevice cursor policy (ARCH-LUNA-GAMEPAD §2.1).
    /// </summary>
    public class SusInputDevicePlaymodeTests
    {
        bool s_savedVisible;

        [SetUp]
        public void SetUp()
        {
            s_savedVisible = Cursor.visible;
            // T-2303: the poll driver installed by EnsureEventSystem below is a
            // DontDestroyOnLoad singleton that keeps polling real OS input for the rest of
            // the Play session (every later PlayMode test in the same batch included). A
            // stray mouse/keyboard event between our NotifyActivity(...) calls below and the
            // single-frame assertions races SusInputDevice.PollLegacy and silently flips
            // ActiveKind back to Pointer — this is what made these two tests flaky in a big
            // batch run (qa@2026-08-28-qa-1.md, T-2303). Suppress it for the test's duration
            // so only OUR NotifyActivity calls drive ActiveKind.
            SusInputDevice.SuppressLegacyPolling = true;
            SusInputDevice.AutoHideCursor = true;
            SusInputDevice.NotifyActivity(SusInputDeviceKind.Pointer);
            SusBootstrap.EnsureEventSystem();
        }

        [TearDown]
        public void TearDown()
        {
            SusInputDevice.AutoHideCursor = true;
            SusInputDevice.NotifyActivity(SusInputDeviceKind.Pointer);
            Cursor.visible = s_savedVisible;
            SusInputDevice.SuppressLegacyPolling = false;
        }

        [UnityTest]
        public IEnumerator NotifyActivity_Gamepad_HidesCursor_ThenPointerRestores()
        {
            Cursor.visible = true;
            SusInputDevice.NotifyActivity(SusInputDeviceKind.Gamepad);
            yield return null;
            Assert.IsFalse(Cursor.visible);

            SusInputDevice.NotifyActivity(SusInputDeviceKind.Keyboard);
            yield return null;
            Assert.IsFalse(Cursor.visible);

            SusInputDevice.NotifyActivity(SusInputDeviceKind.Pointer);
            yield return null;
            Assert.IsTrue(Cursor.visible);
        }

        [UnityTest]
        public IEnumerator AllowsPointerHover_TracksKindAcrossFrames()
        {
            SusInputDevice.NotifyActivity(SusInputDeviceKind.Gamepad);
            yield return null;
            Assert.IsFalse(SusInputDevice.AllowsPointerHover);

            SusInputDevice.NotifyActivity(SusInputDeviceKind.Pointer);
            yield return null;
            Assert.IsTrue(SusInputDevice.AllowsPointerHover);
        }
    }
}
