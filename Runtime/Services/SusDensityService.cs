using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Density preset for UI components that opt into density token classes.
    /// </summary>
    public enum SusDensity
    {
        /// <summary>Default spacing and sizing — no class applied.</summary>
        Default,
        /// <summary>Larger spacing, taller rows — .density-comfortable class.</summary>
        Comfortable,
        /// <summary>Tighter spacing, shorter rows — .density-compact class.</summary>
        Compact
    }

    /// <summary>
    /// Global density service — applies .density-compact / .density-comfortable
    /// CSS classes to the root VisualElement so components can inherit density
    /// token overrides from their own theme USS.
    ///
    /// Usage:
    /// <code>
    /// // One-shot apply (typically from a theme service)
    /// SusDensityService.Instance.SetDensity(root, SusDensity.Compact);
    ///
    /// // React to density changes
    /// Watch(SusDensityService.Current, (_, next) => AdaptToDensity(next));
    /// </code>
    /// </summary>
    public class SusDensityService
    {
        private static SusDensityService s_instance;

        /// <summary>Singleton instance.</summary>
        public static SusDensityService Instance => s_instance ??= new SusDensityService();

        /// <summary>Reactive density prop — components can Watch() it.</summary>
        public static Prop<SusDensity> Current { get; } = new Prop<SusDensity>(SusDensity.Default);

        private SusDensityService() { }

#if UNITY_EDITOR
        // With Domain Reload disabled the singleton and Current survive leaving Play Mode: the
        // density picked in the previous session would leak into the next instead of resetting to
        // Default, and Current would accumulate Watch() handlers closing over destroyed elements.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            Current.ClearSubscribers();
            Current.Value = SusDensity.Default;
        }
#endif

        /// <summary>
        /// Applies density CSS classes to the root and updates the reactive prop.
        /// Call once at startup; idempotent.
        /// </summary>
        public void SetDensity(VisualElement root, SusDensity density)
        {
            if (root == null) return;

            root.EnableInClassList("density-compact", density == SusDensity.Compact);
            root.EnableInClassList("density-comfortable", density == SusDensity.Comfortable);

            Current.Value = density;
        }

        /// <summary>
        /// Convenience: registers a density class on the given root without
        /// changing the reactive prop. Used inside SusBootstrap to restore
        /// the initial density from de/serialized state.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (root == null) return;

            var density = Current.Value;
            root.EnableInClassList("density-compact", density == SusDensity.Compact);
            root.EnableInClassList("density-comfortable", density == SusDensity.Comfortable);
        }
    }
}
