using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Sharq.Core;
using Sharq.Core.Storybook;

// The first story-carrying assembly in the corpus (card T-3033, plan §4.1). It is the Editor TEST
// assembly on purpose: it costs the buyer nothing (it compiles only under UNITY_INCLUDE_TESTS and
// is Editor-only), yet it proves the whole discovery path — attribute on the assembly, attribute
// on the class, provider interface, grouping and the package stamp — with the real registry rather
// than with a mock of it.
[assembly: SusStoryAssembly(Package = "core", PackageId = "com.sharq-it.sus.core")]

namespace Sharq.Core.Editor.Tests
{
    // ── the three components the demo stories are about ─────────────────────
    // Each is built out of core primitives only (Prop<T>, WatchEffect, UseAllowed, OverlayHost) —
    // core has no shipping components of its own, and a story engine that could only show kit
    // widgets would not be an engine.

    /// <summary>Reactive counter: two props, one derived label, one event.</summary>
    public sealed class CoreCounterDemo : SusComponent
    {
        public Prop<string> Label = new("Clicks");
        public Prop<int> Count = new(0);
        public Action<int> OnCount;

        protected override void Build()
        {
            var label = new UnityEngine.UIElements.Label();
            label.AddToClassList("sus-demo-counter__label");
            Add(label);

            var button = new Button(() =>
            {
                Count.Value += 1;
                OnCount?.Invoke(Count.Value);
            })
            { text = "+1" };
            button.AddToClassList("sus-demo-counter__button");
            Add(button);

            BindText(label, () => Label.Value + ": " + Count.Value);
        }
    }

    /// <summary>Closed axis: a prop clamped by <c>UseAllowed</c>, the shape zone D reads.</summary>
    public sealed class CoreSwatchDemo : SusComponent
    {
        public Prop<string> Tone = new("primary");
        public Prop<string> Size = new("md");

        protected override void Build()
        {
            var box = new VisualElement();
            box.AddToClassList("sus-demo-swatch");
            Add(box);

            UseAllowed(Tone, new[] { "primary", "secondary", "success", "warning", "error" },
                "primary", propName: "CoreSwatchDemo.Tone");
            UseAllowed(Size, new[] { "sm", "md", "lg" }, "md", propName: "CoreSwatchDemo.Size");

            WatchEffect(() =>
            {
                box.ClearClassList();
                box.AddToClassList("sus-demo-swatch");
                box.AddToClassList("sus-demo-swatch--" + Tone.Value);
                box.AddToClassList("sus-demo-swatch--" + Size.Value);
            });
        }
    }

    /// <summary>
    /// Overlay case: the component asks core for a host and drops a floating element into it.
    /// It exists so the stage's own <c>OverlayHost</c> (T-3032) has something to catch.
    /// </summary>
    public sealed class CoreOverlayDemo : SusComponent
    {
        public Prop<bool> Open = new(false);
        public Prop<string> Text = new("floating");

        VisualElement _floating;

        protected override void Build()
        {
            var toggle = new Button(() => Open.Value = !Open.Value) { text = "toggle overlay" };
            toggle.AddToClassList("sus-demo-overlay__toggle");
            Add(toggle);

            WatchEffect(() =>
            {
                if (Open.Value) ShowFloating();
                else HideFloating();
            });
        }

        void ShowFloating()
        {
            if (_floating != null) return;
            var host = SusBootstrap.ResolveOverlayHost(this);
            if (host == null) return;
            _floating = new UnityEngine.UIElements.Label(Text.Value);
            _floating.AddToClassList("sus-demo-overlay__panel");
            host.AddToOverlay(_floating, OverlayCategory.Dropdown);
        }

        void HideFloating()
        {
            if (_floating == null) return;
            _floating.RemoveFromHierarchy();
            _floating = null;
        }
    }

    // ── the three stories ───────────────────────────────────────────────────

    [SusStory("core/primitives/counter",
        Name = "Counter",
        Purpose = "reactive props end to end: Prop<T>, a derived label and one event")]
    public sealed class CoreCounterStory : ISusStory
    {
        public SusComponent Create() => new CoreCounterDemo();

        public void Configure(SusStoryContext ctx)
        {
            var c = (CoreCounterDemo)ctx.Component;
            c.Label.Value = ctx.Query("Label") ?? "Clicks";
            // OnCount has no control of its own: it is an EVENT, and events are wired by zone E.
            ctx.Exclude("OnCount", "event, wired by the probe (zone E, step 6)");
        }
    }

    [SusStory("core/primitives/swatch",
        Name = "Swatch",
        Purpose = "closed axes: props clamped by UseAllowed, rendered as segmented controls")]
    public sealed class CoreSwatchStory : ISusStory
    {
        public SusComponent Create() => new CoreSwatchDemo();

        public void Configure(SusStoryContext ctx)
        {
            var c = (CoreSwatchDemo)ctx.Component;
            var tone = ctx.Query("Tone");
            if (!string.IsNullOrEmpty(tone)) c.Tone.Value = tone;
        }
    }

    [SusStory("core/overlay/floating",
        Name = "Floating",
        Purpose = "overlay resolution: the popup must land in the stage canvas, not at the panel root")]
    public sealed class CoreOverlayStory : ISusStory
    {
        public SusComponent Create() => new CoreOverlayDemo();

        public void Configure(SusStoryContext ctx)
        {
            var c = (CoreOverlayDemo)ctx.Component;
            c.Open.Value = string.Equals(ctx.Query("Open"), "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// T-3131 regression fixture: opens its overlay from <see cref="AttachToPanelEvent"/>,
    /// exactly like the real <c>SusModal</c> does for "Model=true before Mounted" (its own
    /// comment: "Watch(Model) is not immediate and runs in Created — OpenOverlay needs parent
    /// (Mounted)"). That timing is what makes <see cref="SusBootstrap.ResolveOverlayHost"/>
    /// walk past a <see cref="SusStorybookHost"/> whose <c>_canvasOverlay</c> does not exist YET
    /// (Mount() adds the component to the canvas, and only THEN calls
    /// <c>GetOrCreateOverlay(_canvas)</c>) and fall back to <c>panel.visualTree</c> — the
    /// document root, an ANCESTOR of the host, not a descendant of it.
    /// </summary>
    public sealed class CoreRootLeakDemo : SusComponent
    {
        public const string MarkerClass = "sus-demo-root-leak__panel";

        protected override void Build()
        {
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var host = SusBootstrap.ResolveOverlayHost(this);
                if (host == null) return;
                var floating = new UnityEngine.UIElements.Label("leaked");
                floating.AddToClassList(MarkerClass);
                host.AddToOverlay(floating, OverlayCategory.Dropdown);
            });
        }
    }

    [SusStory("core/overlay/root-leak",
        Name = "RootLeak",
        Purpose = "T-3131 regression: proves SusStorybookHost.Unmount also clears an overlay " +
                  "that escaped to panel.visualTree, not only _canvasOverlay")]
    public sealed class CoreRootLeakStory : ISusStory
    {
        public SusComponent Create() => new CoreRootLeakDemo();

        public void Configure(SusStoryContext ctx)
        {
        }
    }

    // ── one story born from DATA, to prove the provider path ────────────────

    /// <summary>
    /// Proof that <see cref="ISusStoryProvider"/> works: the same component, one story per tone,
    /// generated from a list instead of from a class each. This is the shape skin presets and HUD
    /// compositions will use (plan §4.1).
    /// </summary>
    public sealed class CoreSwatchPresetProvider : ISusStoryProvider
    {
        public IEnumerable<SusStoryDefinition> Enumerate()
        {
            yield return new SusStoryDefinition
            {
                Id = "core/primitives/swatch-error",
                Name = "Swatch (error)",
                Purpose = "data-born story: the same component pinned to one tone",
                Order = 10,
                Create = () => new CoreSwatchDemo(),
                Configure = ctx => ((CoreSwatchDemo)ctx.Component).Tone.Value = "error",
            };
        }
    }
}
