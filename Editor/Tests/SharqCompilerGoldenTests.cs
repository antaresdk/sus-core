using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Golden / invariant tests for the Sharq compiler pipeline
    /// (SharqFileParser → TemplateParser → BuildMethodGenerator, plus StyleParser /
    /// ScopedCssGenerator). Assertion-based rather than exact-string golden files so
    /// they survive cosmetic formatting changes while guarding the important invariants.
    ///
    /// Safety net for Phase D (parser refactor) and regression guard for the Phase A
    /// fixes: P0.1 (reactive v-for → BindList) and P0.4 (validate: dead-code removal).
    /// </summary>
    public class SharqCompilerGoldenTests
    {
        private static string Gen(string sharq)
        {
            var model = SharqFileParser.Parse(sharq, "TestComponent.sharq");
            return BuildMethodGenerator.Generate(model);
        }

        // ─────────────────────────────────────────────────────────────
        //  SharqFileParser — section split
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void FileParser_SplitsTemplateScriptStyleSections()
        {
            const string sharq =
                "<template><ui:VisualElement /></template>\n" +
                "<script>\npublic int X = 1;\n</script>\n" +
                "<style>.a { color: red; }</style>";

            var model = SharqFileParser.Parse(sharq, "T.sharq");

            Assert.AreEqual("T", model.ClassName);
            StringAssert.Contains("VisualElement", model.TemplateXml);
            StringAssert.Contains("public int X", model.ScriptBody);
            StringAssert.Contains(".a", model.StyleBody);
        }

        [Test]
        public void FileParser_StripsHtmlCommentsFromTemplate()
        {
            const string sharq =
                "<template><!-- secret --><ui:VisualElement /></template>";

            var model = SharqFileParser.Parse(sharq, "T.sharq");

            StringAssert.DoesNotContain("secret", model.TemplateXml);
            StringAssert.Contains("VisualElement", model.TemplateXml);
        }

        [Test]
        public void FileParser_DetectsScopedStyle()
        {
            const string scoped =
                "<template><ui:VisualElement /></template>\n<style scoped>.a { color: red; }</style>";
            const string global =
                "<template><ui:VisualElement /></template>\n<style>.a { color: red; }</style>";

            Assert.IsTrue(SharqFileParser.Parse(scoped, "T.sharq").IsStyleScoped);
            Assert.IsFalse(SharqFileParser.Parse(global, "T.sharq").IsStyleScoped);
        }

        // ─────────────────────────────────────────────────────────────
        //  $extends — custom base class (C2 two-tier model)
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Extends_DefaultsToSusComponent()
        {
            var gen = Gen("<template><ui:VisualElement /></template>\n<script>\npublic int X = 1;\n</script>");
            StringAssert.Contains(": SusComponent", gen);
        }

        [Test]
        public void Extends_EmitsCustomBaseClass()
        {
            var gen = Gen(
                "<template><ui:VisualElement /></template>\n" +
                "<script>\n$extends SusModalBase;\npublic int X = 1;\n</script>");

            StringAssert.Contains("public partial class TestComponent : SusModalBase", gen);
            StringAssert.DoesNotContain(": SusComponent", gen);
        }

        [Test]
        public void Extends_DirectiveIsStrippedFromBody()
        {
            var model = SharqFileParser.Parse(
                "<template><ui:VisualElement /></template>\n" +
                "<script>\n$extends SusToastBase;\npublic int X = 1;\n</script>", "T.sharq");

            Assert.AreEqual("SusToastBase", model.BaseClass);
            StringAssert.DoesNotContain("$extends", model.ScriptBody);
            // And it must not leak into the generated class body as invalid C#.
            StringAssert.DoesNotContain("$extends", BuildMethodGenerator.Generate(model));
        }

        // ─────────────────────────────────────────────────────────────
        //  TemplateParser
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void TemplateParser_ParsesNestedElements()
        {
            var node = TemplateParser.Parse(
                "<ui:VisualElement class=\"a\"><ui:Label /></ui:VisualElement>", "T");

            Assert.AreEqual("ui:VisualElement", node.TagName);
            Assert.AreEqual(1, node.Children.Count);
            Assert.AreEqual("ui:Label", node.Children[0].TagName);
            Assert.IsTrue(node.Children[0].IsSelfClosing);
        }

        [Test]
        public void TemplateParser_ReadsMultiLineAndMixedQuoteAttributes()
        {
            var node = TemplateParser.Parse(
                "<ui:VisualElement\n    class=\"a b\"\n    name='root' />", "T");

            Assert.AreEqual("a b", node.Attributes["class"]);
            Assert.AreEqual("root", node.Attributes["name"]);
        }

        [Test]
        public void TemplateParser_StripsMainElementMarkerFromRoot()
        {
            var node = TemplateParser.Parse(
                "<ui:VisualElement $MainElement class=\"a\"><ui:Label /></ui:VisualElement>", "T");

            Assert.IsTrue(node.IsMainElement);
            Assert.IsFalse(node.Attributes.ContainsKey("$MainElement"));
        }

        // ─────────────────────────────────────────────────────────────
        //  BuildMethodGenerator — v-for (P0.1, mandatory)
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void VFor_Typed_GeneratesReactiveBindList_NotBindListFor()
        {
            const string sharq =
                "<template>\n" +
                "  <ui:VisualElement class=\"list\">\n" +
                "    <ui:VisualElement v-for=\"item in Items\" :key=\"item.Id\">\n" +
                "      <ui:Label :text=\"item.Name\" />\n" +
                "    </ui:VisualElement>\n" +
                "  </ui:VisualElement>\n" +
                "</template>\n" +
                "<script>\npublic List<UnitData> Items = new();\n</script>";

            var code = Gen(sharq);

            StringAssert.Contains("BindList<UnitData>(", code);
            StringAssert.Contains("() => Items,", code);
            StringAssert.Contains("item => item.Id", code);
            StringAssert.DoesNotContain("BindListFor", code);
        }

        [Test]
        public void VFor_PropWrappedCollection_UnwrapsValueInsideFunc()
        {
            const string sharq =
                "<template>\n" +
                "  <ui:VisualElement>\n" +
                "    <ui:VisualElement v-for=\"item in Items\" :key=\"item.Id\">\n" +
                "      <ui:Label :text=\"item.Name\" />\n" +
                "    </ui:VisualElement>\n" +
                "  </ui:VisualElement>\n" +
                "</template>\n" +
                "<script>\npublic Prop<List<UnitData>> Items = new();\n</script>";

            var code = Gen(sharq);

            StringAssert.Contains("BindList<UnitData>(", code);
            StringAssert.Contains("() => Items.Value,", code);
            StringAssert.DoesNotContain("BindListFor", code);
        }

        [Test]
        public void VFor_Untyped_GeneratesNonGenericReactiveBindList()
        {
            const string sharq =
                "<template>\n" +
                "  <ui:VisualElement>\n" +
                "    <ui:VisualElement v-for=\"row in Rows\" :key=\"row.Key\">\n" +
                "      <ui:Label :text=\"row.Label\" />\n" +
                "    </ui:VisualElement>\n" +
                "  </ui:VisualElement>\n" +
                "</template>";

            var code = Gen(sharq);

            StringAssert.Contains("BindList(", code);
            StringAssert.Contains("((dynamic)row).Key", code);
            StringAssert.DoesNotContain("BindListFor", code);
        }

        // ─────────────────────────────────────────────────────────────
        //  BuildMethodGenerator — directives
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void VIf_GeneratesBindVisibility()
        {
            const string sharq =
                "<template><ui:VisualElement><ui:Label v-if=\"Show\" text=\"Hi\" /></ui:VisualElement></template>";
            StringAssert.Contains("BindVisibility(", Gen(sharq));
        }

        [Test]
        public void VShow_GeneratesBindShow()
        {
            const string sharq =
                "<template><ui:VisualElement><ui:Label v-show=\"Show\" text=\"Hi\" /></ui:VisualElement></template>";
            StringAssert.Contains("BindShow(", Gen(sharq));
        }

        [Test]
        public void BindText_OnLabel_GeneratesBindText()
        {
            const string sharq =
                "<template><ui:VisualElement><ui:Label :text=\"Greeting\" /></ui:VisualElement></template>";
            StringAssert.Contains("BindText(", Gen(sharq));
        }

        [Test]
        public void BindClass_ObjectSyntax_GeneratesBindClass()
        {
            const string sharq =
                "<template><ui:VisualElement><ui:VisualElement :class=\"{ active: IsActive }\" /></ui:VisualElement></template>";
            var code = Gen(sharq);
            StringAssert.Contains("BindClass(", code);
            StringAssert.Contains("\"active\"", code);
        }

        // ─────────────────────────────────────────────────────────────
        //  BuildMethodGenerator — [CreateProperty]
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void CreateProperty_GeneratesReactivePropAndUxmlAttribute()
        {
            const string sharq =
                "<template><ui:VisualElement /></template>\n" +
                "<script>\n[CreateProperty]\npublic int Health = 5;\n</script>";

            var code = Gen(sharq);

            StringAssert.Contains("public Prop<int> Health = new(5);", code);
            StringAssert.Contains("[UxmlAttribute(\"Health\")]", code);
        }

        [Test]
        public void CreateProperty_DefaultParam_OverridesInitializer()
        {
            const string sharq =
                "<template><ui:VisualElement /></template>\n" +
                "<script>\n[CreateProperty(default: 42)]\npublic int Health;\n</script>";

            StringAssert.Contains("new(42)", Gen(sharq));
        }

        [Test]
        public void CreateProperty_ValidateParam_DoesNotGenerateDeadValidator()
        {
            // P0.4 regression: validate: DSL was removed — no Validate_* method,
            // and the property still compiles as a normal reactive Prop.
            const string sharq =
                "<template><ui:VisualElement /></template>\n" +
                "<script>\n[CreateProperty(validate: \"value >= 0\")]\npublic int Health = 5;\n</script>";

            var code = Gen(sharq);

            StringAssert.DoesNotContain("Validate_Health", code);
            StringAssert.Contains("public Prop<int> Health = new(5);", code);
        }

        // ─────────────────────────────────────────────────────────────
        //  StyleParser / ScopedCssGenerator
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void StyleParser_GlobalStyle_ReturnsRawCss()
        {
            var model = new SharqFileModel
            {
                ClassName = "T",
                StyleBody = ".x { color: red; }",
                IsStyleScoped = false
            };

            var result = StyleParser.Parse(model);

            Assert.IsTrue(result.HasGlobalCss);
            StringAssert.Contains(".x", result.GlobalCss);
        }

        [Test]
        public void StyleParser_ScopedStyle_ProducesScopedCss()
        {
            var model = new SharqFileModel
            {
                ClassName = "T",
                StyleBody = ".box { color: red; }",
                IsStyleScoped = true
            };

            var result = StyleParser.Parse(model);

            Assert.IsTrue(result.HasScopedCss);
            StringAssert.Contains(".s-", result.ScopedCss);
        }

        [Test]
        public void ScopedCssGenerator_AppendsHashClassToSelector()
        {
            var model = new SharqFileModel
            {
                ClassName = "T",
                StyleBody = ".box { color: red; }",
                IsStyleScoped = true
            };

            var css = ScopedCssGenerator.Generate(model);

            StringAssert.Contains(".box.s-", css);
        }

        // ─────────────────────────────────────────────────────────────
        //  SharqCompilePipeline (P1.5) — importer & batch share one source
        // ─────────────────────────────────────────────────────────────

        // .sharq exercising all three artifact kinds: inline style="..." (→ _static),
        // scoped <style> (→ _scoped) plus a global rule impossible via scoping.
        private const string PipelineSharq =
            "<template><ui:VisualElement style=\"color: red;\"><ui:Label text=\"hi\" /></ui:VisualElement></template>\n" +
            "<script>\npublic int X = 1;\n</script>\n" +
            "<style scoped>.box { color: blue; }</style>";

        [Test]
        public void Pipeline_CodeMatchesGeneratorOutput()
        {
            var model = SharqFileParser.Parse(PipelineSharq, "T.sharq");
            var expected = BuildMethodGenerator.Generate(model);

            var model2 = SharqFileParser.Parse(PipelineSharq, "T.sharq");
            var artifacts = SharqCompilePipeline.Generate(model2);

            Assert.AreEqual(expected, artifacts.Code);
        }

        [Test]
        public void Pipeline_CapturesStaticScopedAndGlobalArtifacts()
        {
            var model = SharqFileParser.Parse(PipelineSharq, "T.sharq");
            var artifacts = SharqCompilePipeline.Generate(model);

            Assert.IsNotNull(artifacts.Code);
            // inline style="color: red;" → _static.g.uss
            Assert.IsNotNull(artifacts.StaticUss, "inline style should yield static USS");
            StringAssert.Contains("color: red", artifacts.StaticUss);
            // <style scoped> → _scoped.g.uss
            Assert.IsNotNull(artifacts.ScopedUss, "scoped <style> should yield scoped USS");
            StringAssert.Contains(".s-", artifacts.ScopedUss);
        }

        [Test]
        public void Pipeline_IsDeterministic()
        {
            var a1 = SharqCompilePipeline.Generate(SharqFileParser.Parse(PipelineSharq, "T.sharq"));
            var a2 = SharqCompilePipeline.Generate(SharqFileParser.Parse(PipelineSharq, "T.sharq"));

            Assert.AreEqual(a1.Code, a2.Code);
            Assert.AreEqual(a1.StaticUss, a2.StaticUss);
            Assert.AreEqual(a1.ScopedUss, a2.ScopedUss);
            Assert.AreEqual(a1.GlobalUss, a2.GlobalUss);
        }

        [Test]
        public void Pipeline_NoInlineStyle_YieldsNullStaticUss()
        {
            const string sharq =
                "<template><ui:VisualElement /></template>\n<script>\npublic int X = 1;\n</script>";

            var artifacts = SharqCompilePipeline.Generate(SharqFileParser.Parse(sharq, "T.sharq"));

            Assert.IsNull(artifacts.StaticUss);
        }

        // ─────────────────────────────────────────────────────────────
        //  P2.1 — robust section scanner (SharqFileParser)
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void FileParser_IgnoresCloseTagInsideHtmlComment()
        {
            // A </template> hidden in a comment must NOT terminate the section early.
            const string sharq =
                "<template><!-- </template> --><ui:VisualElement class=\"real\" /></template>";

            var model = SharqFileParser.Parse(sharq, "T.sharq");

            StringAssert.Contains("real", model.TemplateXml);
            StringAssert.DoesNotContain("<!--", model.TemplateXml);
        }

        [Test]
        public void FileParser_IgnoresCloseTagInsideQuotedAttribute()
        {
            // A </template> inside a quoted attribute value must not cut the section.
            const string sharq =
                "<template><ui:Label text=\"a</template>b\" /></template>";

            var model = SharqFileParser.Parse(sharq, "T.sharq");

            StringAssert.Contains("a</template>b", model.TemplateXml);
            StringAssert.Contains("Label", model.TemplateXml);
        }

        [Test]
        public void FileParser_ScriptWithGenericsAndLessThanIsNotMisparsed()
        {
            // '<' in C# generics / comparisons must not be treated as markup.
            const string sharq =
                "<template><ui:VisualElement /></template>\n" +
                "<script>\npublic List<int> Nums = new();\npublic bool F() => 1 < 2;\n</script>";

            var model = SharqFileParser.Parse(sharq, "T.sharq");

            StringAssert.Contains("List<int>", model.ScriptBody);
            StringAssert.Contains("1 < 2", model.ScriptBody);
        }

        // ─────────────────────────────────────────────────────────────
        //  P2.1 — brace-balanced CSS scanner (StyleParser / ScopedCssGenerator)
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void CssScanner_CountsRulesInsideMediaQuery()
        {
            const string css =
                ".a { color: red; }\n" +
                "@media (min-width: 600px) { .b { color: blue; } .c { color: green; } }";

            Assert.AreEqual(3, CssScanner.CountRules(CssScanner.Parse(css)));
        }

        [Test]
        public void CssScanner_BraceInCommentDoesNotSplitRule()
        {
            const string css = ".a { color: red; /* } */ background: blue; }";

            var nodes = CssScanner.Parse(css);

            Assert.AreEqual(1, CssScanner.CountRules(nodes));
            StringAssert.Contains("background", nodes[0].Declarations);
        }

        [Test]
        public void CssScanner_BraceInStringDoesNotSplitRule()
        {
            const string css = ".a { --x: \"}\"; color: red; }";

            Assert.AreEqual(1, CssScanner.CountRules(CssScanner.Parse(css)));
        }

        [Test]
        public void ScopedCss_PreservesMediaQueryAndScopesInnerSelector()
        {
            var model = new SharqFileModel
            {
                ClassName = "T",
                StyleBody = "@media (min-width: 600px) { .box { color: red; } }",
                IsStyleScoped = true
            };

            var css = ScopedCssGenerator.Generate(model);

            StringAssert.Contains("@media (min-width: 600px)", css);
            StringAssert.Contains(".box.s-", css);
        }

        // ─────────────────────────────────────────────────────────────
        //  P2.1 — TemplateParser robustness
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void TemplateParser_SkipsTextBetweenElements()
        {
            var node = TemplateParser.Parse(
                "<ui:VisualElement>hello <ui:Label /> world</ui:VisualElement>", "T");

            Assert.AreEqual(1, node.Children.Count);
            Assert.AreEqual("ui:Label", node.Children[0].TagName);
        }
    }
}
