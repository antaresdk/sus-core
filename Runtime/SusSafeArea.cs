using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Device safe-area insets for UITK roots. Reads <see cref="Screen.safeArea"/> via
    /// <see cref="Provider"/> (overridable for EditMode / Storybook), converts screen pixels
    /// to panel coordinates the same way as tooltip panel mapping
    /// (<see cref="RuntimePanelUtils.ScreenToPanel"/>), and applies them as padding on the root.
    /// Custom USS variables are intentionally not set — UITK cannot assign custom properties from C#.
    /// </summary>
    public static class SusSafeArea
    {
        static Func<Rect> s_provider = DefaultProvider;
        static (float Top, float Right, float Bottom, float Left) s_insets;
        static readonly HashSet<VisualElement> s_wired = new HashSet<VisualElement>();
        static readonly Dictionary<VisualElement, IVisualElementScheduledItem> s_polls =
            new Dictionary<VisualElement, IVisualElementScheduledItem>();
        static readonly Dictionary<VisualElement, (ScreenOrientation ori, int w, int h)> s_lastScreen =
            new Dictionary<VisualElement, (ScreenOrientation, int, int)>();

        /// <summary>
        /// Source of the safe-area rect in screen pixels (Unity bottom-left origin).
        /// Default: <c>() =&gt; Screen.safeArea</c>. Replace in tests / Storybook with fake insets.
        /// </summary>
        public static Func<Rect> Provider
        {
            get => s_provider ?? DefaultProvider;
            set => s_provider = value ?? DefaultProvider;
        }

        /// <summary>Last computed insets in panel coordinates (Top, Right, Bottom, Left).</summary>
        public static (float Top, float Right, float Bottom, float Left) Insets => s_insets;

        /// <summary>Raised after <see cref="Insets"/> change (and padding is applied on wired roots).</summary>
        public static event Action Changed;

        static Rect DefaultProvider() => Screen.safeArea;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_provider = DefaultProvider;
            s_insets = default;
            s_wired.Clear();
            s_polls.Clear();
            s_lastScreen.Clear();
            Changed = null;
        }
#endif

        /// <summary>
        /// Recomputes insets from <see cref="Provider"/>, writes padding on <paramref name="root"/>,
        /// and wires geometry / orientation listeners so insets stay current.
        /// Idempotent for the same root.
        /// </summary>
        public static void Apply(VisualElement root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            Wire(root);
            RecalcAndApply(root, raiseChanged: true);
        }

        /// <summary>
        /// Recomputes insets and applies padding to every wired root (or only updates
        /// <see cref="Insets"/> when none are wired). Used by tests after changing <see cref="Provider"/>.
        /// </summary>
        public static void Refresh()
        {
            if (s_wired.Count == 0)
            {
                s_insets = ComputeInsets(null);
                Changed?.Invoke();
                return;
            }

            // Snapshot — Changed / detach may mutate the set.
            var roots = new List<VisualElement>(s_wired);
            foreach (var root in roots)
            {
                if (root == null)
                    continue;
                RecalcAndApply(root, raiseChanged: false);
            }
            Changed?.Invoke();
        }

        /// <summary>Test / teardown: clear wired roots and restore default provider.</summary>
        internal static void ResetForTests()
        {
            foreach (var kv in s_polls)
                kv.Value?.Pause();
            s_polls.Clear();
            s_wired.Clear();
            s_lastScreen.Clear();
            s_provider = DefaultProvider;
            s_insets = default;
            Changed = null;
        }

        static void Wire(VisualElement root)
        {
            if (!s_wired.Add(root))
                return;

            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetach);

            // Editor / Game view often resizes the panel without a GeometryChanged on the
            // content root; also catch orientation flips (Screen.orientation / size).
            var poll = root.schedule.Execute(() => PollScreen(root)).Every(250);
            s_polls[root] = poll;
            s_lastScreen[root] = (Screen.orientation, Screen.width, Screen.height);
        }

        static void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.target is VisualElement root && s_wired.Contains(root))
                RecalcAndApply(root, raiseChanged: true);
        }

        static void OnDetach(DetachFromPanelEvent evt)
        {
            if (evt.target is not VisualElement root)
                return;
            Unwire(root);
        }

        static void Unwire(VisualElement root)
        {
            if (!s_wired.Remove(root))
                return;
            root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            root.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            if (s_polls.TryGetValue(root, out var poll))
            {
                poll?.Pause();
                s_polls.Remove(root);
            }
            s_lastScreen.Remove(root);
        }

        static void PollScreen(VisualElement root)
        {
            if (root == null || !s_wired.Contains(root))
                return;
            var now = (Screen.orientation, Screen.width, Screen.height);
            if (s_lastScreen.TryGetValue(root, out var prev) && prev == now)
                return;
            s_lastScreen[root] = now;
            RecalcAndApply(root, raiseChanged: true);
        }

        static void RecalcAndApply(VisualElement root, bool raiseChanged)
        {
            var next = ComputeInsets(root);
            bool changed = !ApproximatelyEqual(s_insets, next);
            s_insets = next;

            root.style.paddingTop = next.Top;
            root.style.paddingRight = next.Right;
            root.style.paddingBottom = next.Bottom;
            root.style.paddingLeft = next.Left;

            if (changed && raiseChanged)
                Changed?.Invoke();
        }

        /// <summary>
        /// Screen-pixel safe rect → panel-space insets via <see cref="RuntimePanelUtils.ScreenToPanel"/>.
        /// When the root has no panel (detached EditMode element), falls back to screen-pixel insets
        /// (1:1), which is enough for Provider-seam tests.
        /// </summary>
        internal static (float Top, float Right, float Bottom, float Left) ComputeInsets(VisualElement root)
        {
            var safe = Provider();
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);

            // Clamp to screen so a bogus Provider cannot produce negative full-screen padding.
            float xMin = Mathf.Clamp(safe.xMin, 0f, sw);
            float xMax = Mathf.Clamp(safe.xMax, 0f, sw);
            float yMin = Mathf.Clamp(safe.yMin, 0f, sh);
            float yMax = Mathf.Clamp(safe.yMax, 0f, sh);
            if (xMax < xMin) (xMin, xMax) = (xMax, xMin);
            if (yMax < yMin) (yMin, yMax) = (yMax, yMin);

            var panel = root?.panel;
            if (panel == null)
            {
                // Screen space: Y up, bottom-left origin → top inset from top edge.
                float left = xMin;
                float right = sw - xMax;
                float bottom = yMin;
                float top = sh - yMax;
                return (top, right, bottom, left);
            }

            // Same mapping path as overlay/tooltip positioning: ScreenToPanel on screen corners.
            var screenTL = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(0f, sh));
            var screenTR = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sw, sh));
            var screenBL = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(0f, 0f));
            var safeTL = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(xMin, yMax));
            var safeTR = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(xMax, yMax));
            var safeBL = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(xMin, yMin));

            float leftP = safeTL.x - screenTL.x;
            float rightP = screenTR.x - safeTR.x;
            float topP = safeTL.y - screenTL.y;
            float bottomP = screenBL.y - safeBL.y;

            return (
                Mathf.Max(0f, topP),
                Mathf.Max(0f, rightP),
                Mathf.Max(0f, bottomP),
                Mathf.Max(0f, leftP));
        }

        static bool ApproximatelyEqual(
            (float Top, float Right, float Bottom, float Left) a,
            (float Top, float Right, float Bottom, float Left) b)
        {
            const float eps = 0.01f;
            return Mathf.Abs(a.Top - b.Top) < eps
                && Mathf.Abs(a.Right - b.Right) < eps
                && Mathf.Abs(a.Bottom - b.Bottom) < eps
                && Mathf.Abs(a.Left - b.Left) < eps;
        }
    }
}
