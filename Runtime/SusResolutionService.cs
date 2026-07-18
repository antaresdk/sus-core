using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Manages responsive resolution breakpoints via CSS classes.
    /// Adds "resolution-low" or "resolution-high" class on root.
    /// USS can then adjust font sizes, spacing, etc.
    ///
    /// Usage:
    /// <code>
    /// SusResolutionService.Instance.Update(root, logicalWidth);
    /// </code>
    /// </summary>
    public class SusResolutionService
    {
        public static readonly SusResolutionService Instance = new();

        public const float ThresholdHigh = 1600f;

        private SusResolutionService() { }

        public void Update(VisualElement root, float logicalWidth)
        {
            if (root == null) return;

            if (logicalWidth >= ThresholdHigh)
            {
                root.RemoveFromClassList("resolution-low");
                root.AddToClassList("resolution-high");
            }
            else
            {
                root.RemoveFromClassList("resolution-high");
                root.AddToClassList("resolution-low");
            }
        }
    }
}
