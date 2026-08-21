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
