using System;

namespace Sharq.Core
{
    /// <summary>
    /// Non-generic interface for reactive sources (Prop&lt;T&gt;, etc.).
    /// Allows Computed&lt;T&gt; to auto-track dependencies without knowing T.
    /// </summary>
    public interface IReactiveSource
    {
        /// <summary>
        /// Subscribe to invalidation notifications. Returns a disposable handle;
        /// disposing it unsubscribes.
        /// </summary>
        IDisposable SubscribeInvalidate(Action onInvalidate);
    }
}
