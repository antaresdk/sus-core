using System.Collections.Generic;

namespace Sharq.Core.Storybook.Env
{
    /// <summary>
    /// One environment axis of zone B (plan ARCH-20260907-STORYBOOK-ENGINE.md §4.4, card T-3036):
    /// a chip that
    /// shows the current value of a core service and cycles it on click. The five built-in axes
    /// (breakpoint, density, theme, scale, input) are core services the engine already owns and
    /// need no provider; <c>skin</c> and <c>locale</c> have none in core and reach zone B only
    /// through <see cref="SusStoryEnvAxisRegistry"/> — no provider registered means no chip, by
    /// design ("no providers, no axis").
    ///
    /// <see cref="Values"/>[0] is the axis's default/auto value: a route only carries an
    /// <c>env.&lt;Id&gt;=</c> entry for an axis sitting away from that first value (mirrors
    /// <see cref="Nav.SusStoryRoute.FromValues"/> for control props), so an untouched environment
    /// keeps a short link and a shared one says exactly what the sharer changed.
    /// </summary>
    public interface ISusStoryEnvAxis
    {
        /// <summary>Stable id used as the query key suffix (<c>env.&lt;Id&gt;</c>) and chip title.</summary>
        string Id { get; }

        /// <summary><see cref="SusIconRegistry"/> alias painted on the chip.</summary>
        string Icon { get; }

        /// <summary>Every value the chip cycles through, in click order. Index 0 is the default.</summary>
        IReadOnlyList<string> Values { get; }

        /// <summary>Value the underlying service reports right now.</summary>
        string Current { get; }

        /// <summary>Applies one of <see cref="Values"/> to the underlying service.</summary>
        void Apply(string value);
    }
}
