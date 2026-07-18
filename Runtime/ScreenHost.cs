using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// The permanent "screens" slot of the <see cref="SusApp"/> scaffold — the single, fixed
    /// mount target for application content. Everything the app shows lives here:
    ///   - <c>SusApp.Mount&lt;T&gt;()</c> adds the root component directly into this host;
    ///   - the router adds its <c>SusRouteView</c> (a <c>SusScreenOutlet</c>) here.
    ///
    /// It is the MIDDLE layer of the guaranteed three-layer stack that <see cref="SusApp"/>
    /// always builds on the UIDocument root, in fixed z-order:
    /// <code>
    /// root
    /// ├── WorldMarkerLayer   ← lowest  (world markers, below screens)
    /// ├── ScreenHost         ← middle  (this: app content / screens)
    /// └── OverlayHost        ← topmost (popups, modals, tooltips, toasts, console)
    /// </code>
    ///
    /// Unlike the sibling layers (<see cref="WorldMarkerLayer"/> / <see cref="OverlayHost"/>,
    /// both absolute full-fill and out of flow), ScreenHost is an in-flow <c>flex-grow: 1</c>
    /// element so it fills the root while the absolute layers overlay it.
    ///
    /// A concrete <see cref="SusLayer"/> alongside <see cref="OverlayHost"/> and
    /// <c>SusScreenOutlet&lt;TScreen&gt;</c>. See <see cref="SusBootstrap.GetOrCreateScreenHost"/>.
    /// </summary>
    public sealed class ScreenHost : SusLayer
    {
        /// <summary>Element name used to find/deduplicate the singleton host per root.</summary>
        public const string ScreenHostName = "__SusScreenHost__";

        /// <summary>USS class for the screens slot (see SusRuntime/_global.uss).</summary>
        public const string UssClassName = "sus-screen-host";

        public ScreenHost()
        {
            name = ScreenHostName;
            AddToClassList(UssClassName);
            // In-flow: fill the flex root while the absolute marker/overlay layers overlay it.
            style.flexGrow = 1f;
            // Relative so router's absolute-fill SusRouteView anchors to this host.
            style.position = Position.Relative;
        }
    }
}
