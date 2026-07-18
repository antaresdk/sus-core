using UnityEngine.UIElements;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Generic PlayMode test base for SusComponent subclasses.
    /// Provides typed Mount/Unmount with automatic Root lifecycle.
    ///
    /// Usage:
    /// <code>
    /// public class SusButtonTests : SusComponentTest&lt;SusButton&gt;
    /// {
    ///     [UnityTest]
    ///     public IEnumerator Click_Fires_OnClick()
    ///     {
    ///         bool fired = false;
    ///         Mount();
    ///         Comp.OnClick += () => fired = true;
    ///         SimulateClick(Comp);
    ///         yield return WaitFrame();
    ///         Assert.IsTrue(fired);
    ///     }
    /// }
    /// </code>
    /// </summary>
    /// <typeparam name="T">SusComponent subclass to test</typeparam>
    public class SusComponentTest<T> : UIDocumentTestHelper
        where T : SusComponent, new()
    {
        /// <summary>Currently mounted component instance.</summary>
        protected T Comp { get; private set; }

        /// <summary>
        /// Creates a new instance of T, adds it to Root, and stores it in Comp.
        /// </summary>
        protected T Mount()
        {
            Comp = new T();
            Root.Add(Comp);
            return Comp;
        }

        /// <summary>
        /// Mounts a pre-created component instead of calling new T().
        /// Useful when constructor arguments or property setup is needed.
        /// </summary>
        protected T Mount(T component)
        {
            Comp = component;
            Comp.RemoveFromHierarchy();
            Root.Add(Comp);
            return Comp;
        }

        /// <summary>
        /// Removes the component from Root and nulls Comp.
        /// Safe to call without a prior Mount().
        /// </summary>
        protected void Unmount()
        {
            if (Comp != null)
            {
                Comp.RemoveFromHierarchy();
                Comp = null;
            }
        }

        /// <summary>
        /// Access the Root VisualElement directly (inherited from UIDocumentTestHelper).
        /// After Mount(), Comp is a child of Root.
        /// </summary>
        protected new VisualElement Root => base.Root;

        /// <summary>
        /// Advance one frame (inherited from UIDocumentTestHelper).
        /// Call after prop changes to let bindings update.
        /// </summary>
        protected new IEnumerator WaitFrame() => base.WaitFrame();

        /// <summary>
        /// Advance N frames (inherited from UIDocumentTestHelper).
        /// </summary>
        protected new IEnumerator WaitFrames(int count) => base.WaitFrames(count);

        [TearDown]
        public override void TearDown()
        {
            Unmount();
            base.TearDown();
        }
    }

    /// <summary>
    /// Static Mount helper for tests that don't want inheritance.
    ///
    /// Usage:
    /// <code>
    /// var comp = SusTestMount.Mount(button);
    /// yield return SusTestMount.WaitFrame();
    /// SusTestMount.Unmount(comp);
    /// </code>
    /// </summary>
    public static class SusTestMount
    {
        /// <summary>
        /// Adds a component to the given root (or the TestSceneHelper.Root).
        /// </summary>
        public static T Mount<T>(T component, VisualElement root = null)
            where T : SusComponent
        {
            component.RemoveFromHierarchy();
            (root ?? TestSceneHelper.Root).Add(component);
            return component;
        }

        /// <summary>Removes a component from its parent.</summary>
        public static void Unmount(SusComponent component)
        {
            if (component != null)
                component.RemoveFromHierarchy();
        }

        /// <summary>Advance one frame (yield return null).</summary>
        public static IEnumerator WaitFrame()
        {
            yield return null;
        }
    }
}
