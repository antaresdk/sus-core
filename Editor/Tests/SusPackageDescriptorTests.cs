using System.IO;
using NUnit.Framework;
using Sharq.Core.Editor;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for <see cref="SusPackageDescriptor"/> parsing and validation
    /// (sharq.gen.json — the declarative package generation contour).
    /// </summary>
    public class SusPackageDescriptorTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sus-pkg-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "Components"));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private string WriteDescriptor(string json)
        {
            var path = Path.Combine(_root, "sharq.gen.json");
            File.WriteAllText(path, json);
            return path;
        }

        [Test]
        public void Load_ValidDescriptor_ResolvesAbsolutePaths()
        {
            var path = WriteDescriptor(
                "{\"displayName\":\"Test\",\"sources\":[\"Components\"]," +
                "\"generated\":\"Runtime/Generated\",\"resources\":\"Runtime/Resources/SusRuntime\"}");

            var d = SusPackageDescriptor.Load(path, "com.test.pkg", _root);

            Assert.NotNull(d);
            Assert.AreEqual("Test", d.displayName);
            Assert.IsTrue(d.watch, "watch defaults to true");
            Assert.AreEqual(1, d.AbsSourceDirs.Count);
            StringAssert.EndsWith("/Components", d.AbsSourceDirs[0]);
            StringAssert.EndsWith("/Runtime/Generated", d.AbsGeneratedDir);
            StringAssert.EndsWith("/Runtime/Resources/SusRuntime", d.AbsResourcesDir);
        }

        [Test]
        public void Load_MinimalDescriptor_NoResources_ReturnsNullResourcesDir()
        {
            var path = WriteDescriptor(
                "{\"sources\":[\"Components\"],\"generated\":\"Runtime/Generated\"}");

            var d = SusPackageDescriptor.Load(path, "com.test.pkg", _root);

            Assert.NotNull(d);
            Assert.AreEqual("com.test.pkg", d.displayName, "displayName falls back to package name");
            Assert.IsNull(d.AbsResourcesDir);
        }

        [Test]
        public void Load_MalformedJson_ReturnsNull()
        {
            var path = WriteDescriptor("{ not valid json !!!");

            LogAssert.ExpectAnyError();
            var d = SusPackageDescriptor.Load(path, "com.test.pkg", _root);

            Assert.IsNull(d);
        }

        [Test]
        public void Load_EmptySources_ReturnsNull()
        {
            var path = WriteDescriptor("{\"sources\":[],\"generated\":\"Runtime/Generated\"}");

            LogAssert.ExpectAnyError();
            Assert.IsNull(SusPackageDescriptor.Load(path, "com.test.pkg", _root));
        }

        [Test]
        public void Load_MissingSourceDirectory_ReturnsNull()
        {
            var path = WriteDescriptor("{\"sources\":[\"DoesNotExist\"],\"generated\":\"Runtime/Generated\"}");

            LogAssert.ExpectAnyError();
            Assert.IsNull(SusPackageDescriptor.Load(path, "com.test.pkg", _root));
        }

        [Test]
        public void Load_GeneratedNestedInsideSource_ReturnsNull()
        {
            var path = WriteDescriptor("{\"sources\":[\"Components\"],\"generated\":\"Components/Generated\"}");

            LogAssert.ExpectAnyError();
            Assert.IsNull(SusPackageDescriptor.Load(path, "com.test.pkg", _root));
        }

        /// <summary>Descriptor errors are reported via Debug.LogError; don't fail the test on them.</summary>
        private static class LogAssert
        {
            public static void ExpectAnyError() =>
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        }
    }
}
