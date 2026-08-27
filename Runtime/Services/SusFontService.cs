using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Applies a <see cref="SusFontAsset"/> to a VisualElement tree.
    ///
    /// Unity has no public API to set USS custom properties (<c>--var</c>) from C#,
    /// so fonts are applied via the inherited <c>-unity-font-definition</c> style
    /// (<see cref="IStyle.unityFontDefinition"/>). The body (Regular) typeface is set
    /// on the root and inherited by all children, unless a more specific USS rule
    /// overrides it on a particular element.
    ///
    /// T-2216: the other five slots (Medium/Bold/Light/Heading/Mono) and Condensed cannot
    /// ride that same inheritance trick — components declare their own more-specific
    /// <c>-unity-font-definition</c> rule for those roles (see <c>_font.uss</c> and any
    /// downstream package's own token bridge), which always wins over an inherited value. So those
    /// roles are dispatched directly: any element under <paramref name="root"/> tagged with
    /// the matching marker USS class (<see cref="HeadingClassName"/> etc.) gets that role's
    /// resolved <see cref="FontDefinition"/> set as an INLINE style, which outranks any USS
    /// rule regardless of selector specificity. Markup opts in by adding the marker class;
    /// nothing is auto-discovered. A filled slot with no marked element anywhere under
    /// <paramref name="root"/> logs a warning instead of silently doing nothing.
    /// </summary>
    public static class SusFontService
    {
        /// <summary>Marker USS class: elements tagged with this get SusFontAsset.ResolveHeading().</summary>
        public const string HeadingClassName = "sus-font-heading";

        /// <summary>Marker USS class: elements tagged with this get SusFontAsset.ResolveMono().</summary>
        public const string MonoClassName = "sus-font-mono";

        /// <summary>Marker USS class: elements tagged with this get SusFontAsset.ResolveBold().</summary>
        public const string BoldClassName = "sus-font-bold";

        /// <summary>Marker USS class: elements tagged with this get SusFontAsset.ResolveMedium().</summary>
        public const string MediumClassName = "sus-font-medium";

        /// <summary>Marker USS class: elements tagged with this get SusFontAsset.ResolveLight().</summary>
        public const string LightClassName = "sus-font-light";

        /// <summary>Marker USS class: elements tagged with this get SusFontAsset.ResolveCondensed().</summary>
        public const string CondensedClassName = "sus-font-condensed";

        /// <summary>
        /// Applies every slot of <paramref name="fontAsset"/> to <paramref name="root"/>:
        /// Regular as the inherited default on the root, and Heading/Mono/Bold/Medium/Light/
        /// Condensed to any descendant tagged with the matching marker class (see the
        /// <c>*ClassName</c> constants on this type). Call once at startup, before mounting UI,
        /// then again whenever the marked subtree changes shape.
        /// </summary>
        public static void ApplyFonts(VisualElement root, SusFontAsset fontAsset)
        {
            if (root == null || fontAsset == null) return;
            ApplyFontDefinition(root, fontAsset.Regular);

            var unapplied = new List<string>();
            ApplyRole(root, HeadingClassName, fontAsset.ResolveHeading(), fontAsset.Heading, "Heading", unapplied);
            ApplyRole(root, MonoClassName, fontAsset.ResolveMono(), fontAsset.Mono, "Mono", unapplied);
            ApplyRole(root, BoldClassName, fontAsset.ResolveBold(), fontAsset.Bold, "Bold", unapplied);
            ApplyRole(root, MediumClassName, fontAsset.ResolveMedium(), fontAsset.Medium, "Medium", unapplied);
            ApplyRole(root, LightClassName, fontAsset.ResolveLight(), fontAsset.Light, "Light", unapplied);
            ApplyRole(root, CondensedClassName, fontAsset.ResolveCondensed(), fontAsset.Condensed, "Condensed", unapplied);

            if (unapplied.Count > 0)
            {
                SusLog.Warn(
                    $"[SusFontService] SusFontAsset '{fontAsset.name}' fills {string.Join(", ", unapplied)} " +
                    $"but no element under '{root.name}' carries the matching marker USS class " +
                    "(sus-font-heading / -mono / -bold / -medium / -light / -condensed — see " +
                    "SusFontService.<Role>ClassName). Only Regular is applied by root inheritance; " +
                    "these slots need markup to opt in via that class, or they silently do nothing.");
            }
        }

        /// <summary>
        /// Applies <paramref name="resolved"/> to every descendant of <paramref name="root"/>
        /// tagged with <paramref name="className"/>. Records <paramref name="slotName"/> in
        /// <paramref name="unapplied"/> when <paramref name="ownSlot"/> is filled but no element
        /// matched (so the caller can warn once, listing every unreachable slot together).
        /// </summary>
        private static void ApplyRole(
            VisualElement root, string className, FontDefinition resolved,
            FontDefinition ownSlot, string slotName, List<string> unapplied)
        {
            bool any = false;
            root.Query<VisualElement>(className: className).ForEach(el =>
            {
                ApplyFontDefinition(el, resolved);
                any = true;
            });
            if (!any && SusFontAsset.HasFont(ownSlot))
                unapplied.Add(slotName);
        }

        /// <summary>
        /// Also applies the body font — and every marker-tagged role, per <see cref="ApplyFonts"/>
        /// — to an overlay host (popups, tooltips, modals) so reparented elements inherit the
        /// custom typeface. Does not repeat the unapplied-slot warning (ApplyFonts already did).
        /// </summary>
        public static void ApplyToOverlayHost(VisualElement root, SusFontAsset fontAsset)
        {
            if (root == null || fontAsset == null) return;
            var overlayHost = root.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            if (overlayHost == null && root.panel?.visualTree != null)
                overlayHost = root.panel.visualTree.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            if (overlayHost == null) return;

            ApplyFontDefinition(overlayHost, fontAsset.Regular);
            var discard = new List<string>();
            ApplyRole(overlayHost, HeadingClassName, fontAsset.ResolveHeading(), fontAsset.Heading, "Heading", discard);
            ApplyRole(overlayHost, MonoClassName, fontAsset.ResolveMono(), fontAsset.Mono, "Mono", discard);
            ApplyRole(overlayHost, BoldClassName, fontAsset.ResolveBold(), fontAsset.Bold, "Bold", discard);
            ApplyRole(overlayHost, MediumClassName, fontAsset.ResolveMedium(), fontAsset.Medium, "Medium", discard);
            ApplyRole(overlayHost, LightClassName, fontAsset.ResolveLight(), fontAsset.Light, "Light", discard);
            ApplyRole(overlayHost, CondensedClassName, fontAsset.ResolveCondensed(), fontAsset.Condensed, "Condensed", discard);
        }

        /// <summary>
        /// Applies a single <see cref="FontDefinition"/> to one element via
        /// <c>-unity-font-definition</c>. Prefers an SDF <c>FontAsset</c>, falling
        /// back to a legacy <see cref="Font"/>. No-op if neither is set.
        /// </summary>
        public static void ApplyFontDefinition(VisualElement el, FontDefinition fd)
        {
            if (el == null) return;
            if (fd.fontAsset != null)
                el.style.unityFontDefinition = FontDefinition.FromSDFFont(fd.fontAsset);
            else if (fd.font != null)
                el.style.unityFontDefinition = FontDefinition.FromFont(fd.font);
        }

        /// <summary>
        /// Reverts <paramref name="root"/> to the USS-defined font.
        /// </summary>
        public static void ResetToDefault(VisualElement root)
        {
            if (root == null) return;
            root.style.unityFontDefinition = StyleKeyword.Null;
        }
    }
}
