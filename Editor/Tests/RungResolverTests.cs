using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-3293 (step 5 of ARCH-20260910-SHARQ-STYLE-LAYER.md §6, DoD §7 item 3), rewritten for
    /// T-3674 (plan `ARCH-20260918-RUNG-LADDER-SHIP.md` §6 S2, DoD §8 item 1) —
    /// <see cref="RungResolver"/>'s compile-time arithmetic: <c>rung(family, rung)</c> resolves
    /// to <c>var(&lt;token&gt;, &lt;value&gt;px)</c> against the package-shipped
    /// <see cref="RungLadder"/> table, a family outside the ladder and a rung outside the family
    /// are both compile errors naming file + line, and a malformed call (D-4) is too.
    ///
    /// Values asserted here are read from <see cref="RungLadder"/> itself (not hand-copied
    /// numbers) — the table's OWN fidelity to `docs-canon/data/dimension-scale.json` is judged
    /// by `dim-scale --check` / plant-t3673 (T-3673), a second copy of that judgment here would
    /// drift the moment either side changes. What these tests own is the RESOLUTION arithmetic:
    /// given a family+rung, does <see cref="RungResolver"/> produce the entry the table already
    /// carries, verbatim.
    /// </summary>
    public class RungResolverTests
    {
        // ─────────────────────────────────────────────────────────────
        //  MiniJson — unrelated to the ladder, still read by SharqSourceMapWriter
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
        //  RungResolver.TryResolve — the arithmetic, against the real table (D-1)
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void TryResolve_KnownFamilyAndRung_MatchesTableEntryVerbatim()
        {
            var entries = RungLadder.Table["control-height"];
            var xs = entries.First(e => e.Rung == "xs");

            var ok = RungResolver.TryResolve("control-height", "xs", out var replacement, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual($"var({xs.Token}, {xs.Value}px)", replacement);
        }

        [Test]
        public void TryResolve_DerivedFamily_PillXs_ResolvesToTablesOwnValue()
        {
            // "pill" is a `derived` family in the source ladder (half of space's donor step) — the
            // table already carries the computed number, RungResolver does not re-derive it.
            var entries = RungLadder.Table["pill"];
            var xs = entries.First(e => e.Rung == "xs");

            var ok = RungResolver.TryResolve("pill", "xs", out var replacement, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual($"var({xs.Token}, {xs.Value}px)", replacement);
        }

        [Test]
        public void TryResolve_UnknownFamily_FailsWithLadderOutError()
        {
            var ok = RungResolver.TryResolve("does-not-exist", "xs", out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("is not in the dimension ladder", error);
            StringAssert.Contains("does-not-exist", error);
            StringAssert.Contains("control-height", error); // "available: ..." lists real families
        }

        [Test]
        public void TryResolve_RungOutsideFamily_Fails()
        {
            var ok = RungResolver.TryResolve("control-height", "not-a-rung", out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("is not in family", error);
            StringAssert.Contains("not-a-rung", error);
        }

        // ─────────────────────────────────────────────────────────────
        //  RungResolver.ResolveAll — text pass, line numbers, passthrough, D-4 malformed calls
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void ResolveAll_NoRungCall_ReturnsSameTextUntouched()
        {
            const string body = ".a { color: red; }";

            var result = RungResolver.ResolveAll(body, fileLabel: "X.sharq", startLine: 1);

            Assert.AreSame(body, result); // fast-path: same reference, not just equal text
        }

        [Test]
        public void ResolveAll_ReplacesCallInPlace_KeepsRestOfDeclarationVerbatim()
        {
            var icon = RungLadder.Table["icon"].First(e => e.Rung == "md");
            var body = "min-height: rung(icon, md); color: red;";

            var result = RungResolver.ResolveAll(body, fileLabel: "X.sharq", startLine: 1);

            Assert.AreEqual($"min-height: var({icon.Token}, {icon.Value}px); color: red;", result);
        }

        [Test]
        public void ResolveAll_MultipleCallsOnDifferentLines_ReportBothWithCorrectLines()
        {
            // Line 1 relative to startLine=5 -> file line 5; second call on line 3 relative -> file line 7.
            const string body = "min-height: rung(icon, md);\n" +
                                 "width: rung(unknown-fam, xs);\n" +
                                 "height: rung(icon, huge);\n";

            var ex = Assert.Throws<InvalidOperationException>(() =>
                RungResolver.ResolveAll(body, fileLabel: "SusThing.sharq", startLine: 5));

            StringAssert.Contains("SusThing.sharq:6:", ex.Message); // "width: rung(unknown-fam" is body line 2 -> 5+1
            StringAssert.Contains("SusThing.sharq:7:", ex.Message); // "height: rung(icon, huge" is body line 3 -> 5+2
            StringAssert.Contains("unknown-fam", ex.Message);
            StringAssert.Contains("huge", ex.Message);
        }

        [Test]
        public void ResolveAll_MalformedArgument_D4_ReportsCallTextAndLine()
        {
            // T-3675 live defect: an unrelated literal-scan tool rewrote the rung NAME argument
            // into `var(--sk-space-10, 10)` — the regex never matches this call shape, and the
            // old resolver shipped it verbatim into the .g.uss. D-4: this is now a compile error.
            const string body = "padding-left: rung(space, var(--sk-space-10, 10));";

            var ex = Assert.Throws<InvalidOperationException>(() =>
                RungResolver.ResolveAll(body, fileLabel: "SusThing.sharq", startLine: 3));

            StringAssert.Contains("SusThing.sharq:3:", ex.Message);
            StringAssert.Contains("malformed", ex.Message);
            StringAssert.Contains("rung(space, var(--sk-space-10, 10))", ex.Message);
        }

        [Test]
        public void ResolveAll_AfterAllCallsResolved_NoRungSubstringSurvives()
        {
            var body = "min-height: rung(icon, md); width: rung(control-height, xs);";

            var result = RungResolver.ResolveAll(body, fileLabel: "X.sharq", startLine: 1);

            StringAssert.DoesNotContain("rung(", result);
        }

        // ─────────────────────────────────────────────────────────────
        //  Integration — through SharqFileParser, from a .sharq OUTSIDE the monorepo (D-1)
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Integration_OutsideMonorepo_ResolvesThroughSharqFileParser()
        {
            // A path under the OS temp directory, nowhere near this repo's docs-canon — proves
            // resolution no longer needs a disk walk-up to find a ladder file (D-1's whole point:
            // the table ships INSIDE the compiled package, not next to the monorepo sources).
            var probePath = Path.Combine(Path.GetTempPath(), "sus-rung-probe-" + Guid.NewGuid().ToString("N"), "RungIntegrationProbe.sharq");

            var icon = RungLadder.Table["icon"].First(e => e.Rung == "md");
            const string sharq =
                "<template><ui:VisualElement /></template>\n" +
                "<style scoped>\n.probe { min-height: rung(icon, md); }\n</style>";

            var model = SharqFileParser.Parse(sharq, probePath);

            StringAssert.Contains($"var({icon.Token}, {icon.Value}px)", model.StyleBody);
            StringAssert.DoesNotContain("rung(", model.StyleBody);
        }

        [Test]
        public void Integration_OutsideMonorepo_UnknownFamily_ThrowsWithFileAndLine()
        {
            var probePath = Path.Combine(Path.GetTempPath(), "sus-rung-probe-" + Guid.NewGuid().ToString("N"), "RungIntegrationProbe.sharq");

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
