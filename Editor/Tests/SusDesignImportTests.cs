using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sharq.Core.Editor.DesignImport;
using UnityEditor.PackageManager;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>T-1423/T-1424 / ARCH-DESIGN-IMPORT §7.1a–b — parser, map, emit override USS + breakpoints.</summary>
    public class SusDesignImportTests
    {
        static string FixturesDir
        {
            get
            {
                var pkg = PackageInfo.FindForAssembly(typeof(DesignImporter).Assembly);
                Assert.IsNotNull(pkg, "package info for sus-core editor asm");
                return Path.Combine(pkg.resolvedPath, "Tools~", "SusDesignImport", "Fixtures");
            }
        }

        static string AliasMapPath
        {
            get
            {
                var pkg = PackageInfo.FindForAssembly(typeof(DesignImporter).Assembly);
                return Path.Combine(pkg.resolvedPath, "Tools~", "SusDesignImport", "alias-map.json");
            }
        }

        static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(FixturesDir, name), Encoding.UTF8);

        static ImportOptions DryOpts(DateTime? ts = null) => new ImportOptions
        {
            DryRun = true,
            AliasMapPath = AliasMapPath,
            TimestampUtc = ts ?? new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc)
        };

        [Test]
        public void Normalize_SusDesignV1_ReadsPrimarySpaceRadius()
        {
            var doc = DesignImporter.Parse(ReadFixture("sample-v1.json"));
            Assert.AreEqual("pixso", doc.Source.Tool);
            Assert.IsTrue(doc.Tokens.Any(t => t.Path == "color.primary" && t.Value == "#0066FF"));
            Assert.IsTrue(doc.Tokens.Any(t => t.Path == "dimension.space.16" && t.Value == "16px"));
            Assert.IsTrue(doc.Tokens.Any(t => t.Path == "dimension.radius.md" && t.Value == "8px"));
            Assert.AreEqual(2, doc.Modes.Count);
            var mobile = doc.Modes.First(m => m.Name == "mobile");
            Assert.AreEqual("breakpoint-sm", mobile.AppliesTo);
            var desktop = doc.Modes.First(m => m.Name == "desktop");
            Assert.AreEqual("", desktop.AppliesTo);
        }

        [Test]
        public void Normalize_TokensStudioLegacy_FlattensGlobal()
        {
            var doc = DesignImporter.Parse(ReadFixture("tokens-studio-legacy.json"));
            Assert.AreEqual("tokens-studio", doc.Source.Tool);
            Assert.IsTrue(doc.Tokens.Any(t => t.Path == "primary" || t.Path.EndsWith(".primary")));
            Assert.IsTrue(doc.Tokens.Any(t => t.Path.Contains("space") && t.Path.Contains("16")));
        }

        [Test]
        public void Import_Fixture_EmitsPrimarySpaceRadius_R22Clean()
        {
            var result = DesignImporter.Import(ReadFixture("sample-v1.json"), DryOpts());
            Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));
            StringAssert.Contains("--sus-primary:", result.Uss);
            StringAssert.Contains("rgb(0, 102, 255)", result.Uss);
            StringAssert.Contains("--sus-space-16:", result.Uss);
            StringAssert.Contains("--sus-radius-md:", result.Uss);
            StringAssert.DoesNotContain("z-index", result.Uss);
            StringAssert.DoesNotContain("box-shadow", result.Uss);
            StringAssert.DoesNotContain("gap:", result.Uss);
            Assert.IsTrue(result.MetaJson.Contains("inputSha256"));
            Assert.IsTrue(result.MetaJson.Contains("sus-design-meta/v1"));
        }

        [Test]
        public void Import_EmitsBreakpointSm_MobileSpaceOverride()
        {
            var result = DesignImporter.Import(ReadFixture("sample-v1.json"), DryOpts());
            Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));
            StringAssert.Contains(".breakpoint-sm {", result.Uss);
            // :root keeps desktop 16px; sm overrides to 12px (fixture mobile mode)
            Assert.IsTrue(
                Regex.IsMatch(result.Uss, @":root\s*\{[^}]*--sus-space-16:\s*16px;", RegexOptions.Singleline),
                "root must keep space-16=16px\n" + result.Uss);
            Assert.IsTrue(
                Regex.IsMatch(
                    result.Uss,
                    @"\.breakpoint-sm\s*\{[^}]*--sus-space-16:\s*12px;",
                    RegexOptions.Singleline),
                "breakpoint-sm must override space-16=12px\n" + result.Uss);
            Assert.AreEqual(1, result.ModeBlocks.Count);
            Assert.AreEqual("breakpoint-sm", result.ModeBlocks[0].AppliesTo);
            Assert.IsTrue(result.MetaJson.Contains("\"appliesTo\": \"breakpoint-sm\""));
            // desktop without appliesTo → warning, no second block
            Assert.IsTrue(result.Warnings.Any(w =>
                w.IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void Import_EmitsOptionalBreakpointMd()
        {
            const string json = @"{
  ""$schema"": ""sus-design/v1"",
  ""tokens"": {
    ""dimension"": {
      ""space.16"": { ""$type"": ""dimension"", ""$value"": ""16px"" }
    }
  },
  ""modes"": {
    ""mobile"": {
      ""appliesTo"": ""breakpoint-sm"",
      ""tokens"": {
        ""dimension"": {
          ""space.16"": { ""$type"": ""dimension"", ""$value"": ""12px"" }
        }
      }
    },
    ""compactTablet"": {
      ""appliesTo"": ""breakpoint-md"",
      ""tokens"": {
        ""dimension"": {
          ""space.16"": { ""$type"": ""dimension"", ""$value"": ""14px"" }
        }
      }
    }
  }
}";
            var result = DesignImporter.Import(json, DryOpts());
            Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));
            StringAssert.Contains(".breakpoint-sm {", result.Uss);
            StringAssert.Contains(".breakpoint-md {", result.Uss);
            Assert.IsTrue(
                Regex.IsMatch(result.Uss, @"\.breakpoint-md\s*\{[^}]*--sus-space-16:\s*14px;", RegexOptions.Singleline),
                result.Uss);
            // sm before md (stable order)
            Assert.Less(result.Uss.IndexOf(".breakpoint-sm", StringComparison.Ordinal),
                result.Uss.IndexOf(".breakpoint-md", StringComparison.Ordinal));
        }

        [Test]
        public void Smoke_WidthAtOrBelow640_AppliesBreakpointSmClass()
        {
            // DoD §6 #5: width ≤640 → SusBreakpointService class matches emitted .breakpoint-sm block
            var root = new VisualElement();
            var svc = SusBreakpointService.Attach(root);

            svc.Update(640);
            Assert.AreEqual(Breakpoint.Sm, svc.Current.Value);
            Assert.IsTrue(root.ClassListContains("breakpoint-sm"),
                "width 640 must apply breakpoint-sm (mobile override selector)");

            svc.Update(500);
            Assert.IsTrue(root.ClassListContains("breakpoint-sm"));

            svc.Update(641);
            Assert.AreEqual(Breakpoint.Md, svc.Current.Value);
            Assert.IsFalse(root.ClassListContains("breakpoint-sm"));
            Assert.IsTrue(root.ClassListContains("breakpoint-md"));

            SusBreakpointService.Detach(root);
        }

        [Test]
        public void Import_IsIdempotent_UssStable()
        {
            var opts = DryOpts(new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
            var json = ReadFixture("sample-v1.json");
            var a = DesignImporter.Import(json, opts);
            opts.TimestampUtc = new DateTime(2026, 8, 21, 18, 0, 0, DateTimeKind.Utc);
            var b = DesignImporter.Import(json, opts);
            Assert.IsTrue(a.Ok && b.Ok);
            Assert.IsTrue(DesignImporter.UssEquals(a.Uss, b.Uss), "USS must be byte-stable across re-import");
            Assert.AreEqual(a.InputSha256, b.InputSha256);
            // meta timestamp differs, hash of input does not
            Assert.AreNotEqual(a.MetaJson, b.MetaJson);
        }

        [Test]
        public void Validate_RejectsGhostSusFail()
        {
            var opts = new ImportOptions { AliasMapPath = AliasMapPath };
            var result = DesignImporter.Validate(ReadFixture("ghost-tokens.json"), opts);
            Assert.IsFalse(result.Ok);
            Assert.IsTrue(
                result.Errors.Any(e => e.IndexOf("ghost", StringComparison.OrdinalIgnoreCase) >= 0)
                || result.GhostCssVars.Any(g => g.IndexOf("sus-fail", StringComparison.OrdinalIgnoreCase) >= 0),
                string.Join("; ", result.Errors));
        }

        [Test]
        public void Validate_RejectsUnknownAliasWithoutEmitFlag()
        {
            const string json = @"{
  ""$schema"": ""sus-design/v1"",
  ""tokens"": {
    ""color"": {
      ""totally.unknown.token"": { ""$type"": ""color"", ""$value"": ""#000000"" }
    }
  }
}";
            var opts = new ImportOptions { AliasMapPath = AliasMapPath };
            var result = DesignImporter.Validate(json, opts);
            Assert.IsFalse(result.Ok);
            Assert.IsTrue(result.UnknownAliases.Count > 0 || result.Errors.Any(e => e.Contains("unknown")));
        }

        [Test]
        public void Map_List_ContainsPrimary()
        {
            var text = DesignImporter.MapList(new ImportOptions { AliasMapPath = AliasMapPath });
            StringAssert.Contains("color.primary → --sus-primary", text);
            StringAssert.DoesNotContain("sk.color.primary", text); // downstream off by default
        }

        [Test]
        public void Map_List_DownstreamOptIn()
        {
            var text = DesignImporter.MapList(new ImportOptions
            {
                AliasMapPath = AliasMapPath,
                Downstream = true
            });
            StringAssert.Contains("sk.color.primary → --sk-color-primary", text);
        }

        [Test]
        public void Import_WritesFiles_WhenNotDryRun()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "sus-design-import-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var opts = new ImportOptions
                {
                    OutDir = tmp,
                    AliasMapPath = AliasMapPath,
                    TimestampUtc = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc)
                };
                var result = DesignImporter.Import(ReadFixture("sample-v1.json"), opts);
                Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));
                var ussPath = Path.Combine(tmp, "imported-tokens.uss");
                var metaPath = Path.Combine(tmp, ".sus-design-meta.json");
                Assert.IsTrue(File.Exists(ussPath));
                Assert.IsTrue(File.Exists(metaPath));
                var uss = File.ReadAllText(ussPath, Encoding.UTF8);
                StringAssert.Contains("--sus-primary:", uss);
                StringAssert.Contains(".breakpoint-sm {", uss);
                // second import identical USS
                var result2 = DesignImporter.Import(ReadFixture("sample-v1.json"), opts);
                Assert.IsTrue(DesignImporter.UssEquals(uss, File.ReadAllText(ussPath, Encoding.UTF8)));
                Assert.IsTrue(result2.Ok);
            }
            finally
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            }
        }
    }
}
