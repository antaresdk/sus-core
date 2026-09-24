using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Card T-3708, plan ARCH-20260922-STORYBOOK-CANVAS-VIEWPORT.md §5 step S1, decision D1: the
    /// canvas keeps ONE declared box — width AND height (decision D18) — while a subject bigger
    /// than it scrolls inside the canvas's OWN viewport instead of growing the canvas sideways
    /// (the T-3389 regression this card exists to undo) or being silently clipped downwards
    /// (decision D3).
    ///
    /// <see cref="UnityTest"/> and the <see cref="EditorWindow"/> rig are the same pattern as
    /// <see cref="SusStorySceneTeardownTests"/>: a resolvedStyle number needs a real layout pass,
    /// which needs a real graphics device, which -batchmode -nographics does not have (T-1731).
    /// </summary>
    public class SusStoryCanvasViewportGeometryTests
    {
        const string Counter = "enginetests/primitives/counter";

        EditorWindow _window;
        SusStorybookHost _host;

        [SetUp]
        public void SetUp()
        {
            Assume.That(!Application.isBatchMode,
                "needs a real graphics device to init an EditorWindow view (T-1731 pattern)");

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            _window = EditorWindow.CreateInstance<EditorWindow>();
            _window.position = new Rect(0, 0, 1400, 900);
            _window.Show();
            _host = new SusStorybookHost();
            _host.style.flexGrow = 1;
            _window.rootVisualElement.Add(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;
            if (_window != null) _window.Close();
            _window = null;

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        /// <summary>The canvas's own viewport (card T-3708, decision D1), found the way the shell
        /// nests it: up from the mount point until a ScrollView turns up.</summary>
        static ScrollView ViewportOf(SusStorybookHost host)
        {
            VisualElement e = host.QaSubjectRoot;
            while (e != null && !(e is ScrollView)) e = e.parent;
            return e as ScrollView;
        }

        [UnityTest]
        public IEnumerator A_subject_wider_than_the_canvas_scrolls_inside_it_without_growing_it()
        {
            Assert.IsTrue(_host.ShowStoryById(Counter));
            yield return null;
            yield return null;

            var canvas = _host.QaCanvas;
            float widthBefore = canvas.resolvedStyle.width;
            float heightBefore = canvas.resolvedStyle.height;
            Assert.That(widthBefore, Is.GreaterThan(0f), "sanity: the canvas laid out at all");

            var subject = _host.QaSubjectRoot.Children().First();
            subject.style.width = 1760;
            yield return null;
            yield return null;

            Assert.That(canvas.resolvedStyle.width, Is.EqualTo(widthBefore).Within(0.5f),
                "decision D18: the canvas keeps its ONE declared box — a wide subject must not " +
                "push it open sideways (the T-3389 regression this card undoes)");
            Assert.That(canvas.resolvedStyle.height, Is.EqualTo(heightBefore).Within(0.5f));

            var viewport = ViewportOf(_host);
            Assert.That(viewport, Is.Not.Null);
            Assert.That(viewport.mode, Is.EqualTo(ScrollViewMode.VerticalAndHorizontal));
            Assert.That(viewport.horizontalScroller.highValue, Is.GreaterThan(0f),
                "a 1760px subject inside a narrower canvas must be reachable by a gesture — a " +
                "scroll range of zero is the T-3389 UNREACHABLE regression again");
        }

        [UnityTest]
        public IEnumerator A_subject_taller_than_the_canvas_scrolls_inside_it_too()
        {
            Assert.IsTrue(_host.ShowStoryById(Counter));
            yield return null;
            yield return null;

            var canvas = _host.QaCanvas;
            float heightBefore = canvas.resolvedStyle.height;

            var subject = _host.QaSubjectRoot.Children().First();
            subject.style.height = 600;
            yield return null;
            yield return null;

            Assert.That(canvas.resolvedStyle.height, Is.EqualTo(heightBefore).Within(0.5f),
                "decision D18: the canvas keeps ONE declared height regardless of the subject");

            var viewport = ViewportOf(_host);
            Assert.That(viewport.verticalScroller.highValue, Is.GreaterThan(0f),
                "a subject taller than the canvas must be reachable by scrolling, not silently " +
                "clipped (decision D3)");
        }
    }
}
