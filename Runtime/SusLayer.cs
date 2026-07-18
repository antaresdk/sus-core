using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Marker base for the CLOSED set of structural layer-host containers owned
    /// exclusively by sus-core (overlays, screens). These are the ONLY nodes in the
    /// SUS hierarchy that are NOT <see cref="SusComponent"/>: pure physical z-slots
    /// that just hold content (Unity USS has no z-index, so layering is DOM order).
    ///
    /// The two-tier hierarchy guarantee (see C2 refactor):
    ///   - CONTENT  → always a <see cref="SusComponent"/> (uniform: reactivity,
    ///                lifecycle, companion USS, click-audit, diagnostics).
    ///   - LAYERS   → this small, fixed set of <see cref="SusLayer"/> hosts, all
    ///                declared here in core. New layer types must be added in core,
    ///                so content can never be "misplaced" into an ad-hoc layer.
    ///
    /// Concrete layers: <see cref="OverlayHost"/> (overlays) and
    /// <c>SusScreenOutlet&lt;TScreen&gt;</c> (screens).
    /// </summary>
    public abstract class SusLayer : VisualElement
    {
    }
}
