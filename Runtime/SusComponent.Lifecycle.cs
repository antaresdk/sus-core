namespace Sharq.Core
{
    public abstract partial class SusComponent
    {
        // ─── Lifecycle hooks (override in derived components) ─────────────

        /// <summary>Called once during constructor, before Build(). Use for field/state init.</summary>
        protected virtual void Created() { }

        /// <summary>Called after Created(), before Build() + LoadCompanionStyleSheets(). Use for pre-build setup.</summary>
        protected virtual void BeforeMounted() { }

        /// <summary>
        /// Called ONCE per instance, after Build(), deferred to next frame. Use for post-build
        /// wiring, Prop watches. NOT called again on a later re-attach (see
        /// <see cref="Attached"/> for that) — a listener set up here and torn down in
        /// <see cref="Unmounted"/> goes silent for good after the first detach that happens
        /// after this has run (card T-3998). Register on the component's own/child elements in
        /// <see cref="Created"/> instead (never torn down, so nothing to re-arm); reserve
        /// <see cref="Attached"/>/<see cref="Detached"/> for subscriptions to an EXTERNAL
        /// source that outlives this component.
        /// </summary>
        protected virtual void Mounted() { }

        /// <summary>
        /// True once <see cref="Mounted"/> has run. Until then the component is only CONSTRUCTED:
        /// every <c>Watch</c>/read that lives in <c>Mounted()</c> has not happened yet, so
        /// <c>SusPropInfo.Dead</c> ("nobody reads this prop") is not yet an answer — it is the
        /// question. Anything that judges a component from outside must wait for this
        /// (<see cref="MountCompleted"/>); the storybook control panel does, since T-3096.
        /// </summary>
        public bool IsMounted { get; private set; }

        /// <summary>
        /// Raised once, right after <see cref="Mounted"/> has run; handlers added afterwards are
        /// never called, so read <see cref="IsMounted"/> first and act immediately when it is true.
        /// </summary>
        public event System.Action MountCompleted;

        /// <summary>Called every frame (~60 FPS) after Mounted().</summary>
        protected virtual void Updated() { }

        /// <summary>
        /// Called on EVERY attach to a panel, including the first one — right after
        /// <c>FlushPendingBindUpdatesOnAttach</c>, same frame, no defer (unlike
        /// <see cref="Mounted"/>). Symmetric with <see cref="Detached"/>: for an instance that
        /// attaches N times, this runs N times and <see cref="Detached"/> runs N-or-(N-1) times
        /// (one fewer if the instance is still attached when it is destroyed). Use it to
        /// subscribe to a source that lives OUTSIDE this component (a model, a service, a
        /// sibling) — a subscription made here and dropped in <see cref="Detached"/> survives a
        /// teleport/relocate/reopen cycle that would otherwise leave <see cref="Mounted"/>'s
        /// one-shot subscriptions dead (card T-3998).
        /// </summary>
        protected virtual void Attached() { }

        /// <summary>Called BEFORE detach from panel. Use to clean up subscriptions while DOM is still alive.</summary>
        protected virtual void BeforeUnmounted() { }

        /// <summary>
        /// Called AFTER detach from panel. Use for final cleanup. UNLIKE <see cref="Attached"/>/
        /// <see cref="Detached"/>, which are symmetric, this and <see cref="Mounted"/> are NOT:
        /// <see cref="Mounted"/> fires once per instance, this fires on EVERY detach — including
        /// ones after the first, and even a detach that never had a matching <see cref="Mounted"/>
        /// (destroyed the same frame it was created). Do not assume 1:1 with <see cref="Mounted"/>.
        /// </summary>
        protected virtual void Unmounted() { }

        /// <summary>
        /// Called on EVERY detach from a panel, right before <see cref="Unmounted"/>, with no
        /// <c>IsRelocating</c> exemption (it runs even for a same-panel teleport, e.g. an overlay
        /// self-mount). See <see cref="Attached"/> for the pairing and when to use it instead of
        /// <see cref="Mounted"/>/<see cref="Unmounted"/>.
        /// </summary>
        protected virtual void Detached() { }
    }
}
