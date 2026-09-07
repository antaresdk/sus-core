using System.Collections.Generic;

namespace Sharq.Core.Storybook.Env
{
    /// <summary>
    /// Bottom-up registration point for environment axes core cannot own (plan §4.4, card T-3036):
    /// kit registers <c>skin</c> over <c>SusSkin</c>, a downstream skin package registers its own
    /// surfaces on top of that, a future locale package registers <c>locale</c>. Core never
    /// references kit — the dependency runs the other way, exactly like <c>Samples~/Storybook</c>
    /// already references <c>Runtime</c> (plan §0.1) — so this registry is the only channel by
    /// which such an axis reaches zone B.
    ///
    /// No provider registered for an id means the chip is absent, not empty: <c>TryGet</c>
    /// returning false is the caller's cue to skip the slot entirely (plan D9-style rule
    /// "провайдеров нет — оси нет").
    /// </summary>
    public static class SusStoryEnvAxisRegistry
    {
        static readonly Dictionary<string, ISusStoryEnvAxis> s_axes = new(System.StringComparer.Ordinal);

        /// <summary>Raised after a <see cref="Register"/> or <see cref="Unregister"/> call actually changed something.</summary>
        public static event System.Action Changed;

        /// <summary>Registers (or replaces) the axis under its own <see cref="ISusStoryEnvAxis.Id"/>.</summary>
        public static void Register(ISusStoryEnvAxis axis)
        {
            if (axis == null || string.IsNullOrEmpty(axis.Id)) return;
            s_axes[axis.Id] = axis;
            Changed?.Invoke();
        }

        /// <summary>Removes the axis registered under <paramref name="id"/>, if any.</summary>
        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (s_axes.Remove(id))
                Changed?.Invoke();
        }

        /// <summary>Looks up a registered axis by id.</summary>
        public static bool TryGet(string id, out ISusStoryEnvAxis axis)
        {
            if (string.IsNullOrEmpty(id))
            {
                axis = null;
                return false;
            }
            return s_axes.TryGetValue(id, out axis);
        }

        /// <summary>Every registered id, for diagnostics/tests. Order is not meaningful.</summary>
        public static IEnumerable<string> RegisteredIds => s_axes.Keys;

#if UNITY_EDITOR
        // With Domain Reload disabled a provider registered by a previous Play session (or a
        // previous EditMode test) would otherwise keep showing a chip for a service instance that
        // no longer exists.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_axes.Clear();
            Changed = null;
        }
#endif
    }
}
