using System;

namespace Sharq.Core
{
    /// <summary>
    /// Thread-isolated dependency tracker (one tracker per thread via [ThreadStatic]).
    /// Auto-wires Computed&lt;T&gt; to Prop&lt;T&gt; within a single thread context.
    ///
    /// Usage inside Computed&lt;T&gt;.Value getter:
    /// <code>
    /// using (DependencyTracker.Track(source => _subs.Add(source.SubscribeInvalidate(() => Invalidate()))))
    ///     _cached = _fn();
    /// </code>
    ///
    /// Usage inside Prop&lt;T&gt;.Value getter:
    /// <code>
    /// DependencyTracker.RegisterSource(this);
    /// </code>
    /// </summary>
    internal static class DependencyTracker
    {
        [ThreadStatic]
        private static Action<IReactiveSource> _collector;

        /// <summary>
        /// Begin tracking. While active, every Prop&lt;T&gt;.Value access
        /// calls collector(propInstance). Returns a scope that restores state on Dispose.
        /// </summary>
        public static IDisposable Track(Action<IReactiveSource> collector)
        {
            var prev = _collector;
            _collector = collector;
            return new Scope(() => _collector = prev);
        }

        /// <summary>
        /// Called by Prop&lt;T&gt;.Value getter. If tracking is active,
        /// registers the source with the collector.
        /// </summary>
        public static void RegisterSource(IReactiveSource source)
        {
            _collector?.Invoke(source);
        }

        /// <summary>
        /// Run fn without registering dependencies.
        /// All Prop.Value / Computed.Value accesses inside fn do NOT become subscriptions
        /// of the current Computed/Effect. Analog of Angular untracked() / SolidJS untrack().
        /// <code>
        /// Computed&lt;string&gt; info = C(() => {
        ///     var theme = DependencyTracker.Untracked(() => themeProp.Value);
        ///     return $"{selected.Value} (theme: {theme})"; // theme does not trigger recomputation
        /// });
        /// </code>
        /// </summary>
        public static T Untracked<T>(Func<T> fn)
        {
            var prev = _collector;
            _collector = null;
            try { return fn(); }
            finally { _collector = prev; }
        }

        /// <summary>Overload for void actions.</summary>
        public static void Untracked(Action action)
        {
            Untracked<object>(() => { action(); return null; });
        }

        private sealed class Scope : IDisposable
        {
            private readonly Action _dispose;
            public Scope(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }
    }
}
