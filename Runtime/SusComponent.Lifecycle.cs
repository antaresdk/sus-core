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

        /// <summary>Called every frame (~60 FPS) after Mounted().</summary>
        protected virtual void Updated() { }

        /// <summary>Called BEFORE detach from panel. Use to clean up subscriptions while DOM is still alive.</summary>
        protected virtual void BeforeUnmounted() { }

        /// <summary>Called AFTER detach from panel. Use for final cleanup.</summary>
        protected virtual void Unmounted() { }
    }
}
