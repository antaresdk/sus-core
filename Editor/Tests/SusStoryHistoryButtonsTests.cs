using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Nav;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The two arrows of zone A — card T-3406, contract of zone A (R142 <c>history-buttons</c>,
    /// plan ARCH-20260911-STORYBOOK-SHELL §4.1; engine plan §4.6: "back/forward buttons live in
    /// zone A, plus Alt+←/Alt+→").
    ///
    /// What was measured on 2026-09-11: <see cref="SusStoryHistory"/> had a cursor, the host had
    /// <c>Back()</c>/<c>Forward()</c> and <c>Alt+←</c>/<c>Alt+→</c> moved it — and zone A had no
    /// button at all, so a buyer who had walked three stories could rewind only if he knew the
    /// shortcut. These tests assert the three things that made it invisible: the buttons exist,
    /// they move the CURRENT STORY (not just a cursor in a list), and at the ends they are dimmed
    /// by the same fact that refuses the click.
    ///
    /// No play mode: the history layer knows nothing about platforms, and a click action is
    /// exercised through the public method the action calls (<see cref="SusStoryNavPanel.GoBack"/>)
    /// because outside a live panel UI Toolkit has no dispatcher and drops a synthesised
    /// ClickEvent silently — which would make the test pass for the wrong reason.
    /// </summary>
    public class SusStoryHistoryButtonsTests
    {
        const string First = "enginetests/primitives/counter";
        const string Second = "enginetests/primitives/swatch";
        const string Third = "enginetests/overlay/floating";

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

        // ── the buttons exist and are where the contract says ────────────────

        [Test]
        public void Zone_A_offers_both_arrows_in_a_history_row()
        {
            var nav = new SusStoryNavPanel();

            var row = nav.Q<VisualElement>(className: "sb-nav__history");
            Assert.That(row, Is.Not.Null, "the contract of zone A names a history row");

            var back = row.Q<Button>(className: "sb-nav__back");
            var forward = row.Q<Button>(className: "sb-nav__forward");
            Assert.That(back, Is.Not.Null, "no back button: the path walked cannot be rewound");
            Assert.That(forward, Is.Not.Null);
            Assert.That(back.text, Is.EqualTo(SusStoryNavPanel.BackGlyph));
            Assert.That(forward.text, Is.EqualTo(SusStoryNavPanel.ForwardGlyph));
            Assert.That(back, Is.SameAs(nav.BackButton));
            Assert.That(forward, Is.SameAs(nav.ForwardButton));
        }

        [Test]
        public void Both_arrows_name_the_keyboard_equivalent_so_the_shortcut_is_discoverable()
        {
            var nav = new SusStoryNavPanel();

            Assert.That(nav.BackButton.tooltip, Does.Contain("Alt"));
            Assert.That(nav.ForwardButton.tooltip, Does.Contain("Alt"));
        }

        // ── the edges ────────────────────────────────────────────────────────

        [Test]
        public void With_no_history_bound_both_arrows_are_dimmed_and_a_click_does_nothing()
        {
            var nav = new SusStoryNavPanel();

            Assert.That(nav.CanGoBack, Is.False);
            Assert.That(nav.CanGoForward, Is.False);
            Assert.That(nav.BackButton.enabledSelf, Is.False, "declared disabled, not merely inert");
            Assert.That(nav.ForwardButton.enabledSelf, Is.False);
            Assert.That(nav.GoBack(), Is.False);
            Assert.That(nav.GoForward(), Is.False);
        }

        [Test]
        public void One_route_is_an_edge_on_both_sides()
        {
            var history = new SusStoryHistory();
            history.Go(new SusStoryRoute(First));

            var nav = new SusStoryNavPanel { History = history };

            Assert.That(nav.BackButton.enabledSelf, Is.False, "nowhere to go back from the first story");
            Assert.That(nav.ForwardButton.enabledSelf, Is.False);
        }

        [Test]
        public void The_dimming_and_the_refusal_come_from_the_same_fact()
        {
            var history = new SusStoryHistory();
            history.Go(new SusStoryRoute(First));
            history.Go(new SusStoryRoute(Second));

            var nav = new SusStoryNavPanel { History = history };

            // Back is offered and works; forward is dimmed and refuses - and then they swap.
            Assert.That(nav.BackButton.enabledSelf, Is.True);
            Assert.That(nav.ForwardButton.enabledSelf, Is.False);
            Assert.That(nav.GoForward(), Is.False, "a dimmed arrow refuses the click it announces");

            Assert.That(nav.GoBack(), Is.True);
            Assert.That(nav.BackButton.enabledSelf, Is.False, "back to the first route: the edge again");
            Assert.That(nav.ForwardButton.enabledSelf, Is.True, "the forward route survived");
            Assert.That(nav.GoBack(), Is.False);
        }

        [Test]
        public void A_new_route_kills_the_forward_edge_and_the_arrow_says_so()
        {
            var history = new SusStoryHistory();
            history.Go(new SusStoryRoute(First));
            history.Go(new SusStoryRoute(Second));
            var nav = new SusStoryNavPanel { History = history };

            nav.GoBack();
            Assert.That(nav.ForwardButton.enabledSelf, Is.True);

            // Browser semantics (SusStoryHistory.Go truncates the tail): the forward route is gone,
            // so the arrow must go dark WITHOUT anybody calling Rebuild - the panel listens.
            history.Go(new SusStoryRoute(Third));

            Assert.That(nav.ForwardButton.enabledSelf, Is.False);
            Assert.That(nav.BackButton.enabledSelf, Is.True);
        }

        // ── the arrows move the STORY, not a cursor in a list ────────────────

        [Test]
        public void The_shell_binds_the_arrows_to_its_own_history_without_being_asked()
        {
            using var host = new SusStorybookHost();

            Assert.That(host.Nav.History, Is.SameAs(host.History),
                "an arrow wired only when the shell remembers is a promise without a deed (R124)");
        }

        [Test]
        public void Back_shows_the_previous_story_and_forward_returns_to_the_later_one()
        {
            using var host = new SusStorybookHost();

            Assert.That(host.ShowStoryById(First), Is.True);
            Assert.That(host.ShowStoryById(Second), Is.True);
            Assert.That(host.CurrentStory.Id, Is.EqualTo(Second));

            Assert.That(host.Nav.GoBack(), Is.True);
            Assert.That(host.CurrentStory.Id, Is.EqualTo(First), "the button moves the mounted story");
            Assert.That(host.Nav.ForwardButton.enabledSelf, Is.True);

            Assert.That(host.Nav.GoForward(), Is.True);
            Assert.That(host.CurrentStory.Id, Is.EqualTo(Second));
        }

        [Test]
        public void The_highlight_of_zone_A_follows_the_arrow_the_way_it_follows_a_click()
        {
            using var host = new SusStorybookHost();

            host.ShowStoryById(First);
            host.ShowStoryById(Second);
            host.Nav.GoBack();

            // §4.6: the highlight is set FROM the route. A button that moved the stage and left the
            // list pointing at the story before it would be the T-2677 defect with a new carrier.
            Assert.That(host.Nav.ActiveStoryId, Is.EqualTo(First));
            var active = host.Query<VisualElement>(className: "sb-nav__row--active").ToList();
            Assert.That(active.Count, Is.EqualTo(1));
        }

        [Test]
        public void The_keyboard_and_the_buttons_are_one_path_of_code()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById(First);
            host.ShowStoryById(Second);

            // Host.Back() is what Alt+Left calls; the button calls Nav.GoBack(). Both must move the
            // same cursor, or the two ways of going back would drift apart.
            Assert.That(host.Back(), Is.True);
            Assert.That(host.CurrentStory.Id, Is.EqualTo(First));
            Assert.That(host.Nav.ForwardButton.enabledSelf, Is.True,
                "the arrows re-read the edges after a move made from the keyboard");

            Assert.That(host.Nav.GoForward(), Is.True);
            Assert.That(host.CurrentStory.Id, Is.EqualTo(Second));
        }
    }
}
