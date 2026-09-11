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
    /// T-3168: the demount contract for what a story puts BESIDE its component — the trigger
    /// button, the demo stage, the caption. T-3131 made the shell clear every overlay host it can
    /// reach and T-3160 gave the matrix a host of its own, yet the 2026-09-09 kit sweep still
    /// photographed seven stories out of eleven with somebody else's content on the canvas
    /// (showcase-3): four copies of one "Open modal" trigger on the modal frames, and a
    /// tutorial's "Inventory / Open your bag here." card on stories several switches later.
    ///
    /// Both are one mechanism. A story has no parent to insert into while <c>Configure</c> runs,
    /// so six kit stories hooked <see cref="AttachToPanelEvent"/> and inserted into
    /// <c>Component.parent</c> — and that event fires AGAIN when the component teleports itself
    /// into an <see cref="OverlayHost"/> to open. The second insert lands inside the host, as a
    /// child no <c>AddToOverlay</c> ever registered, and <c>ClearAll</c> only ever swept what it
    /// had registered.
    ///
    /// Two layers are checked, because both had to change: the CONTRACT
    /// (<see cref="SusStoryContext.AddSibling"/> — the engine parents the scenery once and takes
    /// it back on demount) and the SAFETY NET (<c>OverlayHost.ClearAll</c> leaves the host empty,
    /// strays included, for stories nobody has migrated).
    ///
    /// <see cref="UnityTest"/> rather than <see cref="Test"/> for the same reason as
    /// <see cref="SusStoryMatrixOverlayTeardownTests"/>: this component opens itself at mount
    /// (a scheduled step) and a botched teardown puts things back a FRAME later, so a synchronous
    /// look would miss both ends of it. The <see cref="EditorWindow"/> rig and the app-root
    /// overlay come from that same suite — without a real panel there is no panel-root fallback
    /// to reproduce, and under -batchmode -nographics there is no view to init (T-1731).
    /// </summary>
    public class SusStorySceneTeardownTests
    {
        const string Declared = "enginetests/overlay/scenery";
        const string Diy = "enginetests/overlay/scenery-diy";
        const string Counter = "enginetests/primitives/counter";

        EditorWindow _window;
        SusStorybookHost _host;
        OverlayHost _appOverlay;

        [SetUp]
        public void SetUp()
        {
            Assume.That(!Application.isBatchMode,
                "needs a real graphics device to init an EditorWindow view (T-1731 pattern)");

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            _window = EditorWindow.CreateInstance<EditorWindow>();
            _window.Show();
            _host = new SusStorybookHost();
            _window.rootVisualElement.Add(_host);
            _appOverlay = SusBootstrap.GetOrCreateOverlay(_window.rootVisualElement);
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

        // Counted over the whole panel, so scenery that escaped the shell (into a host on
        // panel.visualTree) counts too — that escape IS the bug.
        int SceneryInPanel() => _window.rootVisualElement.panel.visualTree
            .Query<Label>(className: CoreScenery.MarkerClass).ToList().Count;

        int SceneryInCanvas() =>
            _host.QaCanvas.Query<Label>(className: CoreScenery.MarkerClass).ToList().Count;

        string Where() => string.Join(" · ", _window.rootVisualElement.panel.visualTree
            .Query<Label>(className: CoreScenery.MarkerClass).ToList()
            .Select(e => (e.parent?.GetType().Name ?? "<null>") + "/" +
                         (string.IsNullOrEmpty(e.parent?.name) ? "-" : e.parent.name)));

        [UnityTest]
        public IEnumerator Declared_scenery_is_placed_once_beside_the_component()
        {
            Assert.IsTrue(_host.ShowStoryById(Declared));
            yield return null;
            yield return null;

            // One declaration, one element — even though the component attached, teleported into
            // an overlay host and attached again. The hand-rolled hook produced one copy per
            // attach, which is what four stacked "Open modal" triggers were.
            Assert.That(SceneryInPanel(), Is.EqualTo(1),
                "ctx.AddSibling is honoured exactly once per mount, whatever the component does — " +
                "found at: " + Where());
            Assert.That(SceneryInCanvas(), Is.EqualTo(1),
                "the engine puts scenery on the canvas, not wherever the component happens to be");
        }

        [UnityTest]
        public IEnumerator Declared_scenery_is_gone_after_switching_stories()
        {
            Assert.IsTrue(_host.ShowStoryById(Declared));
            yield return null;
            yield return null;
            Assert.That(SceneryInPanel(), Is.GreaterThanOrEqualTo(1), "sanity: it was there");

            Assert.IsTrue(_host.ShowStoryById(Counter), "switch away -> Unmount()");
            yield return null;
            yield return null;
            yield return null;

            Assert.That(SceneryInPanel(), Is.Zero,
                "T-3168: demount takes back exactly what the engine parented — found at: " + Where());
            Assert.That(_host.QaCanvas.childCount, Is.GreaterThan(0), "the new story did mount");
        }

        [UnityTest]
        public IEnumerator Scenery_a_story_parented_itself_into_an_overlay_host_is_gone_after_switching()
        {
            Assert.IsTrue(_host.ShowStoryById(Diy));
            yield return null;
            yield return null;

            // Sanity: the bug reproduces — the DIY hook fired more than once (canvas, then again
            // inside the host after the teleport), so at least one copy is somewhere that is NOT
            // the canvas. Without this the test could pass for the wrong reason.
            Assert.That(SceneryInPanel(), Is.GreaterThan(SceneryInCanvas()),
                "sanity: a copy must land outside the canvas to exercise T-3168 — found at: " + Where());

            Assert.IsTrue(_host.ShowStoryById(Counter), "switch away -> Unmount()");
            yield return null;
            yield return null;
            yield return null;

            Assert.That(SceneryInPanel(), Is.Zero,
                "T-3168: a cleared overlay host is EMPTY — strays included. Found at: " + Where());
        }

        [UnityTest]
        public IEnumerator Two_switches_later_the_stage_still_holds_only_the_current_story()
        {
            // The live symptom outlived several switches, not just one: a tutorial's card was on
            // stories three and four steps later in the sweep (showcase-3).
            Assert.IsTrue(_host.ShowStoryById(Diy));
            yield return null;
            Assert.IsTrue(_host.ShowStoryById(Declared));
            yield return null;
            Assert.IsTrue(_host.ShowStoryById("enginetests/primitives/swatch"));
            yield return null;
            Assert.IsTrue(_host.ShowStoryById(Counter));
            yield return null;
            yield return null;
            yield return null;

            Assert.That(SceneryInPanel(), Is.Zero,
                "no scenery survives two stories later — found at: " + Where());
            Assert.That(_host.CanvasOverlay, Is.Not.Null);
            Assert.That(_host.CanvasOverlay.childCount, Is.Zero,
                "the canvas overlay holds nothing at all, tracked or not");
            Assert.That(_appOverlay.childCount, Is.Zero,
                "and neither does the application's root host");
        }
    }
}
