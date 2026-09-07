using System;
using System.Reflection;
using Sharq.Core.Storybook.Nav;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// One story as the registry holds it: parsed address, display text, and the factory that
    /// builds live instances. Immutable after discovery except for the cached prop count.
    /// </summary>
    public sealed class SusStoryEntry
    {
        readonly Func<SusComponent> _create;
        readonly Action<SusStoryContext> _configure;
        int _propCount = -1;

        internal SusStoryEntry(
            string id,
            string package,
            string group,
            string slug,
            string name,
            string purpose,
            int order,
            Type declaringType,
            Assembly assembly,
            Func<SusComponent> create,
            Action<SusStoryContext> configure)
        {
            Id = id;
            Package = package;
            Group = group;
            Slug = slug;
            Name = name;
            Purpose = purpose ?? string.Empty;
            Order = order;
            DeclaringType = declaringType;
            Assembly = assembly;
            _create = create;
            _configure = configure;
        }

        /// <summary>Full address: <c>kit/atoms/select</c>.</summary>
        public string Id { get; }

        /// <summary>First segment — the package tab this story lives under.</summary>
        public string Package { get; }

        /// <summary>Second segment — the group header in zone A.</summary>
        public string Group { get; }

        /// <summary>Last segment — kept stable so links in docs and frames survive (§6 step 8).</summary>
        public string Slug { get; }

        /// <summary>Display name (<c>Select</c>).</summary>
        public string Name { get; }

        /// <summary>One line of "what this is for", or empty.</summary>
        public string Purpose { get; }

        /// <summary>Sort key inside the group.</summary>
        public int Order { get; }

        /// <summary>
        /// Build cost declared by the story (<see cref="SusStoryAttribute.Weight"/>, card T-3038). Set by the
        /// registry right after construction; <see cref="SusStoryWeight.Normal"/> otherwise.
        /// </summary>
        public SusStoryWeight Weight { get; internal set; } = SusStoryWeight.Normal;

        /// <summary>The <c>[SusStory]</c> class, or the provider type for data-born stories.</summary>
        public Type DeclaringType { get; }

        /// <summary>Assembly the story was discovered in — the package stamp comes from it.</summary>
        public Assembly Assembly { get; }

        /// <summary>Builds one fresh live instance of the component.</summary>
        public SusComponent Create() => _create();

        /// <summary>
        /// Builds an instance and runs the story's <c>Configure</c> against the given route.
        /// Returns the context so a caller can read the exclusion ledger.
        /// </summary>
        public SusStoryContext Instantiate(SusStoryRoute route, out SusComponent component)
        {
            component = _create();
            var ctx = new SusStoryContext(this, component, route);
            _configure?.Invoke(ctx);
            return ctx;
        }

        /// <summary>
        /// Number of props the component exposes — the figure printed at the right edge of the
        /// zone A row. Computed ONCE by building a throwaway instance and asking
        /// <c>DescribeProps()</c>; a story that cannot be built yields -1 and the row shows a dash
        /// instead of pretending the component has zero props.
        /// </summary>
        public int PropCount
        {
            get
            {
                if (_propCount >= 0) return _propCount;
                try
                {
                    var probe = _create();
                    _propCount = probe?.DescribeProps()?.Count ?? -1;
                }
                catch (Exception e)
                {
                    SusLog.Warn($"[storybook] story '{Id}' could not be built for its prop count: {e.Message}");
                    _propCount = -1;
                }
                return _propCount;
            }
        }

        public override string ToString() => Id + " (" + Name + ")";
    }
}
