using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Invariant tests for the <see cref="SusApp"/> bootstrap facade (P1.6).
    /// Uses the <c>Create(VisualElement)</c> overload with a detached root and
    /// <c>UseTokenCascade(false)</c> to avoid Resources / PanelSettings dependencies.
    /// </summary>
    public class SusAppTests
    {
        [Test]
        public void Create_NullUIDocument_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => SusApp.Create((UnityEngine.UIElements.UIDocument)null));
        }

        [Test]
        public void Create_NullVisualElement_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => SusApp.Create((VisualElement)null));
        }

        [Test]
        public void FluentMethods_ReturnSameInstance()
        {
            var app = SusApp.Create(new VisualElement());

            Assert.AreSame(app, app.UseTheme(SusTheme.Light));
            Assert.AreSame(app, app.UseTokenCascade(false));
            Assert.AreSame(app, app.UseWorldSpace(false));
            Assert.AreSame(app, app.UseCustomStyles("SusRuntime/nope"));
            Assert.AreSame(app, app.Configure(_ => { }));
        }

        [Test]
        public void Configure_RunsAgainstRoot_OnRun()
        {
            var root = new VisualElement();
            VisualElement seen = null;

            SusApp.Create(root)
                  .UseTokenCascade(false)
                  .UseWorldSpace(false)
                  .Configure(r => seen = r)
                  .Run();

            Assert.AreSame(root, seen);
        }

        [Test]
        public void Run_Twice_Throws()
        {
            var app = SusApp.Create(new VisualElement()).UseTokenCascade(false).UseWorldSpace(false);
            app.Run();

            Assert.Throws<InvalidOperationException>(() => app.Run());
        }

        [Test]
        public void Run_ReturnsRoot()
        {
            var root = new VisualElement();
            var result = SusApp.Create(root).UseTokenCascade(false).UseWorldSpace(false).Run();
            Assert.AreSame(root, result);
        }
    }
}
