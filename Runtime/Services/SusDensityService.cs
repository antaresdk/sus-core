using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Density preset for DownstreamLib components.
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
    /// CSS classes to the root VisualElement so all kit components inherit the
    /// density token overrides defined in downstream-tokens.uss.
    ///
    /// Usage:
    /// <code>
    /// // One-shot apply (typically from DownstreamLibThemeService)
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
