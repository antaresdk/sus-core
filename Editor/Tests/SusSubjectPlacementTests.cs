using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.UI;
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
