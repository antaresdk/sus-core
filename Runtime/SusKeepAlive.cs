using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Wraps a VisualElement so it can be hidden (display:none) instead of removed from DOM,
    /// then reactivated later without reconstruction. Analogous to &lt;KeepAlive&gt; in Vue.
    ///
    /// Used by SusRouteView for keepAlive: true route configs — screens stay in DOM with
    /// their full state (Prop&lt;T&gt;, event subscriptions, internal data) preserved.
    ///
    /// Usage:
    /// <code>
    /// var ka = SusKeepAlive.Wrap(myScreen);
    /// root.Add(ka);
    /// // ... later ...
    /// ka.Active = false;  // hide
    /// ka.Active = true;   // show (preserves state)
    /// </code>
    /// </summary>
    public class SusKeepAlive : VisualElement
    {
        private readonly VisualElement _content;

        /// <summary>
        /// Inner container that holds the wrapped element.
        /// </summary>
        public VisualElement Content => _content;

        /// <summary>
        /// Toggle visibility: false = display:none (hidden but in DOM), true = visible.
        /// </summary>
        public bool Active
        {
            get => _content.style.display != DisplayStyle.None;
            set => _content.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private SusKeepAlive()
        {
            _content = new VisualElement();
            Add(_content);
        }

        /// <summary>
        /// Wraps an element in a SusKeepAlive. The element becomes a child of Content.
        /// If already parented elsewhere, it is removed from its previous hierarchy first.
        /// </summary>
        public static SusKeepAlive Wrap(VisualElement element)
        {
            if (element == null) return null;

            var keepAlive = new SusKeepAlive();

            // Remove from previous parent if any
            if (element.parent != null)
                element.RemoveFromHierarchy();

            keepAlive._content.Add(element);
            return keepAlive;
        }
    }
}
