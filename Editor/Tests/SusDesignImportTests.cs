using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sharq.Core.Editor.DesignImport;
using UnityEditor.PackageManager;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>T-1423 / ARCH-DESIGN-IMPORT §7.1a — parser, map, emit override USS.</summary>
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

        [Test]
        public void Normalize_SusDesignV1_ReadsPrimarySpaceRadius()
        {
            var doc = DesignImporter.Parse(ReadFixture("sample-v1.json"));
            Assert.AreEqual("pixso", doc.Source.Tool);
            Assert.IsTrue(doc.Tokens.Any(t => t.Path == "color.primary" && t.Value == "#0066FF"));
            Assert.IsTrue(doc.Tokens.Any(t => t.Path == "dimension.space.16" && t.Value == "16px"));
            Assert.IsTrue(doc.Tokens.Any(t => t.Path == "dimension.radius.md" && t.Value == "8px"));
            Assert.AreEqual(1, doc.Modes.Count);
            Assert.AreEqual("breakpoint-sm", doc.Modes[0].AppliesTo);
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
            var opts = new ImportOptions
            {
                DryRun = true,
                AliasMapPath = AliasMapPath,
                TimestampUtc = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc)
            };
            var result = DesignImporter.Import(ReadFixture("sample-v1.json"), opts);
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
        public void Import_IsIdempotent_UssStable()
        {
            var opts = new ImportOptions
            {
                DryRun = true,
                AliasMapPath = AliasMapPath,
                TimestampUtc = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc)
            };
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
