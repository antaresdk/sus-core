using System;

namespace Sharq.Core
{
    /// <summary>
    /// Click arguments — a basic example of a typed event payload.
    /// </summary>
    public readonly struct ClickArgs
    {
        /// <summary>Element that was clicked.</summary>
        public readonly SusComponent Target;
        public ClickArgs(SusComponent target) => Target = target;
    }

    /// <summary>
    /// Typed Sus component event.
    /// Alternative to string-based Emit/On — with compile-time type checking.
    /// Analog of Angular EventEmitter&lt;T&gt;.
    ///
    /// <code>
    /// // In component (SusButton.sharq script):
    /// public SusEvent&lt;ClickArgs&gt; OnClick = new();
    ///
    /// void HandleClick() {
    ///     if (Disabled.Value) return;
    ///     OnClick.Emit(new ClickArgs(this));
    /// }
    ///
    /// // Consumer:
    /// button.OnClick.Subscribe(args => SusLog.Info($"Clicked: {args.Target}"));
    /// </code>
    /// </summary>
    public class SusEvent<TArgs>
    {
        private event Action<TArgs> _handlers;

        /// <summary>Subscribe to the event.</summary>
        public void Subscribe(Action<TArgs> handler)
        {
            _handlers += handler;
        }

        /// <summary>Unsubscribe from the event.</summary>
        public void Unsubscribe(Action<TArgs> handler)
        {
            _handlers -= handler;
        }

        /// <summary>Invoke all subscribers.</summary>
        public void Emit(TArgs args)
        {
            _handlers?.Invoke(args);
        }
    }

    /// <summary>
    /// Placeholder type for events with no payload.
    /// Used as SusEvent&lt;Unit&gt; for parameterless events.
    /// </summary>
    public readonly struct Unit
    {
        public static readonly Unit Value = default;
    }

    /// <summary>
    /// Extension: Emit() with no arguments for SusEvent&lt;Unit&gt;.
    /// </summary>
    public static class SusEventUnitExtensions
    {
        public static void Emit(this SusEvent<Unit> evt)
        {
            evt.Emit(Unit.Value);
        }
    }
}
