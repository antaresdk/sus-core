using System;
using System.Collections.Generic;
using System.Reflection;

namespace Sharq.Core
{
    public abstract partial class SusComponent
    {
        // ─── Component Event Bus ─────────────────────────────────────────

        private Dictionary<string, Delegate> _eventHandlers;
        private readonly HashSet<string> _bridgedFields = new();
        private readonly List<Delegate> _bridgeDelegates = new(); // keep alive

        /// <summary>
        /// Subscribe to a custom component event by name.
        /// Generated code uses this to wire parent handlers to child
        /// component events (e.g. @save → child.On("save", handler)).
        ///
        /// Also bridges to the matching C# field by convention:
        /// "change" → OnChange, "click" → OnClick, etc.
        /// Downstream components may fire OnChange?.Invoke(value) directly,
        /// so we subscribe to both the dictionary bus AND the C# field.
        /// </summary>
        public void On(string eventName, Delegate handler)
        {
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentNullException(nameof(eventName));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _eventHandlers ??= new Dictionary<string, Delegate>();
            if (_eventHandlers.TryGetValue(eventName, out var existing))
                _eventHandlers[eventName] = Delegate.Combine(existing, handler);
            else
                _eventHandlers[eventName] = handler;

            // ── Bridge to matching C# field (one-time setup) ──
            BridgeFieldOnce(eventName);
        }

        /// <summary>
        /// Subscribe to a typed custom component event.
        /// </summary>
        public void On<T>(string eventName, Action<T> handler)
        {
            On(eventName, (Delegate)handler);
        }

        /// <summary>
        /// Subscribe to a parameterless custom component event.
        /// </summary>
        public void On(string eventName, Action handler)
        {
            On(eventName, (Delegate)handler);
        }

        /// <summary>
        /// Unsubscribe from a custom component event.
        /// </summary>
        public void Off(string eventName, Delegate handler)
        {
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentNullException(nameof(eventName));
            if (handler == null)
                return;
            if (_eventHandlers == null)
                return;

            if (_eventHandlers.TryGetValue(eventName, out var existing))
            {
                var result = Delegate.Remove(existing, handler);
                if (result == null)
                    _eventHandlers.Remove(eventName);
                else
                    _eventHandlers[eventName] = result;
            }
        }

        /// <summary>
        /// Fire a typed custom component event. Subscribers added via
        /// On(eventName, ...) are invoked with the provided payload.
        /// </summary>
        protected void Emit<T>(string eventName, T data)
        {
            if (_eventHandlers == null)
                return;
            if (!_eventHandlers.TryGetValue(eventName, out var handler))
                return;

            if (handler is Action<T> typed)
                typed(data);
            else if (handler is Action parameterless)
                parameterless();
            else
            {
                // Multi-cast: invoke each delegate individually
                foreach (var d in handler.GetInvocationList())
                {
                    if (d is Action<T> t)
                        t(data);
                    else if (d is Action a)
                        a();
                }
            }
        }

        /// <summary>
        /// Fire a parameterless custom component event.
        /// </summary>
        protected void Emit(string eventName)
        {
            if (_eventHandlers == null)
                return;
            if (!_eventHandlers.TryGetValue(eventName, out var handler))
                return;

            if (handler is Action action)
                action();
            else
            {
                foreach (var d in handler.GetInvocationList())
                {
                    if (d is Action a)
                        a();
                }
            }
        }

        // ─── Bridge: dictionary bus ↔ C# field ─────────────────────────

        /// <summary>
        /// One-time bridge: finds the C# field matching the event name
        /// ("change" → OnChange) and adds a delegate that calls Emit()
        /// so dictionary-bus subscribers fire when the C# field is invoked.
        ///
        /// Downstream components may use Action fields directly (OnChange?.Invoke()),
        /// not Emit(). This bridge ensures that On("change", handler) works
        /// regardless of how the component fires its events.
        /// </summary>
        private void BridgeFieldOnce(string eventName)
        {
            var fieldName = "On" + char.ToUpper(eventName[0]) + eventName.Substring(1);
            if (_bridgedFields.Contains(fieldName))
                return;

            var flags = BindingFlags.Public | BindingFlags.Instance;
            var field = GetType().GetField(fieldName, flags);
            if (field == null)
                return;

            _bridgedFields.Add(fieldName);

            Delegate bridge;
            if (field.FieldType == typeof(Action))
            {
                // No-param field (e.g., Action OnClick)
                bridge = new Action(() => Emit(eventName));
            }
            else if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(Action<>))
            {
                // Typed field (e.g., Action<bool> OnChange)
                var argType = field.FieldType.GetGenericArguments()[0];
                var helperType = typeof(FieldBridge<>).MakeGenericType(argType);
                var helper = Activator.CreateInstance(helperType, this, eventName);
                var invokeMethod = helperType.GetMethod("Forward");
                bridge = Delegate.CreateDelegate(field.FieldType, helper, invokeMethod);
            }
            else
            {
                return; // unsupported field type
            }

            _bridgeDelegates.Add(bridge);
            var current = field.GetValue(this) as Delegate;
            field.SetValue(this, current != null ? Delegate.Combine(current, bridge) : bridge);
        }

        /// <summary>
        /// Helper to bridge Action&lt;T&gt; fields to the event bus.
        /// Created once per event+field pair, held alive by _bridgeDelegates.
        /// </summary>
        private sealed class FieldBridge<T>
        {
            private readonly SusComponent _target;
            private readonly string _eventName;
            public FieldBridge(SusComponent target, string eventName)
            {
                _target = target;
                _eventName = eventName;
            }
            public void Forward(T data)
            {
                _target.Emit(_eventName, data);
            }
        }
    }
}
