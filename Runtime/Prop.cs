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

                // notify subscribers with tracking SUSPENDED. Without this,
                // a Prop.Value GET performed synchronously by a subscriber's handler — e.g.
                // SusTooltip's Watch(Params, (_, _) => RebuildParams()) reading Params.Value
                // to rebuild rows — gets attributed to whichever effect happens to be
                // CURRENTLY tracking on this thread, even though that read has nothing to do
                // with that effect's own dependency list. DependencyTracker is a single
                // thread-global collector (see its remarks), not scoped to "the effect that
                // owns this Prop" or "the handler currently running" — it only knows whether
                // SOME Track() scope is active right now. If a component's WatchEffect writes
                // into a child's Prop via a helper that reads-then-writes that SAME Prop
                // (SusTooltip.SetParam: read current Params, merge, write back) WHILE the
                // notification for an EARLIER write in the same run is still bubbling through
                // Changed handlers, the handler's read silently subscribes the ORIGINAL
                // WatchEffect to this Prop — and the very next write of it (a few lines later
                // in the same call) fires that subscription reentrantly, from inside the
                // notification we are already dispatching. That is a genuine infinite
                // self-cycle (bounded only by SusComponent.MaxSteadyStateFlushIterations),
                // not benign cascade noise — confirmed live via SusStatusEffect/blk-status
                // (17x warning flood) and BattleUnitWorldBar (~90+ iterations on
                // every mount), and it survived fixing SetParam's OWN read via
                // Peek() alone, because RebuildParams' nested read reproduced the same
                // mis-tracking through a different call path. Vue/Solid avoid this whole
                // class by pausing tracking around trigger/effect notification; mirroring
                // that here (rather than special-casing every Watch handler that happens to
                // read back its own Prop) fixes it for every current AND future component
                // that reads a Prop from inside a Changed/invalidation handler, not just this
                // one call site.
                DependencyTracker.Untracked(() =>
                {
                    var changed = Changed;
                    changed?.Invoke(old, _value);

                    propertyChanged?.Invoke(this,
                        new BindablePropertyChangedEventArgs(nameof(Value)));

                    var inv = _invalidated;
                    inv?.Invoke();
                });
            }
        }

        /// <summary>
        /// Read the current value WITHOUT registering it as a reactive dependency of the
        /// currently-tracking effect (if any). Analog of Vue's <c>toRaw</c>/SolidJS
        /// <c>untrack</c> peek, scoped to a single read.
        ///
        /// Use for a read-modify-write "accumulator" pattern — read the current value only
        /// to compute the NEXT value that will immediately be written back to this SAME Prop
        /// (e.g. <c>SusTooltip.SetParam</c>: copy the existing list, add/replace one entry,
        /// write the new list) — inside a method that may itself be called from within another
        /// component's <c>WatchEffect</c>/<c>ReactiveEffect</c> (2026-08-20).
        /// <see cref="DependencyTracker"/> is thread-global and keys off whichever effect is
        /// CURRENTLY tracking, not off which component owns this Prop: a plain <c>Value</c> get
        /// inside such a method makes the CALLER's effect depend on this Prop even though the
        /// read is a private implementation detail, not something the caller's effect logic
        /// exposes. Because the write that follows a few lines later always assigns a freshly
        /// allocated instance (<c>new List&lt;T&gt;(...)</c>), <see cref="Prop{T}"/>'s default
        /// reference-equality check never suppresses it — the write ALWAYS invalidates, and
        /// since the get-then-set both happen inside the SAME execution of that effect, the
        /// invalidation re-triggers the very effect that is still on the call stack. Every
        /// re-run repeats the same get-then-set, so the effect keeps re-queuing itself until
        /// <c>SusComponent.MaxSteadyStateFlushIterations</c> gives up and logs "exceeded 100
        /// re-entrant iterations" — a real infinite self-trigger, not just log noise. Reading
        /// via <c>Peek()</c> instead of <c>Value</c> for the merge-source keeps the write's
        /// notification (still fires <see cref="Changed"/>/<see cref="propertyChanged"/>/the
        /// invalidation event for any REAL subscriber) while removing the accidental
        /// dependency registration that caused the cycle.
        /// </summary>
        public T Peek() => _value;

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
        /// Drops every subscriber (<see cref="Changed"/>, <see cref="propertyChanged"/> and
        /// invalidation) without touching the value.
        ///
        /// Needed for Props kept in static fields: with Domain Reload disabled (Fast Enter
        /// Play Mode) they outlive a Play Mode session, so handlers closing over elements of
        /// an already-destroyed panel would keep firing. Call it from a
        /// <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c> reset hook.
        /// </summary>
        public void ClearSubscribers()
        {
            Changed = null;
            propertyChanged = null;
            _invalidated = null;
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
