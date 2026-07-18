using System;

namespace Sharq.Core
{
    /// <summary>
    /// Computed (derived) property that caches its value and recomputes lazily when invalidated.
    /// Automatically subscribes to dependency Prop&lt;T&gt;.Changed events for auto-invalidation.
    ///
    /// **Thread safety:** main-thread only. Dependency tracking via <see cref="DependencyTracker"/>
    /// and the <c>_dirty</c> flag are not synchronized across threads.
    /// </summary>
    public class Computed<T> : IReactiveSource
    {
        private T _cached;
        private bool _dirty = true;
        private readonly Func<T> _fn;
        private event Action _invalidated;
        private readonly System.Collections.Generic.List<IDisposable> _subscriptions
            = new System.Collections.Generic.List<IDisposable>();

        public T Value
        {
            get
            {
                DependencyTracker.RegisterSource(this);
                if (_dirty)
                {
                    // Clean old subscriptions
                    foreach (var s in _subscriptions)
                        s.Dispose();
                    _subscriptions.Clear();

                    // Auto-track Prop<T> / Computed<T> dependencies
                    using (DependencyTracker.Track(source =>
                    {
                        _subscriptions.Add(source.SubscribeInvalidate(MarkDirty));
                    }))
                    {
                        _cached = _fn();
                    }
                    _dirty = false;
                }
                return _cached;
            }
        }

        public Computed(Func<T> fn)
        {
            _fn = fn ?? throw new ArgumentNullException(nameof(fn));
        }

        /// <summary>Mark as dirty and notify subscribers. Next Value access will recompute.</summary>
        public void Invalidate()
        {
            MarkDirty();
        }

        /// <summary>Force immediate recomputation.</summary>
        public void Refresh()
        {
            foreach (var s in _subscriptions)
                s.Dispose();
            _subscriptions.Clear();

            using (DependencyTracker.Track(source =>
            {
                _subscriptions.Add(source.SubscribeInvalidate(MarkDirty));
            }))
            {
                _cached = _fn();
            }
            _dirty = false;
        }

        /// <summary>
        /// Marks dirty and fires _invalidated event, but only on false→true transition
        /// to avoid subscriber spam.
        /// </summary>
        internal void MarkDirty()
        {
            if (_dirty) return;
            _dirty = true;
            var inv = _invalidated;
            inv?.Invoke();
        }

        /// <inheritdoc/>
        IDisposable IReactiveSource.SubscribeInvalidate(Action onInvalidate)
        {
            _invalidated += onInvalidate;
            return new InvalidSubscriber(() => _invalidated -= onInvalidate);
        }

        private sealed class InvalidSubscriber : IDisposable
        {
            private Action _unsubscribe;
            public InvalidSubscriber(Action unsubscribe) => _unsubscribe = unsubscribe;
            public void Dispose()
            {
                _unsubscribe?.Invoke();
                _unsubscribe = null;
            }
        }

        public static implicit operator T(Computed<T> c) => c.Value;
    }
}
