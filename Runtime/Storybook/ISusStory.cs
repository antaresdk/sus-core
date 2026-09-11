using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// One story: how to build the live component the stage shows, and how to dress it
    /// (plan §4.1). Implementations carry <see cref="SusStoryAttribute"/>.
    ///
    /// <see cref="Create"/> returns a LIVE instance — the stage mounts it, the control panel
    /// (step 4) drives its props through <c>DescribeProps()</c>, and the matrix (step 5) builds
    /// more instances from the same factory. It must therefore be callable more than once.
    /// </summary>
    public interface ISusStory
    {
        /// <summary>Builds one fresh instance of the component this story is about.</summary>
        SusComponent Create();

        /// <summary>
        /// Fills the instance in with presets, items and exclusions. Called right after
        /// <see cref="Create"/> and before the instance is mounted.
        /// </summary>
        void Configure(SusStoryContext ctx);
    }

    /// <summary>
    /// A source of stories that are not one-class-per-story: skin presets, HUD compositions,
    /// anything generated FROM DATA (plan §4.1). The provider itself is discovered exactly like
    /// a story class — by type, in an assembly that carries <see cref="SusStoryAssemblyAttribute"/>.
    /// </summary>
    public interface ISusStoryProvider
    {
        /// <summary>Every story this provider knows about, in the order it wants them shown.</summary>
        IEnumerable<SusStoryDefinition> Enumerate();
    }

    /// <summary>
    /// A story described by DATA rather than by a class + attribute — what
    /// <see cref="ISusStoryProvider"/> yields.
    /// </summary>
    public sealed class SusStoryDefinition
    {
        /// <summary>Address: <c>&lt;package&gt;/&lt;group&gt;/&lt;slug&gt;</c>.</summary>
        public string Id { get; set; }

        /// <summary>Human name shown in zone A; defaults to the slug.</summary>
        public string Name { get; set; }

        /// <summary>One line of "what this is for".</summary>
        public string Purpose { get; set; }

        /// <summary>
        /// The catalogue component this story is the story OF — see
        /// <see cref="SusStoryAttribute.Component"/>. Data-born stories declare the link exactly
        /// like class-born ones: a provider that generates one story per skin preset knows the
        /// component it presets better than any blurb-parsing heuristic does.
        /// </summary>
        public Type Component { get; set; }

        /// <summary>
        /// Why this definition names no <see cref="Component"/> — see
        /// <see cref="SusStoryAttribute.NoComponent"/>.
        /// </summary>
        public string NoComponent { get; set; }

        /// <summary>Sort key inside its group.</summary>
        public int Order { get; set; }

        /// <summary>
        /// Prop carrying the closed axis of variants — see <see cref="SusStoryAttribute.Axis"/>.
        /// </summary>
        public string Axis { get; set; }

        /// <summary>
        /// Legal values of <see cref="Axis"/> — see <see cref="SusStoryAttribute.AxisValues"/>.
        /// Data-born stories declare the axis exactly like class-born ones: a provider that
        /// generates one story per preset knows the presets it generated.
        /// </summary>
        public string[] AxisValues { get; set; }

        /// <summary>Build cost of one instance (card T-3038) — see <see cref="SusStoryAttribute.Weight"/>.</summary>
        public SusStoryWeight Weight { get; set; } = SusStoryWeight.Normal;

        /// <summary>Builds one live instance. Required.</summary>
        public Func<SusComponent> Create { get; set; }

        /// <summary>Dresses the instance. Optional.</summary>
        public Action<SusStoryContext> Configure { get; set; }
    }

    /// <summary>
    /// What a story is handed while it is being configured: its own instance, its registry
    /// entry, the route that asked for it (so a story can honour query values), and the ledger
    /// of props it deliberately leaves without a control.
    /// </summary>
    public sealed class SusStoryContext
    {
        readonly Dictionary<string, string> _exclusions = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> _manual = new(StringComparer.Ordinal);   // card T-3034
        readonly List<SusStorySceneElement> _scene = new();                          // card T-3168

        public SusStoryContext(SusStoryEntry entry, SusComponent component, Nav.SusStoryRoute route)
        {
            Entry = entry;
            Component = component;
            Route = route;
        }

        /// <summary>The registry entry this story came from.</summary>
        public SusStoryEntry Entry { get; }

        /// <summary>The live instance just built by <see cref="ISusStory.Create"/>.</summary>
        public SusComponent Component { get; }

        /// <summary>
        /// The route being applied, or null when the story is built outside navigation (matrix
        /// cells, screenshots). Query values are the deep link's <c>?Prop=val</c> pairs.
        /// </summary>
        public Nav.SusStoryRoute Route { get; }

        /// <summary>
        /// Props this story deliberately leaves WITHOUT a control, each with a reason. The
        /// coverage number of DoD §7 p. 1 is <c>props == controls + exclusions</c>, so an
        /// exclusion without a reason is a hole, not a decision — hence the required argument.
        /// </summary>
        public IReadOnlyDictionary<string, string> Exclusions => _exclusions;

        /// <summary>Records one prop as intentionally uncontrolled.</summary>
        public void Exclude(string propName, string reason)
        {
            if (string.IsNullOrEmpty(propName)) throw new ArgumentException("prop name", nameof(propName));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("reason", nameof(reason));
            _exclusions[propName] = reason;
        }

        /// <summary>
        /// Props this story drives with a HAND-WRITTEN control instead of the generated one:
        /// name to reason (reason optional). The generated panel still shows the prop, marked
        /// "manual", so a reader can tell "a person wired this" from "the machine derived this"
        /// (card T-3034, mock-up "Controls Panel").
        /// </summary>
        public IReadOnlyDictionary<string, string> ManualControls => _manual;

        /// <summary>Records one prop as driven by a control the story wrote itself.</summary>
        public void AddManualControl(string propName, string reason = null)
        {
            if (string.IsNullOrEmpty(propName)) throw new ArgumentException("prop name", nameof(propName));
            _manual[propName] = reason ?? string.Empty;
        }

        /// <summary>
        /// Everything this story asked the stage to put NEXT TO the component, in the order it
        /// asked (card T-3168). The engine parents these; the story never does.
        /// </summary>
        public IReadOnlyList<SusStorySceneElement> Scene => _scene;

        /// <summary>
        /// Adds one element BESIDE the component — a trigger button, a demo stage, a caption:
        /// the scenery a story needs on the canvas but that does not belong INSIDE the component
        /// under test.
        ///
        /// Why this exists (card T-3168): a story has no parent to insert into while
        /// <see cref="ISusStory.Configure"/> runs, so six kit stories used to wait for
        /// <c>AttachToPanelEvent</c> and then insert into <c>Component.parent</c>. That event
        /// fires on EVERY attach, and a self-teleporting component (every modal, every tour)
        /// attaches a second time INSIDE an <see cref="OverlayHost"/> when it opens — so the
        /// scenery was inserted again, this time as an untracked child of the overlay host, where
        /// no teardown could see it. Two of the seven contaminated kit frames of 2026-09-09 were
        /// four copies of one "Open modal" trigger; the rest were another story's tutorial stage
        /// surviving several story switches (showcase-3).
        ///
        /// Declaring the scenery instead of parenting it makes the engine the only thing that
        /// touches the hierarchy: it inserts once, next to the mounted instance, and
        /// <c>SusStorybookHost.Unmount</c> removes exactly what it inserted.
        /// </summary>
        /// <param name="element">The scenery element. Ignored when null.</param>
        /// <param name="after">
        /// False (default) puts it BEFORE the component — the usual place for a trigger; true puts
        /// it after, for a caption or a second demo row.
        /// </param>
        public void AddSibling(VisualElement element, bool after = false)
        {
            if (element == null) return;
            _scene.Add(new SusStorySceneElement(element, after));
        }

        /// <summary>Value of one deep-link query key, or null.</summary>
        public string Query(string key)
        {
            if (Route == null || key == null) return null;
            return Route.Query.TryGetValue(key, out var v) ? v : null;
        }
    }

    /// <summary>
    /// One piece of scenery a story declared through <see cref="SusStoryContext.AddSibling"/>
    /// (card T-3168): the element, and which side of the component it goes on.
    /// </summary>
    public sealed class SusStorySceneElement
    {
        public SusStorySceneElement(VisualElement element, bool after)
        {
            Element = element;
            After = after;
        }

        /// <summary>The scenery element itself.</summary>
        public VisualElement Element { get; }

        /// <summary>True when it belongs after the component rather than before it.</summary>
        public bool After { get; }
    }
}
