using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Probe;
using Sharq.Core.Storybook.UI;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Zone C reaches a subject bigger than itself, and says so — card T-3389, plan
    /// ARCH-20260911-STORYBOOK-SHELL.md §4.7.
    ///
    /// The half of decision D18 that landed first gave the canvas ONE declared height and an
    /// overflow, which is what makes two frames of one address comparable. The half measured here
    /// is the other one: the stage used to scroll on a single axis, so a subject wider than the
    /// stage was not merely clipped but UNREACHABLE. Measured in Play on 2026-09-11 before the
    /// fix: an element 1760 px wide inside an 880 px canvas ended at x=1783 while the stage
    /// content stayed at 880 and the horizontal scroller carried display:None — 903 px of the
    /// subject could not be brought into view by any gesture. After it: content 1806 against a
    /// viewport of 880, scroll range 926, scroller 8 px tall.
    ///
    /// Both assertions below are structural rather than pictorial, and on purpose: a screenshot
    /// cannot tell "there is nothing more to the right" from "the rest is unreachable".
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

        [Test]
        public void The_stage_scrolls_on_both_axes()
        {
            using var host = new SusStorybookHost();
            var stage = StageOf(host);

            Assert.That(stage, Is.Not.Null, "zone C is a ScrollView; the canvas lives inside it");
            Assert.That(stage.mode, Is.EqualTo(ScrollViewMode.VerticalAndHorizontal),
                "the default mode is Vertical, and with it a subject wider than the stage has no " +
                "gesture that reaches it");
        }

        [Test]
        public void The_size_line_is_told_which_two_boxes_to_compare_the_subject_with()
        {
            using var host = new SusStorybookHost();
            var stage = StageOf(host);

            Assert.That(host.Sizes.Canvas, Is.SameAs(host.QaCanvas),
                "the vertical question is asked of the canvas: it keeps one declared height (D18)");
            Assert.That(host.Sizes.StageViewport, Is.SameAs(stage.contentViewport),
                "the horizontal question is asked of the viewport: with both axes on, a wide " +
                "subject takes the canvas sideways with it and the canvas stops being a witness");
        }

        // ── the sentence itself, in numbers ──────────────────────────────────

        [TestCase(400f, 200f, 880f, 350f, "")]
        [TestCase(880f, 350f, 880f, 350f, "")]
        public void A_subject_that_fits_says_nothing(
            float w, float h, float viewport, float canvas, string expected)
        {
            Assert.That(SusStorySizes.FitNoteFor(w, h, viewport, canvas), Is.EqualTo(expected));
        }

        [Test]
        public void A_subject_wider_than_the_viewport_says_where_the_rest_is()
        {
            Assert.That(SusStorySizes.FitNoteFor(1760f, 200f, 880f, 350f),
                Is.EqualTo(SusStorySizes.WideNote));
        }

        [Test]
        public void A_subject_taller_than_the_canvas_says_it_is_being_cut()
        {
            Assert.That(SusStorySizes.FitNoteFor(400f, 900f, 880f, 350f),
                Is.EqualTo(SusStorySizes.ClipNote));
        }

        [Test]
        public void A_subject_bigger_both_ways_says_both_things()
        {
            Assert.That(SusStorySizes.FitNoteFor(1760f, 900f, 880f, 350f),
                Is.EqualTo(SusStorySizes.WideNote + " · " + SusStorySizes.ClipNote));
        }

        /// <summary>
        /// Before the first layout pass every figure is NaN. Saying "wider than the stage" then
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
