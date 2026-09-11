using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-3293 (step 5 of ARCH-20260910-SHARQ-STYLE-LAYER.md §6, DoD §7 item 3) —
    /// <see cref="RungResolver"/>'s compile-time arithmetic: <c>rung(family, rung)</c> resolves
    /// to <c>var(&lt;token&gt;, &lt;value at lg×default&gt;px)</c>, a family outside the ladder
    /// and a rung outside the family are both compile errors naming file + line.
    ///
    /// Two tiers, deliberately kept apart:
    ///  • the bulk of the cases below run against a small SYNTHETIC <see cref="DimensionScale"/>
    ///    (<see cref="SyntheticScaleJson"/>) so they stay correct regardless of how the real
    ///    ladder (`docs-canon/data/dimension-scale.json`) is edited by unrelated cards later —
    ///    that file already has its own judge (`dim-scale --check`, R133) and re-deciding its
    ///    numbers here would just be a second, driftable copy of the same fact;
    ///  • <see cref="Integration_RealLadder_ResolvesThroughSharqFileParser"/> is the one test that
    ///    DOES load the real file, proving the disk-lookup + <see cref="SharqFileParser"/> wiring
    ///    (path walk-up, line numbers) works end to end — same split T-3289/T-3290 already use
    ///    (corpus-idempotency vs. exact fixtures) for the same reason.
    /// </summary>
    public class RungResolverTests
    {
        // Base row (lg×default) values baked in for the synthetic families below, computed by
        // hand once and re-derived in comments next to each assertion — not read back from the
        // production code under test.
        private const string SyntheticScaleJson = @"{
            ""schema"": ""sus-dimension-scale/v1"",
            ""breakpoints"": [""sm"",""md"",""lg"",""xl"",""2xl""],
            ""densities"": [""compact"",""default"",""comfortable""],
            ""families"": {
                ""test-tier"": {
                    ""breakpoint"": ""variable"",
                    ""token"": ""--sk-test-{rung}"",
                    ""rungs"": [""xs"",""sm"",""md""],
                    ""steps"": [[10,20,30],[11,21,31],[12,22,32]],
                    ""bp"": {""sm"":0,""md"":0,""lg"":1,""xl"":2,""2xl"":2},
                    ""dens"": {""compact"":-1,""default"":0,""comfortable"":1}
                },
                ""test-named"": {
                    ""breakpoint"": ""variable"",
                    ""tokens"": {""--sk-tooltip-max-width"":""tooltip"",""--sk-menu-min-width"":""menu""},
                    ""rungs"": [""tooltip"",""menu""],
                    ""steps"": [[100,50],[120,60]],
                    ""bp"": {""sm"":0,""md"":0,""lg"":1,""xl"":1,""2xl"":1},
                    ""dens"": {""compact"":0,""default"":0,""comfortable"":0}
                },
                ""test-half"": {
                    ""breakpoint"": ""derived"",
                    ""derive"": {""from"": ""test-tier"", ""op"": ""half""},
                    ""token"": ""--sk-test-half-{rung}"",
                    ""rungs"": [""xs"",""sm"",""md""]
                },
                ""test-neg"": {
                    ""breakpoint"": ""derived"",
                    ""derive"": {""from"": ""test-tier"", ""op"": ""neg""},
                    ""token"": ""--sk-test-neg-{rung}"",
                    ""rungs"": [""xs"",""sm"",""md""]
                },
                ""test-invariant"": {
                    ""breakpoint"": ""invariant"",
                    ""values"": {""--sk-test-thin"": 1}
                }
            }
        }";

        private static DimensionScale Scale() => DimensionScale.Parse(SyntheticScaleJson);

        // ─────────────────────────────────────────────────────────────
        //  MiniJson
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void MiniJson_ParsesNestedObjectsArraysAndScalars()
        {
            const string json = "{\"a\": 1, \"b\": [1,2,3], \"c\": {\"d\": \"x\\\"y\"}, \"e\": true, \"f\": null, \"g\": -1.5}";

            var root = MiniJson.Deserialize(json) as System.Collections.Generic.Dictionary<string, object>;

            Assert.IsNotNull(root);
            Assert.AreEqual(1L, root["a"]);
            var arr = root["b"] as System.Collections.Generic.List<object>;
            Assert.AreEqual(3, arr.Count);
            Assert.AreEqual(3L, arr[2]);
            var nested = root["c"] as System.Collections.Generic.Dictionary<string, object>;
            Assert.AreEqual("x\"y", nested["d"]);
            Assert.AreEqual(true, root["e"]);
            Assert.IsNull(root["f"]);
            Assert.AreEqual(-1.5, (double)root["g"]);
        }

        // ─────────────────────────────────────────────────────────────
        //  DimensionScale — family shape
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void DimensionScale_VariableFamily_HasLadder()
        {
            var scale = Scale();
            Assert.IsTrue(scale.Families["test-tier"].HasLadder);
            Assert.AreEqual("--sk-test-xs", scale.Families["test-tier"].TokenNameFor("xs"));
        }

        [Test]
        public void DimensionScale_NamedTierFamily_ResolvesTokenFromTokensMap()
        {
            var scale = Scale();
            Assert.AreEqual("--sk-tooltip-max-width", scale.Families["test-named"].TokenNameFor("tooltip"));
        }

        [Test]
        public void DimensionScale_InvariantFamily_HasNoLadder()
        {
            var scale = Scale();
            Assert.IsFalse(scale.Families["test-invariant"].HasLadder);
        }

        // ─────────────────────────────────────────────────────────────
        //  RungResolver.TryResolve — the arithmetic (D-7)
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void TryResolve_VariableFamily_UsesLgDefaultStep()
        {
            // bp.lg=1 + dens.default=0 = step 1 -> steps[1] = [11,21,31]; "sm" is rung index 1 -> 21.
            var ok = RungResolver.TryResolve(Scale(), "test-tier", "sm", out var replacement, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("var(--sk-test-sm, 21px)", replacement);
        }

        [Test]
        public void TryResolve_NamedTierFamily_UsesOwnTokensMap()
        {
            // step 1 -> steps[1] = [120,60]; "menu" is rung index 1 -> 60.
            var ok = RungResolver.TryResolve(Scale(), "test-named", "menu", out var replacement, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("var(--sk-menu-min-width, 60px)", replacement);
        }

        [Test]
        public void TryResolve_DerivedFamily_Half_RoundsAwayFromZero()
        {
            // Donor test-tier step 1 -> [11,21,31]; "md" -> 31; half(31) rounds to 16 (15.5 AwayFromZero).
            var ok = RungResolver.TryResolve(Scale(), "test-half", "md", out var replacement, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("var(--sk-test-half-md, 16px)", replacement);
        }

        [Test]
        public void TryResolve_DerivedFamily_Neg_Negates()
        {
            // Donor step 1 -> [11,21,31]; "xs" -> 11; neg -> -11.
            var ok = RungResolver.TryResolve(Scale(), "test-neg", "xs", out var replacement, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("var(--sk-test-neg-xs, -11px)", replacement);
        }

        [Test]
        public void TryResolve_UnknownFamily_FailsWithLadderOutError()
        {
            var ok = RungResolver.TryResolve(Scale(), "does-not-exist", "xs", out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("is not in the dimension ladder", error);
            StringAssert.Contains("does-not-exist", error);
        }

        [Test]
        public void TryResolve_InvariantFamily_TreatedAsOutsideLadder()
        {
            // D-7's "family outside the ladder" — an invariant family has no rung concept at all
            // (no bp/dens/steps by design, plan §0), so rung() on it is the same error class as
            // a family that doesn't exist in the JSON.
            var ok = RungResolver.TryResolve(Scale(), "test-invariant", "thin", out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("is not in the dimension ladder", error);
        }

        [Test]
        public void TryResolve_RungOutsideFamily_Fails()
        {
            var ok = RungResolver.TryResolve(Scale(), "test-tier", "3xl", out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("is not in family", error);
            StringAssert.Contains("3xl", error);
        }

        // ─────────────────────────────────────────────────────────────
        //  RungResolver.ResolveAll — text pass, line numbers, passthrough
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void ResolveAll_NoRungCall_ReturnsSameTextUntouched()
        {
            const string body = ".a { color: red; }";

            var result = RungResolver.ResolveAll(body, scale: null, loadError: "unused", fileLabel: "X.sharq", startLine: 1);

            Assert.AreSame(body, result); // fast-path: same reference, not just equal text
        }

        [Test]
        public void ResolveAll_ReplacesCallInPlace_KeepsRestOfDeclarationVerbatim()
        {
            const string body = "min-height: rung(test-tier, sm); color: red;";

            var result = RungResolver.ResolveAll(body, Scale(), loadError: null, fileLabel: "X.sharq", startLine: 1);

            Assert.AreEqual("min-height: var(--sk-test-sm, 21px); color: red;", result);
        }

        [Test]
        public void ResolveAll_MultipleCallsOnDifferentLines_ReportBothWithCorrectLines()
        {
            // Line 1 relative to startLine=5 -> file line 5; second call on line 3 relative -> file line 7.
            const string body = "min-height: rung(test-tier, sm);\n" +
                                 "width: rung(unknown-fam, xs);\n" +
                                 "height: rung(test-tier, huge);\n";

            var ex = Assert.Throws<InvalidOperationException>(() =>
                RungResolver.ResolveAll(body, Scale(), loadError: null, fileLabel: "SusThing.sharq", startLine: 5));

            StringAssert.Contains("SusThing.sharq:6:", ex.Message); // "width: rung(unknown-fam" is body line 2 -> 5+1
            StringAssert.Contains("SusThing.sharq:7:", ex.Message); // "height: rung(test-tier, huge" is body line 3 -> 5+2
            StringAssert.Contains("unknown-fam", ex.Message);
            StringAssert.Contains("huge", ex.Message);
        }

        [Test]
        public void ResolveAll_LoadFailure_ReportsLoadErrorPerCall()
        {
            const string body = "min-height: rung(test-tier, sm);";

            var ex = Assert.Throws<InvalidOperationException>(() =>
                RungResolver.ResolveAll(body, scale: null, loadError: "dimension-scale.json not found",
                    fileLabel: "X.sharq", startLine: 1));

            StringAssert.Contains("X.sharq:1:", ex.Message);
            StringAssert.Contains("dimension-scale.json not found", ex.Message);
        }

        // ─────────────────────────────────────────────────────────────
        //  Integration — real ladder, through SharqFileParser (wiring, not arithmetic)
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Integration_RealLadder_ResolvesThroughSharqFileParser()
        {
            // A real absolute path inside the repo (this project's Assets/..), so RungResolver's
            // directory walk-up actually finds docs-canon/data/dimension-scale.json — exactly
            // what happens for a real .sharq compiled inside the monorepo's dev projects.
            var probePath = Path.Combine(Application.dataPath, "..", "RungIntegrationProbe.sharq");

            const string sharq =
                "<template><ui:VisualElement /></template>\n" +
                "<style scoped>\n.probe { min-height: rung(icon, md); }\n</style>";

            var model = SharqFileParser.Parse(sharq, probePath);

            // icon family, lg×default step: bp.lg=1 + dens.default=0 = step 1 -> steps[1] =
            // [12,16,20,24,28] (docs-canon/data/dimension-scale.json, rungs xs/sm/md/lg/xl) ->
            // "md" is rung index 2 -> 20 — cross-checked against the family's own note
            // ("ladder step 1 == :root (--sk-icon-size 20)").
            StringAssert.Contains("var(--sk-icon-md, 20px)", model.StyleBody);
            StringAssert.DoesNotContain("rung(", model.StyleBody);
        }

        [Test]
        public void Integration_RealLadder_UnknownFamily_ThrowsWithFileAndLine()
        {
            var probePath = Path.Combine(Application.dataPath, "..", "RungIntegrationProbe.sharq");

            const string sharq =
                "<template><ui:VisualElement /></template>\n" +
                "<style scoped>\n.probe {\n    min-height: rung(not-a-real-family, xs);\n}\n</style>";

            var ex = Assert.Throws<InvalidOperationException>(() => SharqFileParser.Parse(sharq, probePath));

            StringAssert.Contains("RungIntegrationProbe.sharq:4:", ex.Message);
            StringAssert.Contains("not-a-real-family", ex.Message);
            StringAssert.Contains("is not in the dimension ladder", ex.Message);
        }
    }
}
