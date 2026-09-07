using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>Typographic roles a SUS font set can fill. Each one maps to a marker USS
    /// class (<see cref="SusFontService.ClassNameFor"/>) whose typeface is declared in
    /// <c>_font.uss</c>.</summary>
    public enum SusFontRole
    {
        /// <summary>Body text. Also the panel default (<c>:root</c> in <c>_font.uss</c>), so an
        /// element needs this class only to opt back OUT of a more specific role.</summary>
        Regular,
        /// <summary>Emphasis / labels.</summary>
        Medium,
        /// <summary>Strong emphasis.</summary>
        Bold,
        /// <summary>Thin / captions.</summary>
        Light,
        /// <summary>Headings.</summary>
        Heading,
        /// <summary>Monospaced (code / stats).</summary>
        Mono,
        /// <summary>Narrow display cut for large titles.</summary>
        Condensed,
    }

    /// <summary>
    /// Switches typographic ROLES on a VisualElement tree by adding/removing marker USS classes.
    ///
    /// T-2767 (D-069 / R120) — what changed and why. Until now this service wrote the typeface
    /// itself: <c>el.style.unityFontDefinition = FontDefinition.From...(...)</c>. An inline style
    /// outranks every USS rule regardless of selector specificity, so whatever this service had
    /// written could not be restyled by a theme, a skin, or a project override sheet - the exact
    /// harm R120 exists to stop. The typeface of every role is now declared in USS
    /// (<c>_font.uss</c>, rules <c>.sus-font-*</c>, values from the <c>--sus-font-family-*</c> /
    /// <c>--font-family-*</c> token chain), and this service only puts the class on or takes it
    /// off. Nothing here writes an inline appearance style any more.
    ///
    /// Consequence for <see cref="SusFontAsset"/>: a ScriptableObject reference cannot be handed
    /// to USS from C# (Unity has no public API to author a StyleSheet or to set a USS custom
    /// property at runtime), so a filled font set is no longer pushed onto the tree by
    /// <see cref="ApplyFonts"/>. The supported route is one Editor step -
    /// <c>Window / SUS / Fonts / Export Font Set to USS</c> (<c>SusFontUssExporter</c>) writes
    /// <c>Assets/Resources/SusRuntime/_font.uss</c> with the set's <c>--sus-font-family-*</c>
    /// declarations, after which the whole cascade (root text, every component rule, every marker
    /// class below, skins, overlays) reads the project's typefaces. <see cref="ApplyFonts"/> says
    /// so in a warning when it is called with a font set that USS has not been told about.
    /// </summary>
    public static class SusFontService
    {
        /// <summary>Marker USS class for <see cref="SusFontRole.Regular"/>.</summary>
        public const string RegularClassName = "sus-font-regular";

        /// <summary>Marker USS class for <see cref="SusFontRole.Medium"/>.</summary>
        public const string MediumClassName = "sus-font-medium";

        /// <summary>Marker USS class for <see cref="SusFontRole.Bold"/>.</summary>
        public const string BoldClassName = "sus-font-bold";

        /// <summary>Marker USS class for <see cref="SusFontRole.Light"/>.</summary>
        public const string LightClassName = "sus-font-light";

        /// <summary>Marker USS class for <see cref="SusFontRole.Heading"/>.</summary>
        public const string HeadingClassName = "sus-font-heading";

        /// <summary>Marker USS class for <see cref="SusFontRole.Mono"/>.</summary>
        public const string MonoClassName = "sus-font-mono";

        /// <summary>Marker USS class for <see cref="SusFontRole.Condensed"/>.</summary>
        public const string CondensedClassName = "sus-font-condensed";

        /// <summary>Every marker class this service manages, indexed by <see cref="SusFontRole"/>.</summary>
        public static readonly string[] RoleClassNames =
        {
            RegularClassName, MediumClassName, BoldClassName, LightClassName,
            HeadingClassName, MonoClassName, CondensedClassName,
        };

        /// <summary>Marker USS class that carries <paramref name="role"/>'s typeface.</summary>
        public static string ClassNameFor(SusFontRole role) => RoleClassNames[(int)role];

        /// <summary>
        /// Puts <paramref name="role"/>'s marker class on <paramref name="el"/>, removing whatever
        /// other role class it carried (an element has exactly one typographic role). The typeface
        /// itself comes from <c>_font.uss</c> - this call writes no style.
        /// </summary>
        public static void ApplyRoleClass(VisualElement el, SusFontRole role)
        {
            if (el == null) return;
            var wanted = ClassNameFor(role);
            foreach (var cls in RoleClassNames)
                el.EnableInClassList(cls, cls == wanted);
        }

        /// <summary>
        /// Removes every role marker class from <paramref name="el"/>, so it falls back to the
        /// inherited body typeface (or to whatever a component/skin rule declares for it).
        /// </summary>
        public static void ClearRoleClasses(VisualElement el)
        {
            if (el == null) return;
            foreach (var cls in RoleClassNames)
                el.RemoveFromClassList(cls);
        }

        /// <summary>
        /// Kept for API compatibility (<c>SusApp.UseFonts</c> calls it). Applies no style: since
        /// T-2767 the typeface of every role lives in USS, so a tree whose markup carries the
        /// marker classes is already correct without this call. When <paramref name="fontAsset"/>
        /// carries typefaces, this warns once with the one step that actually makes them reach the
        /// cascade (the Editor exporter named on this type) - silently doing nothing would be the
        /// worse failure.
        /// </summary>
        public static void ApplyFonts(VisualElement root, SusFontAsset fontAsset)
        {
            if (root == null || fontAsset == null) return;

            var filled = FilledSlots(fontAsset);
            if (filled == null) return;

            SusLog.Warn(
                $"[SusFontService] SusFontAsset '{fontAsset.name}' fills {filled} but a font set is " +
                "no longer applied from C# (T-2767): an inline -unity-font-definition outranks every " +
                "USS rule, so nothing downstream could restyle it. Export the set once - " +
                "Window > SUS > Fonts > Export Font Set to USS - which writes " +
                "Assets/Resources/SusRuntime/_font.uss with --sus-font-family-*; the whole cascade " +
                "reads it, including the marker classes sus-font-heading / -mono / -bold / " +
                "-medium / -light / -condensed (SusFontService.ClassNameFor).");
        }

        /// <summary>
        /// Kept for API compatibility. Does nothing: the OverlayHost lives in the same panel as
        /// the app root, so it inherits the USS-declared body typeface and resolves the same
        /// marker classes - the reparenting hole this method used to plug existed only because
        /// the font was an inline style on the app root.
        /// </summary>
        public static void ApplyToOverlayHost(VisualElement root, SusFontAsset fontAsset)
        {
        }

        /// <summary>
        /// Removes any inline <c>-unity-font-definition</c> left on <paramref name="root"/> by
        /// project code, handing the element back to the USS cascade.
        /// </summary>
        public static void ResetToDefault(VisualElement root)
        {
            if (root == null) return;
            root.style.unityFontDefinition = StyleKeyword.Null;
        }

        /// <summary>Human-readable list of the slots <paramref name="asset"/> actually fills,
        /// or null when it fills none.</summary>
        private static string FilledSlots(SusFontAsset asset)
        {
            var slots = new List<string>();
            if (SusFontAsset.HasFont(asset.Regular)) slots.Add("Regular");
            if (SusFontAsset.HasFont(asset.Medium)) slots.Add("Medium");
            if (SusFontAsset.HasFont(asset.Bold)) slots.Add("Bold");
            if (SusFontAsset.HasFont(asset.Light)) slots.Add("Light");
            if (SusFontAsset.HasFont(asset.Heading)) slots.Add("Heading");
            if (SusFontAsset.HasFont(asset.Mono)) slots.Add("Mono");
            if (SusFontAsset.HasFont(asset.Condensed)) slots.Add("Condensed");
            return slots.Count == 0 ? null : string.Join(", ", slots);
        }
    }
}
