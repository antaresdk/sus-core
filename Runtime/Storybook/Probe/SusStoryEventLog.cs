using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sharq.Core.Storybook.Probe
{
    /// <summary>
    /// The event half of zone E (plan ARCH-20260907-STORYBOOK-ENGINE.md §4.7, card T-3040):
    /// what the mounted story ACTUALLY raised this session, and what it promised and never
    /// raised.
    ///
    /// Wiring is automatic and generic: <see cref="SusComponent.DescribeEvents"/> names every
    /// public <c>On*</c> delegate of the instance and <see cref="SusComponent.SubscribeEvent"/>
    /// attaches to it without knowing the payload type (card T-3031). Nothing is registered by
    /// hand, so a component that grows a new event grows a new chip on the next mount — the
    /// engine cannot fall behind the corpus the way a hand-written catalogue did (T-2997).
    ///
    /// "Declared but never fired" is the same idea R124 judges in the source: a promise with no
    /// deed. Here it is measured on the LIVE instance instead of on the text, which is the only
    /// place where "the button raises OnChange" can be checked at all.
    /// </summary>
    public sealed class SusStoryEventLog : IDisposable
    {
        /// <summary>How many of the most recent calls the strip shows (mock-up: 4).</summary>
        public const int ChipCount = 4;

        /// <summary>Longest argument text kept in a chip before it is cut.</summary>
        public const int ArgTextLimit = 24;

        readonly List<IDisposable> _subs = new();
        readonly List<string> _calls = new();
        readonly List<string> _declared = new();
        readonly HashSet<string> _fired = new(StringComparer.Ordinal);

        bool _disposed;

        /// <summary>Raised after every recorded call — zone E redraws on it.</summary>
        public event Action<string, string> Fired;

        /// <summary>Every event the mounted instance declares, in <c>DescribeEvents</c> order.</summary>
        public IReadOnlyList<string> Declared => _declared;

        /// <summary>Every recorded call this session, oldest first ("OnChange(\"Uzbekistan\")").</summary>
        public IReadOnlyList<string> Calls => _calls;

        /// <summary>The last <see cref="ChipCount"/> calls, oldest first — what the strip prints.</summary>
        public IReadOnlyList<string> Recent
        {
            get
            {
                if (_calls.Count <= ChipCount) return _calls;
                return _calls.GetRange(_calls.Count - ChipCount, ChipCount);
            }
        }

        /// <summary>Declared events that never fired this session, in declaration order.</summary>
        public IReadOnlyList<string> Unfired
        {
            get
            {
                var rest = new List<string>();
                for (int i = 0; i < _declared.Count; i++)
                    if (!_fired.Contains(_declared[i])) rest.Add(_declared[i]);
                return rest;
            }
        }

        /// <summary>
        /// Drops every subscription and every record, then subscribes to all events of
        /// <paramref name="component"/>. Passing null just resets — that is the story switch.
        /// </summary>
        public void Attach(SusComponent component)
        {
            Reset();
            if (_disposed || component == null) return;

            var events = component.DescribeEvents();
            for (int i = 0; i < events.Count; i++)
            {
                var info = events[i];
                if (info == null || string.IsNullOrEmpty(info.Name)) continue;
                _declared.Add(info.Name);

                string name = info.Name;
                var sub = component.SubscribeEvent(name, arg => Record(name, arg));
                if (sub != null) _subs.Add(sub);
            }
        }

        /// <summary>Detaches from the current instance and forgets the session.</summary>
        public void Reset()
        {
            for (int i = 0; i < _subs.Count; i++)
            {
                try { _subs[i]?.Dispose(); }
                catch (Exception e) { SusLog.Error("[storybook] event unsubscribe failed: " + e); }
            }
            _subs.Clear();
            _calls.Clear();
            _declared.Clear();
            _fired.Clear();
        }

        /// <summary>Records one call by hand — the seam a test and a QA sink drive.</summary>
        public void Record(string eventName, object arg)
        {
            if (_disposed || string.IsNullOrEmpty(eventName)) return;
            _fired.Add(eventName);
            string call = Format(eventName, arg);
            _calls.Add(call);
            Fired?.Invoke(eventName, call);
        }

        /// <summary>
        /// One chip's text: <c>OnOpen()</c>, <c>OnChange("Uzbekistan")</c>, <c>OnCount(3)</c>.
        /// Strings keep their quotes on purpose — "" and no argument at all are different
        /// answers to "did anything reach the handler".
        /// </summary>
        public static string Format(string eventName, object arg)
        {
            if (arg == null) return eventName + "()";
            string text = arg is string s
                ? "\"" + Cut(s) + "\""
                : Cut(arg is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : arg.ToString());
            return eventName + "(" + text + ")";
        }

        static string Cut(string value)
        {
            if (value == null) return string.Empty;
            return value.Length <= ArgTextLimit ? value : value.Substring(0, ArgTextLimit) + "…";
        }

        public void Dispose()
        {
            if (_disposed) return;
            Reset();
            _disposed = true;
        }
    }
}
