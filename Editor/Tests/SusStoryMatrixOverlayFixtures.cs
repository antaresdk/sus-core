using UnityEngine.UIElements;
using Sharq.Core;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-3160 regression fixture: the shape of a real modal story — a self-teleporting
    /// <see cref="SusModalBase"/> that is ALREADY open when it mounts, because the story's
    /// <c>Configure()</c> set its open prop (that is literally what <c>kit/molecules/modal</c>
    /// does with <c>Model = true</c>).
    ///
    /// Two things about that shape make it the fixture zone C needs, and neither is reproduced by
    /// <see cref="CoreRootLeakDemo"/> (T-3131), which drops a plain Label into a host:
    /// <list type="number">
    /// <item>it declares a closed axis, so <see cref="Sharq.Core.Storybook.UI.SusStoryMatrix"/>
    /// builds a grid of cells — and every cell is another instance that opens itself;</item>
    /// <item>it leaves the overlay through <c>SusOverlayComponent.UnmountSelfFromOverlay</c>,
    /// whose answer to a host-initiated <c>ClearAll()</c> is a SCHEDULED restore into the
    /// original parent — the frame-wide part of the leak.</item>
    /// </list>
    /// </summary>
    public sealed class CoreMatrixModalDemo : SusModalBase
    {
        /// <summary>Class every instance of this demo carries, so a test can count them anywhere.</summary>
        public const string MarkerClass = "sus-demo-matrix-modal";

        /// <summary>Open flag, named like the real modal's so the story reads the same.</summary>
        public Prop<bool> Model = new(false);

        /// <summary>The closed axis the matrix turns into rows.</summary>
        public Prop<string> Variant = new("primary");

        protected override void Build()
        {
            AddToClassList(MarkerClass);
            UseAllowed(Variant, new[] { "primary", "danger" }, "primary",
                propName: "CoreMatrixModalDemo.Variant");

            var label = new Label();
            label.AddToClassList("sus-demo-matrix-modal__text");
            Add(label);
            BindText(label, () => "dialog " + Variant.Value);
        }

        // Same timing as SusModal: Watch() is not immediate, so an instance that was configured
        // open before it had a parent opens at mount time — when the nearest OverlayHost is
        // whatever the tree happens to offer.
        protected override void Mounted()
        {
            if (Model.Value) OpenInOverlay(dismissOnClickOutside: false, onDismiss: null);
        }

        protected override void Unmounted()
        {
            if (IsRelocatingToOverlay) return;
            CloseFromOverlay();
        }
    }

    [SusStory("enginetests/overlay/matrix-modal",
        Name = "Matrix modal",
        Component = typeof(CoreMatrixModalDemo),
        Purpose = "T-3160 regression: a story that is open at t=0 and has an axis, so zone C's " +
                  "matrix builds cells that open themselves too")]
    public sealed class CoreMatrixModalStory : ISusStory
    {
        public SusComponent Create() => new CoreMatrixModalDemo();

        public void Configure(SusStoryContext ctx)
        {
            ((CoreMatrixModalDemo)ctx.Component).Model.Value = true;
        }
    }
}
