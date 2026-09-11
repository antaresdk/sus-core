using NUnit.Framework;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The EditMode suite of the engine drives the engine's own BENCHES, so for the length of a
    /// run it declares them visible (card T-3409/T-3410, plan §4.1b, D22).
    ///
    /// Why this file exists instead of a line in ninety-odd tests. After T-3409 the default
    /// selection of <see cref="SusStoryRegistry"/> is PRODUCT — no tab, no tree row, no search hit
    /// for a fixture package — and this suite asserts things like "the nav lists one row per story
    /// of the active package" about a package that is now a bench. Those assertions are still the
    /// right assertions: they are about the SHELL, and the bench is the only corpus the engine
    /// carries. Flipping the switch once for the namespace keeps them about the shell instead of
    /// turning every one of them into a test about visibility.
    ///
    /// Visibility ITSELF is tested with the switch off, and only there:
    /// <see cref="SusStorybookFixtureVisibilityTests"/>.
    ///
    /// <see cref="OneTimeTearDown"/> puts back whatever resolver the editor entry point installed,
    /// because this flag is a static of a Runtime class — a suite that left it on would hand the
    /// next assembly in the run a storybook full of broken toys.
    /// </summary>
    [SetUpFixture]
    public class SusStorybookFixtureScope
    {
        System.Func<string, bool> _saved;
        bool _savedByAddress;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _saved = SusStoryRegistry.FixtureVisibility;
            _savedByAddress = SusStoryRegistry.FixturesRequestedByAddress;
            SusStoryRegistry.FixtureVisibility = _ => true;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            SusStoryRegistry.FixtureVisibility = _saved;
            SusStoryRegistry.FixturesRequestedByAddress = _savedByAddress;
            SusStoryRegistry.Invalidate();
        }
    }
}
