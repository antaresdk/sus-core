using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Global scale service — applies a uniform scale factor to the root
    /// VisualElement via style.scale (transform). Useful for accessibility,
    /// 4K monitors, and couch-gaming.
    ///
    /// For component-level fine-grained control, CSS variable --sk-scale
    /// (default 1 in a downstream token sheet) can be used with calc():
    ///   width: calc(40px * var(--sk-scale));
    ///
    /// Usage:
    /// <code>
    /// // One-shot apply
    /// SusScaleService.Instance.SetScale(root, 1.25f);
    ///
    /// // React to scale changes
    /// Watch(SusScaleService.Current, (_, next) => AdaptTo(next));
    /// </code>
    /// </summary>
    public class SusScaleService
    {
        private static SusScaleService s_instance;

        /// <summary>Singleton instance.</summary>
        public static SusScaleService Instance => s_instance ??= new SusScaleService();

        /// <summary>Current scale factor (1.0 = 100%).</summary>
        public static Prop<float> Current { get; } = new Prop<float>(1f);

        /// <summary>Minimum allowed scale.</summary>
        public static Prop<float> Min { get; } = new Prop<float>(0.75f);

        /// <summary>Maximum allowed scale.</summary>
        public static Prop<float> Max { get; } = new Prop<float>(1.5f);

        private SusScaleService() { }

        /// <summary>
        /// Applies a uniform scale transform to the root element.
        /// Scale is clamped to [Min, Max]. Idempotent.
        /// </summary>
        public void SetScale(VisualElement root, float scale)
        {
            if (root == null) return;

            var clamped = Mathf.Clamp(scale, Min.Value, Max.Value);
            Current.Value = clamped;
            root.style.scale = new Scale(new Vector3(clamped, clamped, 1f));
        }

        /// <summary>
        /// Restores the current scale on a freshly-created root element
        /// (e.g. after scene reload). Called from SusBootstrap.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (root == null) return;

            var s = Current.Value;
            if (!Mathf.Approximately(s, 1f))
            {
                root.style.scale = new Scale(new Vector3(s, s, 1f));
            }
        }
    }
}
