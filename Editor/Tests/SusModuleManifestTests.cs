using NUnit.Framework;
using Sharq.Core.Editor.Diagnostics;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for <see cref="SusModuleManifest.Parse"/> — the <c>sus-module.json</c> reader behind
    /// SUS Set Doctor v2 (ARCH-PACK-CLASSIC.md §2.3 D7 / §5.5, T-556/T-557). A neutral fixture
    /// module name ("Widgets") is used since this file lives in the free/MIT sus-core repo.
    /// </summary>
    public class SusModuleManifestTests
    {
        private const string ValidJson = @"{
            ""schema"": ""sus-module/v1"",
            ""id"": ""widgets"",
            ""dir"": ""Widgets"",
            ""root"": ""Sharq"",
            ""package"": ""com.sharq-it.sus.widgets"",
            ""version"": ""2.0.0"",
            ""sha"": ""def456"",
            ""paths"": [ ""Sharq/Widgets"", ""Sharq/Widgets/README.md"", ""Sharq/Widgets/sus-module.json"" ]
        }";

        [Test]
        public void Parse_ValidJson_ReturnsPopulatedManifest()
        {
            var m = SusModuleManifest.Parse(ValidJson);

            Assert.NotNull(m);
            Assert.AreEqual("sus-module/v1", m.schema);
            Assert.AreEqual("widgets", m.id);
            Assert.AreEqual("Widgets", m.dir);
            Assert.AreEqual("Sharq", m.root);
            Assert.AreEqual("com.sharq-it.sus.widgets", m.package);
            Assert.AreEqual("2.0.0", m.version);
            Assert.AreEqual(3, m.paths.Length);
            CollectionAssert.Contains(m.paths, "Sharq/Widgets/README.md");
        }

        [Test]
        public void Parse_Null_ReturnsNull() => Assert.IsNull(SusModuleManifest.Parse(null));

        [Test]
        public void Parse_Empty_ReturnsNull() => Assert.IsNull(SusModuleManifest.Parse(""));

        [Test]
        public void Parse_MalformedJson_ReturnsNullNotThrow() =>
            Assert.IsNull(SusModuleManifest.Parse("{ not valid json"));

        [Test]
        public void Parse_UnrelatedJsonObject_ReturnsNull() =>
            // Structurally valid JSON, but not a sus-module manifest (no schema/id/dir/root) —
            // must be rejected, not half-accepted with null fields.
            Assert.IsNull(SusModuleManifest.Parse(@"{ ""foo"": ""bar"" }"));

        [Test]
        public void Parse_WrongSchema_ReturnsNull() =>
            // A future/foreign schema version must never be half-parsed as v1 — R12 forward-compat.
            Assert.IsNull(SusModuleManifest.Parse(@"{ ""schema"": ""sus-module/v2"", ""id"": ""x"", ""dir"": ""X"", ""root"": ""Sharq"", ""paths"": [] }"));

        [Test]
        public void Parse_MissingSchema_ReturnsNull() =>
            // The pre-T-556 sus-set.json shape (and any other foreign JSON) has no "schema" field.
            Assert.IsNull(SusModuleManifest.Parse(@"{ ""id"": ""x"", ""dir"": ""X"", ""root"": ""Sharq"", ""paths"": [] }"));

        [Test]
        public void Parse_MissingPaths_ReturnsNull() =>
            Assert.IsNull(SusModuleManifest.Parse(@"{ ""schema"": ""sus-module/v1"", ""id"": ""x"", ""dir"": ""X"", ""root"": ""Sharq"" }"));

        [Test]
        public void Parse_MissingDir_ReturnsNull() =>
            Assert.IsNull(SusModuleManifest.Parse(@"{ ""schema"": ""sus-module/v1"", ""id"": ""x"", ""root"": ""Sharq"", ""paths"": [] }"));
    }
}
