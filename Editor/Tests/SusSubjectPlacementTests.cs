using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.UI;
using Sharq.Core.Storybook.Probe;
using Sharq.Core.Editor.TestSupport;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Card T-4522 (POOL-01 R1): the stage places what it mounts. A subject absolute over its
    /// parent (a screen) used to sit in a 0 px viewport content and be clipped to nothing; a
    /// subject whose children are all absolute had no height at all. Same EditorWindow rig as
    /// <see cref="SusStoryCanvasViewportGeometryTests"/>: a resolvedStyle number needs a real
    /// layout pass.
    /// </summary>
    public class SusSubjectPlacementTests
    {
        const string CoreSheet = "Packages/com.sharq-it.sus.core/Runtime/Storybook/Storybook.uss";

        static EditorWindow s_window;
        static System.Reflection.MethodInfo s_repaintImmediate;
        SusStorybookHost _host;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assume.That(!Application.isBatchMode,
                "needs a real graphics device to init an EditorWindow view (T-1731 pattern)");

            s_window = SusEditorWindowTestHost.CreateAndShow(1400f, 900f);
            SusBootstrap.LoadTokenCascade(s_window.rootVisualElement);
            s_repaintImmediate = typeof(EditorWindow).GetMethod("RepaintImmediately",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (s_window != null) s_window.Close();
            s_window = null;
        }

        [SetUp]
        public void SetUp()
        {
            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.BuildFrom(new[] { typeof(CoreCounterStory).Assembly });

            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(CoreSheet);
            Assert.That(sheet, Is.Not.Null, "the sheet under test must be imported: " + CoreSheet);

            _host = new SusStorybookHost(sheet);
            _host.style.flexGrow = 1;
            s_window.rootVisualElement.Add(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host?.RemoveFromHierarchy();
            _host = null;

            SusStoryRegistry.ClearDeclaredPackages();
            SusStoryRegistry.Invalidate();
        }

        IEnumerator Settle()
        {
            for (int i = 0; i < 4; i++)
            {
                yield return null;
                s_repaintImmediate?.Invoke(s_window, null);
            }
        }

        static Rect Visible(VisualElement e, VisualElement within)
        {
            var a = e.worldBound;
            var b = within.worldBound;
            return Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
                Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
        }

        [UnityTest]
        public IEnumerator An_absolute_screen_gets_room_in_its_frame_and_is_seen()
        {
            Assert.IsTrue(_host.ShowStoryById(PlacementScreenStory.Id));
            yield return Settle();

            var placement = _host.QaPlacement;
            Assert.IsNotNull(placement);
            Assert.IsTrue(placement.Framed, "a subject absolute by its own sheet gets a framed frame");
            var subject = placement.Subject;
            Assert.AreEqual(Position.Absolute, subject.resolvedStyle.position,
                "the product instance is not restyled: its position is its own");
            Assert.IsFalse(HasShellClass(subject), "and it wears no class of the shell (T-3421)");

            var content = _host.QaSubjectRoot;
            Assert.That(content.layout.height, Is.GreaterThan(200f),
                "the viewport content is no longer 0 px tall under a screen");
            var seen = Visible(subject, content);
            Assert.That(seen.height, Is.GreaterThan(200f), "and the screen shows inside it");
        }

        [UnityTest]
        public IEnumerator A_subject_of_layers_only_is_reported_not_resized()
        {
            Assert.IsTrue(_host.ShowStoryById(PlacementLayersStory.Id));
            yield return Settle();

            var placement = _host.QaPlacement;
            Assert.IsTrue(placement.Empty, "every child absolute: the subject has no height - the story's to size");
            Assert.IsFalse(placement.Framed);
            Assert.IsFalse(HasShellClass(placement.Subject));
        }

        [UnityTest]
        public IEnumerator An_in_flow_subject_is_left_alone()
        {
            Assert.IsTrue(_host.ShowStoryById("enginetests/primitives/counter"));
            yield return Settle();

            var placement = _host.QaPlacement;
            Assert.IsFalse(placement.Framed);
            Assert.IsFalse(placement.Empty);
            Assert.IsFalse(placement.Watching, "judged on its first layout and let go");
            Assert.IsFalse(_host.QaSubjectRoot.ClassListContains(SusSubjectPlacement.FrameNaturalClass),
                "a fixture with no role keeps the canvas width");
        }

        static bool HasShellClass(VisualElement e)
        {
            foreach (var c in e.GetClasses())
                if (c.StartsWith("sb-", System.StringComparison.Ordinal)) return true;
            return false;
        }

        [Test]
        public void Controls_and_groups_keep_their_natural_width_and_nothing_else_does()
        {
            Assert.IsTrue(SusSubjectPlacement.KeepsNaturalWidth(SusStateRoles.Control));
            Assert.IsTrue(SusSubjectPlacement.KeepsNaturalWidth(SusStateRoles.Group));
            Assert.IsFalse(SusSubjectPlacement.KeepsNaturalWidth(SusStateRoles.Surface));
            Assert.IsFalse(SusSubjectPlacement.KeepsNaturalWidth(SusStateRoles.Display));
            Assert.IsFalse(SusSubjectPlacement.KeepsNaturalWidth(null), "a scene has no role");
        }

        // ── owner 2026-10-22: the UI-scale chip scales the logical screen, not the picture ──

        [UnityTest]
        public IEnumerator The_ui_scale_chip_lays_the_viewport_out_smaller_and_fills_the_canvas()
        {
            Assert.IsTrue(_host.ShowStoryById("enginetests/primitives/counter"));
            yield return Settle();
            var scaleAxis = System.Linq.Enumerable.First(_host.Env.ActiveAxes(), a => a.Id == "scale");
            var canvas = _host.QaCanvas;
            VisualElement viewport = _host.QaSubjectRoot;
            while (viewport != null && !(viewport is ScrollView)) viewport = viewport.parent;
            Assert.IsNotNull(viewport, "the canvas viewport");
            try
            {
                _host.Env.CycleForTest("scale"); // 100 -> 125
                yield return Settle();

                Assert.IsTrue(viewport.ClassListContains(SusStorybookHost.ViewportScaledClass));
                var box = canvas.contentRect;
                Assert.That(viewport.layout.width, Is.EqualTo(box.width / 1.25f).Within(1f),
                    "laid out at canvas / scale, as a real UI scale leaves a smaller logical screen");
                var contentRight = canvas.worldBound.xMin + canvas.resolvedStyle.borderLeftWidth
                                   + canvas.resolvedStyle.paddingLeft + box.width;
                Assert.That(viewport.worldBound.xMax, Is.EqualTo(contentRight).Within(1.5f),
                    "magnified back from its top-left corner it ends where the canvas content ends, not past it");
            }
            finally
            {
                scaleAxis.Apply("100");
            }
            yield return Settle();
        }

        [Test]
        public void Every_environment_chip_names_what_it_switches()
        {
            Assert.AreEqual("theme", SusStoryEnvBar.CaptionOf("theme"));
            Assert.AreEqual("storybook", SusStoryEnvBar.CaptionOf("shell-theme"),
                "two chips both saying dark was the complaint: the tool's own theme is named apart");
            Assert.AreEqual("UI scale", SusStoryEnvBar.CaptionOf("scale"));
            Assert.AreEqual("skin", SusStoryEnvBar.CaptionOf("skin"), "an axis of another package is named by its id");
        }

        // ── owner's rule 2026-10-22: no state widget for an undeclared composite ──────────

        [UnityTest]
        public IEnumerator An_undeclared_composite_gets_no_state_widget()
        {
            Assert.IsTrue(_host.ShowStoryById(PlacementScreenStory.Id));
            yield return Settle();

            var matrix = _host.Matrix;
            Assert.AreEqual(SusStoryMatrixMode.None, matrix.MatrixMode,
                "a screen nobody gave a role, too large for a cell: its forced states would all repeat rest");
            StringAssert.Contains("composite", matrix.SilentReason);
            Assert.IsTrue(matrix.ClassListContains("sb-hidden"), "the widget is absent, not folded");
            Assert.IsNotNull(_host.QaPlacement?.Subject, "the live instance is still on the stage");
        }

        [UnityTest]
        public IEnumerator A_declared_composite_keeps_its_states()
        {
            SusStateRoles.Declare(nameof(PlacementScreenDemo), SusStateRoles.Group);
            try
            {
                Assert.IsTrue(_host.ShowStoryById(PlacementScreenStory.Id));
                yield return Settle();

                var matrix = _host.Matrix;
                Assert.IsNull(matrix.SilentReason, "the role table owns the answer for a declared role");
                Assert.AreNotEqual(SusStoryMatrixMode.None, matrix.MatrixMode);
                Assert.IsFalse(matrix.ClassListContains("sb-hidden"));
            }
            finally
            {
                SusStateRoles.Reset();
            }
        }
    }

    // ── fixtures ────────────────────────────────────────────────────────────

    /// <summary>A screen: absolute over its parent by its own sheet, with an in-flow body.</summary>
    public sealed class PlacementScreenDemo : SusComponent
    {
        const string Sheet = "Packages/com.sharq-it.sus.core/Editor/Tests/SusSubjectPlacementFixture.uss";

        protected override void Build()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Sheet);
            if (sheet != null) styleSheets.Add(sheet);
            AddToClassList("placement-fixture-screen");
            var body = new VisualElement();
            body.AddToClassList("placement-fixture-screen__body");
            Add(body);
        }
    }

    /// <summary>A stack of layers: every child absolute, so the root has no height of its own.</summary>
    public sealed class PlacementLayersDemo : SusComponent
    {
        const string Sheet = "Packages/com.sharq-it.sus.core/Editor/Tests/SusSubjectPlacementFixture.uss";

        protected override void Build()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Sheet);
            if (sheet != null) styleSheets.Add(sheet);
            for (int i = 0; i < 2; i++)
            {
                var layer = new VisualElement();
                layer.AddToClassList("placement-fixture-layer");
                Add(layer);
            }
        }
    }

    [SusStory(Id, Name = "Placement · Screen", Component = typeof(PlacementScreenDemo), Order = 20,
        Purpose = "a subject absolute over its parent, as a screen is")]
    public sealed class PlacementScreenStory : ISusStory
    {
        public const string Id = "enginetests/primitives/placement-screen";
        public SusComponent Create() => new PlacementScreenDemo();
        public void Configure(SusStoryContext ctx) { }
    }

    [SusStory(Id, Name = "Placement · Layers", Component = typeof(PlacementLayersDemo), Order = 20,
        Purpose = "a subject whose children are all absolute")]
    public sealed class PlacementLayersStory : ISusStory
    {
        public const string Id = "enginetests/primitives/placement-layers";
        public SusComponent Create() => new PlacementLayersDemo();
        public void Configure(SusStoryContext ctx) { }
    }
}
