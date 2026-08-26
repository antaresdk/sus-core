using System;
using System.Collections.Generic;

namespace Sharq.Core
{
    /// <summary>
    /// Groups reactive subscriptions (WatchHandle) for batch disposal.
    /// Analogous to Vue's effectScope() / SolidJS createRoot().
    ///
    /// Usage inside SusComponent:
    /// <code>
    /// private EffectScope _tooltipScope;
    ///
    /// void ShowTooltip()
    /// {
    ///     _tooltipScope?.Dispose();
    ///     _tooltipScope = CreateScope();
    ///     Watch(mousePos, (_, p) => PositionTooltip(p), _tooltipScope);
    ///     Watch(targetData, (_, d) => FillTooltip(d), _tooltipScope);
    /// }
    ///
    /// void HideTooltip() => _tooltipScope?.Dispose();
    /// </code>
    ///
    /// Usage outside SusComponent (composable pattern):
    /// <code>
    /// using var scope = new EffectScope();
    /// scope.Watch(myProp, (_, v) => SusLog.Info($"{v}"));
    /// // all subscriptions auto-disposed at end of block
    /// </code>
    /// </summary>
    public class EffectScope : IDisposable
    {
        private readonly List<IDisposable> _handles = new();
        private readonly List<Action> _onDispose = new();
        private bool _disposed;

        /// <summary>
        /// Register a subscription handle. Called by SusComponent.Watch(scope) overload.
        /// </summary>
        public void Register(IDisposable handle)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EffectScope));
            _handles.Add(handle);
        }

        /// <summary>
        /// Subscribe to a Prop change, scoped to this EffectScope.
        /// The returned WatchHandle is auto-registered — no need to call Register() manually.
        /// </summary>
        public WatchHandle Watch<T>(Prop<T> source, Action<T, T> callback)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EffectScope));

            void Handler(T oldVal, T newVal) => callback(oldVal, newVal);
            source.Changed += Handler;

            var handle = new WatchHandle(() => source.Changed -= Handler);
            _handles.Add(handle);
            return handle;
        }

        /// <summary>
        /// Register a cleanup action to run on Dispose().
        /// Analogous to Vue's onScopeDispose().
        /// </summary>
        public void OnDispose(Action cleanup)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EffectScope));
            _onDispose.Add(cleanup);
        }

        /// <summary>
        /// Dispose all registered handles and cleanup actions.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var h in _handles)
                h.Dispose();
            _handles.Clear();

            foreach (var c in _onDispose)
                c();
            _onDispose.Clear();
        }
    }
}
