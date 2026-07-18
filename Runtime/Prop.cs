using System;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("com.sharq-it.sus.core.editor.tests")]

namespace Sharq.Core
{
    /// <summary>
    /// Reactive property with change notification and Unity UI Toolkit data binding support.
    /// </summary>
    public class Prop<T> : INotifyBindablePropertyChanged, IDataSourceViewHashProvider, IReactiveSource
    {
        private T _value;
        private readonly Func<T, T, bool> _equals;
        private event Action _invalidated;

        public T Value
        {
            get
            {
                DependencyTracker.RegisterSource(this);
                return _value;
            }
            set
            {
                if (_equals(_value, value))
                    return;

                var old = _value;
                _value = value;

                var changed = Changed;
                changed?.Invoke(old, _value);

                propertyChanged?.Invoke(this,
                    new BindablePropertyChangedEventArgs(nameof(Value)));

                var inv = _invalidated;
                inv?.Invoke();
            }
        }

        /// <summary>Fires when Value changes. Args: (oldValue, newValue).</summary>
        public event Action<T, T> Changed;

        /// <summary>Unity data binding change notification.</summary>
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        /// <summary>Hash code for Unity's data source view tracking.</summary>
        public long GetViewHashCode() => _value?.GetHashCode() ?? 0;

        /// <summary>Implicit conversion for ergonomic usage in bind expressions.</summary>
        public static implicit operator T(Prop<T> p) => p.Value;

        /// <summary>
        /// Force-notify subscribers of a change WITHOUT replacing the value.
        /// Use after in-place mutations (list.Add, dict[key] = val, field = x).
        /// Analog of Vue triggerRef().
        /// </summary>
        public void ForceNotify()
        {
            Changed?.Invoke(_value, _value);
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(Value)));
            _invalidated?.Invoke();
        }

        /// <summary>
        /// Safe in-place mutation: runs mutate and automatically calls ForceNotify.
        /// <code>
        /// Rows.Mutate(list => list.Add(newRow));
        /// </code>
        /// </summary>
        public void Mutate(Action<T> mutate)
        {
            mutate(_value);
            ForceNotify();
        }

        /// <summary>
        /// Create a derived Prop&lt;R&gt; that tracks only the selected field.
        /// Analog of Zustand selector: store.Select(s => s.count).
        /// <code>
        /// Prop&lt;int&gt; count = squadList.Select(s => s.Count);
        /// </code>
        /// </summary>
        public Prop<R> Select<R>(Func<T, R> selector)
        {
            var derived = new Prop<R>(selector(Value));

            // P2.2: hold the derived prop WEAKLY so the subscription doesn't pin it in
            // memory for the source's lifetime. Once the consumer drops the derived prop
            // it becomes collectable, and the next change self-removes the handler.
            var weak = new WeakReference<Prop<R>>(derived);
            Action<T, T> handler = null;
            handler = (_, newVal) =>
            {
                if (weak.TryGetTarget(out var d))
                    d.Value = selector(newVal);
                else
                    Changed -= handler;
            };
            Changed += handler;
            return derived;
        }

        /// <summary>
        /// Like <see cref="Select{R}(Func{T, R})"/>, but returns a subscription via
        /// <paramref name="subscription"/> for deterministic unsubscribe (Dispose).
        /// Use when the derived Prop lifetime is known (in components —
        /// <c>_bindings.Add(subscription)</c>).
        /// </summary>
        public Prop<R> Select<R>(Func<T, R> selector, out IDisposable subscription)
        {
            var derived = new Prop<R>(selector(Value));
            Action<T, T> handler = (_, newVal) => derived.Value = selector(newVal);
            Changed += handler;
            subscription = new InvalidSubscriber(() => Changed -= handler);
            return derived;
        }

        /// <summary>
        /// Create a read-only wrapper. Consumers can read .Value and subscribe
        /// to .Changed, but cannot change the value. Analog of Vue readonly(ref).
        /// </summary>
        public ReadonlyProp<T> AsReadonly() => new(this);

        /// <summary>
        /// Subscribe to invalidation. Computed&lt;T&gt; uses this for auto-tracking.
        /// </summary>
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

        public Prop(T initial = default, Func<T, T, bool> equals = null)
        {
            _value = initial;
            _equals = equals ?? System.Collections.Generic.EqualityComparer<T>.Default.Equals;
        }
    }
}
