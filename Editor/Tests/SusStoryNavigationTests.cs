using System.Collections.Generic;
using NUnit.Framework;
using Sharq.Core.Storybook.Nav;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The navigation half of card T-3033 (plan §4.6, DoD §7 p. 13): the deep link parses and
    /// serialises in both directions, the history behaves like the browser's two arrows, and a
    /// route that came from the address bar is never pushed back into it.
    ///
    /// Not one test needs play mode or a browser: that is the whole point of keeping the layer
    /// free of platform knowledge and confining the bridge to <see cref="SusStoryUrl"/>.
    /// </summary>
    public class SusStoryNavigationTests
    {
        // ── route: text in, text out ─────────────────────────────────────────

        [Test]
        public void Hash_of_a_bare_story_is_the_canonical_path()
        {
            Assert.That(new SusStoryRoute("kit/atoms/select").ToHash(), Is.EqualTo("#/kit/atoms/select"));
        }

        [Test]
        public void Query_survives_a_round_trip()
        {
            var route = new SusStoryRoute("kit/atoms/select",
                new Dictionary<string, string> { ["Size"] = "lg", ["Label"] = "Country" });

            Assert.That(SusStoryRoute.TryParse(route.ToHash(), out var back), Is.True);
            Assert.That(back.StoryId, Is.EqualTo("kit/atoms/select"));
            Assert.That(back.Query["Size"], Is.EqualTo("lg"));
            Assert.That(back.Query["Label"], Is.EqualTo("Country"));
            Assert.That(back, Is.EqualTo(route));
        }

        [Test]
        public void Query_keys_are_ordered_so_the_same_state_yields_the_same_link()
        {
            var a = new SusStoryRoute("kit/atoms/select",
                new Dictionary<string, string> { ["Size"] = "lg", ["Color"] = "error" });
            var b = new SusStoryRoute("kit/atoms/select",
                new Dictionary<string, string> { ["Color"] = "error", ["Size"] = "lg" });

            Assert.That(a.ToHash(), Is.EqualTo(b.ToHash()));
            Assert.That(a.ToHash(), Is.EqualTo("#/kit/atoms/select?Color=error&Size=lg"));
        }

        [Test]
        public void Values_with_separators_and_spaces_survive_escaping()
        {
            var route = new SusStoryRoute("kit/atoms/textfield",
                new Dictionary<string, string> { ["Hint"] = "a=b&c d" });

            Assert.That(SusStoryRoute.TryParse(route.ToHash(), out var back), Is.True);
            Assert.That(back.Query["Hint"], Is.EqualTo("a=b&c d"));
        }

        [Test]
        public void FromValues_writes_only_what_differs_from_the_defaults()
        {
            var defaults = new Dictionary<string, string> { ["Size"] = "md", ["Color"] = "primary" };
            var values = new Dictionary<string, string> { ["Size"] = "lg", ["Color"] = "primary" };

            var route = SusStoryRoute.FromValues("kit/atoms/select", values, defaults);

            Assert.That(route.Query.Count, Is.EqualTo(1));
            Assert.That(route.ToHash(), Is.EqualTo("#/kit/atoms/select?Size=lg"));
        }

        [Test]
        public void A_full_url_parses_and_the_legacy_hash_still_resolves()
        {
            Assert.That(SusStoryRoute.TryParse(
                "https://sus.example/storybook/index.html#/game/slots/hold?Progress=62", out var modern), Is.True);
            Assert.That(modern.StoryId, Is.EqualTo("game/slots/hold"));
            Assert.That(modern.Query["Progress"], Is.EqualTo("62"));

            // Pre-engine form written by today's shell: links already pasted into docs, cards and
            // frames must keep resolving after the migration (plan D11).
            Assert.That(SusStoryRoute.TryParse("#story=kit/atoms/select&Size=lg", out var legacy), Is.True);
            Assert.That(legacy.StoryId, Is.EqualTo("kit/atoms/select"));
            Assert.That(legacy.Query["Size"], Is.EqualTo("lg"));
            Assert.That(legacy.Query.ContainsKey("story"), Is.False);
        }

        [Test]
        public void Garbage_does_not_parse_into_an_empty_route()
        {
            Assert.That(SusStoryRoute.TryParse(null, out _), Is.False);
            Assert.That(SusStoryRoute.TryParse("", out _), Is.False);
            Assert.That(SusStoryRoute.TryParse("#", out _), Is.False);
            Assert.That(SusStoryRoute.TryParse("https://sus.example/index.html", out _), Is.False);
        }

        [Test]
        public void A_parsed_route_is_marked_as_coming_from_the_address_bar()
        {
            Assert.That(SusStoryRoute.Parse("#/kit/atoms/select").FromUrl, Is.True);
            Assert.That(new SusStoryRoute("kit/atoms/select").FromUrl, Is.False);
        }

        [Test]
        public void Equality_ignores_where_the_route_came_from()
        {
            var clicked = new SusStoryRoute("kit/atoms/select");
            var typed = SusStoryRoute.Parse("#/kit/atoms/select");

            Assert.That(typed, Is.EqualTo(clicked));
            Assert.That(typed.GetHashCode(), Is.EqualTo(clicked.GetHashCode()));
        }

        // ── history ──────────────────────────────────────────────────────────

        [Test]
        public void Back_and_forward_walk_the_same_sequence_the_clicks_made()
        {
            var h = new SusStoryHistory();
            for (int i = 0; i < 10; i++) h.Go(new SusStoryRoute("kit/atoms/s" + i));

            Assert.That(h.Entries.Count, Is.EqualTo(10));
            Assert.That(h.Current.StoryId, Is.EqualTo("kit/atoms/s9"));

            for (int i = 8; i >= 0; i--)
            {
                Assert.That(h.Back(), Is.True);
                Assert.That(h.Current.StoryId, Is.EqualTo("kit/atoms/s" + i));
            }

            Assert.That(h.Back(), Is.False, "nothing before the first entry");
            Assert.That(h.CanGoBack, Is.False);

            for (int i = 1; i <= 9; i++)
            {
                Assert.That(h.Forward(), Is.True);
                Assert.That(h.Current.StoryId, Is.EqualTo("kit/atoms/s" + i));
            }

            Assert.That(h.Forward(), Is.False);
        }

        [Test]
        public void Navigating_after_going_back_drops_the_forward_tail()
        {
            var h = new SusStoryHistory();
            h.Go(new SusStoryRoute("kit/atoms/a"));
            h.Go(new SusStoryRoute("kit/atoms/b"));
            h.Go(new SusStoryRoute("kit/atoms/c"));
            h.Back();

            h.Go(new SusStoryRoute("kit/atoms/d"));

            Assert.That(h.CanGoForward, Is.False);
            Assert.That(h.Entries.Count, Is.EqualTo(3));
            Assert.That(h.Current.StoryId, Is.EqualTo("kit/atoms/d"));
            Assert.That(h.Back(), Is.True);
            Assert.That(h.Current.StoryId, Is.EqualTo("kit/atoms/b"));
        }

        [Test]
        public void Re_selecting_the_shown_story_does_not_pile_up_entries()
        {
            var h = new SusStoryHistory();
            h.Go(new SusStoryRoute("kit/atoms/a"));
            h.Go(new SusStoryRoute("kit/atoms/a"));
            h.Go(new SusStoryRoute("kit/atoms/a"));

            Assert.That(h.Entries.Count, Is.EqualTo(1));
            Assert.That(h.CanGoBack, Is.False);
        }

        [Test]
        public void Replace_changes_the_query_without_burying_the_previous_story()
        {
            var h = new SusStoryHistory();
            h.Go(new SusStoryRoute("kit/atoms/a"));
            h.Go(new SusStoryRoute("kit/atoms/b"));

            for (int v = 0; v < 20; v++)
                h.Replace(new SusStoryRoute("kit/atoms/b",
                    new Dictionary<string, string> { ["Value"] = v.ToString() }));

            Assert.That(h.Entries.Count, Is.EqualTo(2));
            Assert.That(h.Current.Query["Value"], Is.EqualTo("19"));
            Assert.That(h.Back(), Is.True);
            Assert.That(h.Current.StoryId, Is.EqualTo("kit/atoms/a"));
        }

        [Test]
        public void Changed_fires_once_per_actual_move()
        {
            var h = new SusStoryHistory();
            int fired = 0;
            h.Changed += _ => fired++;

            h.Go(new SusStoryRoute("kit/atoms/a"));
            h.Go(new SusStoryRoute("kit/atoms/a"));   // same route: no move, no event
            h.Go(new SusStoryRoute("kit/atoms/b"));
            h.Back();
            h.Back();                                  // nowhere to go: no event

            Assert.That(fired, Is.EqualTo(3));
        }

        [Test]
        public void Capacity_drops_the_oldest_and_keeps_the_cursor_on_the_current_route()
        {
            var h = new SusStoryHistory { Capacity = 3 };
            for (int i = 0; i < 5; i++) h.Go(new SusStoryRoute("kit/atoms/s" + i));

            Assert.That(h.Entries.Count, Is.EqualTo(3));
            Assert.That(h.Current.StoryId, Is.EqualTo("kit/atoms/s4"));
            Assert.That(h.Entries[0].StoryId, Is.EqualTo("kit/atoms/s2"));
            Assert.That(h.Cursor, Is.EqualTo(2));
        }

        // ── url bridge (loop guard, testable without a browser) ──────────────

        [Test]
        public void A_route_that_came_from_the_address_bar_is_not_written_back()
        {
            using var url = new SusStoryUrl();

            Assert.That(url.Push(new SusStoryRoute("kit/atoms/a")), Is.True);
            Assert.That(url.Address, Is.EqualTo("#/kit/atoms/a"));

            var fromBar = SusStoryRoute.Parse("#/kit/atoms/b");
            Assert.That(url.Push(fromBar), Is.False, "loop guard: fromUrl routes are not pushed");
            Assert.That(url.Address, Is.EqualTo("#/kit/atoms/a"));
        }

        [Test]
        public void Pushing_the_address_already_shown_is_a_no_op()
        {
            using var url = new SusStoryUrl();
            Assert.That(url.Push(new SusStoryRoute("kit/atoms/a")), Is.True);
            Assert.That(url.Push(new SusStoryRoute("kit/atoms/a")), Is.False);
        }

        [Test]
        public void An_external_address_change_arrives_as_a_route_marked_fromUrl()
        {
            using var url = new SusStoryUrl();
            SusStoryRoute seen = null;
            url.ExternalChanged += r => seen = r;

            // What the .jslib bridge calls on popstate / hashchange.
            url.HandleExternal("#/game/slots/hold?Progress=62");

            Assert.That(seen, Is.Not.Null);
            Assert.That(seen.StoryId, Is.EqualTo("game/slots/hold"));
            Assert.That(seen.Query["Progress"], Is.EqualTo("62"));
            Assert.That(seen.FromUrl, Is.True);
            Assert.That(url.Address, Is.EqualTo("#/game/slots/hold?Progress=62"));
        }

        [Test]
        public void Url_and_history_together_do_not_loop()
        {
            // The wiring the shell uses: external change -> history.Go -> (would-be) push back.
            using var url = new SusStoryUrl();
            var history = new SusStoryHistory();
            int pushes = 0;

            history.Changed += route =>
            {
                if (route != null && !route.FromUrl && url.Push(route)) pushes++;
            };
            url.ExternalChanged += route => history.Go(route);

            history.Go(new SusStoryRoute("kit/atoms/a"));     // click: one push
            url.HandleExternal("#/kit/atoms/b");              // back button: no push

            Assert.That(pushes, Is.EqualTo(1));
            Assert.That(history.Current.StoryId, Is.EqualTo("kit/atoms/b"));
            Assert.That(history.Entries.Count, Is.EqualTo(2));
        }
    }
}
