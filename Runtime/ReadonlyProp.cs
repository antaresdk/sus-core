using System;

namespace Sharq.Core
{
    /// <summary>
    /// Read-only wrapper around Prop&lt;T&gt;. Provides Value getter and Change subscription,
    /// but no setter — preventing accidental mutation by consumers.
    /// Analogous to Vue readonly(ref) / Angular signal.asReadonly().
    ///
    /// <code>
    /// // Component exposes:
    /// private Prop&lt;int&gt; _count = new(0);
    /// public ReadonlyProp&lt;int&gt; Count => _count.AsReadonly();
    ///
    /// // Consumer:
    /// int c = component.Count;            // ✅ implicit operator
    /// component.Count.Changed += (o,n) => {}; // ✅ subscription
    /// component.Count.Value = 5;          // ❌ compile error
    /// </code>
    /// </summary>
    public class ReadonlyProp<T> : IReactiveSource
    {
        private readonly Prop<T> _source;

        /// <summary>Read the current value (no subscription in non-tracked context).</summary>
        public T Value => _source.Value;

        /// <summary>Subscribe to value changes. Args: (oldValue, newValue).</summary>
        public event Action<T, T> Changed
        {
            add => _source.Changed += value;
            remove => _source.Changed -= value;
        }

        internal ReadonlyProp(Prop<T> source) => _source = source;

        /// <summary>Implicit conversion for ergonomic usage.</summary>
        public static implicit operator T(ReadonlyProp<T> p) => p.Value;

        /// <summary>Create a selector from a readonly source.</summary>
        public ReadonlyProp<R> Select<R>(Func<T, R> selector)
            => _source.Select(selector).AsReadonly();

        /// <summary>Used by DependencyTracker for auto-tracking.</summary>
        IDisposable IReactiveSource.SubscribeInvalidate(Action onInvalidate)
            => ((IReactiveSource)_source).SubscribeInvalidate(onInvalidate);
    }
}
