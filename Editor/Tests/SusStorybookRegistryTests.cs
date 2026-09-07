using System.Linq;
using NUnit.Framework;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Nav;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The registry half of card T-3033: discovery by reflection, grouping into package tabs,
    /// the provider path and the empty-package case (plan §4.1, mock-up "Storybook Shell").
    ///
    /// Every test builds the catalogue from ONE assembly (this one) rather than from the whole
    /// domain: what else the Editor happens to have loaded must not be able to change the answer.
    /// </summary>
    public class SusStorybookRegistryTests
    {
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
        public void Discovery_finds_every_attributed_story()
        {
            var ids = SusStoryRegistry.LastRegisteredStoryIds;

            Assert.That(ids, Contains.Item("core/primitives/counter"));
            Assert.That(ids, Contains.Item("core/primitives/swatch"));
            Assert.That(ids, Contains.Item("core/overlay/floating"));
        }

        [Test]
        public void Discovery_finds_provider_born_stories_too()
        {
            Assert.That(SusStoryRegistry.LastRegisteredStoryIds,
                Contains.Item("core/primitives/swatch-error"));

            var entry = SusStoryRegistry.Find("core/primitives/swatch-error");
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.DeclaringType, Is.EqualTo(typeof(CoreSwatchPresetProvider)));
        }

        [Test]
        public void Entry_carries_the_text_the_attribute_declared()
        {
            var entry = SusStoryRegistry.Find("core/primitives/counter");

            Assert.That(entry.Name, Is.EqualTo("Counter"));
            Assert.That(entry.Purpose, Does.Contain("reactive props"));
            Assert.That(entry.Package, Is.EqualTo("core"));
            Assert.That(entry.Group, Is.EqualTo("primitives"));
            Assert.That(entry.Slug, Is.EqualTo("counter"));
        }

        [Test]
        public void Find_is_case_insensitive_and_returns_null_for_an_unknown_id()
        {
            Assert.That(SusStoryRegistry.Find("CORE/PRIMITIVES/COUNTER"), Is.Not.Null);
            Assert.That(SusStoryRegistry.Find("kit/atoms/nope"), Is.Null);
        }

        [Test]
        public void Stories_group_into_packages_and_groups_in_display_order()
        {
            var core = SusStoryRegistry.FindPackage("core");
            Assert.That(core, Is.Not.Null);
            Assert.That(core.IsEmpty, Is.False);

            var groups = core.Groups.Select(g => g.Id).ToList();
            // "primitives" precedes "overlay" in the declared group order, not alphabetically —
            // the shell shows the building blocks before the things layered on top of them.
            Assert.That(groups, Is.EqualTo(new[] { "primitives", "overlay" }));

            var primitives = core.Groups.First(g => g.Id == "primitives");
            Assert.That(primitives.Stories.Select(s => s.Slug).ToList(),
                Is.EqualTo(new[] { "counter", "swatch", "swatch-error" }));
        }

        [Test]
        public void Package_stamp_comes_from_the_editor_package_manager_not_from_a_literal()
        {
            var core = SusStoryRegistry.FindPackage("core");

            Assert.That(core.PackageId, Is.EqualTo("com.sharq-it.sus.core"));
            // The test assembly ships inside sus-core, so the live PackageInfo lookup must have
            // answered. A literal in the attribute would go stale on the next release bump, so the
            // check is "a version was resolved", not "the version equals 1.0.29".
            Assert.That(core.Version, Is.Not.Empty, "package version was not resolved");
        }

        [Test]
        public void A_declared_package_with_no_stories_still_gets_a_tab()
        {
            // This is the empty-plate case of the mock-up: the package IS loaded, its story
            // provider is missing. Hiding the tab would turn "nobody wrote stories yet" into
            // "this package does not exist".
            SusStoryRegistry.DeclarePackage("router", "com.sharq-it.sus.router", "0.9.2");
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            var router = SusStoryRegistry.FindPackage("router");
            Assert.That(router, Is.Not.Null);
            Assert.That(router.IsEmpty, Is.True);
            Assert.That(router.StoryCount, Is.Zero);
            Assert.That(router.PackageId, Is.EqualTo("com.sharq-it.sus.router"));
            Assert.That(router.Version, Is.EqualTo("0.9.2"));
        }

        [Test]
        public void Package_tabs_follow_the_declared_order_not_the_alphabet()
        {
            SusStoryRegistry.DeclarePackage("skin");
            SusStoryRegistry.DeclarePackage("router");
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            var keys = SusStoryRegistry.Packages.Select(p => p.Key).ToList();
            Assert.That(keys, Is.EqualTo(new[] { "core", "router", "skin" }));
        }

        [Test]
        public void Assembly_without_the_opt_in_attribute_contributes_nothing()
        {
            // Core's own runtime assembly holds no stories and does not opt in; scanning it must
            // yield an empty catalogue rather than a full GetTypes() sweep with surprises in it.
            SusStoryRegistry.BuildFrom(new[] { typeof(SusComponent).Assembly });

            Assert.That(SusStoryRegistry.Stories, Is.Empty);
            Assert.That(SusStoryRegistry.Packages, Is.Empty);
        }

        [Test]
        public void PropCount_is_the_number_DescribeProps_reports()
        {
            var entry = SusStoryRegistry.Find("core/primitives/counter");
            var probe = entry.Create();

            Assert.That(entry.PropCount, Is.EqualTo(probe.DescribeProps().Count));
            Assert.That(entry.PropCount, Is.EqualTo(2), "Label and Count");
        }

        [Test]
        public void Instantiate_runs_Configure_and_honours_the_route_query()
        {
            var entry = SusStoryRegistry.Find("core/primitives/counter");
            var route = new SusStoryRoute(entry.Id,
                new System.Collections.Generic.Dictionary<string, string> { ["Label"] = "Taps" });

            var ctx = entry.Instantiate(route, out var component);

            Assert.That(((CoreCounterDemo)component).Label.Value, Is.EqualTo("Taps"));
            Assert.That(ctx.Exclusions.ContainsKey("OnCount"), Is.True);
            Assert.That(ctx.Exclusions["OnCount"], Is.Not.Empty, "an exclusion must carry a reason");
        }

        [Test]
        public void Create_returns_a_fresh_instance_every_time()
        {
            // The matrix of step 5 builds one instance per cell from the same factory; a story
            // that handed back a cached element would make every cell the same element.
            var entry = SusStoryRegistry.Find("core/primitives/swatch");
            Assert.That(entry.Create(), Is.Not.SameAs(entry.Create()));
        }
    }
}
