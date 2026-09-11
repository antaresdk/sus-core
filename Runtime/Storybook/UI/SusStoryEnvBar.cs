using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Env;
using Sharq.Core.Storybook.Nav;

namespace Sharq.Core.Storybook.UI
{
    /// <summary>
    /// Zone B — the chip group of card T-3036 (plan §4.4, mock-up "Storybook Shell" chipDefs).
    /// Renders one chip per active axis in the fixed order the mock-up draws them
    /// (breakpoint, density, theme, skin, scale, input, locale); <c>skin</c> and <c>locale</c>
    /// only appear once something registers them in <see cref="SusStoryEnvAxisRegistry"/> — no
    /// provider, no chip.
    ///
    /// A click cycles the axis to its next value (wrapping) and re-renders every chip, because the
    /// theme chip's own icon (moon/sun) depends on the value it just became — re-deriving the
    /// whole row is simpler than teaching each chip to patch itself and the row is five to seven
    /// elements, not a list.
    /// </summary>
    public sealed class SusStoryEnvBar : VisualElement, IDisposable
    {
        internal const string EnvQueryPrefix = "env.";

        // Mock-up order: breakpoint, density, theme, skin, scale, input, locale. "shell-theme"
        // comes last on purpose (card T-3371, decision D4): the first seven say what the SUBJECT
        // is looking at, and the last one says what the INSTRUMENT looks like — putting it next to
        // "theme" would invite exactly the confusion the two chips exist to end.
        static readonly string[] Order =
            { "breakpoint", "density", "theme", "skin", "scale", "input", "locale", "shell-theme" };

        /// <summary>Shell-root class the zone D drawer opens with (card T-3377, decision D25).</summary>
        internal const string PanelOpenClass = "sb-shell--panel-open";

        const string ScrimClass = "sb-shell__scrim";

        readonly Dictionary<string, ISusStoryEnvAxis> _builtin = new(StringComparer.Ordinal);
        readonly ScrollView _chips = new(ScrollViewMode.Horizontal);
        readonly VisualElement _shellRoot;
        readonly Button _props;
        VisualElement _hookedScrim;
        bool _disposed;

        /// <summary>Raised after any chip click actually changed a service (route text may be stale now).</summary>
        public event Action Changed;

        public SusStoryEnvBar(VisualElement shellRoot, VisualElement previewRoot)
        {
            AddToClassList("sb-env__bar");

            _chips.AddToClassList("sb-env__chips");
            _chips.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Add(_chips);

            // Card T-3377, decision D25 — the entry into zone D on a narrow shell. Below 800px the
            // props panel used to be a bare `display: none`: no button, no drawer, no label, so
            // the storybook at 640 was a viewer with no controls and "how do I change a prop here"
            // had no answer at all. Zone A had had a burger, a drawer, a scrim and Ctrl+K at that
            // same threshold since T-3033; this reuses the mechanism instead of inventing one.
            //
            // It lives in zone B rather than in zone D for the obvious reason: an entry parked
            // inside the closed drawer is not an entry. The button carries a WORD, not only a
            // glyph — the DoD asks for a labelled entry, and an unlabelled icon is how you get the
            // same complaint back one wave later.
            _shellRoot = shellRoot;
            _props = new Button(TogglePanel) { text = "props" };
            _props.AddToClassList("sb-env__props");
            _props.tooltip = "zone D — props panel (Esc or the scrim closes it)";
            Add(_props);

            if (_shellRoot != null)
                _shellRoot.RegisterCallback<KeyDownEvent>(OnShellKeyDown);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);

            foreach (var axis in SusBuiltinEnvAxes.CreateAll(shellRoot, previewRoot))
                _builtin[axis.Id] = axis;

            SusStoryEnvAxisRegistry.Changed += Rebuild;
            Rebuild();
        }

        /// <summary>Opens or closes zone D's drawer (card T-3377). Public so a test can drive it.</summary>
        public void TogglePanel() => SetPanelOpen(!PanelOpen);

        /// <summary>Whether zone D's drawer is open right now.</summary>
        public bool PanelOpen => _shellRoot != null && _shellRoot.ClassListContains(PanelOpenClass);

        void SetPanelOpen(bool open)
        {
            if (_shellRoot == null) return;
            _shellRoot.EnableInClassList(PanelOpenClass, open);
        }

        void OnShellKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != UnityEngine.KeyCode.Escape || !PanelOpen) return;
            SetPanelOpen(false);
            evt.StopPropagation();
        }

        // The scrim is a sibling built by the host and is NOT parented yet while this constructor
        // runs, so it cannot be looked up there — the lookup waits for the panel instead.
        void OnAttachToPanel(AttachToPanelEvent _)
        {
            if (_hookedScrim != null || _shellRoot == null) return;
            var scrim = _shellRoot.Q(className: ScrimClass);
            if (scrim == null) return;
            _hookedScrim = scrim;
            scrim.RegisterCallback<PointerDownEvent>(OnScrimDown);
        }

        void OnScrimDown(PointerDownEvent _) => SetPanelOpen(false);

        /// <summary>Every chip currently shown, in display order (builtin ∪ registry, no gaps).</summary>
        public IEnumerable<ISusStoryEnvAxis> ActiveAxes()
        {
            foreach (var id in Order)
            {
                if (_builtin.TryGetValue(id, out var builtin)) yield return builtin;
                else if (SusStoryEnvAxisRegistry.TryGet(id, out var provided)) yield return provided;
            }
        }

        bool TryFindAxis(string id, out ISusStoryEnvAxis axis)
        {
            if (_builtin.TryGetValue(id, out axis)) return true;
            return SusStoryEnvAxisRegistry.TryGet(id, out axis);
        }

        /// <summary>
        /// Query entries for every axis sitting away from its default (<c>Values[0]</c>), keyed
        /// <c>env.&lt;Id&gt;</c> (plan §4.4: "environment axes are written into the deep-link
        /// query under a separate prefix"). Empty when the whole environment is at default — the
        /// reason an untouched link stays short.
        /// </summary>
        public IReadOnlyDictionary<string, string> CurrentDeltas()
        {
            Dictionary<string, string> result = null;
            foreach (var axis in ActiveAxes())
            {
                var values = axis.Values;
                if (values == null || values.Count == 0) continue;
                var current = axis.Current;
                if (string.Equals(current, values[0], StringComparison.Ordinal)) continue;
                result ??= new Dictionary<string, string>(StringComparer.Ordinal);
                result[EnvQueryPrefix + axis.Id] = current ?? string.Empty;
            }
            return (IReadOnlyDictionary<string, string>)result ?? EmptyDeltas;
        }

        static readonly IReadOnlyDictionary<string, string> EmptyDeltas = new Dictionary<string, string>();

        /// <summary>
        /// Strips every <c>env.*</c> entry out of <paramref name="route"/>'s query, applying each
        /// to its axis (unknown ids are dropped silently — a link from a build that had more
        /// providers than this one must still open the story). Returns the route unchanged when it
        /// carried no env entries, so callers never pay for an allocation on the common path.
        /// </summary>
        public SusStoryRoute ConsumeFromRoute(SusStoryRoute route)
        {
            if (route == null || route.Query.Count == 0) return route;

            Dictionary<string, string> clean = null;
            bool sawEnv = false;
            foreach (var kv in route.Query)
            {
                if (kv.Key.StartsWith(EnvQueryPrefix, StringComparison.Ordinal))
                {
                    sawEnv = true;
                    var id = kv.Key.Substring(EnvQueryPrefix.Length);
                    if (TryFindAxis(id, out var axis)) axis.Apply(kv.Value);
                    continue;
                }
                clean ??= new Dictionary<string, string>(StringComparer.Ordinal);
                clean[kv.Key] = kv.Value;
            }

            if (!sawEnv) return route;
            Rebuild();
            return route.WithQuery(clean);
        }

        void Rebuild()
        {
            _chips.Clear();

            ISusStoryEnvAxis last = null;
            foreach (var axis in ActiveAxes()) last = axis;

            foreach (var axis in ActiveAxes())
            {
                var chip = new VisualElement { name = "sb-env-chip-" + axis.Id };
                chip.AddToClassList("sb-env__chip");
                if (ReferenceEquals(axis, last)) chip.AddToClassList("sb-env__chip--last");
                // The instrument's own switch is set apart by a rule, not by hope that a reader
                // notices two chips saying "dark" mean different things (card T-3371, D4).
                if (string.Equals(axis.Id, "shell-theme", StringComparison.Ordinal))
                    chip.AddToClassList("sb-env__chip--shell");

                var icon = new SusIconElement(axis.Icon);
                icon.AddToClassList("sb-env__chip-icon");

                var value = new Label(axis.Current) { name = "sb-env-chip-value" };
                value.AddToClassList("sb-env__chip-value");

                chip.Add(icon);
                chip.Add(value);
                chip.RegisterCallback<ClickEvent>(_ => Cycle(axis));
                _chips.Add(chip);
            }
        }

        /// <summary>
        /// Drives the same code path a chip click does. UI Toolkit drops a synthetic
        /// <see cref="ClickEvent"/> outside a live panel (see <c>SusStorybookShellTests</c>'s own
        /// note on this), so this is how EditMode tests exercise the click without one.
        /// </summary>
        internal void CycleForTest(string axisId)
        {
            foreach (var axis in ActiveAxes())
            {
                if (!string.Equals(axis.Id, axisId, StringComparison.Ordinal)) continue;
                Cycle(axis);
                return;
            }
        }

        void Cycle(ISusStoryEnvAxis axis)
        {
            var values = axis.Values;
            if (values == null || values.Count == 0) return;

            int index = -1;
            for (int i = 0; i < values.Count; i++)
            {
                if (!string.Equals(values[i], axis.Current, StringComparison.Ordinal)) continue;
                index = i;
                break;
            }
            axis.Apply(values[(index + 1) % values.Count]);

            Rebuild();
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SusStoryEnvAxisRegistry.Changed -= Rebuild;
            UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            if (_shellRoot != null) _shellRoot.UnregisterCallback<KeyDownEvent>(OnShellKeyDown);
            if (_hookedScrim != null)
            {
                _hookedScrim.UnregisterCallback<PointerDownEvent>(OnScrimDown);
                _hookedScrim = null;
            }
        }
    }
}
