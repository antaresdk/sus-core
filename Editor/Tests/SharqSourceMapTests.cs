using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-3295 (step 6 of ARCH-20260910-SHARQ-STYLE-LAYER.md §6, DoD §7 item 3, decision d:927b8e) —
    /// the generat↔source correspondence map (plan §4.4): <c>&lt;Class&gt;.g.uss.map.json</c> /
    /// <c>&lt;Class&gt;_scoped.g.uss.map.json</c>, next to whichever USS it describes.
    ///
    /// Three tiers, same split T-3289/T-3290/T-3293 already established for this compiler:
    ///  • unit tests on the plumbing (<see cref="TextLines"/>, <see cref="CssScanner"/>'s new
    ///    <c>DeclLine</c>, <see cref="SharqSourceMapWriter"/>'s JSON shape/escaping) — synthetic,
    ///    hand-counted inputs;
    ///  • <see cref="RoundTrip_UssLineAndSrcLineNameTheSameDeclaration"/> — the load-bearing one:
    ///    for EVERY row the map claims, look the USS line up in the actual generated USS text and
    ///    the .sharq line up in the actual source text, and assert they name the SAME CSS
    ///    property. This is self-verifying (it never hand-counts a line number), so it stays
    ///    correct across future emitter formatting changes the way <see cref="SharqFixtureTests"/>
    ///    explicitly would NOT (that harness pins exact bytes on purpose — this one pins the
    ///    CORRESPONDENCE, which is the actual contract §4.4 promises);
    ///  • lifecycle tests on <see cref="SharqCompilePipeline.WriteSourceMap"/> (atomic path,
    ///    delete-when-stale, scoped↔global suffix never both present at once).
    /// </summary>
    public class SharqSourceMapTests
    {
        // ───────────────────────── TextLines ─────────────────────────

        [Test]
        public void TextLines_CountsNewlinesInRange()
        {
            const string s = "a\nb\nc\nd";
            Assert.AreEqual(0, TextLines.CountNewlines(s, 0, 1));
            Assert.AreEqual(1, TextLines.CountNewlines(s, 0, 2));
            Assert.AreEqual(3, TextLines.CountNewlines(s, 0, s.Length));
            Assert.AreEqual(0, TextLines.CountNewlines("", 0, 5));
            Assert.AreEqual(3, TextLines.CountNewlines(s, 0, 999), "clamps `to` past the string end");
        }

        // ───────────────────────── CssScanner.DeclLine ─────────────────────────

        [Test]
        public void CssScanner_DeclLine_FlatRules()
        {
            // 1:".a {"  2:"    color: red;"  3:"    background: blue;"  4:"}"  5:""  6:".b { color: green; }"
            var css = ".a {\n    color: red;\n    background: blue;\n}\n\n.b { color: green; }\n";
            var nodes = CssScanner.Parse(css);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual("color: red;\n    background: blue;", nodes[0].Declarations);
            Assert.AreEqual(2, nodes[0].DeclLine, "leading '\\n' after '{' shifts DeclLine past the blank open-brace line");
            Assert.AreEqual("color: green;", nodes[1].Declarations);
            Assert.AreEqual(6, nodes[1].DeclLine, "single-line rule: DeclLine == the selector's own line");
        }

        [Test]
        public void CssScanner_DeclLine_NestedRuleGetsItsOwnLine()
        {
            // 1:".a {"  2:"    color: red;"  3:"    &:hover {"  4:"        color: blue;"  5:"    }"  6:"}"
            var css = ".a {\n    color: red;\n    &:hover {\n        color: blue;\n    }\n}\n";
            var nodes = CssScanner.Parse(css);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual("color: red;", nodes[0].Declarations);
            Assert.AreEqual(2, nodes[0].DeclLine);

            Assert.AreEqual(1, nodes[0].Children.Count);
            var nested = nodes[0].Children[0];
            Assert.AreEqual("color: blue;", nested.Declarations);
            Assert.AreEqual(4, nested.DeclLine, "nested '&' rule's DeclLine is its OWN body's line, not the parent's");
        }

        [Test]
        public void CssScanner_DeclLine_EmptyDeclarationsStayZero()
        {
            // A rule with only a nested child has no declarations of its own (T-3291 shape).
            var css = ".a {\n    &:hover { color: blue; }\n}\n";
            var nodes = CssScanner.Parse(css);
            Assert.AreEqual("", nodes[0].Declarations);
            Assert.AreEqual(0, nodes[0].DeclLine, "empty Declarations ⇒ nothing to point the map at");
        }

        // ───────────────────────── SharqSourceMapWriter ─────────────────────────

        [Test]
        public void Writer_BuildsValidJson_WithFileLineOffsetApplied()
        {
            var local = new List<SharqMapLine>
            {
                new() { Uss = 12, Src = 3, Origin = "style" },
                new() { Uss = 40, Src = 1, Origin = "variants:size:xs" },
            };
            // styleBodyFileLine=404 ⇒ local Src 1 is FILE line 404, local Src 3 is FILE line 406 —
            // deliberately chosen to land on the plan's own example numbers (§4.4: uss:12→src:406).
            var json = SharqSourceMapWriter.Build("Components/atoms/SusButton.sharq", "deadbeef", 404, local);

            var obj = (Dictionary<string, object>)MiniJson.Deserialize(json);
            Assert.AreEqual("Components/atoms/SusButton.sharq", obj["sharq"]);
            Assert.AreEqual("deadbeef", obj["sha1"]);

            var lines = (List<object>)obj["lines"];
            Assert.AreEqual(2, lines.Count);

            var row0 = (Dictionary<string, object>)lines[0];
            Assert.AreEqual(12L, row0["uss"]);
            Assert.AreEqual(406L, row0["src"]); // 404 + (3 - 1)
            Assert.AreEqual("style", row0["origin"]);

            var row1 = (Dictionary<string, object>)lines[1];
            Assert.AreEqual(40L, row1["uss"]);
            Assert.AreEqual(404L, row1["src"]); // 404 + (1 - 1)
            Assert.AreEqual("variants:size:xs", row1["origin"]);
        }

        [Test]
        public void Writer_EscapesQuotesAndBackslashesInPath()
        {
            var json = SharqSourceMapWriter.Build(@"C:\projects\sus\SusButton.sharq", "abc", 1,
                new List<SharqMapLine>());
            var obj = (Dictionary<string, object>)MiniJson.Deserialize(json);
            Assert.AreEqual("C:/projects/sus/SusButton.sharq", obj["sharq"], "backslashes normalized to '/'");

            var withQuote = SharqSourceMapWriter.Build("a\"b.sharq", "abc", 1, new List<SharqMapLine>());
            var obj2 = (Dictionary<string, object>)MiniJson.Deserialize(withQuote);
            Assert.AreEqual("a\"b.sharq", obj2["sharq"], "round-trips through the reader despite the embedded quote");
        }

        [Test]
        public void Writer_EmptyLines_StillValidJson()
        {
            var json = SharqSourceMapWriter.Build("x.sharq", "abc", 1, new List<SharqMapLine>());
            var obj = (Dictionary<string, object>)MiniJson.Deserialize(json);
            Assert.AreEqual(0, ((List<object>)obj["lines"]).Count);
        }

        // ───────────────────────── End-to-end: pipeline → map ─────────────────────────

        /// <summary>
        /// The load-bearing test. Every row the map claims is checked against the ACTUAL text
        /// on both sides — no hand-counted line numbers anywhere in this method — across the
        /// four shapes StyleParser can take: global raw pass-through, scoped (no variants),
        /// global+@variants, scoped+@variants mixed with a plain rule (the plan §4.4 example's
        /// own shape: one "style" row, one "variants:axis:value" row).
        /// </summary>
        [TestCase(
            "<template><div class=\"btn\"></div></template>\n" +
            "<style>\n.btn {\n    color: red;\n}\n\n.btn .icon {\n    width: 12px;\n}\n</style>\n",
            TestName = "Global_NoVariants_RawPassthrough")]
        [TestCase(
            "<template><div class=\"btn\"></div></template>\n" +
            "<style scoped>\n.btn {\n    color: red;\n}\n</style>\n",
            TestName = "Scoped_NoVariants")]
        [TestCase(
            "<template><div class=\"btn\"></div></template>\n" +
            "<style>\n@variants size {\n    xs {\n        width: 10px;\n    }\n    md {\n        width: 20px;\n    }\n}\n</style>\n",
            TestName = "Global_Variants")]
        [TestCase(
            "<template><div class=\"btn\"></div></template>\n" +
            "<style scoped>\n.btn {\n    color: red;\n}\n\n@variants size {\n    xs {\n        width: 10px;\n    }\n}\n</style>\n",
            TestName = "Scoped_PlainRulePlusVariants_PlanExampleShape")]
        public void RoundTrip_UssLineAndSrcLineNameTheSameDeclaration(string sharqSource)
        {
            var model = SharqFileParser.Parse(sharqSource, "SusRoundTrip.sharq");
            var artifacts = SharqCompilePipeline.Generate(model);
            Assert.IsNotNull(artifacts.SourceMap, "this fixture has a <style> body — a map row is expected");

            var uss = artifacts.ScopedUss ?? artifacts.GlobalUss;
            Assert.IsNotNull(uss);
            var ussLines = uss.Replace("\r\n", "\n").Split('\n');
            var srcLines = sharqSource.Replace("\r\n", "\n").Split('\n');

            var obj = (Dictionary<string, object>)MiniJson.Deserialize(artifacts.SourceMap);
            Assert.AreEqual("SusRoundTrip.sharq", obj["sharq"]);
            var lines = (List<object>)obj["lines"];
            Assert.Greater(lines.Count, 0);

            var sawStyle = false;
            var sawVariants = false;

            foreach (var raw in lines)
            {
                var row = (Dictionary<string, object>)raw;
                var ussLine = (int)(long)row["uss"];
                var srcLine = (int)(long)row["src"];
                var origin = (string)row["origin"];

                Assert.GreaterOrEqual(ussLine, 1);
                Assert.Less(ussLine - 1, ussLines.Length, "uss line points past the generated file");
                Assert.GreaterOrEqual(srcLine, 1);
                Assert.Less(srcLine - 1, srcLines.Length, "src line points past the .sharq file");

                var ussText = ussLines[ussLine - 1].Trim();
                var srcText = srcLines[srcLine - 1].Trim();

                // Both sides must carry the SAME declaration (property name at least — the uss
                // side may have re-wrapped whitespace, but never a different property or value).
                Assert.AreEqual(srcText, ussText,
                    $"uss:{ussLine} ({ussText}) and src:{srcLine} ({srcText}) named different text (origin={origin})");

                if (origin == "style") sawStyle = true;
                if (origin.StartsWith("variants:")) sawVariants = true;
            }

            if (sharqSource.Contains("@variants"))
                Assert.IsTrue(sawVariants, "fixture declares @variants — expected at least one variants:* row");
            if (sharqSource.Contains(".btn {\n    color: red;"))
                Assert.IsTrue(sawStyle, "fixture has a plain rule — expected at least one \"style\" row");
        }

        [Test]
        public void NoStyleSection_ProducesNoMap()
        {
            var model = SharqFileParser.Parse("<template><div></div></template>", "NoStyle.sharq");
            var artifacts = SharqCompilePipeline.Generate(model);
            Assert.IsNull(artifacts.SourceMap);
            Assert.IsNull(SharqCompilePipeline.MapFileName("NoStyle", in artifacts));
        }

        [Test]
        public void Sha1_Is40HexChars_AndChangesWithContent()
        {
            var a = SharqFileParser.Parse("<template><div></div></template>\n<style>.a{color:red;}</style>", "A.sharq");
            var b = SharqFileParser.Parse("<template><div></div></template>\n<style>.a{color:blue;}</style>", "A.sharq");

            Assert.AreEqual(40, a.SourceSha1.Length);
            StringAssert.IsMatch("^[0-9a-f]{40}$", a.SourceSha1);
            Assert.AreNotEqual(a.SourceSha1, b.SourceSha1);
        }

        // ───────────────────────── MapFileName / WriteSourceMap lifecycle ─────────────────────────

        [Test]
        public void MapFileName_PicksSuffixMatchingWhicheverUssIsPresent()
        {
            var scoped = new SharqCompilePipeline.Artifacts { SourceMap = "{}", ScopedUss = "x" };
            Assert.AreEqual("Foo_scoped.g.uss.map.json", SharqCompilePipeline.MapFileName("Foo", in scoped));

            var global = new SharqCompilePipeline.Artifacts { SourceMap = "{}", GlobalUss = "x" };
            Assert.AreEqual("Foo.g.uss.map.json", SharqCompilePipeline.MapFileName("Foo", in global));

            var none = new SharqCompilePipeline.Artifacts { SourceMap = null, GlobalUss = "x" };
            Assert.IsNull(SharqCompilePipeline.MapFileName("Foo", in none));
        }

        [Test]
        public void WriteSourceMap_WritesOnlyTheMatchingSuffix_AndCleansUpTheOtherOnFlip()
        {
            var dir = Path.Combine(Path.GetTempPath(), "sharq-map-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var scopedPath = Path.Combine(dir, "Foo_scoped.g.uss.map.json");
                var globalPath = Path.Combine(dir, "Foo.g.uss.map.json");

                var scoped = new SharqCompilePipeline.Artifacts { SourceMap = "{\"a\":1}", ScopedUss = "x" };
                SharqCompilePipeline.WriteSourceMap(in scoped, "Foo", dir);
                Assert.IsTrue(File.Exists(scopedPath));
                Assert.IsFalse(File.Exists(globalPath));

                // Component flips scoped → global: the stale scoped map must not survive.
                var global = new SharqCompilePipeline.Artifacts { SourceMap = "{\"a\":1}", GlobalUss = "x" };
                SharqCompilePipeline.WriteSourceMap(in global, "Foo", dir);
                Assert.IsFalse(File.Exists(scopedPath), "flipping to global must delete the stale scoped map");
                Assert.IsTrue(File.Exists(globalPath));

                // <style> removed entirely: both must go.
                var none = new SharqCompilePipeline.Artifacts { SourceMap = null };
                SharqCompilePipeline.WriteSourceMap(in none, "Foo", dir);
                Assert.IsFalse(File.Exists(scopedPath));
                Assert.IsFalse(File.Exists(globalPath));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
