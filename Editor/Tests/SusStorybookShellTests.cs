using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Nav;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Zone A and the shell skeleton of card T-3033: the panel is built FROM the registry, the
    /// highlight is set FROM the route, the search filters, the empty package shows its plate, and
    /// zones B/D/E exist as addressable empty slots for steps 4, 5 and 6.
    /// </summary>
    public class SusStorybookShellTests
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

        static int RowCount(VisualElement root) =>
            root.Query<VisualElement>(className: "sus-sb-nav__row").ToList().Count;

        static VisualElement ActiveRow(VisualElement root) =>
            root.Query<VisualElement>(className: "sus-sb-nav__row--active").ToList().FirstOrDefault();

        [Test]
        public void Nav_lists_one_row_per_story_of_the_active_package()
        {
            var nav = new SusStoryNavPanel { ActivePackage = "core" };

            Assert.That(RowCount(nav), Is.EqualTo(SusStoryRegistry.FindPackage("core").StoryCount));
        }

        [Test]
        public void Nav_shows_a_tab_per_package_and_marks_the_active_one()
        {
            SusStoryRegistry.DeclarePackage("router", "com.sharq-it.sus.router", "0.9.2");
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            var nav = new SusStoryNavPanel { ActivePackage = "router" };
            var tabs = nav.Query<Button>(className: "sus-sb-nav__tab").ToList();

            Assert.That(tabs.Select(t => t.text).ToList(), Is.EqualTo(new[] { "core", "router" }));
            var active = tabs.Where(t => t.ClassListContains("sus-sb-nav__tab--active")).ToList();
            Assert.That(active.Count, Is.EqualTo(1));
            Assert.That(active[0].text, Is.EqualTo("router"));
        }

        [Test]
        public void Group_headers_are_upper_case()
        {
            var nav = new SusStoryNavPanel { ActivePackage = "core" };
            var headers = nav.Query<Label>(className: "sus-sb-nav__group").ToList().Select(l => l.text).ToList();

            Assert.That(headers, Is.EqualTo(new[] { "PRIMITIVES", "OVERLAY" }));
        }

        [Test]
        public void Row_shows_the_prop_count_next_to_the_name()
        {
            var nav = new SusStoryNavPanel { ActivePackage = "core" };
            var counts = nav.Query<Label>(className: "sus-sb-nav__row-count").ToList().Select(l => l.text).ToList();

            Assert.That(counts, Has.Count.EqualTo(RowCount(nav)));
            Assert.That(counts, Has.No.Member(""), "a row without a number would hide the coverage gap");
        }

        [Test]
        public void Highlight_follows_the_active_story_id()
        {
            var nav = new SusStoryNavPanel { ActiveStoryId = "core/overlay/floating" };

            var row = ActiveRow(nav);
            Assert.That(row, Is.Not.Null);
            Assert.That(row.Q<Label>(className: "sus-sb-nav__row-name").text, Is.EqualTo("Floating"));
            // Setting the story also switched the package tab: entering by deep link must not
            // leave the list showing a different package than the highlighted row.
            Assert.That(nav.ActivePackage, Is.EqualTo("core"));
        }

        [Test]
        public void Empty_package_shows_the_plate_with_the_full_id_version_and_attribute_hint()
        {
            SusStoryRegistry.DeclarePackage("router", "com.sharq-it.sus.router", "0.9.2");
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            var nav = new SusStoryNavPanel { ActivePackage = "router" };

            Assert.That(RowCount(nav), Is.Zero);
            var plate = nav.Q<VisualElement>(className: "sus-sb-nav__empty");
            Assert.That(plate, Is.Not.Null);
            Assert.That(plate.Q<Label>(className: "sus-sb-nav__empty-title").text,
                Does.Contain("com.sharq-it.sus.router"));
            Assert.That(plate.Q<Label>(className: "sus-sb-nav__empty-text").text, Does.Contain("0.9.2"));
            Assert.That(plate.Q<Label>(className: "sus-sb-nav__empty-code").text, Does.Contain("[SusStory("));
        }

        [Test]
        public void Footer_shows_the_anomaly_count_and_the_package_version()
        {
            var nav = new SusStoryNavPanel { ActivePackage = "core" };

            Assert.That(nav.Q<Label>(className: "sus-sb-nav__health-text").text, Is.EqualTo("0 anomalies"));
            Assert.That(nav.Q<VisualElement>(className: "sus-sb-nav__dot")
                .ClassListContains("sus-sb-nav__dot--bad"), Is.False);
            Assert.That(nav.Q<Label>(className: "sus-sb-nav__version").text, Is.Not.Empty);

            nav.AnomalyCount = 3;
            Assert.That(nav.Q<Label>(className: "sus-sb-nav__health-text").text, Is.EqualTo("3 anomalies"));
            Assert.That(nav.Q<VisualElement>(className: "sus-sb-nav__dot")
                .ClassListContains("sus-sb-nav__dot--bad"), Is.True);
        }

        [Test]
        public void Search_filters_rows_by_name()
        {
            var nav = new SusStoryNavPanel { ActivePackage = "core" };
            Assert.That(nav.Q<TextField>(className: "sus-sb-nav__search-input"), Is.Not.Null,
                "the search field the Ctrl+K shortcut focuses");

            nav.Filter = "swatch";

            var names = nav.Query<Label>(className: "sus-sb-nav__row-name").ToList().Select(l => l.text).ToList();
            Assert.That(names, Is.EqualTo(new[] { "Swatch", "Swatch (error)" }));

            nav.Filter = "zzz";
            Assert.That(RowCount(nav), Is.Zero);
            Assert.That(nav.Q<Label>(className: "sus-sb-nav__empty-text").text, Does.Contain("zzz"));

            nav.Filter = "";
            Assert.That(RowCount(nav), Is.EqualTo(SusStoryRegistry.FindPackage("core").StoryCount));
        }

        [Test]
        public void Selecting_a_row_reports_the_entry_but_does_not_set_the_highlight_itself()
        {
            var nav = new SusStoryNavPanel { ActivePackage = "core" };
            SusStoryEntry picked = null;
            nav.StorySelected += e => picked = e;

            // Select() is exactly what the row button's click action calls. A real ClickEvent is
            // not an option here: outside a live panel UI Toolkit has no dispatcher and silently
            // drops the event, which would make this test pass for the wrong reason.
            var row = nav.Query<Button>(className: "sus-sb-nav__row").ToList()
                .First(b => b.Q<Label>(className: "sus-sb-nav__row-name").text == "Counter");
            Assert.That(row, Is.Not.Null);
            nav.Select(SusStoryRegistry.Find("core/primitives/counter"));

            Assert.That(picked, Is.Not.Null);
            Assert.That(picked.Id, Is.EqualTo("core/primitives/counter"));
            // The click reports; the HOST turns it into a route and the route sets the highlight
            // (plan §4.6). If the panel highlighted itself, a deep link would fight the click.
            Assert.That(ActiveRow(nav), Is.Null);
        }

        // ── shell skeleton ───────────────────────────────────────────────────

        [Test]
        public void Host_exposes_zones_A_to_E_as_addressable_slots()
        {
            using var host = new SusStorybookHost();

            Assert.That(host.Q<VisualElement>(className: "sus-sb-nav"), Is.Not.Null, "zone A");
            Assert.That(host.Q<VisualElement>("sus-storybook-zone-b"), Is.Not.Null);
            Assert.That(host.Q<VisualElement>("sus-storybook-zone-c"), Is.Not.Null);
            Assert.That(host.Q<VisualElement>("sus-storybook-zone-d"), Is.Not.Null);
            Assert.That(host.Q<VisualElement>("sus-storybook-zone-e"), Is.Not.Null);
            Assert.That(host.QaCanvas, Is.Not.Null);
        }

        [Test]
        public void Host_mounts_the_first_story_and_lists_every_id()
        {
            using var host = new SusStorybookHost();

            Assert.That(host.CurrentStory, Is.Not.Null);
            Assert.That(host.LastRegisteredStoryIds, Is.EqualTo(SusStoryRegistry.LastRegisteredStoryIds));
            Assert.That(host.QaCanvas.childCount, Is.GreaterThan(0), "the story is on the stage");
        }

        [Test]
        public void ShowStoryById_moves_the_stage_the_history_and_the_highlight_together()
        {
            using var host = new SusStorybookHost();

            Assert.That(host.ShowStoryById("core/overlay/floating"), Is.True);

            Assert.That(host.CurrentStory.Id, Is.EqualTo("core/overlay/floating"));
            Assert.That(host.History.Current.StoryId, Is.EqualTo("core/overlay/floating"));
            Assert.That(host.Nav.ActiveStoryId, Is.EqualTo("core/overlay/floating"));
            Assert.That(host.Url.Address, Is.EqualTo("#/core/overlay/floating"));
        }

        [Test]
        public void An_unknown_id_is_refused_and_said_out_loud()
        {
            using var host = new SusStorybookHost();
            var before = host.CurrentStory;

            Assert.That(host.ShowStoryById("kit/atoms/nope"), Is.False);
            Assert.That(host.CurrentStory, Is.SameAs(before), "the stage is not blanked");
        }

        [Test]
        public void Back_returns_to_the_previous_story_and_remounts_it()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById("core/primitives/swatch");
            host.ShowStoryById("core/overlay/floating");

            Assert.That(host.Back(), Is.True);

            Assert.That(host.CurrentStory.Id, Is.EqualTo("core/primitives/swatch"));
            Assert.That(host.Nav.ActiveStoryId, Is.EqualTo("core/primitives/swatch"));
            Assert.That(host.QaCanvas.childCount, Is.GreaterThan(0));
        }

        [Test]
        public void An_address_bar_change_switches_the_story_without_a_reload_and_is_not_echoed()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById("core/primitives/counter");
            int entriesBefore = host.History.Entries.Count;

            host.Url.HandleExternal("#/core/overlay/floating");

            Assert.That(host.CurrentStory.Id, Is.EqualTo("core/overlay/floating"));
            Assert.That(host.Nav.ActiveStoryId, Is.EqualTo("core/overlay/floating"),
                "the highlight follows the address, not the click (T-2677 p. 2)");
            Assert.That(host.History.Entries.Count, Is.EqualTo(entriesBefore + 1));
            Assert.That(host.Url.Address, Is.EqualTo("#/core/overlay/floating"));
        }

        [Test]
        public void The_deep_link_of_the_shown_route_is_the_text_the_share_button_copies()
        {
            using var host = new SusStorybookHost();
            host.Navigate(new SusStoryRoute("core/primitives/counter",
                new System.Collections.Generic.Dictionary<string, string> { ["Label"] = "Taps" }));

            Assert.That(host.Url.Address, Is.EqualTo("#/core/primitives/counter?Label=Taps"));
            Assert.That(host.Q<Label>(className: "sus-sb__link").text,
                Is.EqualTo("#/core/primitives/counter?Label=Taps"));
        }
    }
}
