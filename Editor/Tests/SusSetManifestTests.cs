using NUnit.Framework;
using Sharq.Core.Editor.Diagnostics;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for <see cref="SusSetManifest.Parse"/> — the <c>sus-set.&lt;set&gt;.json</c> reader
    /// behind SUS Set Doctor v2 (ARCH-PACK-CLASSIC.md §2.3 D7 / §5.5, T-556/T-557). A neutral
    /// fixture module name ("Widgets") is used since this file lives in the free/MIT sus-core repo.
    /// </summary>
    public class SusSetManifestTests
    {
        private const string ValidJson = @"{
            ""schema"": ""sus-set/v2"",
            ""set"": ""widgets-set"",
            ""displayName"": ""Widgets Set"",
            ""version"": ""1.2.3"",
            ""lead"": ""widgets"",
            ""root"": ""Sharq"",
            ""modules"": [ ""core"", ""widgets"" ],
            ""sharedPaths"": [ ""Sharq"", ""Sharq/README.txt"", ""Sharq/sus-set.widgets-set.json"" ]
        }";

        [Test]
        public void Parse_ValidJson_ReturnsPopulatedManifest()
        {
            var m = SusSetManifest.Parse(ValidJson);

            Assert.NotNull(m);
            Assert.AreEqual("sus-set/v2", m.schema);
            Assert.AreEqual("widgets-set", m.set);
            Assert.AreEqual("Widgets Set", m.displayName);
            Assert.AreEqual("1.2.3", m.version);
            Assert.AreEqual("widgets", m.lead);
            Assert.AreEqual("Sharq", m.root);
            Assert.AreEqual(2, m.modules.Length);
            CollectionAssert.Contains(m.modules, "widgets");
            Assert.AreEqual(3, m.sharedPaths.Length);
            CollectionAssert.Contains(m.sharedPaths, "Sharq/README.txt");
        }

        [Test]
        public void Parse_Null_ReturnsNull() => Assert.IsNull(SusSetManifest.Parse(null));

        [Test]
        public void Parse_Empty_ReturnsNull() => Assert.IsNull(SusSetManifest.Parse(""));

        [Test]
        public void Parse_MalformedJson_ReturnsNullNotThrow() =>
            Assert.IsNull(SusSetManifest.Parse("{ not valid json"));

        [Test]
        public void Parse_UnrelatedJsonObject_ReturnsNull() =>
            // Structurally valid JSON, but not a sus-set manifest (no schema/root/modules) —
            // must be rejected, not half-accepted with null fields.
            Assert.IsNull(SusSetManifest.Parse(@"{ ""foo"": ""bar"" }"));

        [Test]
        public void Parse_MissingRoot_ReturnsNull() =>
            Assert.IsNull(SusSetManifest.Parse(@"{ ""schema"": ""sus-set/v2"", ""set"": ""x"", ""modules"": [] }"));

        [Test]
        public void Parse_MissingModules_ReturnsNull() =>
            Assert.IsNull(SusSetManifest.Parse(@"{ ""schema"": ""sus-set/v2"", ""set"": ""x"", ""root"": ""Sharq"" }"));

        [Test]
        public void Parse_MissingSet_ReturnsNull() =>
            Assert.IsNull(SusSetManifest.Parse(@"{ ""schema"": ""sus-set/v2"", ""root"": ""Sharq"", ""modules"": [] }"));

        [Test]
        public void Parse_WrongSchema_ReturnsNull() =>
            // The pre-T-556 sus-set.json shape has no "schema" field at all (and a different
            // module/paths structure even if it did) — must never be half-accepted as v2 (R12).
            Assert.IsNull(SusSetManifest.Parse(@"{ ""schema"": ""sus-set/v1"", ""set"": ""x"", ""root"": ""Sharq"", ""modules"": [] }"));

        [Test]
        public void Parse_MissingSchema_ReturnsNull() =>
            Assert.IsNull(SusSetManifest.Parse(@"{ ""set"": ""x"", ""root"": ""Sharq"", ""modules"": [] }"));
    }
}
