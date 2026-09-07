using System.Globalization;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.UI
{
    /// <summary>
    /// The bottom line of zone C: what the mounted instance ACTUALLY measures right now, read
    /// from <c>resolvedStyle</c> (plan ARCH-20260907-STORYBOOK-ENGINE.md §4.5 p. 5, card T-3038).
    ///
    /// Same source as the neighbouring dimension-token campaign on purpose. A storybook that
    /// printed the numbers from the token table would tell the reader what the tokens SAY; these
    /// are what the layout engine did with them after the skin, the density axis and the
    /// breakpoint had their turn — which is the only figure worth arguing with.
    ///
    /// The overlay note next to it answers the other question a screenshot raises: a popup that
    /// vanished from the frame is usually a popup that went to the panel root instead of the
    /// stage. When the stage's own <c>OverlayHost</c> holds something, the line says so.
    /// </summary>
    public sealed class SusStorySizes : VisualElement
    {
        /// <summary>Shown when the instance has not been laid out yet.</summary>
        public const string Unmeasured = "—";

        /// <summary>Note shown while the stage overlay holds something.</summary>
        public const string OverlayNote = "overlay — in the stage OverlayHost, inside the frame";

        readonly Label _sizes = new();
        readonly Label _overlay = new();

        VisualElement _tracked;

        public SusStorySizes()
        {
            AddToClassList("sus-sb-sizes");
            _sizes.AddToClassList("sus-sb-sizes__metrics");
            _overlay.AddToClassList("sus-sb-sizes__overlay");
            _overlay.text = OverlayNote;
            _overlay.AddToClassList("sus-sb-hidden");
            Add(_sizes);
            Add(_overlay);
            Refresh();
        }

        /// <summary>The metrics line as it currently reads.</summary>
        public string SizesText => _sizes.text;

        /// <summary>True while the overlay note is visible.</summary>
        public bool OverlayNoteVisible => !_overlay.ClassListContains("sus-sb-hidden");

        /// <summary>The element being measured, or null.</summary>
        public VisualElement Tracked => _tracked;

        /// <summary>
        /// Points the line at a new instance (null clears it). Re-reads on every layout pass of
        /// that instance, because a prop changed in zone D changes the numbers.
        /// </summary>
        public void Track(VisualElement element)
        {
            if (_tracked != null) _tracked.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            _tracked = element;
            if (_tracked != null) _tracked.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            Refresh();
        }

        /// <summary>Shows or hides the "the popup is in the stage host" note.</summary>
        public void SetOverlayOpen(bool open) => _overlay.EnableInClassList("sus-sb-hidden", !open);

        /// <summary>Re-reads <c>resolvedStyle</c> — the seam a test drives instead of a frame.</summary>
        public void Refresh()
        {
            if (_tracked == null)
            {
                _sizes.text = string.Empty;
                return;
            }

            var s = _tracked.resolvedStyle;
            _sizes.text =
                "h " + N(s.height) +
                " · fs " + N(s.fontSize) +
                " · pad " + N(s.paddingLeft) +
                " · radius " + N(s.borderTopLeftRadius) +
                "  (resolvedStyle)";
        }

        void OnGeometry(GeometryChangedEvent _) => Refresh();

        static string N(float value) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? Unmeasured
                : value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
