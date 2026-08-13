using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Global scale service — **manual** accessibility zoom via root.style.scale.
    /// Not used for screen-size adaptation (that is SusBreakpointService only).
    /// Default remains 1.0; call SetScale only when the product explicitly offers zoom.
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

#if UNITY_EDITOR
        // With Domain Reload disabled the singleton and the static Props survive leaving Play
        // Mode: Current would keep the zoom level from the previous session instead of resetting
        // to 1.0 (a fresh root always renders unscaled), and all three Props would accumulate
        // Watch() handlers closing over elements of a destroyed panel.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            Current.ClearSubscribers();
            Current.Value = 1f;
            Min.ClearSubscribers();
            Max.ClearSubscribers();
        }
#endif

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
