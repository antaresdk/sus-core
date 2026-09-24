using System.Collections.Generic;
using NUnit.Framework;
using Sharq.Core.Editor.TestSupport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-4159 (skin sheet layer, step S1): <see cref="SusComponent.SetTrailingSheets"/> /
    /// <see cref="SusComponent.ClearTrailingSheets"/> — sheet ORDER on live components (the
    /// cascade effect itself is pinned in PlayMode by <c>TrailingSheetsCascadePlaymodeTests</c>
    /// and by S0's <c>UssCascadePrecedencePlaymodeTests</c>). Covers: no layer leaves every
    /// component's sheets untouched, tail after own sheets, removal by the recipient registry,
    /// on/off/on idempotency, nested and dynamically attached components, code-only components,
    /// nearest scope, detach, companion reload/hot reload keeping the tail last, and overlay
    /// copies (inside and outside the scope).
    /// One off-screen editor window per fixture gives the components a real panel (attach events).
    /// </summary>
    public class SusComponentTrailingSheetsTests
    {
        /// <summary>Component carrying one own sheet — stands in for a generated companion sheet.</summary>
        internal sealed class StyledComp : SusComponent
        {
            internal static StyleSheet NextOwn;
            public readonly StyleSheet Own;

            public StyledComp()
            {
                Own = s_lastOwn;
            }

            private static StyleSheet s_lastOwn;

            protected override void Build()
            {
                s_lastOwn = NextOwn;
                if (NextOwn != null) styleSheets.Add(NextOwn);
            }

            /// <summary>Simulates a rebuild that appends a companion sheet and runs the companion load.</summary>
            public void RebuildWithSheet(StyleSheet sheet)
            {
                styleSheets.Add(sheet);
                LoadCompanionStyleSheets();
            }
        }

        /// <summary>Code-only component: no sheet of its own.</summary>
        internal sealed class PlainComp : SusComponent
        {
            protected override void Build() { }
        }

        private EditorWindow _window;
        private VisualElement _root;
        private readonly List<Object> _owned = new List<Object>();
        private readonly List<VisualElement> _scopes = new List<VisualElement>();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _window = SusEditorWindowTestHost.CreateAndShow(800f, 600f);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_window != null) _window.Close();
            _window = null;
        }

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement { name = "t4159-root" };
            _window.rootVisualElement.Add(_root);
            Assert.IsNotNull(_root.panel, "fixture: the editor window must give the root a panel");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _scopes) SusComponent.ClearTrailingSheets(s);
            _scopes.Clear();
            _root.RemoveFromHierarchy();
            StyledComp.NextOwn = null;
            foreach (var o in _owned) if (o != null) Object.DestroyImmediate(o);
            _owned.Clear();
        }

        private StyleSheet NewSheet(string name)
        {
            var s = ScriptableObject.CreateInstance<StyleSheet>();
            s.name = name;
            _owned.Add(s);
            return s;
        }

        private StyledComp NewStyled(string ownName)
        {
            StyledComp.NextOwn = NewSheet(ownName);
            var c = new StyledComp();
            StyledComp.NextOwn = null;
            return c;
        }

        private void Set(VisualElement scope, params StyleSheet[] sheets)
        {
            if (!_scopes.Contains(scope)) _scopes.Add(scope);
            SusComponent.SetTrailingSheets(scope, sheets);
        }

        private static List<StyleSheet> Sheets(VisualElement el)
        {
            var list = new List<StyleSheet>();
            for (int i = 0; i < el.styleSheets.count; i++) list.Add(el.styleSheets[i]);
            return list;
        }

        private static void AssertSheets(VisualElement el, string what, params StyleSheet[] expected)
        {
            CollectionAssert.AreEqual(expected, Sheets(el),
                $"{what}: got [{string.Join(", ", Sheets(el).ConvertAll(s => s != null ? s.name : "null"))}]");
        }

        // ─── No layer ─────────────────────────────────────────────────────

        [Test]
        public void NoScope_ComponentSheetsUnchanged()
        {
            var c = NewStyled("own");
            _root.Add(c);
            AssertSheets(c, "no trailing scope registered", c.Own);
            Assert.AreEqual(0, c.TrailingSheets.Count);
        }

        // ─── Set / Clear on live components ───────────────────────────────

        [Test]
        public void Set_AppendsTailAfterOwnSheet_OnAttachedComponent()
        {
            var c = NewStyled("own");
            _root.Add(c);
            var t1 = NewSheet("tail-1");
            var t2 = NewSheet("tail-2");

            Set(_root, t1, t2);

            AssertSheets(c, "after Set", c.Own, t1, t2);
            CollectionAssert.AreEqual(new[] { t1, t2 }, c.TrailingSheets);
        }

        [Test]
        public void Clear_RemovesTail_RestoresExactList()
        {
            var c = NewStyled("own");
            _root.Add(c);
            var t = NewSheet("tail");
            Set(_root, t);

            SusComponent.ClearTrailingSheets(_root);

            AssertSheets(c, "after Clear", c.Own);
            Assert.AreEqual(0, c.TrailingSheets.Count);
            Assert.AreEqual(0, SusComponent.GetTrailingSheets(_root).Count);
        }

        [Test]
        public void OnOffOn_Idempotent_NoDuplicates()
        {
            var c = NewStyled("own");
            _root.Add(c);
            var t = NewSheet("tail");

            Set(_root, t);
            Set(_root, t);
            AssertSheets(c, "Set twice", c.Own, t);

            SusComponent.ClearTrailingSheets(_root);
            SusComponent.ClearTrailingSheets(_root);
            AssertSheets(c, "Clear twice", c.Own);

            Set(_root, t);
            AssertSheets(c, "on again", c.Own, t);
        }

        [Test]
        public void Set_ReplacesPreviousList()
        {
            var c = NewStyled("own");
            _root.Add(c);
            var a = NewSheet("a");
            var b = NewSheet("b");
            Set(_root, a);

            Set(_root, b, a);

            AssertSheets(c, "replaced list", c.Own, b, a);
        }

        [Test]
        public void Set_EmptyList_ActsAsClear_NullScopeThrows()
        {
            var c = NewStyled("own");
            _root.Add(c);
            Set(_root, NewSheet("tail"));

            SusComponent.SetTrailingSheets(_root, new StyleSheet[0]);

            AssertSheets(c, "empty Set", c.Own);
            Assert.Throws<System.ArgumentNullException>(() =>
                SusComponent.SetTrailingSheets(null, new[] { NewSheet("x") }));
        }

        // ─── Nested / dynamic / code-only ─────────────────────────────────

        [Test]
        public void NestedComponents_EachLevelEndsWithTail()
        {
            var outer = NewStyled("outer");
            var inner = NewStyled("inner");
            var innermost = NewStyled("innermost");
            outer.Add(inner);
            inner.Add(innermost);
            _root.Add(outer);
            var t = NewSheet("tail");

            Set(_root, t);

            AssertSheets(outer, "outer", outer.Own, t);
            AssertSheets(inner, "inner", inner.Own, t);
            AssertSheets(innermost, "innermost", innermost.Own, t);

            SusComponent.ClearTrailingSheets(_root);
            AssertSheets(outer, "outer cleared", outer.Own);
            AssertSheets(inner, "inner cleared", inner.Own);
            AssertSheets(innermost, "innermost cleared", innermost.Own);
        }

        [Test]
        public void DynamicComponent_AttachedAfterSet_PicksUpTail()
        {
            var t = NewSheet("tail");
            Set(_root, t);

            var host = new VisualElement();
            _root.Add(host);
            var late = NewStyled("late");
            host.Add(late);

            AssertSheets(late, "attached after Set", late.Own, t);
        }

        [Test]
        public void CodeOnlyComponent_GetsNoTail_AndIsNotTracked()
        {
            var t = NewSheet("tail");
            Set(_root, t);
            var before = SusComponent.TrailingRecipientCount;

            var plain = new PlainComp();
            _root.Add(plain);

            AssertSheets(plain, "code-only component");
            Assert.AreEqual(before, SusComponent.TrailingRecipientCount, "code-only component is not a recipient");
        }

        [Test]
        public void ComponentOutsideScope_GetsNoTail()
        {
            var scope = new VisualElement { name = "scope" };
            var outside = new VisualElement { name = "outside" };
            _root.Add(scope);
            _root.Add(outside);
            var inScope = NewStyled("in");
            var outOfScope = NewStyled("out");
            scope.Add(inScope);
            outside.Add(outOfScope);
            var t = NewSheet("tail");

            Set(scope, t);

            AssertSheets(inScope, "inside scope", inScope.Own, t);
            AssertSheets(outOfScope, "outside scope", outOfScope.Own);
        }

        [Test]
        public void NestedScopes_NearestWins_ClearFallsBackToOuter()
        {
            var inner = new VisualElement { name = "inner-scope" };
            _root.Add(inner);
            var c = NewStyled("own");
            inner.Add(c);
            var outerTail = NewSheet("outer-tail");
            var innerTail = NewSheet("inner-tail");

            Set(_root, outerTail);
            Set(inner, innerTail);
            AssertSheets(c, "nearest scope", c.Own, innerTail);

            SusComponent.ClearTrailingSheets(inner);
            AssertSheets(c, "falls back to outer", c.Own, outerTail);
        }

        [Test]
        public void ScopeOnComponentItself_AppliesToIt()
        {
            var c = NewStyled("own");
            _root.Add(c);
            var t = NewSheet("tail");

            Set(c, t);

            AssertSheets(c, "scope is the component", c.Own, t);
        }

        // ─── Detach ───────────────────────────────────────────────────────

        [Test]
        public void Detach_StopsTracking_ReattachOutsideScopeDropsTail()
        {
            var scope = new VisualElement { name = "scope" };
            var outside = new VisualElement { name = "outside" };
            _root.Add(scope);
            _root.Add(outside);
            var c = NewStyled("own");
            scope.Add(c);
            var t = NewSheet("tail");
            Set(scope, t);
            var tracked = SusComponent.TrailingRecipientCount;

            c.RemoveFromHierarchy();
            Assert.AreEqual(tracked - 1, SusComponent.TrailingRecipientCount, "detach removes the recipient");

            outside.Add(c);
            AssertSheets(c, "re-attached outside the scope", c.Own);

            c.RemoveFromHierarchy();
            scope.Add(c);
            AssertSheets(c, "re-attached inside the scope", c.Own, t);
        }

        // ─── Invariant: tail stays last ───────────────────────────────────

        [Test]
        public void CompanionLoadAfterRebuild_KeepsTailLast()
        {
            var c = NewStyled("own");
            _root.Add(c);
            var t = NewSheet("tail");
            Set(_root, t);
            var rebuilt = NewSheet("rebuilt.g");

            c.RebuildWithSheet(rebuilt);

            AssertSheets(c, "after rebuild + companion load", c.Own, rebuilt, t);
        }

        [Test]
        public void HotReloadSheet_KeepsTailLast()
        {
            var c = NewStyled("own");
            _root.Add(c);
            var t = NewSheet("tail");
            Set(_root, t);
            var hot = NewSheet("hot");

            c.ApplyHotReloadStyleSheet(".g", hot);

            Assert.AreSame(t, c.styleSheets[c.styleSheets.count - 1], "tail is last after hot reload");
            Assert.AreSame(hot, c.styleSheets[c.styleSheets.count - 2], "hot-reloaded sheet sits right before the tail");
        }

        [Test]
        public void ReloadCompanionStyleSheets_KeepsTail()
        {
            var c = NewStyled("own");
            _root.Add(c);
            var t = NewSheet("tail");
            Set(_root, t);

            c.ReloadCompanionStyleSheets();

            Assert.AreSame(t, c.styleSheets[c.styleSheets.count - 1], "tail is last after companion reload");
        }

        // ─── Overlay copies ───────────────────────────────────────────────

        [Test]
        public void OverlayPortal_InsideScope_CopyCarriesTailLast_AndLosesItOnClear()
        {
            var host = new OverlayHost();
            var comp = NewStyled("own");
            var content = new VisualElement { name = "portaled" };
            comp.Add(content);
            _root.Add(comp);
            _root.Add(host);
            var t = NewSheet("tail");
            Set(_root, t);

            host.AddToOverlay(content, OverlayCategory.Modal);

            Assert.AreSame(host, content.parent, "fixture: content moved into the overlay host");
            Assert.AreSame(t, content.styleSheets[content.styleSheets.count - 1], "copy ends with the tail");
            Assert.IsTrue(content.styleSheets.Contains(comp.Own), "companion sheet copied");

            SusComponent.ClearTrailingSheets(_root);
            Assert.IsFalse(content.styleSheets.Contains(t), "Clear reaches the overlay copy");
            Assert.IsTrue(content.styleSheets.Contains(comp.Own), "companion copy stays");

            Set(_root, t);
            Assert.AreSame(t, content.styleSheets[content.styleSheets.count - 1], "Set again reaches the copy");

            host.ClearAll();
        }

        [Test]
        public void OverlayPortal_PortaledBeforeSet_FollowsLaterSet()
        {
            var host = new OverlayHost();
            var comp = NewStyled("own");
            var content = new VisualElement { name = "portaled" };
            comp.Add(content);
            _root.Add(comp);
            _root.Add(host);
            host.AddToOverlay(content, OverlayCategory.Modal);
            var t = NewSheet("tail");

            Set(_root, t);

            Assert.AreSame(t, content.styleSheets[content.styleSheets.count - 1], "copy made before Set follows it");
            host.ClearAll();
        }

        [Test]
        public void OverlayLayerOutsideScope_DirectlyMountedComponent_GetsNoTail()
        {
            var scope = new VisualElement { name = "sub-root-scope" };
            var host = new OverlayHost();
            _root.Add(scope);
            _root.Add(host); // overlay layer is a sibling of the scope, not a descendant
            var t = NewSheet("tail");
            Set(scope, t);

            var modal = NewStyled("modal");
            host.Add(modal);

            AssertSheets(modal, "component mounted into an overlay layer outside the scope", modal.Own);
            host.ClearAll();
        }

        [Test]
        public void ThemeServiceCopy_FollowsSourceTail()
        {
            var comp = NewStyled("own");
            _root.Add(comp);
            var t = NewSheet("tail");
            Set(_root, t);
            var card = new VisualElement { name = "card" };

            SusThemeService.CopyStyleSheets(comp, card);
            Assert.AreSame(t, card.styleSheets[card.styleSheets.count - 1], "copy ends with the tail");

            SusComponent.ClearTrailingSheets(_root);
            Assert.IsFalse(card.styleSheets.Contains(t), "Clear reaches the copy");
            Assert.IsTrue(card.styleSheets.Contains(comp.Own), "companion copy stays");
        }
    }
}
