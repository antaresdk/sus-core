using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Probe;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The canvas reaches a subject bigger than itself, and says so — card T-3708, plan
    /// ARCH-20260922-STORYBOOK-CANVAS-VIEWPORT.md, decision D1 (previously card T-3389, plan
    /// ARCH-20260911-STORYBOOK-SHELL.md §4.7).
    ///
    /// Decision D18 gave the canvas ONE declared height and an overflow, which is what makes two
    /// frames of one address comparable. T-3389 then made the STAGE scroll on both axes so a
    /// subject wider than it would be reachable — but a ScrollView's own box tracks its content on
    /// the cross axis too when both axes scroll, so the fix made the CANVAS itself grow sideways
    /// with the subject (measured 880 -> 1806 for a 1760px-wide element) instead of staying one
    /// declared box. D1 undoes that: the stage is back to ONE axis (vertical only — zone C's own
    /// furniture, not the subject, needed it in the first place), and a subject bigger than the
    /// canvas scrolls inside a viewport that belongs to the CANVAS instead
    /// (<see cref="SusStorybookHost.QaSubjectRoot"/>). Geometry numbers for both axes are in
    /// <see cref="SusStoryCanvasViewportGeometryTests"/>; the tests here are the structural half —
    /// on purpose, since a screenshot cannot tell "there is nothing more to the right" from "the
    /// rest is unreachable".
    /// </summary>
    public class SusStoryStageScrollTests
    {
        const string Counter = "enginetests/primitives/counter";

        [SetUp]
        public void SetUp()
        {
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });
            SusStoryFrame.Reset();
            SusStoryQa.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SusStoryFrame.Reset();
            SusStoryQa.Clear();
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        /// <summary>The ScrollView that zone C is built on, found the way the shell nests it.</summary>
        static ScrollView StageOf(SusStorybookHost host)
        {
            VisualElement e = host.QaCanvas;
            while (e != null && !(e is ScrollView)) e = e.parent;
            return e as ScrollView;
        }

        /// <summary>The canvas's own viewport (card T-3708, decision D1), found the same way.</summary>
        static ScrollView ViewportOf(SusStorybookHost host)
        {
            VisualElement e = host.QaSubjectRoot;
            while (e != null && !(e is ScrollView)) e = e.parent;
            return e as ScrollView;
        }

        [Test]
        public void The_stage_is_vertical_and_the_canvas_viewport_scrolls_both_axes()
        {
            using var host = new SusStorybookHost();
            var stage = StageOf(host);
            var viewport = ViewportOf(host);

            Assert.That(stage, Is.Not.Null, "zone C is a ScrollView; the canvas lives inside it");
            Assert.That(stage.mode, Is.EqualTo(ScrollViewMode.Vertical),
                "zone C's own furniture (crumbs, title, matrix) is what needed to scroll, and it " +
                "only ever ran off the BOTTOM of a narrow window (card T-3708, decision D1)");

            Assert.That(viewport, Is.Not.Null, "the canvas holds its own ScrollView (D1)");
            Assert.That(viewport, Is.Not.SameAs(stage), "the viewport is nested INSIDE the canvas");
            Assert.That(viewport.mode, Is.EqualTo(ScrollViewMode.VerticalAndHorizontal),
                "a subject bigger than the canvas on either axis has no gesture that reaches it " +
                "otherwise (same UNREACHABLE failure T-3389 fixed, moved one level down)");
        }

        [Test]
        public void The_size_line_is_told_which_two_boxes_to_compare_the_subject_with()
        {
            using var host = new SusStorybookHost();
            var viewport = ViewportOf(host);

            Assert.That(host.Sizes.Canvas, Is.SameAs(host.QaCanvas),
                "the vertical question is asked of the canvas: it keeps one declared height (D18)");
            Assert.That(host.Sizes.StageContent, Is.SameAs(viewport.contentContainer),
                "sideways the question is now about the canvas viewport's own content, which in " +
                "practice tracks the mounted subject (card T-3708, decision D1)");
            Assert.That(host.Sizes.StageViewport, Is.SameAs(viewport.contentViewport),
                "and it is asked against what the reader can actually see of the canvas");
        }

        // ── the sentence itself, in numbers ──────────────────────────────────

        [TestCase(400f, 200f, 880f, 350f, "")]
        [TestCase(880f, 350f, 880f, 350f, "")]
        public void A_stage_that_fits_says_nothing(
            float content, float height, float viewport, float canvas, string expected)
        {
            Assert.That(SusStorySizes.FitNoteFor(content, height, viewport, canvas),
                Is.EqualTo(expected));
        }

        /// <summary>Content wider than the canvas viewport it scrolls inside (card T-3708).</summary>
        [Test]
        public void A_subject_wider_than_the_canvas_says_it_scrolls_inside_it()
        {
            Assert.That(SusStorySizes.FitNoteFor(615f, 200f, 272f, 350f),
                Is.EqualTo(SusStorySizes.WideNote));
        }

        [Test]
        public void A_subject_taller_than_the_canvas_says_it_scrolls_inside_it_too()
        {
            Assert.That(SusStorySizes.FitNoteFor(400f, 900f, 880f, 350f),
                Is.EqualTo(SusStorySizes.ClipNote));
        }

        [Test]
        public void A_subject_over_on_both_counts_says_both_things()
        {
            Assert.That(SusStorySizes.FitNoteFor(1760f, 900f, 880f, 350f),
                Is.EqualTo(SusStorySizes.WideNote + " · " + SusStorySizes.ClipNote));
        }

        /// <summary>
        /// Before the first layout pass every figure is NaN. Saying "wider than the canvas" then
        /// would be a guess printed as a fact, and the reader has no way to tell the two apart.
        /// </summary>
        [Test]
        public void An_unmeasured_stage_claims_nothing()
        {
            Assert.That(SusStorySizes.FitNoteFor(float.NaN, float.NaN, float.NaN, float.NaN),
                Is.EqualTo(string.Empty));
            Assert.That(SusStorySizes.FitNoteFor(1760f, 900f, float.NaN, 0f),
                Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// The note rides the throttled read of card T-3362 and must not turn into a second layout
        /// trigger: a stage nobody touched is re-read without a single write.
        /// </summary>
        [Test]
        public void Re_reading_an_unchanged_stage_writes_nothing()
        {
            using var host = new SusStorybookHost();
            host.ShowStoryById(Counter);
            host.Sizes.Refresh();

            int before = host.Sizes.Writes;
            host.Sizes.Refresh();
            host.Sizes.Refresh();

            Assert.That(host.Sizes.Writes, Is.EqualTo(before),
                "two reads of an unchanged stage are zero writes (D16)");
            Assert.That(host.Sizes.FitText, Is.Empty,
                "an unmeasured stage says nothing about fit");
        }
    }
}
