using NUnit.Framework;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// R-D8 / T-1123 — SusDevtools (Attach / Toggle / Detach). EditMode.
    /// </summary>
    public class SusDevtoolsTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            SusDevtools.Detach();
            _root = new VisualElement { name = "devtools-root", focusable = true };
        }

        [TearDown]
        public void TearDown()
        {
            SusDevtools.Detach();
            _root = null;
        }

        [Test]
        public void Attach_Null_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => SusDevtools.Attach(null));
        }

        [Test]
        public void Attach_CreatesHiddenPanel_NamedSusDevtools()
        {
            SusDevtools.Attach(_root);

            var panel = _root.Q("sus-devtools");
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.ClassListContains("sus-devtools"));
            // T-3129: hidden is the `sus-hidden` CLASS now (T-2749/R120), not an inline display.
            Assert.IsTrue(panel.ClassListContains("sus-hidden"));
            Assert.IsFalse(SusDevtools.IsVisible);
        }

        [Test]
        public void Toggle_ShowsThenHides()
        {
            SusDevtools.Attach(_root);
            Assert.IsFalse(SusDevtools.IsVisible);

            SusDevtools.Toggle();
            Assert.IsTrue(SusDevtools.IsVisible);
            Assert.IsFalse(_root.Q("sus-devtools").ClassListContains("sus-hidden"));

            SusDevtools.Toggle();
            Assert.IsFalse(SusDevtools.IsVisible);
            Assert.IsTrue(_root.Q("sus-devtools").ClassListContains("sus-hidden"));
        }

        [Test]
        public void Attach_Idempotent_DoesNotDuplicatePanel()
        {
            SusDevtools.Attach(_root);
            SusDevtools.Attach(_root);

            Assert.AreEqual(1, _root.Query("sus-devtools").ToList().Count);
        }

        [Test]
        public void Detach_RemovesPanel_AndClearsVisible()
        {
            SusDevtools.Attach(_root);
            SusDevtools.Toggle();
            Assert.IsTrue(SusDevtools.IsVisible);

            SusDevtools.Detach();

            Assert.IsNull(_root.Q("sus-devtools"));
            Assert.IsFalse(SusDevtools.IsVisible);
        }
    }
}
