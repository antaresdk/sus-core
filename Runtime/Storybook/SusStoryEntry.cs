using System;
using System.Collections.Generic;
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
        /// The catalogue component this story is the story OF, or null when the story declared it
        /// has no single one (plan §4.1a, D19). Set by the registry from
        /// <see cref="SusStoryAttribute.Component"/> after it checked the type really is a
        /// <see cref="SusComponent"/>: a link that does not point at a component is not a link.
        /// </summary>
        public Type ComponentType { get; internal set; }

        /// <summary>
        /// Why this story names no <see cref="ComponentType"/>, or empty. A story with neither is
        /// not "a story without a component" — it is a story that never said, and
        /// <see cref="DeclaresComponentLink"/> is how a caller tells the two apart.
        /// </summary>
        public string NoComponentReason { get; internal set; } = string.Empty;

        /// <summary>
        /// True when the story SAID something about its component link — either it named the
        /// component or it named the reason there is none.
        /// </summary>
        public bool DeclaresComponentLink =>
            ComponentType != null || !string.IsNullOrWhiteSpace(NoComponentReason);

        /// <summary>
        /// Build cost declared by the story (<see cref="SusStoryAttribute.Weight"/>, card T-3038). Set by the
        /// registry right after construction; <see cref="SusStoryWeight.Normal"/> otherwise.
        /// </summary>
        public SusStoryWeight Weight { get; internal set; } = SusStoryWeight.Normal;

        /// <summary>
        /// Prop the story declared its closed variant axis on
        /// (<see cref="SusStoryAttribute.Axis"/>, card T-3379). Always a name, never null:
        /// <see cref="SusStoryAxis.DefaultPropName"/> when the story named none.
        /// </summary>
        public string AxisProp { get; internal set; } = SusStoryAxis.DefaultPropName;

        /// <summary>
        /// Legal values of <see cref="AxisProp"/> as the STORY declared them, normalised by
        /// <see cref="SusStoryAxis.Normalize"/>; empty when the story declared none. Empty is not
        /// "no axis" on its own — the component may still clamp the prop itself, and
        /// <see cref="SusStoryAxis.Resolve"/> is what puts the two halves together.
        /// </summary>
        public IReadOnlyList<string> AxisValues { get; internal set; } = Array.Empty<string>();

        /// <summary>True when the STORY itself enumerated the axis.</summary>
        public bool DeclaresAxis => AxisValues.Count > 0;

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
