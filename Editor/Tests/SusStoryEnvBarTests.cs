using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Env;
using Sharq.Core.Storybook.Nav;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Zone B of card T-3036 (plan §4.4): every built-in chip cycles its real core service and
    /// back, a registry-provided axis (the kit skin slot, or a future locale one) only
    /// shows up while something is registered for it, and the environment survives a story switch
    /// while still round-tripping through the deep-link's <c>env.*</c> query.
    /// </summary>
    public class SusStoryEnvBarTests
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
            SusStoryEnvAxisRegistry.Unregister("skin");
            SusStoryEnvAxisRegistry.Unregister("locale");

            // Density/theme/scale/input are process-global (plan §4.4: theme repaints the whole
            // shell, not one story) — a leftover from one test would otherwise leak into the next.
            var scrap = new VisualElement();
            SusThemeService.Instance.SetTheme(scrap, SusTheme.Dark);
            SusDensityService.Instance.SetDensity(scrap, SusDensity.Default);
            SusScaleService.Instance.SetScale(scrap, 1f);
            SusInputDevice.NotifyActivity(SusInputDeviceKind.Pointer);
        }

        static SusStoryEnvBar NewBar(out VisualElement shell, out VisualElement preview)
        {
            shell = new VisualElement();
            preview = new VisualElement();
            return new SusStoryEnvBar(shell, preview);
        }

        // ── built-in chips ───────────────────────────────────────────────────

        [Test]
        public void Renders_one_chip_per_builtin_axis_in_mockup_order()
        {
            using var bar = NewBar(out _, out _);

            var ids = bar.ActiveAxes().Select(a => a.Id).ToList();
            Assert.That(ids, Is.EqualTo(new[] { "breakpoint", "density", "theme", "scale", "input" }),
                "skin/locale have no provider here, so they are absent, not empty (plan §4.4)");

            var chips = bar.Query<VisualElement>(className: "sus-sb-env__chip").ToList();
            Assert.That(chips.Count, Is.EqualTo(5));
            Assert.That(chips.Last().ClassListContains("sus-sb-env__chip--last"), Is.True);
            foreach (var chip in chips)
            {
                Assert.That(chip.Q<Label>(className: "sus-sb-env__chip-value"), Is.Not.Null);
            }
        }

        [Test]
        public void Breakpoint_axis_overrides_SusBreakpointService_and_auto_clears_it()
        {
            using var bar = NewBar(out _, out var preview);
            var axis = bar.ActiveAxes().First(a => a.Id == "breakpoint");
            Assert.That(axis.Current, Is.EqualTo("auto"));
            Assert.That(SusBreakpointService.For(preview).Override, Is.Null);

            axis.Apply("lg");

            Assert.That(axis.Current, Is.EqualTo("lg"));
            Assert.That(SusBreakpointService.For(preview).Override, Is.EqualTo(Breakpoint.Lg));
            Assert.That(preview.ClassListContains("breakpoint-lg"), Is.True);

            axis.Apply("auto");

            Assert.That(axis.Current, Is.EqualTo("auto"));
            Assert.That(SusBreakpointService.For(preview).Override, Is.Null);
        }

        [Test]
        public void Density_axis_drives_SusDensityService_on_the_preview_root()
        {
            using var bar = NewBar(out _, out var preview);
            var axis = bar.ActiveAxes().First(a => a.Id == "density");
            Assert.That(axis.Current, Is.EqualTo("default"));

            axis.Apply("compact");

            Assert.That(axis.Current, Is.EqualTo("compact"));
            Assert.That(SusDensityService.Current.Value, Is.EqualTo(SusDensity.Compact));
            Assert.That(preview.ClassListContains("density-compact"), Is.True);

            axis.Apply("default");

            Assert.That(SusDensityService.Current.Value, Is.EqualTo(SusDensity.Default));
            Assert.That(preview.ClassListContains("density-compact"), Is.False);
        }

        [Test]
        public void Theme_axis_repaints_the_whole_shell_root_not_the_preview()
        {
            using var bar = NewBar(out var shell, out var preview);
            var axis = bar.ActiveAxes().First(a => a.Id == "theme");
            Assert.That(axis.Current, Is.EqualTo("dark"));
            Assert.That(axis.Icon, Is.EqualTo("moon"));

            axis.Apply("light");

            Assert.That(axis.Current, Is.EqualTo("light"));
            Assert.That(axis.Icon, Is.EqualTo("sun"));
            Assert.That(SusThemeService.Current.Value, Is.EqualTo(SusTheme.Light));
            Assert.That(shell.ClassListContains("theme-light"), Is.True,
                "theme is how the TOOL reads (mock-up data-theme on the whole shell), not a per-story prop");
            Assert.That(preview.ClassListContains("theme-light"), Is.False);
        }

        [Test]
        public void Scale_axis_drives_SusScaleService_as_a_percentage_on_the_preview_root()
        {
            using var bar = NewBar(out _, out var preview);
            var axis = bar.ActiveAxes().First(a => a.Id == "scale");
            Assert.That(axis.Current, Is.EqualTo("100"));

            axis.Apply("150");

            Assert.That(axis.Current, Is.EqualTo("150"));
            Assert.That(SusScaleService.Current.Value, Is.EqualTo(1.5f).Within(0.001f));

            axis.Apply("100");
            Assert.That(SusScaleService.Current.Value, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Input_axis_drives_SusInputDevice_ActiveKind()
        {
            using var bar = NewBar(out _, out _);
            var axis = bar.ActiveAxes().First(a => a.Id == "input");
            Assert.That(axis.Current, Is.EqualTo("mouse"));

            axis.Apply("gamepad");

            Assert.That(axis.Current, Is.EqualTo("gamepad"));
            Assert.That(SusInputDevice.ActiveKind, Is.EqualTo(SusInputDeviceKind.Gamepad));

            axis.Apply("mouse");
            Assert.That(SusInputDevice.ActiveKind, Is.EqualTo(SusInputDeviceKind.Pointer));
        }

        [Test]
        public void A_click_cycles_to_the_next_value_and_wraps()
        {
            using var bar = NewBar(out _, out _);
            var axis = bar.ActiveAxes().First(a => a.Id == "theme");
            Assert.That(axis.Values, Is.EqualTo(new[] { "dark", "light" }));

            bar.CycleForTest("theme");
            Assert.That(axis.Current, Is.EqualTo("light"));

            bar.CycleForTest("theme");
            Assert.That(axis.Current, Is.EqualTo("dark"), "cycling past the last value wraps to the first");
        }

        // ── registry-provided axes (skin, locale) ─────────────────────────────

        sealed class FakeAxis : ISusStoryEnvAxis
        {
            string _current;
            public FakeAxis(string id, params string[] values) { Id = id; Values = values; _current = values[0]; }
            public string Id { get; }
            public string Icon => "palette";
            public IReadOnlyList<string> Values { get; }
            public string Current => _current;
            public void Apply(string value) => _current = value;
        }

        [Test]
        public void No_provider_means_no_chip_a_provider_means_one_in_mockup_order()
        {
            using var bar = NewBar(out _, out _);
            Assert.That(bar.ActiveAxes().Any(a => a.Id == "skin"), Is.False);

            SusStoryEnvAxisRegistry.Register(new FakeAxis("skin", "(none)", "TestSkin"));

            var ids = bar.ActiveAxes().Select(a => a.Id).ToList();
            Assert.That(ids, Is.EqualTo(new[] { "breakpoint", "density", "theme", "skin", "scale", "input" }),
                "skin sits between theme and scale, exactly the mock-up chipDefs order");
            Assert.That(bar.Query<VisualElement>(className: "sus-sb-env__chip").ToList().Count, Is.EqualTo(6));

            SusStoryEnvAxisRegistry.Unregister("skin");
            Assert.That(bar.ActiveAxes().Any(a => a.Id == "skin"), Is.False);
            Assert.That(bar.Query<VisualElement>(className: "sus-sb-env__chip").ToList().Count, Is.EqualTo(5));
        }

        // ── deep-link round trip (plan §4.4: "оси среды пишутся в query отдельным префиксом") ──

        [Test]
        public void CurrentDeltas_is_empty_at_defaults_and_carries_only_non_default_axes()
        {
            using var bar = NewBar(out _, out _);
            Assert.That(bar.CurrentDeltas(), Is.Empty, "an untouched environment must not clutter the link");

            bar.ActiveAxes().First(a => a.Id == "theme").Apply("light");
            bar.ActiveAxes().First(a => a.Id == "scale").Apply("125");

            var deltas = bar.CurrentDeltas();
            Assert.That(deltas.Count, Is.EqualTo(2));
            Assert.That(deltas["env.theme"], Is.EqualTo("light"));
            Assert.That(deltas["env.scale"], Is.EqualTo("125"));
        }

        [Test]
        public void ConsumeFromRoute_applies_env_entries_and_strips_them_leaving_control_props_alone()
        {
            using var bar = NewBar(out _, out var preview);
            var route = new SusStoryRoute("core/primitives/counter", new Dictionary<string, string>
            {
                ["env.theme"] = "light",
                ["env.density"] = "compact",
                ["Label"] = "Taps"
            });

            var clean = bar.ConsumeFromRoute(route);

            Assert.That(SusThemeService.Current.Value, Is.EqualTo(SusTheme.Light));
            Assert.That(SusDensityService.Current.Value, Is.EqualTo(SusDensity.Compact));
            Assert.That(clean.Query.Keys, Is.EquivalentTo(new[] { "Label" }),
                "env.* must not reach the story's own control-prop query");
            Assert.That(clean.Query["Label"], Is.EqualTo("Taps"));
        }

        [Test]
        public void ConsumeFromRoute_is_a_no_op_when_the_route_carries_no_env_entries()
        {
            using var bar = NewBar(out _, out _);
            var route = new SusStoryRoute("core/primitives/counter",
                new Dictionary<string, string> { ["Label"] = "Taps" });

            Assert.That(bar.ConsumeFromRoute(route), Is.SameAs(route));
        }

        // ── host integration ─────────────────────────────────────────────────

        [Test]
        public void Host_reuses_the_single_address_label_and_share_button_inside_zone_B()
        {
            using var host = new SusStorybookHost();

            Assert.That(host.Q<Label>(className: "sus-sb__link"), Is.Not.Null);
            Assert.That(host.Query<Label>(className: "sus-sb__link").ToList().Count, Is.EqualTo(1),
                "T-3036 must move, not duplicate, the T-3033 deep-link label");
            Assert.That(host.Query<Button>(className: "sus-sb__share").ToList().Count, Is.EqualTo(1));
            Assert.That(host.ZoneEnvironment.Contains(host.Q<Label>(className: "sus-sb__link")), Is.True);
        }

        [Test]
        public void Switching_a_chip_does_not_reset_the_current_story_and_the_link_reproduces_it()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById("core/primitives/counter");

            host.Env.CycleForTest("theme");

            Assert.That(host.CurrentStory.Id, Is.EqualTo("core/primitives/counter"),
                "plan §4.4: switching an environment axis must not reset the story");
            Assert.That(host.Url.Address, Does.Contain("env.theme=light"));

            host.Env.CycleForTest("theme");
            Assert.That(host.Url.Address, Does.Not.Contain("env.theme"),
                "back at the default, the link stays short again");
        }

        [Test]
        public void Environment_survives_navigating_to_a_different_story()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById("core/primitives/counter");
            host.Env.CycleForTest("density");

            host.ShowStoryById("core/overlay/floating");

            var density = host.Env.ActiveAxes().First(a => a.Id == "density");
            Assert.That(density.Current, Is.EqualTo("comfortable"),
                "environment is process state, not part of any one story's route");
        }

        [Test]
        public void A_link_opened_with_env_entries_applies_them_before_the_story_is_shown()
        {
            using var host = new SusStorybookHost();

            host.Url.HandleExternal("#/core/primitives/counter?env.theme=light&Label=Taps");

            Assert.That(SusThemeService.Current.Value, Is.EqualTo(SusTheme.Light));
            Assert.That(host.CurrentStory.Id, Is.EqualTo("core/primitives/counter"));
        }
    }
}
