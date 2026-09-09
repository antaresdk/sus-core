using NUnit.Framework;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// R-D8 / T-1123 — SusKeepAlive (Wrap / Active / DOM preserve). EditMode.
    /// </summary>
    public class SusKeepAliveTests
    {
        [Test]
        public void Wrap_Null_ReturnsNull()
        {
            Assert.IsNull(SusKeepAlive.Wrap(null));
        }

        [Test]
        public void Wrap_PlacesElementInContent_AndStartsActive()
        {
            var child = new Label("screen");
            var ka = SusKeepAlive.Wrap(child);

            Assert.IsNotNull(ka);
            Assert.AreSame(child, ka.Content.ElementAt(0));
            Assert.AreSame(ka.Content, child.parent);
            Assert.IsTrue(ka.Active);
            // T-3141: EditMode, element detached from any panel - there is no cascade to
            // resolve, so the `sus-hidden` class is the state (SusKeepAlive stopped writing
            // inline display in T-3129, which made the old style.display read vacuous).
            Assert.IsFalse(ka.Content.ClassListContains("sus-hidden"));
        }

        [Test]
        public void Wrap_RemovesFromPreviousParent()
        {
            var oldParent = new VisualElement();
            var child = new Label("migrating");
            oldParent.Add(child);
            Assert.AreEqual(1, oldParent.childCount);

            var ka = SusKeepAlive.Wrap(child);

            Assert.AreEqual(0, oldParent.childCount);
            Assert.AreSame(ka.Content, child.parent);
        }

        [Test]
        public void Active_False_HidesWithDisplayNone_True_Restores()
        {
            var child = new Label("stateful");
            var ka = SusKeepAlive.Wrap(child);

            ka.Active = false;
            Assert.IsFalse(ka.Active);
            // T-3129: SusKeepAlive hides with the `sus-hidden` class (T-2749/R120).
            Assert.IsTrue(ka.Content.ClassListContains("sus-hidden"));
            Assert.AreSame(ka.Content, child.parent, "child stays in DOM when hidden");

            ka.Active = true;
            Assert.IsTrue(ka.Active);
            Assert.IsFalse(ka.Content.ClassListContains("sus-hidden"));
            Assert.AreSame(ka.Content, child.parent);
        }

        [Test]
        public void Active_Toggle_PreservesChildIdentity()
        {
            var child = new Label("keep-me") { name = "screen-a" };
            var ka = SusKeepAlive.Wrap(child);
            ka.Active = false;
            ka.Active = true;

            Assert.AreEqual(1, ka.Content.childCount);
            Assert.AreSame(child, ka.Content.Q<Label>("screen-a"));
            Assert.AreEqual("keep-me", child.text);
        }
    }
}
