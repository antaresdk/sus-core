using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Zone D is derived from a MOUNTED instance (T-3096). Every control snapshots
    /// <c>SusPropInfo.Dead</c> when it is built, and a component registers the watchers of its
    /// content props in <c>Mounted()</c> — a frame after the constructor. Building the panel
    /// inside <c>Mount()</c> therefore judged an instance that had not started reading anything
    /// yet, and the footer accused a live SusButton of four dead props (kadr kit-button.png,
    /// T-3041). The host now waits for <see cref="SusComponent.MountCompleted"/>.
    ///
    /// An EditMode host is never attached to a panel, so its stories never reach Mounted() — which
    /// is precisely what makes the waiting visible here.
    /// </summary>
    public class SusZoneDMountTimingTests
    {
        const string Counter = "enginetests/primitives/counter";

        [SetUp]
        public void SetUp()
        {
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
        }

        [TearDown]
        public void TearDown()
        {
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        [Test]
        public void ZoneD_isNotBuiltFromAnInstanceThatHasNotMountedYet()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById(Counter);

            var story = host.QaSubjectRoot.Children().OfType<SusComponent>().FirstOrDefault();
            Assert.That(story, Is.Not.Null, "the story is on the canvas");
            Assert.That(story.IsMounted, Is.False, "a detached host never reaches Mounted()");
            Assert.That(host.Controls, Is.Null,
                "no panel is built from an unmounted component — its dead-prop snapshot would be a lie");
            Assert.That(
                host.ZoneControls.Children().OfType<Label>().Any(l => l.text.Contains("waiting")),
                Is.True,
                "and the zone says what it is waiting for instead of showing an empty panel");
        }
    }
}
