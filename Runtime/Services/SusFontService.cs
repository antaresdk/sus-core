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
    /// </summary>
    public static class SusFontService
    {
        /// <summary>
        /// Applies the body (Regular) font from <paramref name="fontAsset"/> to
        /// <paramref name="root"/> as the inherited default typeface.
        /// Call once at startup, before mounting UI.
        /// </summary>
        public static void ApplyFonts(VisualElement root, SusFontAsset fontAsset)
        {
            if (root == null || fontAsset == null) return;
            ApplyFontDefinition(root, fontAsset.Regular);
        }

        /// <summary>
        /// Also applies the body font to an overlay host (popups, tooltips, modals)
        /// so reparented elements inherit the custom typeface.
        /// </summary>
        public static void ApplyToOverlayHost(VisualElement root, SusFontAsset fontAsset)
        {
            if (root == null || fontAsset == null) return;
            var overlayHost = root.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            if (overlayHost == null && root.panel?.visualTree != null)
                overlayHost = root.panel.visualTree.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            if (overlayHost != null)
                ApplyFontDefinition(overlayHost, fontAsset.Regular);
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
