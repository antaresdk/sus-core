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

        /// <summary>
        /// Note shown while zone C reaches past the part of it the reader can see (card T-3389,
        /// plan §4.7: the fact of scrolling is SAID, it does not stay silent). Without it the
        /// reader sees a cut-off stage and no reason to suspect there is more of it.
        /// </summary>
        public const string WideNote = "wider than the window — scroll sideways for the rest";

        /// <summary>
        /// Note shown while the canvas cuts the subject off at the bottom. The canvas keeps ONE
        /// declared height (decision D18) so that two frames of one address stay comparable, and
        /// that height is not negotiable by the subject — so the cut has to be said out loud.
        /// </summary>
        public const string ClipNote = "taller than the canvas — one declared height, the rest is clipped";

        readonly Label _sizes = new();
        readonly Label _overlay = new();
        readonly Label _fit = new();

        VisualElement _tracked;
        string _shown = string.Empty;
        string _shownFit = string.Empty;
        bool _stale = true;

        public SusStorySizes()
        {
            AddToClassList("sb-sizes");
            _sizes.AddToClassList("sb-sizes__metrics");
            _overlay.AddToClassList("sb-sizes__overlay");
            _overlay.text = OverlayNote;
            _overlay.AddToClassList("sb-hidden");
            _fit.AddToClassList("sb-sizes__fit");
            _fit.AddToClassList("sb-hidden");
            Add(_sizes);
            Add(_overlay);
            Add(_fit);
            Refresh();
        }

        /// <summary>The metrics line as it currently reads.</summary>
        public string SizesText => _sizes.text;

        /// <summary>True while the overlay note is visible.</summary>
        public bool OverlayNoteVisible => !_overlay.ClassListContains("sb-hidden");

        /// <summary>The fit note as it currently reads; empty when the subject fits.</summary>
        public string FitText => _fit.text;

        /// <summary>
        /// The canvas box the subject must fit into. Set by the shell; null in a test that has no
        /// panel, and then the vertical half of the fit note simply never fires (card T-3389).
        /// </summary>
        public VisualElement Canvas { get; set; }

        /// <summary>
        /// Everything zone C holds (the stage ScrollView content container), which is what the
        /// horizontal question is asked of. Not the subject: at a narrow window it is zone C's own
        /// furniture that sticks out first — measured in Play on 2026-09-11, the stage content is
        /// 615px wide at every window from 320 to 560, against a viewport of 272 to 512. Asking the
        /// subject would stay silent through all of it while a third of the stage sat out of reach.
        /// </summary>
        public VisualElement StageContent { get; set; }

        /// <summary>
        /// The part of zone C the reader actually sees without scrolling (the stage ScrollView
        /// viewport). Null in a test with no panel, and then the note simply never fires.
        /// </summary>
        public VisualElement StageViewport { get; set; }

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

            var fit = FitNote();

            if (text == _shown && fit == _shownFit) return;
            Writes++;
            if (text != _shown)
            {
                _shown = text;
                _sizes.text = text;
            }
            if (fit == _shownFit) return;
            _shownFit = fit;
            _fit.text = fit;
            _fit.EnableInClassList("sb-hidden", fit.Length == 0);
        }

        /// <summary>
        /// What zone C says about a subject that does not fit. Reads the SAME resolvedStyle pass as
        /// the metrics line above, so saying it costs no extra layout (card T-3362, decision D16).
        /// </summary>
        string FitNote()
        {
            if (_tracked == null) return string.Empty;

            return FitNoteFor(Width(StageContent), _tracked.resolvedStyle.height,
                Width(StageViewport), Height(Canvas));
        }

        /// <summary>
        /// The note for a zone C <paramref name="contentWidth"/> wide inside a viewport
        /// <paramref name="viewportWidth"/> wide, holding a subject <paramref name="subjectHeight"/>
        /// tall in a canvas <paramref name="canvasHeight"/> tall. The two questions have two
        /// different witnesses on purpose: sideways it is the whole stage that runs out of window,
        /// downwards it is the subject that runs out of canvas.
        ///
        /// Pure arithmetic, so the sentence the reader gets is judged by a test with numbers in it
        /// and not by a screenshot (card T-3389).
        ///
        /// Any figure may still be NaN before the first layout pass, and an unmeasured stage must
        /// claim nothing — silence about an unknown beats a note that turns out to be wrong.
        /// </summary>
        public static string FitNoteFor(float contentWidth, float subjectHeight, float viewportWidth, float canvasHeight)
        {
            bool wide = Exceeds(contentWidth, viewportWidth);
            bool tall = Exceeds(subjectHeight, canvasHeight);
            if (wide && tall) return WideNote + " · " + ClipNote;
            if (wide) return WideNote;
            return tall ? ClipNote : string.Empty;
        }

        /// <summary>True when <paramref name="size"/> sticks out of <paramref name="limit"/>.</summary>
        static bool Exceeds(float size, float limit)
        {
            if (float.IsNaN(size) || float.IsInfinity(size)) return false;
            if (float.IsNaN(limit) || float.IsInfinity(limit) || limit <= 0f) return false;
            return size > limit + 0.5f;
        }

        static float Width(VisualElement box) => box == null ? float.NaN : box.contentRect.width;

        static float Height(VisualElement box) => box == null ? float.NaN : box.contentRect.height;

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
