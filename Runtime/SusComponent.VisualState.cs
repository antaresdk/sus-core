using System;
using System.Collections.Generic;

namespace Sharq.Core
{
    public abstract partial class SusComponent
    {
        private static readonly HashSet<string> VisualStateNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "normal", "hover", "pressed", "disabled", "selected", "error",
            "loading", "focused", "readonly", "active"
        };

        private string _visualState = "normal";
        private string _visualStateClassPrefix;

        /// <summary>
        /// Current mutually-exclusive visual state (normal/hover/pressed/disabled/…).
        /// </summary>
        public string VisualState => _visualState;

        /// <summary>
        /// Class prefix for state classes, e.g. <c>"sus-button"</c> → <c>sus-button--disabled</c>.
        /// When null, classes are applied as <c>sus-vs--{state}</c>.
        /// </summary>
        public string VisualStateClassPrefix
        {
            get => _visualStateClassPrefix;
            set => _visualStateClassPrefix = value;
        }

        /// <summary>Fires after a successful <see cref="SetVisualState"/> (old, new).</summary>
        public event Action<string, string> VisualStateChanged;

        /// <summary>
        /// Atomically switch among a mutually-exclusive visual-state class group.
        /// Removes all known state classes for the prefix, then adds the new one
        /// (except <c>normal</c>, which only clears).
        /// </summary>
        public void SetVisualState(string state)
        {
            if (string.IsNullOrEmpty(state)) state = "normal";
            state = state.ToLowerInvariant();

            if (!VisualStateNames.Contains(state))
            {
                SusLog.Warn(
                    $"[SusComponent] Unknown visual state '{state}' on {GetType().Name}. " +
                    "Known: " + string.Join(", ", VisualStateNames));
                return;
            }

            if (_visualState == state) return;

            var old = _visualState;
            var prefix = string.IsNullOrEmpty(_visualStateClassPrefix)
                ? "sus-vs"
                : _visualStateClassPrefix;

            foreach (var s in VisualStateNames)
            {
                if (s == "normal") continue;
                RemoveFromClassList($"{prefix}--{s}");
            }

            if (state != "normal")
                AddToClassList($"{prefix}--{state}");

            _visualState = state;
            VisualStateChanged?.Invoke(old, state);
        }

        /// <summary>
        /// Derive visual state from common boolean props (disabled &gt; error &gt; loading &gt;
        /// selected &gt; focused &gt; active &gt; readonly &gt; normal). Call from Watch handlers.
        /// </summary>
        protected void SyncVisualState(
            bool disabled = false,
            bool error = false,
            bool loading = false,
            bool selected = false,
            bool focused = false,
            bool active = false,
            bool @readonly = false)
        {
            if (disabled) SetVisualState("disabled");
            else if (error) SetVisualState("error");
            else if (loading) SetVisualState("loading");
            else if (selected) SetVisualState("selected");
            else if (focused) SetVisualState("focused");
            else if (active) SetVisualState("active");
            else if (@readonly) SetVisualState("readonly");
            else SetVisualState("normal");
        }
    }
}
