using System;

namespace Sharq.Core
{
    /// <summary>
    /// Handle returned by SusComponent.Watch&lt;T&gt;().
    /// Calling Dispose() unsubscribes the watcher from the Prop's Changed event.
    /// </summary>
    public class WatchHandle : IDisposable
    {
        private Action _unsubscribe;

        internal WatchHandle(Action unsubscribe)
        {
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        }

        /// <summary>Unsubscribes the watcher. Safe to call multiple times.</summary>
        public void Dispose()
        {
            var a = _unsubscribe;
            _unsubscribe = null;
            a?.Invoke();
        }
    }
}
