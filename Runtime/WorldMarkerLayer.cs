using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Screen-space layer for flat world markers (variant-B fallback of
    /// <see cref="WorldSpaceService"/>: health bars / nameplates projected via
    /// <c>WorldToScreenPoint</c> when there is no 3D <see cref="SusWorldSpacePanel"/>).
    ///
    /// <b>Why a dedicated layer.</b> World markers belong to objects in the 3D scene and must
    /// render <b>UNDER</b> all screen UI — a menu or HUD must be able to cover them. The
    /// <see cref="OverlayHost"/> is always the LAST child of the screen root (popups/modals on
    /// top), so world markers must NEVER live there or they would paint over screens. This layer
    /// is inserted as the FIRST child of the screen root (lowest z), below screens and overlays.
    ///
    /// Third concrete <see cref="SusLayer"/> alongside <see cref="OverlayHost"/> (overlays) and
    /// <c>SusScreenOutlet&lt;TScreen&gt;</c> (screens). Full-fill, pointer-transparent so it never
    /// steals clicks meant for the screens above it.
    /// </summary>
    public sealed class WorldMarkerLayer : SusLayer
    {
        /// <summary>Element name used to find/deduplicate the singleton layer per root.</summary>
        public const string LayerName = "__SusWorldMarkerLayer__";

        public WorldMarkerLayer()
        {
            name = LayerName;
            // Absolute full-fill so projected markers position freely within the panel.
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            // Never intercept pointer events — screens above must receive all input.
            pickingMode = PickingMode.Ignore;
        }
    }
}
