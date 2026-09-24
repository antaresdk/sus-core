using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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

            // Density/theme/scale/input are process-global PROPS — what each one paints is scoped
            // to the stage subtree since D27 (card T-3394), but the reactive prop behind it is one
            // per process, so a leftover from one test would otherwise leak into the next.
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
            // SIX built-ins, not the five of card T-3036: card T-3371 / decision D27 split the one
            // "theme" chip in two, because a single chip repainted the tool and the subject at once
            // — a dark screenshot of a component was then taken in a different viewer than the
            // light one. "shell-theme" is last on purpose (D4): every chip before it says what the
            // SUBJECT looks like, the last one what the INSTRUMENT looks like.
            Assert.That(ids, Is.EqualTo(new[] { "breakpoint", "density", "theme", "scale", "input", "shell-theme" }),
                "skin/locale have no provider here, so they are absent, not empty (plan §4.4)");

            var chips = bar.Query<VisualElement>(className: "sb-env__chip").ToList();
            Assert.That(chips.Count, Is.EqualTo(6));
            Assert.That(chips.Last().ClassListContains("sb-env__chip--last"), Is.True);
            Assert.That(chips.Last().ClassListContains("sb-env__chip--shell"), Is.True,
                "the instrument's own switch is set apart by a rule, not by hope (T-3371, D4)");
            foreach (var chip in chips)
            {
                Assert.That(chip.Q<Label>(className: "sb-env__chip-value"), Is.Not.Null);
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

        /// <summary>
        /// Card T-3394. This test used to be named "...repaints_the_whole_shell_root_not_the_preview"
        /// and asserted the OPPOSITE. That expectation died with decision D27 of plan
        /// ARCH-20260911-STORYBOOK-SHELL.md: while the theme chip painted the shell, one click took
        /// zone A's background from 0.078 to 0.922 and the canvas to 0.961, so the dark frame of a
        /// component was shot in a DIFFERENT viewer than the light frame and the difference could
        /// no longer be attributed to the component at all. Since T-3371 the tool has its own
        /// switch — <c>shell-theme</c>, covered below — and this one is the SUBJECT's switch.
        /// </summary>
        [Test]
        public void Theme_axis_repaints_the_stage_subtree_not_the_shell_root()
        {
            using var bar = NewBar(out var shell, out var preview);
            var axis = bar.ActiveAxes().First(a => a.Id == "theme");
            Assert.That(axis.Current, Is.EqualTo("dark"));
            Assert.That(axis.Icon, Is.EqualTo("moon"));

            axis.Apply("light");

            Assert.That(axis.Current, Is.EqualTo("light"));
            Assert.That(axis.Icon, Is.EqualTo("sun"));
            Assert.That(SusThemeService.Current.Value, Is.EqualTo(SusTheme.Light));
            Assert.That(preview.ClassListContains("theme-light"), Is.True,
                "the chip changes the SUBJECT, and the stage canvas is the subject's root (D27)");
            Assert.That(shell.ClassListContains("theme-light"), Is.False,
                "not one pixel outside the stage subtree moves when a zone B axis is switched (D27 DoD)");
        }

        /// <summary>
        /// The half of card T-3394 that was a DEFECT, not a stale expectation. Handing the axis
        /// <c>previewRoot</c> is not enough on its own: <c>SusThemeService.SetTheme</c> resolves the
        /// CASCADE root before applying (<c>SusBootstrap.TokenCascadeRoot</c>, or the nearest
        /// ancestor already carrying a <c>theme-*</c> class), and in the live storybook that root is
        /// the UIDocument's <c>rootVisualElement</c> — an ANCESTOR of the shell
        /// (<c>SusStorybookBehaviour.OnEnable</c> calls <c>SusBootstrap.LoadTokenCascade</c> on it
        /// and adds the shell as its child). So D27 held only in a detached EditMode fixture, where
        /// there is no cascade root to escape to, and failed in the editor, where it matters. This
        /// fixture reproduces the real chain: cascade root (carrying the sheet's default
        /// <c>theme-dark</c>, <c>_theme.uss</c> ":root, .theme-dark") -> shell -> stage canvas.
        /// </summary>
        [Test]
        public void Theme_axis_does_not_escape_up_to_the_cascade_root_above_the_shell()
        {
            var cascadeRoot = new VisualElement();
            cascadeRoot.AddToClassList("theme-dark");
            var shell = new VisualElement();
            var preview = new VisualElement();
            cascadeRoot.Add(shell);
            shell.Add(preview);

            using var bar = new SusStoryEnvBar(shell, preview);
            bar.ActiveAxes().First(a => a.Id == "theme").Apply("light");

            Assert.That(preview.ClassListContains("theme-light"), Is.True,
                "the stage canvas is the only element the subject's theme may land on");
            Assert.That(shell.ClassListContains("theme-light"), Is.False);
            Assert.That(cascadeRoot.ClassListContains("theme-light"), Is.False,
                "escaping to the cascade root repaints the whole instrument — the T-3371 defect");
            Assert.That(cascadeRoot.ClassListContains("theme-dark"), Is.True,
                "and it must not be stripped either: the shell keeps its own resting theme above");
        }

        /// <summary>
        /// The INSTRUMENT's switch (card T-3371, decision D4): it moves a shell-only class and
        /// touches no core service, which is the whole reason there are two chips instead of one.
        /// </summary>
        [Test]
        public void Shell_theme_axis_repaints_the_shell_root_and_no_core_service()
        {
            using var bar = NewBar(out var shell, out var preview);
            var axis = bar.ActiveAxes().First(a => a.Id == "shell-theme");
            Assert.That(axis.Current, Is.EqualTo("dark"),
                "dark is the shell's resting state, so an untouched link carries no env.shell-theme");

            axis.Apply("light");

            Assert.That(axis.Current, Is.EqualTo("light"));
            Assert.That(shell.ClassListContains("sb-shell--theme-light"), Is.True);
            Assert.That(SusThemeService.Current.Value, Is.EqualTo(SusTheme.Dark),
                "the tool's skin is not the subject's theme — no core service may move");
            Assert.That(preview.ClassListContains("theme-light"), Is.False);
            Assert.That(shell.ClassListContains("theme-light"), Is.False);

            axis.Apply("dark");
            Assert.That(shell.ClassListContains("sb-shell--theme-light"), Is.False);
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
            Assert.That(ids, Is.EqualTo(new[] { "breakpoint", "density", "theme", "skin", "scale", "input", "shell-theme" }),
                "skin sits between theme and scale, exactly the mock-up chipDefs order; the shell's "
                + "own chip stays behind all of them (T-3371, D4) — no provider gets past it");
            Assert.That(bar.Query<VisualElement>(className: "sb-env__chip").ToList().Count, Is.EqualTo(7));

            SusStoryEnvAxisRegistry.Unregister("skin");
            Assert.That(bar.ActiveAxes().Any(a => a.Id == "skin"), Is.False);
            Assert.That(bar.Query<VisualElement>(className: "sb-env__chip").ToList().Count, Is.EqualTo(6));
        }

        // ── deep-link round trip (plan §4.4: "env axes go into the query under their own prefix") ──

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
            var route = new SusStoryRoute("enginetests/primitives/counter", new Dictionary<string, string>
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
            var route = new SusStoryRoute("enginetests/primitives/counter",
                new Dictionary<string, string> { ["Label"] = "Taps" });

            Assert.That(bar.ConsumeFromRoute(route), Is.SameAs(route));
        }

        // ── host integration ─────────────────────────────────────────────────

        [Test]
        public void Host_reuses_the_single_address_label_and_share_button_inside_zone_B()
        {
            using var host = new SusStorybookHost();

            Assert.That(host.Q<Label>(className: "sb-shell__link"), Is.Not.Null);
            Assert.That(host.Query<Label>(className: "sb-shell__link").ToList().Count, Is.EqualTo(1),
                "T-3036 must move, not duplicate, the T-3033 deep-link label");
            Assert.That(host.Query<Button>(className: "sb-shell__share").ToList().Count, Is.EqualTo(1));
            Assert.That(host.ZoneEnvironment.Contains(host.Q<Label>(className: "sb-shell__link")), Is.True);
        }

        [Test]
        public void Switching_a_chip_does_not_reset_the_current_story_and_the_link_reproduces_it()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById("enginetests/primitives/counter");

            host.Env.CycleForTest("theme");

            Assert.That(host.CurrentStory.Id, Is.EqualTo("enginetests/primitives/counter"),
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
            host.ShowStoryById("enginetests/primitives/counter");
            host.Env.CycleForTest("density");

            host.ShowStoryById("enginetests/overlay/floating");

            var density = host.Env.ActiveAxes().First(a => a.Id == "density");
            Assert.That(density.Current, Is.EqualTo("comfortable"),
                "environment is process state, not part of any one story's route");
        }

        [Test]
        public void A_link_opened_with_env_entries_applies_them_before_the_story_is_shown()
        {
            using var host = new SusStorybookHost();

            host.Url.HandleExternal("#/enginetests/primitives/counter?env.theme=light&Label=Taps");

            Assert.That(SusThemeService.Current.Value, Is.EqualTo(SusTheme.Light));
            Assert.That(host.CurrentStory.Id, Is.EqualTo("enginetests/primitives/counter"));
        }
    }

    /// <summary>
    /// Card T-3364, RETURN ux-reviewer-4 (2026-09-24): the chip strip's horizontal scroller
    /// (<see cref="SusStoryEnvBar"/> sets <c>horizontalScrollerVisibility = Auto</c>) needs a
    /// REAL panel to reproduce — a detached <see cref="SusStorybookHost"/> never lays out, so
    /// <c>resolvedStyle</c> on its chips stays zero and the defect these tests guard against is
    /// invisible in a bare <c>NewBar</c> fixture. Same technique as
    /// <see cref="SusStorybookHostOverlayTeardownTests"/> — an <see cref="EditorWindow"/> gives a
    /// panel without Play — plus the reflection call to the window's own private
    /// <c>RepaintImmediately</c> that both the builder harness and the ux-reviewer's own
    /// measurement (report 2026-09-24-ux-reviewer-4) used to force a synchronous layout pass:
    /// EditMode's own update tick is not enough to read a geometry change back the same call.
    ///
    /// Found live: at a chip-strip width that overflows (900 / 480 with the six built-in chips),
    /// the ScrollView's measure pass — squeezed at the time into <c>.sb-env__bar</c>'s then-fixed
    /// <c>height: 30px</c> — read its own shrunk viewport as VERTICAL overflow too and drew a
    /// second, vertical scroller nobody asked for, eating another 8px and clipping every chip's
    /// bottom 8px. The fix is two lines: <c>verticalScrollerVisibility = Hidden</c> on the
    /// ScrollView (<see cref="SusStoryEnvBar"/>) and <c>min-height</c> instead of a hard
    /// <c>height</c> on <c>.sb-env__bar</c> (<c>Storybook.uss</c>), so the bar can grow to fit a
    /// showing horizontal scroller instead of squeezing its content into a ceiling.
    /// </summary>
    public class SusStoryEnvBarChipsScrollGeometryTests
    {
        const string CoreSheet = "Packages/com.sharq-it.sus.core/Runtime/Storybook/Storybook.uss";

        EditorWindow _window;
        SusStorybookHost _host;
        System.Reflection.MethodInfo _repaintImmediate;

        [SetUp]
        public void SetUp()
        {
            Assume.That(!UnityEngine.Application.isBatchMode,
                "needs a real graphics device to init an EditorWindow view (T-1731 pattern)");

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(CoreSheet);
            Assert.That(sheet, Is.Not.Null, "the sheet under test must be imported: " + CoreSheet);

            _window = EditorWindow.CreateInstance<EditorWindow>();
            _window.Show();
            SusBootstrap.LoadTokenCascade(_window.rootVisualElement);
            _host = new SusStorybookHost(sheet);
            _window.rootVisualElement.Add(_host);
            _host.style.flexGrow = 1;
            _host.ShowStoryById("enginetests/primitives/counter");

            _repaintImmediate = typeof(EditorWindow).GetMethod("RepaintImmediately",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;
            if (_window != null) _window.Close();
            _window = null;

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        void SetWidth(float width)
        {
            _window.position = new Rect(50, 50, width, 500);
            // Twice: the first pass is what discovers the overflow and shows the horizontal
            // scroller, the second is what the ScrollView's own measure re-runs against a panel
            // that now HAS that scroller in its tree (same two-call shape the builder harness
            // and ux-reviewer used; one call under-reports the squeeze this test exists to catch).
            _repaintImmediate?.Invoke(_window, null);
            _repaintImmediate?.Invoke(_window, null);
        }

        ScrollView Chips() => _host.Q<ScrollView>(className: "sb-env__chips");

        [TestCase(900f)]
        [TestCase(480f)]
        public void Overflow_width_keeps_the_viewport_at_least_a_chip_tall_and_the_vertical_scroller_hidden(float width)
        {
            SetWidth(width);
            var chips = Chips();
            Assert.That(chips, Is.Not.Null);

            // Sanity: this width must actually reproduce the overflow the defect needs, or the
            // rest of the assertions would pass for the wrong reason (no scroller shown at all).
            Assert.That(chips.horizontalScroller.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex),
                "sanity: width " + width + " must overflow the strip for this test to mean anything");

            var chip = chips.Q<VisualElement>(className: "sb-env__chip");
            Assert.That(chip, Is.Not.Null);

            Assert.That(chips.contentViewport.resolvedStyle.height, Is.GreaterThanOrEqualTo(chip.resolvedStyle.height),
                "a visible chip must not be clipped by a viewport shorter than itself (DoD, T-3364 RETURN)");
            Assert.That(chips.verticalScroller.resolvedStyle.display, Is.EqualTo(DisplayStyle.None),
                "the strip only ever scrolls sideways — a vertical scroller here has nothing to scroll");
            Assert.That(chips.resolvedStyle.height, Is.LessThanOrEqualTo(_host.Q<VisualElement>(className: "sb-env__bar").resolvedStyle.height + 0.5f),
                "the ScrollView must stay within sb-env__bar, not spill past it (DoD, T-3364 RETURN)");
        }
    }
}
