namespace Sharq.Core
{
    public abstract partial class SusComponent
    {
        // ─── Lifecycle hooks (override in derived components) ─────────────

        /// <summary>Called once during constructor, before Build(). Use for field/state init.</summary>
        protected virtual void Created() { }

        /// <summary>Called after Created(), before Build() + LoadCompanionStyleSheets(). Use for pre-build setup.</summary>
        protected virtual void BeforeMounted() { }

        /// <summary>Called after Build(), deferred to next frame. Use for post-build wiring, Prop watches.</summary>
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

        /// <summary>Called BEFORE detach from panel. Use to clean up subscriptions while DOM is still alive.</summary>
        protected virtual void BeforeUnmounted() { }

        /// <summary>Called AFTER detach from panel. Use for final cleanup.</summary>
        protected virtual void Unmounted() { }
    }
}
