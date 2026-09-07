using System;
using System.Collections.Generic;

namespace Sharq.Core.Storybook.Controls
{
    /// <summary>
    /// Everything a control needs to know beyond its own <see cref="SusPropInfo"/>: which
    /// component the prop belongs to (dependency conditions are asked of the component, not of
    /// the prop), which story is on the stage, and which colour tokens the skin offers.
    /// </summary>
    public sealed class SusControlContext
    {
        static readonly string[] DefaultTokens = { "primary", "secondary", "neutral", "success", "error", "ink" };

        public SusControlContext(
            SusComponent component,
            SusStoryContext story = null,
            IReadOnlyList<string> colorTokens = null)
        {
            Component = component ?? throw new ArgumentNullException(nameof(component));
            Story = story;
            ColorTokens = colorTokens ?? DefaultTokens;
        }

        /// <summary>The live instance the controls drive.</summary>
        public SusComponent Component { get; }

        /// <summary>Story context of the mounted story, or null (matrix cells, tests).</summary>
        public SusStoryContext Story { get; }

        /// <summary>
        /// Colour token names offered as swatches. Each name <c>t</c> is drawn by the USS class
        /// <c>sus-sb-ctl__swatch--t</c>, so the swatch shows the SKIN's colour and the C# side
        /// never names a colour value (R53/R120).
        /// </summary>
        public IReadOnlyList<string> ColorTokens { get; }

        /// <summary>The colour tokens every panel starts with.</summary>
        public static IReadOnlyList<string> StandardColorTokens => DefaultTokens;

        /// <summary>True when the story declared it drives this prop with a hand-written control.</summary>
        public bool IsManual(string propName) =>
            Story != null && propName != null && Story.ManualControls.ContainsKey(propName);

        /// <summary>Reason the story gave for excluding this prop, or null when it did not.</summary>
        public string ExclusionReason(string propName)
        {
            if (Story == null || propName == null) return null;
            return Story.Exclusions.TryGetValue(propName, out var reason) ? reason : null;
        }
    }

    /// <summary>
    /// Extension point of the control table (card T-3034, plan ARCH-20260907-STORYBOOK-ENGINE §4.3):
    /// a package
    /// that owns a prop TYPE the built-in table does not know — or wants a richer control for one
    /// it does — registers a provider with <see cref="SusControlFactory.Register"/>.
    ///
    /// The first registered provider whose <see cref="CanBuild"/> returns true wins over the
    /// built-in table; registration order is "last registered asked first", so a later package
    /// can override an earlier one. The glyph picker of card T-3035 arrives exactly this way:
    /// core ships a stub field for icon props and the picker replaces it without touching core.
    /// </summary>
    public interface ISusControlProvider
    {
        /// <summary>True when this provider wants to build the control for <paramref name="prop"/>.</summary>
        bool CanBuild(SusPropInfo prop);

        /// <summary>
        /// Builds the control. Returning null is legal and honest — it means "this prop has no
        /// control", and the panel then counts the prop as UNCOVERED instead of pretending.
        /// </summary>
        SusControl Build(SusPropInfo prop, SusControlContext context);
    }
}
