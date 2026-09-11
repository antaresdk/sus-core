using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.Env
{
    /// <summary>
    /// The SIX environment axes core owns outright: five that change the SUBJECT (plan §4.4, card
    /// T-3036 — breakpoint, density, theme, scale, input) and one that changes the INSTRUMENT
    /// (<see cref="ShellThemeAxis"/>, card T-3371). The five wrap the real core service directly —
    /// a story's rendering is meant to react exactly as it would in a shipped app, not to a
    /// storybook-only stand-in.
    ///
    /// EVERY axis that changes the SUBJECT binds to <c>previewRoot</c> (the stage canvas, card
    /// T-3033's <c>QaCanvas</c>): breakpoint, density, scale — and, since card T-3371, theme too.
    /// "Binds" has to mean the element itself: card T-3394 found theme still leaving the stage
    /// through <c>SusThemeService</c>'s cascade-root resolution — see <see cref="ThemeAxis.Apply"/>.
    ///
    /// Theme used to bind to <c>shellRoot</c>, which is card T-3371's defect and decision D27 of
    /// plan ARCH-20260911-STORYBOOK-SHELL.md. One click on the "theme" chip repainted the whole
    /// instrument: zone A's background went 0.078 → 0.922 while the canvas went to 0.961, which is
    /// also where the light-text-on-light-canvas came from. The damage is not cosmetic — it makes
    /// the inspection undemonstrable, because the dark screenshot of a component was then taken in
    /// a DIFFERENT viewer than the light one, and the difference can no longer be attributed to
    /// the component. The shell now owns its own two-theme token set (<c>--sb-*</c> in
    /// <c>Storybook.uss</c>) and its own chip, <see cref="ShellThemeAxis"/>, so "what the tool
    /// looks like" and "what the component looks like" are two separate switches.
    ///
    /// Input has no root — <see cref="SusInputDevice"/> is process-global by construction.
    /// </summary>
    static class SusBuiltinEnvAxes
    {
        public static IEnumerable<ISusStoryEnvAxis> CreateAll(VisualElement shellRoot, VisualElement previewRoot)
        {
            yield return new BreakpointAxis(previewRoot);
            yield return new DensityAxis(previewRoot);
            yield return new ThemeAxis(previewRoot);
            yield return new ScaleAxis(previewRoot);
            yield return new InputAxis();
            yield return new ShellThemeAxis(shellRoot);
        }

        /// <summary>
        /// The shell's OWN light/dark switch (card T-3371, decision D4): it puts
        /// <c>sus-sb--theme-light</c> on the shell root, where <c>Storybook.uss</c> redefines the
        /// thirteen <c>--sb-*</c> colour names. It touches no core service and therefore no story:
        /// that is the point of having two chips instead of one.
        ///
        /// Values[0] is "dark" — the shell's resting state and the mock-up's default artboard — so
        /// a shared link only carries <c>env.shell-theme=</c> when the sharer actually flipped the
        /// instrument, not every time.
        /// </summary>
        internal sealed class ShellThemeAxis : ISusStoryEnvAxis
        {
            internal const string LightClass = "sus-sb--theme-light";
            static readonly string[] s_values = { "dark", "light" };
            readonly VisualElement _root;

            public ShellThemeAxis(VisualElement root) => _root = root;

            public string Id => "shell-theme";
            public string Icon => "palette";
            public IReadOnlyList<string> Values => s_values;

            public string Current =>
                _root != null && _root.ClassListContains(LightClass) ? "light" : "dark";

            public void Apply(string value)
            {
                if (_root == null) return;
                _root.EnableInClassList(LightClass,
                    string.Equals(value, "light", StringComparison.OrdinalIgnoreCase));
            }
        }

        sealed class BreakpointAxis : ISusStoryEnvAxis
        {
            static readonly string[] s_values = { "auto", "sm", "md", "lg", "xl", "2xl" };
            readonly VisualElement _root;

            public BreakpointAxis(VisualElement root)
            {
                _root = root;
                // Idempotent — safe even when previewRoot never joins a panel (EditMode tests).
                SusBreakpointService.Attach(root);
            }

            public string Id => "breakpoint";
            public string Icon => "frame-corners";
            public IReadOnlyList<string> Values => s_values;

            public string Current
            {
                get
                {
                    var over = SusBreakpointService.For(_root).Override;
                    return over.HasValue ? Token(over.Value) : "auto";
                }
            }

            public void Apply(string value)
            {
                var svc = SusBreakpointService.For(_root);
                svc.SetOverride(string.Equals(value, "auto", StringComparison.Ordinal)
                    ? (Breakpoint?)null
                    : Parse(value));
            }

            static string Token(Breakpoint bp) => bp switch
            {
                Breakpoint.Sm => "sm",
                Breakpoint.Md => "md",
                Breakpoint.Lg => "lg",
                Breakpoint.Xl => "xl",
                _ => "2xl"
            };

            static Breakpoint Parse(string token) => token switch
            {
                "sm" => Breakpoint.Sm,
                "md" => Breakpoint.Md,
                "lg" => Breakpoint.Lg,
                "xl" => Breakpoint.Xl,
                _ => Breakpoint.Xxl
            };
        }

        sealed class DensityAxis : ISusStoryEnvAxis
        {
            static readonly string[] s_values = { "default", "comfortable", "compact" };
            readonly VisualElement _root;

            public DensityAxis(VisualElement root) => _root = root;

            public string Id => "density";
            public string Icon => "rows";
            public IReadOnlyList<string> Values => s_values;

            public string Current => SusDensityService.Current.Value switch
            {
                SusDensity.Compact => "compact",
                SusDensity.Comfortable => "comfortable",
                _ => "default"
            };

            public void Apply(string value)
            {
                var density = value switch
                {
                    "compact" => SusDensity.Compact,
                    "comfortable" => SusDensity.Comfortable,
                    _ => SusDensity.Default
                };
                SusDensityService.Instance.SetDensity(_root, density);
            }
        }

        sealed class ThemeAxis : ISusStoryEnvAxis
        {
            static readonly string[] s_values = { "dark", "light" };
            readonly VisualElement _root;

            public ThemeAxis(VisualElement root)
            {
                _root = root;
                // The axis DECLARES its root a scoped cascade root (card T-3400), and that
                // declaration is what lets SusThemeService.SetTheme below mean "this element".
                // Declared here as well as in SusStorybookHost so the guarantee holds for every
                // tree this axis is built over, including the detached EditMode fixtures where
                // there is no host at all. Idempotent and null-safe.
                SusThemeService.MarkScopedCascadeRoot(root);
            }

            public string Id => "theme";
            public string Icon => string.Equals(Current, "light", StringComparison.Ordinal) ? "sun" : "moon";
            public IReadOnlyList<string> Values => s_values;
            public string Current => SusThemeService.Current.Value.Name;

            public void Apply(string value)
            {
                var theme = string.Equals(value, "light", StringComparison.OrdinalIgnoreCase)
                    ? SusTheme.Light
                    : SusTheme.Dark;
                if (_root == null) return;

                // ONE mechanism, and it lives in the service (card T-3400; this supersedes the
                // hand-rolled class layout the axis carried for card T-3394). Back then
                // SetTheme could not mean "this element": it resolved the cascade root first
                // and preferred SusBootstrap.TokenCascadeRoot — in the live storybook the
                // UIDocument root, an ANCESTOR of the shell — so the chip repainted the whole
                // instrument. The fix then was to write the class here by hand; the fix now is
                // that the root is DECLARED a scope (constructor above) and ResolveCascadeRoot
                // stops there, so the service behaves that way for every caller instead of for
                // this one chip. Keeping both would be two mechanisms arguing over one class.
                //
                // Going back through the service also buys what the hand-rolled version could
                // not do: the stage's own OverlayHost and its open children are given the theme
                // class too, so a popup opened on the stage is painted in the theme the stage is
                // showing. The scope check inside SetTheme is what keeps that search from
                // reaching the application's overlay host above the shell.
                //
                // A class lower down is enough: .theme-dark/.theme-light only re-alias --thm-*
                // (_theme.uss L2, ":root, .theme-dark"), and Unity resolves var() from the
                // CONSUMING element upwards, so the nearest theme class above a component wins.
                // That is the same scoped override kit stories already ship
                // (Samples~/Stories/Store*Story.cs "ThemedCardPanel").
                SusThemeService.Instance.SetTheme(_root, theme);
            }
        }

        sealed class ScaleAxis : ISusStoryEnvAxis
        {
            // "100" (SusScaleService's own default, plan §4.4) must be Values[0] — an
            // env.scale delta is only written for a story shared away from 100%.
            static readonly string[] s_values = { "100", "125", "150", "75" };
            readonly VisualElement _root;

            public ScaleAxis(VisualElement root) => _root = root;

            public string Id => "scale";
            public string Icon => "magnifying-glass-plus";
            public IReadOnlyList<string> Values => s_values;

            public string Current =>
                Mathf.RoundToInt(SusScaleService.Current.Value * 100f).ToString(CultureInfo.InvariantCulture);

            public void Apply(string value)
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    SusScaleService.Instance.SetScale(_root, pct / 100f);
            }
        }

        sealed class InputAxis : ISusStoryEnvAxis
        {
            static readonly string[] s_values = { "mouse", "keyboard", "gamepad" };

            public string Id => "input";
            public string Icon => Current switch
            {
                "gamepad" => "game-controller",
                "keyboard" => "keyboard",
                _ => "mouse"
            };
            public IReadOnlyList<string> Values => s_values;

            public string Current => SusInputDevice.ActiveKind switch
            {
                SusInputDeviceKind.Gamepad => "gamepad",
                SusInputDeviceKind.Keyboard => "keyboard",
                _ => "mouse"
            };

            public void Apply(string value)
            {
                var kind = value switch
                {
                    "gamepad" => SusInputDeviceKind.Gamepad,
                    "keyboard" => SusInputDeviceKind.Keyboard,
                    _ => SusInputDeviceKind.Pointer
                };
                // A real mouse/keyboard event elsewhere in the editor can flip ActiveKind straight
                // back — SusInputDevice tracks last activity, it has no override concept, and
                // SuppressLegacyPolling is a test-only escape hatch this axis must not reach for
                // (see SusInputDevice's own doc comment). The chip previews glyphs; it does not
                // pretend to lock the device family.
                SusInputDevice.NotifyActivity(kind);
            }
        }
    }
}
