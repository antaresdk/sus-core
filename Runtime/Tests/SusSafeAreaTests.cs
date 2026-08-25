using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// C-S2 / D19 — SusSafeArea Provider seam (EditMode, no device).
    /// </summary>
    public class SusSafeAreaTests
    {
        [TearDown]
        public void TearDown()
        {
            SusSafeArea.ResetForTests();
        }

        [Test]
        public void Provider_Default_IsScreenSafeArea()
        {
            // Default provider must equal Screen.safeArea (editor = full screen → zero insets).
            var fromProvider = SusSafeArea.Provider();
            Assert.AreEqual(Screen.safeArea, fromProvider);
        }

        [Test]
        public void Provider_Override_DrivesInsets_WithoutPanel()
        {
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);

            // Fake notch: 40px top, 20 left, 20 right, 30 bottom (screen pixels).
            SusSafeArea.Provider = () => new Rect(20f, 30f, sw - 40f, sh - 70f);

            var insets = SusSafeArea.ComputeInsets(null);
            Assert.AreEqual(40f, insets.Top, 0.01f);
            Assert.AreEqual(20f, insets.Right, 0.01f);
            Assert.AreEqual(30f, insets.Bottom, 0.01f);
            Assert.AreEqual(20f, insets.Left, 0.01f);
        }

        [Test]
        public void Apply_SetsPadding_AndRaisesChanged()
        {
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            SusSafeArea.Provider = () => new Rect(10f, 15f, sw - 25f, sh - 50f);

            int raised = 0;
            SusSafeArea.Changed += () => raised++;

            var root = new VisualElement();
            SusSafeArea.Apply(root);

            Assert.AreEqual(1, raised);
            Assert.AreEqual(35f, SusSafeArea.Insets.Top, 0.01f);   // sh - (15 + sh - 50) = 35
            Assert.AreEqual(15f, SusSafeArea.Insets.Right, 0.01f);  // sw - (10 + sw - 25) = 15
            Assert.AreEqual(15f, SusSafeArea.Insets.Bottom, 0.01f);
            Assert.AreEqual(10f, SusSafeArea.Insets.Left, 0.01f);

            Assert.AreEqual(35f, root.style.paddingTop.value.value, 0.01f);
            Assert.AreEqual(15f, root.style.paddingRight.value.value, 0.01f);
            Assert.AreEqual(15f, root.style.paddingBottom.value.value, 0.01f);
            Assert.AreEqual(10f, root.style.paddingLeft.value.value, 0.01f);
        }

        [Test]
        public void Apply_FullScreenProvider_ZeroInsets()
        {
            // Desktop / editor: safe area == screen → no padding change for the player.
            SusSafeArea.Provider = () => new Rect(0f, 0f, Screen.width, Screen.height);
            var root = new VisualElement();
            SusSafeArea.Apply(root);

            Assert.AreEqual(0f, SusSafeArea.Insets.Top, 0.01f);
            Assert.AreEqual(0f, SusSafeArea.Insets.Right, 0.01f);
            Assert.AreEqual(0f, SusSafeArea.Insets.Bottom, 0.01f);
            Assert.AreEqual(0f, SusSafeArea.Insets.Left, 0.01f);
            Assert.AreEqual(0f, root.style.paddingTop.value.value, 0.01f);
        }

        [Test]
        public void SusApp_UseSafeArea_AppliesOnRun()
        {
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            SusSafeArea.Provider = () => new Rect(8f, 12f, sw - 18f, sh - 40f);

            var root = new VisualElement();
            SusApp.Create(root)
                .UseTokenCascade(false)
                .UseWorldSpace(false)
                .UseSafeArea()
                .Run();

            Assert.AreEqual(28f, root.style.paddingTop.value.value, 0.01f);
            Assert.AreEqual(10f, root.style.paddingRight.value.value, 0.01f);
            Assert.AreEqual(12f, root.style.paddingBottom.value.value, 0.01f);
            Assert.AreEqual(8f, root.style.paddingLeft.value.value, 0.01f);
        }

        [Test]
        public void SusApp_UseSafeAreaFalse_DoesNotPad()
        {
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            SusSafeArea.Provider = () => new Rect(50f, 50f, sw - 100f, sh - 100f);

            var root = new VisualElement();
            SusApp.Create(root)
                .UseTokenCascade(false)
                .UseWorldSpace(false)
                .UseSafeArea(false)
                .Run();

            // StyleKeyword.Null / unset when never written.
            Assert.IsTrue(
                root.style.paddingTop.keyword == StyleKeyword.Null
                || Mathf.Approximately(root.style.paddingTop.value.value, 0f));
        }

        [Test]
        public void Refresh_AfterProviderChange_UpdatesInsets()
        {
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            SusSafeArea.Provider = () => new Rect(0f, 0f, sw, sh);

            var root = new VisualElement();
            SusSafeArea.Apply(root);
            Assert.AreEqual(0f, SusSafeArea.Insets.Top, 0.01f);

            SusSafeArea.Provider = () => new Rect(0f, 0f, sw, sh - 44f);
            SusSafeArea.Refresh();

            Assert.AreEqual(44f, SusSafeArea.Insets.Top, 0.01f);
            Assert.AreEqual(44f, root.style.paddingTop.value.value, 0.01f);
        }
    }
}
