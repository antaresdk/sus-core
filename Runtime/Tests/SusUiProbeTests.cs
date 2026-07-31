#if UNITY_EDITOR || DEVELOPMENT_BUILD
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Diagnostics;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>Phase 0 smoke: SusUiProbe returns parseable JSON without touching the Console.</summary>
    public class SusUiProbeTests
    {
        [Test]
        public void GetTreeJson_ReturnsNonEmptyParseableTree()
        {
            var root = new VisualElement { name = "root" };
            root.Add(new Label("hello") { name = "greeting" });

            var json = SusUiProbe.GetTreeJson(root);

            Assert.IsNotNull(json);
            Assert.IsTrue(json.StartsWith("["), "tree JSON must start with [");
            Assert.IsTrue(json.EndsWith("]"), "tree JSON must end with ]");
            StringAssert.Contains("\"name\":\"greeting\"", json);
            StringAssert.Contains("\"text\":\"hello\"", json);
        }

        [Test]
        public void GetHealthJson_CountsElementsAndHasAnomaliesArray()
        {
            var root = new VisualElement();
            root.Add(new VisualElement());
            root.Add(new VisualElement());

            var json = SusUiProbe.GetHealthJson(root);

            StringAssert.Contains("\"totalElements\":3", json);
            StringAssert.Contains("\"anomalies\":[", json);
        }

        [Test]
        public void GetPropsJson_MissingComponent_ReturnsError()
        {
            var root = new VisualElement();
            var json = SusUiProbe.GetPropsJson(root, "DoesNotExist");
            StringAssert.Contains("\"error\":\"not found\"", json);
        }
    }
}
#endif
