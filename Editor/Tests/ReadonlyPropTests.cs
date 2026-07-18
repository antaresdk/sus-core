using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    public class ReadonlyPropTests
    {
        [Test]
        public void Value_ReturnsSourceValue()
        {
            var p = new Prop<int>(42);
            var r = p.AsReadonly();

            Assert.AreEqual(42, r.Value);
        }

        [Test]
        public void Value_TracksSourceChanges()
        {
            var p = new Prop<string>("old");
            var r = p.AsReadonly();

            p.Value = "new";

            Assert.AreEqual("new", r.Value);
        }

        [Test]
        public void Changed_Event_MirrorsSource()
        {
            var p = new Prop<int>(1);
            var r = p.AsReadonly();
            int oldVal = -1, newVal = -1;
            r.Changed += (o, n) => { oldVal = o; newVal = n; };

            p.Value = 42;

            Assert.AreEqual(1, oldVal);
            Assert.AreEqual(42, newVal);
        }

        [Test]
        public void Changed_Unsubscribe_Works()
        {
            var p = new Prop<int>(0);
            var r = p.AsReadonly();
            int fireCount = 0;
            void Handler(int _, int __) => fireCount++;
            r.Changed += Handler;

            p.Value = 1;
            Assert.AreEqual(1, fireCount);

            r.Changed -= Handler;
            p.Value = 2;
            Assert.AreEqual(1, fireCount, "Should not fire after unsubscribe");
        }

        [Test]
        public void ImplicitOperator_ReturnsValue()
        {
            var p = new Prop<string>("test");
            var r = p.AsReadonly();

            string val = r;

            Assert.AreEqual("test", val);
        }

        [Test]
        public void Select_OnReadonly_ReturnsReadonly()
        {
            var p = new Prop<int>(5);
            var r = p.AsReadonly();
            var selected = r.Select(x => x * 2);

            Assert.IsInstanceOf<ReadonlyProp<int>>(selected);
            Assert.AreEqual(10, selected.Value);

            p.Value = 7;
            Assert.AreEqual(14, selected.Value);
        }
    }
}
