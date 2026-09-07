using System;
using System.Collections.Generic;

namespace Sharq.Core.Storybook.Nav
{
    /// <summary>
    /// Back / forward for the shell (plan §4.6). A list of routes plus a cursor — no Unity type,
    /// no platform call, no dependency beyond core, which is exactly why it is testable in
    /// EditMode without entering play mode (DoD §7 p. 13).
    ///
    /// Semantics are the browser's, because that is what a person expects from the two arrows:
    /// <see cref="Go"/> truncates everything after the cursor and appends; <see cref="Back"/> and
    /// <see cref="Forward"/> only move the cursor.
    /// </summary>
    public sealed class SusStoryHistory
    {
        readonly List<SusStoryRoute> _entries = new();
        int _cursor = -1;

        /// <summary>Upper bound on remembered routes; the oldest fall off the front.</summary>
        public int Capacity { get; set; } = 200;

        /// <summary>Raised whenever <see cref="Current"/> changes, with the new route.</summary>
        public event Action<SusStoryRoute> Changed;

        /// <summary>Every remembered route, oldest first.</summary>
        public IReadOnlyList<SusStoryRoute> Entries => _entries;

        /// <summary>Index of <see cref="Current"/> in <see cref="Entries"/>, or -1 when empty.</summary>
        public int Cursor => _cursor;

        /// <summary>Route being shown, or null before the first navigation.</summary>
        public SusStoryRoute Current => _cursor >= 0 && _cursor < _entries.Count ? _entries[_cursor] : null;

        /// <summary>True when there is an older route to return to.</summary>
        public bool CanGoBack => _cursor > 0;

        /// <summary>True when a forward route survived (nothing was pushed since going back).</summary>
        public bool CanGoForward => _cursor >= 0 && _cursor < _entries.Count - 1;

        /// <summary>
        /// Navigates to a route: drops the forward tail and appends. Navigating to the route
        /// already shown is a no-op — otherwise re-clicking the selected story would fill the
        /// history with copies and make the back button do nothing visible.
        /// Returns true when the history actually moved.
        /// </summary>
        public bool Go(SusStoryRoute route)
        {
            if (route == null) return false;
            if (Current != null && Current.Equals(route)) return false;

            if (_cursor < _entries.Count - 1)
                _entries.RemoveRange(_cursor + 1, _entries.Count - _cursor - 1);

            _entries.Add(route);
            _cursor = _entries.Count - 1;

            if (Capacity > 0 && _entries.Count > Capacity)
            {
                int drop = _entries.Count - Capacity;
                _entries.RemoveRange(0, drop);
                _cursor -= drop;
            }

            Changed?.Invoke(Current);
            return true;
        }

        /// <summary>
        /// Replaces the current entry without growing the history — used when only the query
        /// changed (a control was dragged), so that dragging a slider does not bury the previous
        /// STORY under fifty entries.
        /// </summary>
        public bool Replace(SusStoryRoute route)
        {
            if (route == null) return false;
            if (_cursor < 0) return Go(route);
            if (_entries[_cursor].Equals(route)) return false;
            _entries[_cursor] = route;
            Changed?.Invoke(Current);
            return true;
        }

        /// <summary>Steps one route back; false when there is nowhere to go.</summary>
        public bool Back()
        {
            if (!CanGoBack) return false;
            _cursor--;
            Changed?.Invoke(Current);
            return true;
        }

        /// <summary>Steps one route forward; false when there is nowhere to go.</summary>
        public bool Forward()
        {
            if (!CanGoForward) return false;
            _cursor++;
            Changed?.Invoke(Current);
            return true;
        }

        /// <summary>Forgets everything. Does not raise <see cref="Changed"/>.</summary>
        public void Clear()
        {
            _entries.Clear();
            _cursor = -1;
        }
    }
}
