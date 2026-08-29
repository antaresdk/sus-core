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
    /// <remarks>
    /// <para>
    /// <b>Breakpoint self-tagging (T-2468):</b> this harness mounts <typeparamref name="T"/>
    /// directly into <see cref="Root"/> without running the <see cref="SusBootstrap"/> token
    /// cascade (no <c>SusApp</c>/<c>SusBootstrap.Mount</c> call — <see cref="SusBootstrap.TokenCascadeRoot"/>
    /// stays null). Without a cascade root, <see cref="SusBreakpointService.For(SusComponent)"/>
    /// falls back through <c>SusThemeService.ResolveCascadeRoot</c> to the component itself, so
    /// EACH mounted component tags its OWN <see cref="SusBreakpointService"/> by its OWN resolved
    /// width instead of sharing one root-driven breakpoint — unlike production, where every
    /// component under one <c>SusApp</c> shares the same cascade-root width. This is a known,
    /// class-level gap of the isolated-mount harness, not a per-test bug — decided NOT to fix by
    /// bootstrapping a full cascade in <c>SetUp</c>, because that would change breakpoint tagging
    /// for all ~166 existing <c>SusComponentTest&lt;T&gt;</c> fixtures across kit/game at once
    /// (every mounted component would jump from self-width tagging to the fixed 1920px test-panel
    /// width), an unbounded regression surface for a harness change. Tests whose assertions depend
    /// on a specific breakpoint (pixel/class checks) should call <see cref="ForceBreakpoint"/>
    /// instead of reaching for <c>SusBreakpointService.For(x).SetOverride(...)</c> directly.
    /// </para>
    /// </remarks>
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

        /// <summary>
        /// Pins <paramref name="component"/>'s <see cref="SusBreakpointService"/> to a fixed
        /// <see cref="Breakpoint"/>, bypassing the harness's per-component self-width tagging
        /// (see the class remarks). Use this — not a raw
        /// <c>SusBreakpointService.For(x).SetOverride(...)</c> call — whenever a test asserts
        /// breakpoint-dependent pixels/classes on a component mounted in isolation, so the
        /// workaround is one documented API instead of copy-pasted per test. Call once per
        /// component that reads its own breakpoint (e.g. each card in a rail), since every
        /// <see cref="SusComponent"/> here can resolve its own service. Takes the
        /// <see cref="SusComponent"/> overload of <see cref="SusBreakpointService.For(SusComponent)"/>
        /// deliberately — it walks the parent chain / cascade fallback the same way
        /// production code (<c>SusComponent.BreakpointService</c>) does, unlike the
        /// <see cref="VisualElement"/> overload. Pass <c>null</c> to resume auto-width.
        /// </summary>
        protected static void ForceBreakpoint(SusComponent component, Breakpoint? breakpoint)
            => SusBreakpointService.For(component).SetOverride(breakpoint);

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
