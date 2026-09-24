using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Sharq.Core.Editor.TestSupport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-4159 (skin sheet layer, step S1) — cost measurement of trailing sheets on a lobby-sized
    /// tree: 170 styled components, 5 component levels deep, six component "types" with a
    /// 40-rule own sheet each. Measures, median of several runs:
    /// <list type="bullet">
    /// <item>full restyle of the tree (all sheets marked dirty + one styles pass) with no layer,
    /// with a small override layer (30 rules) and with a large one (600 rules — the "author put
    /// the whole brand sheet into the layer" case the plan warns about);</item>
    /// <item><see cref="SusComponent.SetTrailingSheets"/> / <see cref="SusComponent.ClearTrailingSheets"/>
    /// call cost on 170 live recipients, plus the styles pass that follows;</item>
    /// <item>attaching the whole tree with and without a registered scope.</item>
    /// </list>
    /// Numbers go to the log with the <c>[T-4159 perf]</c> prefix. Only generous sanity bounds are
    /// asserted — the numbers are the deliverable, not a CI gate.
    /// </summary>
    public class SusTrailingSheetsPerfTests
    {
        private const int Runs = 15;
        private const int TypeCount = 6;

        internal sealed class PerfComp : SusComponent
        {
            internal static StyleSheet NextSheet;
            internal static string NextClass;

            protected override void Build()
            {
                if (NextClass != null) AddToClassList(NextClass);
                if (NextSheet != null) styleSheets.Add(NextSheet);
            }
        }

        private EditorWindow _window;
        private VisualElement _root;
        private readonly List<Object> _owned = new List<Object>();
        private StyleSheet[] _typeSheets;
        private MethodInfo _applyStyles;
        private MethodInfo _dirtyStyleSheets;
        private int _componentCount;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _window = SusEditorWindowTestHost.CreateAndShow(1400f, 900f);
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
            if (SusRuntimeHotReload.StyleSheetFromUss == null)
                Assert.Ignore("No USS-from-text factory registered.");
            _root = new VisualElement { name = "t4159-perf-root" };
            _window.rootVisualElement.Add(_root);
            _applyStyles = FindPanelMethod(_root.panel, "ApplyStyles");
            _dirtyStyleSheets = FindPanelMethod(_root.panel, "DirtyStyleSheets");
            if (_applyStyles == null)
                Assert.Inconclusive("Panel has no ApplyStyles() on this Unity version — cannot time a styles pass.");

            _typeSheets = new StyleSheet[TypeCount];
            for (int t = 0; t < TypeCount; t++)
                _typeSheets[t] = Uss($"PerfType{t}.g", OwnSheetUss(t, 40));
        }

        [TearDown]
        public void TearDown()
        {
            SusComponent.ClearTrailingSheets(_root);
            _root.RemoveFromHierarchy();
            PerfComp.NextSheet = null;
            PerfComp.NextClass = null;
            foreach (var o in _owned) if (o != null) Object.DestroyImmediate(o);
            _owned.Clear();
        }

        private StyleSheet Uss(string name, string uss)
        {
            var s = SusRuntimeHotReload.StyleSheetFromUss(uss, name);
            Assert.IsNotNull(s, $"factory returned no sheet for {name}");
            _owned.Add(s);
            return s;
        }

        private static MethodInfo FindPanelMethod(IPanel panel, string name)
        {
            for (var t = panel?.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, System.Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
        }

        private static string OwnSheetUss(int type, int rules)
        {
            var sb = new StringBuilder();
            sb.Append($".perf-t{type} {{ padding-left: 2px; background-color: rgb(10, 20, {type * 30}); }}\n");
            for (int k = 1; k < rules; k++)
                sb.Append($".perf-t{type}--v{k} {{ margin-left: {k % 7}px; border-left-width: {k % 3}px; }}\n");
            return sb.ToString();
        }

        /// <summary>Override rules at the same specificity as the own sheets, cycling over the types.</summary>
        private static string OverrideUss(int rules)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < rules; k++)
            {
                var t = k % TypeCount;
                if (k < TypeCount)
                    sb.Append($".perf-t{t} {{ background-color: rgb(200, 10, {t * 30}); padding-left: 3px; }}\n");
                else
                    sb.Append($".perf-t{t}--v{k} {{ margin-right: {k % 5}px; border-right-width: {k % 2}px; }}\n");
            }
            return sb.ToString();
        }

        private PerfComp Comp(int type)
        {
            PerfComp.NextSheet = _typeSheets[type];
            PerfComp.NextClass = $"perf-t{type}";
            var c = new PerfComp();
            PerfComp.NextSheet = null;
            PerfComp.NextClass = null;
            _componentCount++;
            return c;
        }

        /// <summary>
        /// Lobby-shaped tree: screen → header → 6 buttons; 6 panels → title, list → 12 rows → badge.
        /// 1 + 1 + 6 + 6 × (1 + 1 + 1 + 12 + 12) = 170 components, 5 component levels.
        /// </summary>
        private VisualElement BuildLobby()
        {
            _componentCount = 0;
            var screen = Comp(0);
            var header = Comp(1);
            screen.Add(header);
            for (int i = 0; i < 6; i++) header.Add(Comp(2));
            for (int p = 0; p < 6; p++)
            {
                var panel = Comp(3);
                screen.Add(panel);
                panel.Add(Comp(1));
                var list = Comp(4);
                panel.Add(list);
                for (int r = 0; r < 12; r++)
                {
                    var row = Comp(5);
                    list.Add(row);
                    row.Add(Comp(2));
                }
            }
            return screen;
        }

        private void StylesPass() => _applyStyles.Invoke(_root.panel, null);

        private double TimeRestyle(VisualElement tree)
        {
            var samples = new List<double>(Runs);
            for (int i = 0; i < Runs; i++)
            {
                // Full re-match: every sheet dirty (falls back to a class toggle on the tree root).
                if (_dirtyStyleSheets != null) _dirtyStyleSheets.Invoke(_root.panel, null);
                else tree.EnableInClassList("perf-toggle", i % 2 == 0);
                var sw = Stopwatch.StartNew();
                StylesPass();
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
            return Median(samples);
        }

        private static double Median(List<double> xs)
        {
            xs.Sort();
            return xs[xs.Count / 2];
        }

        private static void Report(string line)
        {
            Debug.Log("[T-4159 perf] " + line);
            TestContext.Out.WriteLine("[T-4159 perf] " + line);
        }

        [Test]
        public void Lobby170_RestyleAndSetClearCost()
        {
            var tree = BuildLobby();
            Assert.AreEqual(170, _componentCount, "fixture: lobby tree size");
            _root.Add(tree);
            StylesPass();

            var small = new[] { Uss("PerfOverrides.small", OverrideUss(30)) };
            var large = new[] { Uss("PerfOverrides.large", OverrideUss(600)) };

            var restyleNone = TimeRestyle(tree);

            // Set / Clear call cost + the styles pass right after (median over runs).
            var setCall = new List<double>();
            var setPass = new List<double>();
            var clearCall = new List<double>();
            var clearPass = new List<double>();
            for (int i = 0; i < Runs; i++)
            {
                var sw = Stopwatch.StartNew();
                SusComponent.SetTrailingSheets(_root, small);
                sw.Stop();
                setCall.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                StylesPass();
                sw.Stop();
                setPass.Add(sw.Elapsed.TotalMilliseconds);

                sw.Restart();
                SusComponent.ClearTrailingSheets(_root);
                sw.Stop();
                clearCall.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                StylesPass();
                sw.Stop();
                clearPass.Add(sw.Elapsed.TotalMilliseconds);
            }

            SusComponent.SetTrailingSheets(_root, small);
            StylesPass();
            var tailed = 0;
            tree.Query<PerfComp>().ForEach(c => { if (c.TrailingSheets.Count == 1) tailed++; });
            Assert.AreEqual(170, tailed, "every styled component carries the tail");
            var restyleSmall = TimeRestyle(tree);

            SusComponent.SetTrailingSheets(_root, large);
            StylesPass();
            var restyleLarge = TimeRestyle(tree);
            SusComponent.ClearTrailingSheets(_root);
            StylesPass();

            Report($"components=170 depth=5 ownRules=40/type restyle(no layer)={restyleNone:0.000}ms " +
                   $"restyle(layer 30 rules)={restyleSmall:0.000}ms restyle(layer 600 rules)={restyleLarge:0.000}ms");
            Report($"SetTrailingSheets call={Median(setCall):0.000}ms +stylesPass={Median(setPass):0.000}ms; " +
                   $"ClearTrailingSheets call={Median(clearCall):0.000}ms +stylesPass={Median(clearPass):0.000}ms");

            Assert.Less(Median(setCall), 50.0, "Set on 170 recipients stays well under a frame budget multiple");
            Assert.Less(Median(clearCall), 50.0, "Clear on 170 recipients stays well under a frame budget multiple");
        }

        [Test]
        public void Lobby170_AttachCost_WithAndWithoutScope()
        {
            var small = new[] { Uss("PerfOverrides.small", OverrideUss(30)) };
            var without = new List<double>();
            var with = new List<double>();
            var withoutPass = new List<double>();
            var withPass = new List<double>();

            for (int i = 0; i < Runs; i++)
            {
                var tree = BuildLobby();
                var sw = Stopwatch.StartNew();
                _root.Add(tree);
                sw.Stop();
                without.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                StylesPass();
                sw.Stop();
                withoutPass.Add(sw.Elapsed.TotalMilliseconds);
                tree.RemoveFromHierarchy();

                SusComponent.SetTrailingSheets(_root, small);
                tree = BuildLobby();
                sw.Restart();
                _root.Add(tree);
                sw.Stop();
                with.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                StylesPass();
                sw.Stop();
                withPass.Add(sw.Elapsed.TotalMilliseconds);
                Assert.AreEqual(1, ((PerfComp)tree).TrailingSheets.Count, "attached under the scope picks up the tail");
                tree.RemoveFromHierarchy();
                SusComponent.ClearTrailingSheets(_root);
            }

            Report($"attach 170 components: no scope={Median(without):0.000}ms +stylesPass={Median(withoutPass):0.000}ms; " +
                   $"scope with 30-rule layer={Median(with):0.000}ms +stylesPass={Median(withPass):0.000}ms");
        }
    }
}
