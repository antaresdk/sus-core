using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.Env
{
    /// <summary>
    /// The five environment axes core owns outright (plan §4.4, card T-3036): breakpoint, density,
    /// theme, scale and input. Each wraps the real core service directly — a story's rendering is
    /// meant to react exactly as it would in a shipped app, not to a storybook-only stand-in.
    ///
    /// Breakpoint/density/scale bind to <c>previewRoot</c> (the stage canvas, card T-3033's
    /// <c>QaCanvas</c>): the preview simulates a viewport for the ONE mounted story without
    /// resizing zone A's nav list or zone B's own chip row. Theme binds to <c>shellRoot</c> (the
    /// whole host): dark/light is how the tool itself reads, not a per-component prop, and the
    /// mock-up's <c>data-theme</c> attribute repaints the entire shell for the same reason. Input
    /// has no root — <see cref="SusInputDevice"/> is process-global by construction.
    /// </summary>
    static class SusBuiltinEnvAxes
    {
        public static IEnumerable<ISusStoryEnvAxis> CreateAll(VisualElement shellRoot, VisualElement previewRoot)
        {
            yield return new BreakpointAxis(previewRoot);
            yield return new DensityAxis(previewRoot);
            yield return new ThemeAxis(shellRoot);
            yield return new ScaleAxis(previewRoot);
            yield return new InputAxis();
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

            public ThemeAxis(VisualElement root) => _root = root;

            public string Id => "theme";
            public string Icon => string.Equals(Current, "light", StringComparison.Ordinal) ? "sun" : "moon";
            public IReadOnlyList<string> Values => s_values;
            public string Current => SusThemeService.Current.Value.Name;

            public void Apply(string value)
            {
                var theme = string.Equals(value, "light", StringComparison.OrdinalIgnoreCase)
                    ? SusTheme.Light
                    : SusTheme.Dark;
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
