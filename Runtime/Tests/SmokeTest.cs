using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Smoke test: verify that the UIDocumentTestHelper creates a working UIDocument
    /// with an accessible rootVisualElement.
    /// </summary>
    public class SmokeTest : UIDocumentTestHelper
    {
        [UnityTest]
        public IEnumerator RootVisualElement_IsAccessible()
        {
            Assert.IsNotNull(Doc);
            Assert.IsNotNull(Root);

            var label = new Label("smoke");
            Root.Add(label);

            yield return WaitFrame();

            Assert.IsTrue(Root.Contains(label));
            Assert.AreEqual("smoke", label.text);
        }

        [UnityTest]
        public IEnumerator WaitFrame_AdvancesScheduler()
        {
            var label = new Label("before");
            Root.Add(label);

            // schedule.Execute runs after layout, need WaitForEndOfFrame to flush
            label.schedule.Execute(() => label.text = "after").StartingIn(0);

            yield return new WaitForEndOfFrame();

            Assert.AreEqual("after", label.text);
        }
    }
}
