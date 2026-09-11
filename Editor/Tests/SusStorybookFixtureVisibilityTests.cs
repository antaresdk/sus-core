using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Nav;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// "The engine's benches are separated from the product" — the three claims of cards T-3409,
    /// T-3410 and T-3411 (plan ARCH-20260907-STORYBOOK-ENGINE §4.1b, decisions D21–D25).
    ///
    /// The finding this answers is the owner's: the storybook showed a <c>core</c> tab holding
    /// fourteen engine fixtures, four of them broken on purpose, while core ships no components at
    /// all. The tab existed because a TEST assembly had named itself <c>core</c>, and nothing in
    /// the engine could tell a bench from a product.
    ///
    /// Everything here runs with the switch OFF — <see cref="SusStorybookFixtureScope"/> turns it
    /// on for the rest of the suite, so this is the one class that has to put it back and measure
    /// the default. Off is the default a buyer gets.
    /// </summary>
    public class SusStorybookFixtureVisibilityTests
    {
        Func<string, bool> _saved;
        bool _savedByAddress;

        [SetUp]
        public void SetUp()
        {
            _saved = SusStoryRegistry.FixtureVisibility;
            _savedByAddress = SusStoryRegistry.FixturesRequestedByAddress;
            SusStoryRegistry.FixturesRequestedByAddress = false;
            SusStoryRegistry.FixtureVisibility = null;       // the shipping default: benches hidden
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
        }

        [TearDown]
        public void TearDown()
        {
            SusStoryRegistry.FixturesRequestedByAddress = _savedByAddress;
            SusStoryRegistry.FixtureVisibility = _saved;
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        // ── T-3409: the mark declares the kind, and the selections split by it ──

        [Test]
        public void The_bench_declares_itself_a_fixture_and_says_so_on_every_record()
        {
            var pkg = SusStoryRegistry.FindPackage("enginetests");

            Assert.That(pkg, Is.Not.Null, "the bench package is still found by key");
            Assert.That(pkg.Kind, Is.EqualTo(SusStoryPackageKind.Fixture));
            Assert.That(pkg.IsFixture, Is.True);

            var story = SusStoryRegistry.Find("enginetests/primitives/counter");
            Assert.That(story.Kind, Is.EqualTo(SusStoryPackageKind.Fixture),
                "the kind of the assembly mark reaches the story record, not just the package");
        }

        [Test]
        public void A_mark_that_says_nothing_is_a_product_mark()
        {
            // The default is the whole reason 204 product stories needed no edit: only a bench
            // has to declare itself.
            Assert.That(new SusStoryAssemblyAttribute().Kind,
                Is.EqualTo(SusStoryPackageKind.Product));

            SusStoryRegistry.DeclarePackage("router", "com.sharq-it.sus.router", "0.9.2");
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
            Assert.That(SusStoryRegistry.FindPackage("router").Kind,
                Is.EqualTo(SusStoryPackageKind.Product));
        }

        [Test]
        public void The_product_selection_holds_no_fixture_and_the_full_one_holds_them_all()
        {
            Assert.That(SusStoryRegistry.LastRegisteredStoryIds, Is.Empty,
                "the bench is the only corpus in this assembly, so the product selection is empty");
            Assert.That(SusStoryRegistry.Stories, Is.Empty);
            Assert.That(SusStoryRegistry.Packages, Is.Empty,
                "a hidden bench leaves no tab and no empty plate either (D25)");

            Assert.That(SusStoryRegistry.AllIds, Is.Not.Empty);
            Assert.That(SusStoryRegistry.AllIds, Contains.Item("enginetests/primitives/counter"));
            Assert.That(SusStoryRegistry.AllPackages.Select(p => p.Key).ToList(),
                Contains.Item("enginetests"));

            foreach (var id in SusStoryRegistry.LastRegisteredStoryIds)
                Assert.That(SusStoryRegistry.Find(id).Kind, Is.EqualTo(SusStoryPackageKind.Product),
                    id + " is a fixture and reached the product selection");
        }

        [Test]
        public void A_product_package_next_to_the_bench_still_gets_its_tab()
        {
            // The filter must cut by KIND and not by "this scan found something odd": a declared
            // product package with zero stories keeps the empty plate it has had since T-3033.
            SusStoryRegistry.DeclarePackage("router", "com.sharq-it.sus.router", "0.9.2");
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            Assert.That(SusStoryRegistry.Packages.Select(p => p.Key).ToList(),
                Is.EqualTo(new[] { "router" }));
        }

        // ── T-3410: the shell does not LIST a bench, and always OPENS one ───────

        [Test]
        public void Zone_A_shows_no_tab_no_row_and_no_search_hit_for_a_bench()
        {
            var nav = new SusStoryNavPanel();

            Assert.That(nav.Query<Button>(className: "sb-nav__tab").ToList(), Is.Empty,
                "a hidden bench must not draw a tab");

            nav.ActivePackage = "enginetests";
            Assert.That(nav.Query<VisualElement>(className: "sb-nav__row").ToList(), Is.Empty,
                "a hidden bench must not draw tree rows even when it is the active package");
            Assert.That(nav.Query<Label>(className: "sb-nav__group").ToList(), Is.Empty);

            nav.Filter = "counter";
            Assert.That(nav.Query<VisualElement>(className: "sb-nav__row").ToList(), Is.Empty,
                "search must not be the back door into the bench");
        }

        [Test]
        public void A_direct_address_opens_a_bench_story_with_the_switch_off()
        {
            // This is the half that keeps ~96 citations of the EditMode suite alive: the address
            // is the contract, the listing is a preference.
            using var host = new SusStorybookHost();

            Assert.That(host.ShowStoryById("enginetests/showcase/bogus"), Is.True);
            Assert.That(host.CurrentStory?.Id, Is.EqualTo("enginetests/showcase/bogus"));
        }

        [Test]
        public void A_deep_link_opens_a_bench_story_with_the_switch_off()
        {
            using var host = new SusStorybookHost();
            host.Url.HandleExternal("#/enginetests/primitives/counter");

            Assert.That(host.CurrentStory?.Id, Is.EqualTo("enginetests/primitives/counter"));
        }

        [Test]
        public void The_address_can_ask_for_the_benches_and_then_they_are_listed()
        {
            using var host = new SusStorybookHost();
            Assert.That(SusStoryRegistry.Packages, Is.Empty, "hidden before the address asked");

            host.Url.HandleExternal("#/enginetests/primitives/counter?fixtures=1");

            Assert.That(SusStoryRegistry.Packages.Select(p => p.Key).ToList(),
                Contains.Item("enginetests"));
            Assert.That(SusStoryRegistry.LastRegisteredStoryIds,
                Contains.Item("enginetests/primitives/counter"));
        }

        [Test]
        public void The_injected_switch_lists_the_benches_too()
        {
            SusStoryRegistry.FixtureVisibility = _ => true;

            Assert.That(SusStoryRegistry.Packages.Select(p => p.Key).ToList(),
                Contains.Item("enginetests"));
            Assert.That(SusStoryRegistry.LastRegisteredStoryIds.Count,
                Is.EqualTo(SusStoryRegistry.AllIds.Count),
                "with the switch on the two selections are the same list");
        }

        // ── T-3411: the sweep record carries the kind ───────────────────────────

        [Test]
        public void The_session_record_of_a_bench_story_names_its_kind()
        {
            // The dump is written by the QA sink in sus-dev, but the FACT it prints is built here:
            // zone E stamps the report with the kind of the story it just finished, so the writer
            // has nothing to guess and nothing to look up by prefix.
            using var host = new SusStorybookHost();
            Assert.That(host.ShowStoryById("enginetests/primitives/counter"), Is.True);

            var report = host.Probe.BuildReport();
            Assert.That(report.StoryId, Is.EqualTo("enginetests/primitives/counter"));
            Assert.That(report.Kind, Is.EqualTo(SusStoryPackageKind.Fixture));
        }

        [Test]
        public void A_sweep_that_walks_the_default_selection_meets_no_fixture()
        {
            // The live leak was exactly this shape: a sweep that walked "everything loaded" wrote
            // core/primitives/counter into storybook-session.json, and a product rule judged it.
            foreach (var story in SusStoryRegistry.Stories)
                Assert.That(story.IsFixture, Is.False, story.Id);
        }
    }
}
