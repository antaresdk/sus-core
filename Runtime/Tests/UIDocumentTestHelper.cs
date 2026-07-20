using UnityEngine;
using UnityEngine.UIElements;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Base class for PlayMode tests that require a UIDocument + rootVisualElement.
    ///
    /// Usage:
    /// <code>
    /// public class MyPlaymodeTests : UIDocumentTestHelper
    /// {
    ///     [UnityTest]
    ///     public IEnumerator MyTest()
    ///     {
    ///         var label = new Label("hello");
    ///         Root.Add(label);
    ///         yield return WaitFrame();
    ///         Assert.AreEqual("hello", label.text);
    ///     }
    /// }
    /// </code>
    /// </summary>
    public class UIDocumentTestHelper
    {
        protected GameObject Go { get; private set; }
        protected UIDocument Doc { get; private set; }
        protected VisualElement Root { get; private set; }

        /// <summary>
        /// Override to provide a custom PanelSettings asset.
        /// If null, creates a default PanelSettings with ConstantPixelSize at 1920x1080.
        /// </summary>
        protected virtual PanelSettings GetPanelSettings()
        {
            // Try to load from Resources
            var settings = Resources.Load<PanelSettings>("SusTestPanelSettings");
            if (settings != null) return settings;

            // Fallback: create minimal PanelSettings at runtime
            return CreateTestPanelSettings();
        }

        /// <summary>
        /// Creates a minimal PanelSettings at runtime — ConstantPixelSize, 1920x1080.
        /// Call from non-subclassed test fixtures or when you need a standalone settings.
        /// </summary>
        public static PanelSettings CreateTestPanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "SusTestPanelSettings_Fallback";
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.match = 0.5f;

            // Avoid console error: "No Theme Style Sheet set to PanelSettings …"
            // Same default TSS as SusBootstrap (Resources/SusRuntime/SusDefault.tss).
            var tss = Resources.Load<ThemeStyleSheet>("SusRuntime/SusDefault");
            if (tss != null)
                settings.themeStyleSheet = tss;

            return settings;
        }

        [SetUp]
        public virtual void SetUp()
        {
            Go = new GameObject("TestUI", typeof(UIDocument));
            Doc = Go.GetComponent<UIDocument>();
            Doc.panelSettings = GetPanelSettings();

            Root = Doc.rootVisualElement;
            Assert.IsNotNull(Root, "rootVisualElement should not be null");
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (Go != null)
            {
                Object.DestroyImmediate(Go);
                Go = null;
            }
            Doc = null;
            Root = null;
        }

        /// <summary>
        /// Advance one frame (yield return null).
        /// </summary>
        protected IEnumerator WaitFrame()
        {
            yield return null;
        }

        /// <summary>
        /// Advance N frames.
        /// </summary>
        protected IEnumerator WaitFrames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        /// <summary>
        /// Simulate a mouse click at the center of the given VisualElement.
        /// </summary>
        protected void SimulateClick(VisualElement target)
        {
            using var evt = PointerDownEvent.GetPooled();
            evt.target = target;
            target.SendEvent(evt);

            using var evtUp = PointerUpEvent.GetPooled();
            evtUp.target = target;
            target.SendEvent(evtUp);
        }

        /// <summary>
        /// Simulate a mouse enter on the given VisualElement.
        /// </summary>
        protected void SimulatePointerEnter(VisualElement target)
        {
            using var evt = PointerEnterEvent.GetPooled();
            evt.target = target;
            target.SendEvent(evt);
        }

        /// <summary>
        /// Simulate a mouse leave on the given VisualElement.
        /// </summary>
        protected void SimulatePointerLeave(VisualElement target)
        {
            using var evt = PointerLeaveEvent.GetPooled();
            evt.target = target;
            target.SendEvent(evt);
        }

        /// <summary>
        /// Simulate keyboard input on the given VisualElement.
        /// </summary>
        protected void SimulateKeyDown(VisualElement target, KeyCode key,
            EventModifiers modifiers = EventModifiers.None)
        {
            using var evt = KeyDownEvent.GetPooled((char)0, key, modifiers);
            evt.target = target;
            target.SendEvent(evt);
        }
    }
}
