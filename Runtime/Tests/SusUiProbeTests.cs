#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Sharq.Core.Diagnostics;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>Minimal SusComponent fixtures for SusUiProbe health heuristics.</summary>
    internal class ProbeSizedComp : SusComponent
    {
        protected override void Build()
        {
            style.width = 40;
            style.height = 24;
        }
    }

    internal class ProbeZeroComp : SusComponent
    {
        protected override void Build()
        {
            // Force both axes to 0 so layout cannot stretch a "broken" hit-target.
            style.width = 0;
            style.height = 0;
            style.minWidth = 0;
            style.minHeight = 0;
            style.flexGrow = 0;
            style.flexShrink = 0;
        }
    }

    /// <summary>Phase 0 smoke: SusUiProbe returns parseable JSON without touching the Console.</summary>
    public class SusUiProbeTests
    {
        [Test]
        public void GetTreeJson_ReturnsNonEmptyParseableTree()
        {
            var root = new VisualElement { name = "root" };
            root.Add(new Label("hello") { name = "greeting" });

            var json = SusUiProbe.GetTreeJson(root);

            Assert.IsNotNull(json);
            Assert.IsTrue(json.StartsWith("["), "tree JSON must start with [");
            Assert.IsTrue(json.EndsWith("]"), "tree JSON must end with ]");
            StringAssert.Contains("\"name\":\"greeting\"", json);
            StringAssert.Contains("\"text\":\"hello\"", json);
        }

        [Test]
        public void GetTreeJson_WithBackgroundImage_EmitsImageSourceSizeAndScaleMode()
        {
            // T-654 / D-028: sidecar image {src,w,h,scaleMode} from backgroundImage source pixels.
            var tex = new Texture2D(64, 32, TextureFormat.RGBA32, false) { name = "probe-hero" };
            try
            {
                var hero = new VisualElement { name = "hero" };
                hero.style.width = 240;
                hero.style.height = 80;
                hero.style.backgroundImage = new StyleBackground(tex);
                hero.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;

                var root = new VisualElement { name = "root" };
                root.Add(hero);

                var json = SusUiProbe.GetTreeJson(root);

                StringAssert.Contains("\"name\":\"hero\"", json);
                StringAssert.Contains("\"image\":{", json);
                StringAssert.Contains("\"w\":64", json);
                StringAssert.Contains("\"h\":32", json);
                StringAssert.Contains("\"scaleMode\":\"stretch-to-fill\"", json);
                // Transient textures have no AssetDatabase path — name is acceptable src.
                StringAssert.Contains("\"src\":", json);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void GetTreeJson_WithScaleToFit_EmitsSafeScaleMode()
        {
            var tex = new Texture2D(100, 100, TextureFormat.RGBA32, false) { name = "probe-fit" };
            try
            {
                var el = new VisualElement { name = "fit" };
                el.style.backgroundImage = new StyleBackground(tex);
                el.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                var json = SusUiProbe.GetTreeJson(el);
                StringAssert.Contains("\"scaleMode\":\"scale-to-fit\"", json);
                StringAssert.Contains("\"w\":100", json);
                StringAssert.Contains("\"h\":100", json);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void GetTreeJson_WithoutBackgroundImage_OmitsImageField()
        {
            var el = new VisualElement { name = "plain" };
            var json = SusUiProbe.GetTreeJson(el);
            StringAssert.DoesNotContain("\"image\":", json);
        }

        [Test]
        public void GetHealthJson_CountsElementsAndHasAnomaliesArray()
        {
            var root = new VisualElement();
            root.Add(new VisualElement());
            root.Add(new VisualElement());

            var json = SusUiProbe.GetHealthJson(root);

            StringAssert.Contains("\"totalElements\":3", json);
            StringAssert.Contains("\"anomalies\":[", json);
        }

        [Test]
        public void GetPropsJson_MissingComponent_ReturnsError()
        {
            var root = new VisualElement();
            var json = SusUiProbe.GetPropsJson(root, "DoesNotExist");
            StringAssert.Contains("\"error\":\"not found\"", json);
        }

        [Test]
        public void F_WithSyntheticNaNOrInfinity_EmitsJsonNullNotBareToken()
        {
            // T-2209: `resolvedStyle`/`worldBound` are NaN before the first layout pass. JSON has
            // no NaN/Infinity literal — writing v.ToString("F0") for such a value produces the
            // bare token `NaN`, which breaks JSON.parse for the WHOLE geometry sidecar (R36 G0
            // "does not parse"), not just the one field. Reflection: F() is a private static
            // formatter with no VisualElement dependency, so the synthetic NaN is probed directly
            // rather than fighting Unity's layout timing to reproduce it end-to-end.
            var f = typeof(SusUiProbe).GetMethod("F", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "SusUiProbe.F helper not found — update this test alongside any rename");

            Assert.AreEqual("null", (string)f.Invoke(null, new object[] { float.NaN }));
            Assert.AreEqual("null", (string)f.Invoke(null, new object[] { float.PositiveInfinity }));
            Assert.AreEqual("null", (string)f.Invoke(null, new object[] { float.NegativeInfinity }));
            Assert.AreEqual("12", (string)f.Invoke(null, new object[] { 12f }));
        }

        [Test]
        public void GetTreeJson_UnresolvedBounds_OmitsBoundsAndFlagsUnresolved()
        {
            // T-2209 end-to-end: before layout runs, VisualElement.worldBound components are NaN.
            // AppendNode must NOT coerce that to a JSON `NaN` token (unparseable) or to `0`/`null`
            // numbers (reads as a real zero-size element to frame-geometry.mjs's rectOf/G2 — the
            // exact false positive the honest fix must avoid). It omits w/h/x/y and says
            // `"resolved":false` instead, so a reader can tell "not measured yet" from "measured
            // and got zero".
            var root = new VisualElement { name = "root" };
            var pending = new VisualElement { name = "pending-layout" };
            root.Add(pending);

            var json = SusUiProbe.GetTreeJson(root);

            // The literal token JSON.parse cannot handle — this is the actual R36 G0 defect.
            StringAssert.DoesNotContain("NaN", json);
            // Detached from any panel, worldBound cannot resolve — the node must say so instead
            // of silently reporting a numeric bound frame-geometry.mjs would read as real.
            StringAssert.Contains("\"resolved\":false", json);
        }

        [Test]
        public void GetTreeJson_ListViewClosedContentContainer_WalksPhysicalRows()
        {
            // T-2849: BaseVerticalCollectionView (ListView/MultiColumnListView) redirects
            // Add()/Children() to nothing — el.childCount==0 even though el.hierarchy.childCount
            // is the real internal ScrollView, itself holding the row elements. Before this fix
            // AppendNode walked el.Children() only, so a live table-style component's rows never
            // reached geometry.json (children:0 next to a screenshot with visible rows) and no
            // query could ever resolve a row via the sidecar — reproduced live on a
            // MultiColumnListView-backed data table and confirmed here with a plain ListView so
            // the fixture needs no panel/layout.
            var items = new[] { "mission-1", "mission-2", "mission-3" };
            var lv = new ListView
            {
                name = "probe-list",
                itemsSource = items,
                makeItem = () => new Label(),
                bindItem = (e, i) => ((Label)e).text = items[i],
            };
            lv.Rebuild();

            var root = new VisualElement { name = "root" };
            root.Add(lv);

            Assert.AreEqual(0, lv.childCount, "fixture must reproduce the closed contentContainer (logical Children() empty)");
            Assert.Greater(lv.hierarchy.childCount, 0, "fixture must have a real physical child (the internal ScrollView) to walk into");

            var json = SusUiProbe.GetTreeJson(root);

            StringAssert.Contains("\"name\":\"probe-list\"", json);
            // The internal ScrollView (physical child) must now be emitted, not skipped.
            StringAssert.Contains("unity-collection-view__scroll-view", json);
            // children count on the list-view node must reflect the physical fallback, not 0.
            var idx = json.IndexOf("\"name\":\"probe-list\"", System.StringComparison.Ordinal);
            var childrenIdx = json.IndexOf("\"children\":", idx, System.StringComparison.Ordinal);
            Assert.Greater(childrenIdx, -1);
            var digitsStart = childrenIdx + "\"children\":".Length;
            var digitsEnd = digitsStart;
            while (digitsEnd < json.Length && char.IsDigit(json[digitsEnd])) digitsEnd++;
            var childrenValue = int.Parse(json.Substring(digitsStart, digitsEnd - digitsStart));
            Assert.Greater(childrenValue, 0, "probe-list children count must use the physical fallback, not logical childCount==0");
        }

        [Test]
        public void GetHealthJson_DetachedZeroSize_NotAnomaly()
        {
            // Without a panel, zero bounds are not actionable — do not spam anomalies.
            var root = new VisualElement();
            root.Add(new ProbeSizedComp { name = "detached" });

            var json = SusUiProbe.GetHealthJson(root);

            StringAssert.DoesNotContain("visible but zero-size", json);
            StringAssert.Contains("\"susComponents\":1", json);
        }
    }

    /// <summary>PlayMode: structural-collapse heuristics for sus_ui_health (T-030).</summary>
    public class SusUiProbeHealthPlaymodeTests : UIDocumentTestHelper
    {
        [UnityTest]
        public IEnumerator GetHealthJson_ClosedPopupChild_NotAnomaly()
        {
            var host = new VisualElement { name = "popup-host" };
            host.style.display = DisplayStyle.None;
            host.style.width = 200;
            host.style.height = 120;
            var child = new ProbeSizedComp { name = "select-list" };
            host.Add(child);
            Root.Add(host);
            yield return WaitFrames(2);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.DoesNotContain("visible but zero-size", json);
        }

        [UnityTest]
        public IEnumerator GetHealthJson_IdleLoaderChild_NotAnomaly()
        {
            var loader = new VisualElement { name = "loader" };
            loader.style.display = DisplayStyle.None;
            var spinner = new ProbeSizedComp { name = "spinner" };
            loader.Add(spinner);
            Root.Add(loader);
            yield return WaitFrames(2);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.DoesNotContain("visible but zero-size", json);
        }

        [UnityTest]
        public IEnumerator GetHealthJson_IgnorePicker_NotAnomaly()
        {
            var icon = new ProbeZeroComp { name = "decor" };
            icon.pickingMode = PickingMode.Ignore;
            Root.Add(icon);
            yield return WaitFrames(2);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.DoesNotContain("visible but zero-size", json);
        }

        [UnityTest]
        public IEnumerator GetHealthJson_VisibleZeroSize_IsAnomaly()
        {
            Root.style.alignItems = Align.FlexStart;
            var broken = new ProbeZeroComp { name = "broken" };
            broken.pickingMode = PickingMode.Position;
            Root.Add(broken);
            yield return WaitFrames(3);

            Assert.AreEqual(DisplayStyle.Flex, broken.resolvedStyle.display);
            Assert.IsTrue(broken.visible);
            Assert.LessOrEqual(broken.worldBound.width, 0f);
            Assert.LessOrEqual(broken.worldBound.height, 0f);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.Contains("visible but zero-size", json);
            StringAssert.Contains("#broken", json);
        }

        [UnityTest]
        public IEnumerator GetHealthJson_SizedComponent_NoAnomaly()
        {
            var ok = new ProbeSizedComp { name = "ok" };
            Root.Add(ok);
            yield return WaitFrames(2);

            Assert.Greater(ok.worldBound.width, 0f);
            Assert.Greater(ok.worldBound.height, 0f);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.DoesNotContain("visible but zero-size", json);
        }

        [UnityTest]
        public IEnumerator GetHealthJson_AncestorZeroSize_NotAnomaly()
        {
            var host = new VisualElement { name = "collapsed-host" };
            host.style.display = DisplayStyle.Flex;
            host.style.width = 0;
            host.style.height = 0;
            host.style.minWidth = 0;
            host.style.minHeight = 0;
            var child = new ProbeSizedComp { name = "nested" };
            host.Add(child);
            Root.style.alignItems = Align.FlexStart;
            Root.Add(host);
            yield return WaitFrames(3);

            Assert.LessOrEqual(host.worldBound.width, 0f);
            Assert.LessOrEqual(host.worldBound.height, 0f);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.DoesNotContain("visible but zero-size", json);
        }
    }

    /// <summary>PlayMode: synthetic scroll for UX probes (T-040).</summary>
    public class SusUiProbeScrollPlaymodeTests : UIDocumentTestHelper
    {
        private static ScrollView MakeOverflowScroll(VisualElement root)
        {
            var sv = new ScrollView(ScrollViewMode.Vertical) { name = "probe-scroll" };
            sv.style.width = 200;
            sv.style.height = 120;
            for (int i = 0; i < 30; i++)
            {
                var row = new Label($"row-{i}") { name = $"row-{i}" };
                row.style.height = 28;
                sv.Add(row);
            }
            root.Add(sv);
            return sv;
        }

        [UnityTest]
        public IEnumerator ScrollJson_Offset_MovesScrollOffset()
        {
            var sv = MakeOverflowScroll(Root);
            yield return WaitFrames(3);

            Assert.AreEqual(0f, sv.scrollOffset.y, 0.5f);

            var json = SusUiProbe.ScrollJson(Root, "probe-scroll", "offset", y: 200f);

            StringAssert.Contains("\"ok\":true", json);
            StringAssert.Contains("\"mode\":\"offset\"", json);
            Assert.Greater(sv.scrollOffset.y, 50f);
            StringAssert.Contains("\"after\":{", json);
        }

        [UnityTest]
        public IEnumerator ScrollJson_Wheel_MovesScrollOffset()
        {
            var sv = MakeOverflowScroll(Root);
            yield return WaitFrames(3);

            var before = sv.scrollOffset.y;
            var json = SusUiProbe.ScrollJson(Root, "#probe-scroll", "wheel", dy: 240f);
            yield return WaitFrames(2);

            StringAssert.Contains("\"ok\":true", json);
            StringAssert.Contains("\"mode\":\"wheel\"", json);
            Assert.Greater(sv.scrollOffset.y, before);
        }

        [UnityTest]
        public IEnumerator ScrollJson_MissingTarget_ReturnsError()
        {
            yield return WaitFrames(1);
            var json = SusUiProbe.ScrollJson(Root, "no-such-scroll", "offset", y: 10f);
            StringAssert.Contains("\"ok\":false", json);
            StringAssert.Contains("scroll view not found", json);
        }

        [UnityTest]
        public IEnumerator ScrollJson_ScrollTo_IntoChild()
        {
            var sv = MakeOverflowScroll(Root);
            yield return WaitFrames(3);

            var json = SusUiProbe.ScrollJson(Root, "probe-scroll", into: "row-25");
            yield return WaitFrames(2);

            StringAssert.Contains("\"ok\":true", json);
            StringAssert.Contains("\"mode\":\"scrollTo\"", json);
            Assert.Greater(sv.scrollOffset.y, 100f);
        }
    }

    /// <summary>
    /// PlayMode: the geometry vocabulary (T-3474). Before it the detector knew one question --
    /// "is a visible SusComponent 0x0?" -- and printed "0 anomalies" over the owner's frame where
    /// a table icon hung out of its row and a progress bar was cut by its column. Every class
    /// below is planted with a positive case AND a negative control, because a detector that
    /// fires on healthy layout is the same lie in the other direction.
    /// </summary>
    public class SusUiProbeGeometryPlaymodeTests : UIDocumentTestHelper
    {
        static VisualElement Box(string name, float w, float h)
        {
            var el = new VisualElement { name = name };
            el.style.position = Position.Absolute;
            el.style.left = 0;
            el.style.top = 0;
            el.style.width = w;
            el.style.height = h;
            return el;
        }

        static VisualElement Child(string name, float w, float h)
        {
            var el = new VisualElement { name = name };
            el.style.width = w;
            el.style.height = h;
            el.style.minWidth = w;
            el.style.minHeight = h;
            el.style.flexShrink = 0;
            return el;
        }

        [UnityTest]
        public IEnumerator OutOfBounds_ChildTallerThanRow_IsNamedWithPixels()
        {
            // The owner's defect in miniature: a 64px glyph inside a 40px row.
            var row = Box("row", 200f, 40f);
            row.Add(Child("glyph", 64f, 64f));
            Root.Add(row);
            yield return WaitFrames(3);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.Contains(SusUiProbe.ClassOutOfBounds, json);
            StringAssert.Contains("#glyph", json);
            StringAssert.Contains("24px", json);
            StringAssert.Contains("bottom", json);
        }

        [UnityTest]
        public IEnumerator OutOfBounds_ChildFitsRow_NoAnomaly()
        {
            var row = Box("row-ok", 200f, 40f);
            row.Add(Child("glyph-ok", 24f, 24f));
            Root.Add(row);
            yield return WaitFrames(3);

            var anomalies = SusUiProbe.GetAnomalies(Root);

            CollectionAssert.IsEmpty(anomalies);
        }

        [UnityTest]
        public IEnumerator Clipped_ParentHidesOverflow_IsNamedClipped()
        {
            var cell = Box("cell", 120f, 32f);
            cell.style.overflow = Overflow.Hidden;
            cell.Add(Child("bar", 200f, 20f));
            Root.Add(cell);
            yield return WaitFrames(3);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.Contains(SusUiProbe.ClassClipped, json);
            StringAssert.Contains("#bar", json);
            StringAssert.Contains("80px", json);
            StringAssert.Contains("right", json);
        }

        [UnityTest]
        public IEnumerator Clipped_ContentFitsClippingParent_NoAnomaly()
        {
            var cell = Box("cell-ok", 120f, 32f);
            cell.style.overflow = Overflow.Hidden;
            cell.Add(Child("bar-ok", 100f, 20f));
            Root.Add(cell);
            yield return WaitFrames(3);

            var anomalies = SusUiProbe.GetAnomalies(Root);

            CollectionAssert.IsEmpty(anomalies);
        }

        [UnityTest]
        public IEnumerator Overlap_NegativeMarginSiblings_IsNamedWithPixels()
        {
            var row = Box("overlap-row", 300f, 40f);
            row.style.flexDirection = FlexDirection.Row;
            row.Add(Child("left", 100f, 20f));
            var right = Child("right", 100f, 20f);
            right.style.marginLeft = -40f;
            row.Add(right);
            Root.Add(row);
            yield return WaitFrames(3);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.Contains(SusUiProbe.ClassOverlap, json);
            StringAssert.Contains("#left", json);
            StringAssert.Contains("#right", json);
            StringAssert.Contains("40x20px", json);
        }

        [UnityTest]
        public IEnumerator Overlap_PlainRow_NoAnomaly()
        {
            var row = Box("row-plain", 300f, 40f);
            row.style.flexDirection = FlexDirection.Row;
            row.Add(Child("a", 100f, 20f));
            row.Add(Child("b", 100f, 20f));
            Root.Add(row);
            yield return WaitFrames(3);

            var anomalies = SusUiProbe.GetAnomalies(Root);

            CollectionAssert.IsEmpty(anomalies);
        }

        [UnityTest]
        public IEnumerator OffCanvas_ElementParkedOutsidePanel_IsNamed()
        {
            var stray = Box("stray", 40f, 40f);
            stray.style.left = -4000f;
            stray.style.top = -4000f;
            Root.Add(stray);
            yield return WaitFrames(3);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.Contains(SusUiProbe.ClassOffCanvas, json);
            StringAssert.Contains("#stray", json);
        }

        [UnityTest]
        public IEnumerator OffCanvas_ElementInsidePanel_NoAnomaly()
        {
            Root.Add(Box("home", 40f, 40f));
            yield return WaitFrames(3);

            var anomalies = SusUiProbe.GetAnomalies(Root);

            CollectionAssert.IsEmpty(anomalies);
        }

        [UnityTest]
        public IEnumerator OverlayOnAbsolute_IsExemptWithReason_NotAnomaly()
        {
            // An overlay leaving its host is the legitimate case the card asks to DECLARE,
            // not to pass over in silence: it must still be measured and still be printed.
            var host = Box("overlay-host", 80f, 24f);
            var pop = Child("popup", 200f, 120f);
            pop.style.position = Position.Absolute;
            pop.style.left = 0;
            pop.style.top = 24f;
            host.Add(pop);
            Root.Add(host);
            yield return WaitFrames(3);

            var exempt = new List<string>();
            var anomalies = SusUiProbe.GetAnomalies(Root, exempt);

            CollectionAssert.IsEmpty(anomalies);
            Assert.IsTrue(exempt.Exists(l => l.Contains("#popup") && l.Contains("absolute")),
                "overlay must be reported as exempt WITH a reason, not silently dropped: "
                + string.Join(" | ", exempt));
        }

        [UnityTest]
        public IEnumerator DeclaredExemptClass_MovesFindingToExemptList()
        {
            var row = Box("declared-row", 200f, 40f);
            var glyph = Child("declared-glyph", 64f, 64f);
            glyph.AddToClassList("sus-anomaly-ok");
            row.Add(glyph);
            Root.Add(row);
            yield return WaitFrames(3);

            var exempt = new List<string>();
            var anomalies = SusUiProbe.GetAnomalies(Root, exempt);

            CollectionAssert.IsEmpty(anomalies);
            Assert.IsTrue(exempt.Exists(l => l.Contains("#declared-glyph") && l.Contains("sus-anomaly-ok")),
                "declared exemption must carry its reason into the exempt list: "
                + string.Join(" | ", exempt));
        }

        [UnityTest]
        public IEnumerator ScrolledContent_IsNotAnAnomaly()
        {
            var sv = new ScrollView { name = "probe-scroll" };
            sv.style.position = Position.Absolute;
            sv.style.left = 0;
            sv.style.top = 0;
            sv.style.width = 200f;
            sv.style.height = 100f;
            for (int i = 0; i < 20; i++)
                sv.Add(Child("row-" + i, 180f, 30f));
            Root.Add(sv);
            yield return WaitFrames(4);

            var anomalies = SusUiProbe.GetAnomalies(Root);

            CollectionAssert.IsEmpty(anomalies);
        }

        [UnityTest]
        public IEnumerator HealthJson_PrintsTheVocabularyItAsked()
        {
            Root.Add(Box("plain", 40f, 40f));
            yield return WaitFrames(3);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.Contains("\"vocabulary\"", json);
            foreach (var cls in SusUiProbe.AnomalyClasses)
                StringAssert.Contains(cls, json);
        }

        [UnityTest]
        public IEnumerator TableRowsUnderClosedComposite_AreVisited()
        {
            // T-2849 parity: a MultiColumnListView/ListView reports childCount == 0, so the old
            // Walk never reached a single table row -- the second reason the owner's frame said
            // "0 anomalies". A defect planted inside such a composite must now be named.
            var list = new ListView { name = "closed-composite" };
            list.style.position = Position.Absolute;
            list.style.left = 0;
            list.style.top = 0;
            list.style.width = 300f;
            list.style.height = 200f;
            Root.Add(list);
            yield return WaitFrames(2);

            var host = list.hierarchy.childCount > 0 ? list.hierarchy[0] : null;
            Assert.IsNotNull(host, "ListView must expose a physical child to host the planted row");
            Assert.AreEqual(0, list.childCount, "fixture is only meaningful while the logical view is empty");

            var row = Box("planted-row", 200f, 40f);
            row.Add(Child("planted-glyph", 64f, 64f));
            host.hierarchy.Add(row);
            yield return WaitFrames(3);

            var json = SusUiProbe.GetHealthJson(Root);

            StringAssert.Contains("#planted-glyph", json);
        }
    }
}
#endif
