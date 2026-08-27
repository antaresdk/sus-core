#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
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
            // "не парсится"), not just the one field. Reflection: F() is a private static
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
}
#endif
