using NUnit.Framework;
using Sharq.Core.Editor.Diagnostics;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for <see cref="SusSetManifest.Parse"/> — the <c>sus-set.json</c> reader behind
    /// SUS Set Doctor (ARCH-PACK-CLASSIC.md §5.3, T-368). A neutral fixture module name
    /// ("Widgets") is used since this file lives in the free/MIT sus-core repo.
    /// </summary>
    public class SusSetManifestTests
    {
        private const string ValidJson = @"{
            ""set"": ""widgets-set"",
            ""displayName"": ""Widgets Set"",
            ""version"": ""1.2.3"",
            ""root"": ""Sharq"",
            ""modules"": [
                { ""id"": ""core"", ""dir"": ""Core"", ""version"": ""1.0.14"", ""sha"": ""abc123"" },
                { ""id"": ""widgets"", ""dir"": ""Widgets"", ""version"": ""2.0.0"", ""sha"": ""def456"" }
            ],
            ""paths"": [ ""Sharq"", ""Sharq/Core"", ""Sharq/Core/README.md"" ]
        }";

        [Test]
        public void Parse_ValidJson_ReturnsPopulatedManifest()
        {
            var m = SusSetManifest.Parse(ValidJson);

            Assert.NotNull(m);
            Assert.AreEqual("widgets-set", m.set);
            Assert.AreEqual("Widgets Set", m.displayName);
            Assert.AreEqual("1.2.3", m.version);
            Assert.AreEqual("Sharq", m.root);
            Assert.AreEqual(2, m.modules.Length);
            Assert.AreEqual("core", m.modules[0].id);
            Assert.AreEqual("Core", m.modules[0].dir);
            Assert.AreEqual("1.0.14", m.modules[0].version);
            Assert.AreEqual(3, m.paths.Length);
            CollectionAssert.Contains(m.paths, "Sharq/Core/README.md");
        }

        [Test]
        public void Parse_Null_ReturnsNull()
        {
            Assert.IsNull(SusSetManifest.Parse(null));
        }

        [Test]
        public void Parse_Empty_ReturnsNull()
        {
            Assert.IsNull(SusSetManifest.Parse(""));
        }

        [Test]
        public void Parse_MalformedJson_ReturnsNullNotThrow()
        {
            Assert.IsNull(SusSetManifest.Parse("{ not valid json"));
        }

        [Test]
        public void Parse_UnrelatedJsonObject_ReturnsNull()
        {
            // Structurally valid JSON, but not a sus-set manifest (no root/modules) —
            // must be rejected, not half-accepted with null fields.
            Assert.IsNull(SusSetManifest.Parse(@"{ ""foo"": ""bar"" }"));
        }

        [Test]
        public void Parse_MissingRoot_ReturnsNull()
        {
            Assert.IsNull(SusSetManifest.Parse(@"{ ""set"": ""x"", ""modules"": [] }"));
        }

        [Test]
        public void Parse_MissingModules_ReturnsNull()
        {
            Assert.IsNull(SusSetManifest.Parse(@"{ ""set"": ""x"", ""root"": ""Sharq"" }"));
        }
    }
}
