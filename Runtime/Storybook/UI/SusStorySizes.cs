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
        string _shown = string.Empty;
        bool _stale = true;

        public SusStorySizes()
        {
            AddToClassList("sb-sizes");
            _sizes.AddToClassList("sb-sizes__metrics");
            _overlay.AddToClassList("sb-sizes__overlay");
            _overlay.text = OverlayNote;
            _overlay.AddToClassList("sb-hidden");
            Add(_sizes);
            Add(_overlay);
            Refresh();
        }

        /// <summary>The metrics line as it currently reads.</summary>
        public string SizesText => _sizes.text;

        /// <summary>True while the overlay note is visible.</summary>
        public bool OverlayNoteVisible => !_overlay.ClassListContains("sb-hidden");

        /// <summary>The element being measured, or null.</summary>
        public VisualElement Tracked => _tracked;

        /// <summary>
        /// Points the line at a new instance (null clears it).
        ///
        /// Until card T-3362 this also subscribed the line to the tracked element's
        /// <c>GeometryChangedEvent</c>. That closed a loop with no stopping condition (plan
        /// ARCH-20260911-STORYBOOK-SHELL.md §2.5, decision D16): the handler wrote the measured
        /// numbers into a label, the label is a participant in the layout of the same row, a
        /// different text is a different width, and a different width is another layout pass.
        /// The line now waits to be ASKED — by a prop write, by an environment axis, or by the
        /// shell's throttled tick after a canvas layout pass (<see cref="MarkStale"/>).
        /// </summary>
        public void Track(VisualElement element)
        {
            _tracked = element;
            _stale = true;
            Refresh();
        }

        /// <summary>
        /// Records that the numbers may have changed and the line has not caught up yet — raised by
        /// the shell when the canvas finished a layout pass (card T-3362).
        /// </summary>
        public void MarkStale() => _stale = true;

        /// <summary>True while a <see cref="MarkStale"/> has not been honoured.</summary>
        public bool Stale => _stale;

        /// <summary>
        /// Re-reads only if <see cref="MarkStale"/> was called since the last read; returns
        /// whether it did. What the shell's throttled tick calls, so a still stage costs one
        /// boolean and not four <c>resolvedStyle</c> reads.
        /// </summary>
        public bool RefreshIfStale()
        {
            if (!_stale) return false;
            Refresh();
            return true;
        }

        /// <summary>Shows or hides the "the popup is in the stage host" note.</summary>
        public void SetOverlayOpen(bool open) => _overlay.EnableInClassList("sb-hidden", !open);

        /// <summary>
        /// Re-reads <c>resolvedStyle</c> — the seam a test drives instead of a frame. Writes the
        /// label only when the text actually changed: an identical assignment still costs the
        /// layout pass this whole card is about (T-3362).
        /// </summary>
        public void Refresh()
        {
            _stale = false;

            var text = string.Empty;
            if (_tracked != null)
            {
                var s = _tracked.resolvedStyle;
                text =
                    "h " + N(s.height) +
                    " · fs " + N(s.fontSize) +
                    " · pad " + N(s.paddingLeft) +
                    " · radius " + N(s.borderTopLeftRadius) +
                    "  (resolvedStyle)";
            }

            if (text == _shown) return;
            _shown = text;
            Writes++;
            _sizes.text = text;
        }

        /// <summary>
        /// How many times the line actually CHANGED (card T-3362). The acceptance figure the
        /// judge of D15/D16 reads: two layout passes over an unchanged stage must add zero.
        /// </summary>
        public int Writes { get; private set; }

        static string N(float value) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? Unmeasured
                : value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
