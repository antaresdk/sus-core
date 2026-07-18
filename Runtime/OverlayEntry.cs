using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// A single entry in the overlay stack. Tracks element, category,
    /// click-outside-to-dismiss flag, and optional dismiss callback.
    /// </summary>
    public class OverlayEntry
    {
        public VisualElement Element;
        public OverlayCategory Category;
        public bool DismissOnClickOutside;
        public System.Action OnDismiss;

        public OverlayEntry(VisualElement element, OverlayCategory category,
            bool dismissOnClickOutside = false, System.Action onDismiss = null)
        {
            Element = element;
            Category = category;
            DismissOnClickOutside = dismissOnClickOutside;
            OnDismiss = onDismiss;
        }
    }
}
