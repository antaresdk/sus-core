using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    public class ComputedTests
    {
        [Test]
        public void Value_ReturnsComputedResult()
        {
            var p = new Prop<int>(5);
            var c = new Computed<int>(() => p.Value * 2);

            Assert.AreEqual(10, c.Value);
        }

        [Test]
        public void Value_CachesResult_UntilInvalidated()
        {
            int callCount = 0;
            var p = new Prop<int>(3);
            var c = new Computed<int>(() => { callCount++; return p.Value; });

            _ = c.Value;
            _ = c.Value;
            _ = c.Value;

            Assert.AreEqual(1, callCount, "Repeated reads should not recompute");
        }

        [Test]
        public void Invalidate_CausesRecomputation()
        {
            int callCount = 0;
            var p = new Prop<int>(3);
            var c = new Computed<int>(() => { callCount++; return p.Value; });

            _ = c.Value;
            Assert.AreEqual(1, callCount);

            p.Value = 7; // should invalidate computed
            _ = c.Value;

            Assert.AreEqual(2, callCount);
            Assert.AreEqual(7, c.Value);
        }

        [Test]
        public void Chain_PropToComputedA_ToComputedB()
        {
            var p = new Prop<int>(5);
            var a = new Computed<int>(() => p.Value * 2);     // 10
            var b = new Computed<int>(() => a.Value + 1);     // 11

            Assert.AreEqual(10, a.Value);
            Assert.AreEqual(11, b.Value);

            p.Value = 10;
            Assert.AreEqual(20, a.Value);
            Assert.AreEqual(21, b.Value);
        }

        [Test]
        public void ImplementsIReactiveSource()
        {
            var p = new Prop<int>(0);
            var c = new Computed<int>(() => p.Value);

            Assert.IsInstanceOf<IReactiveSource>(c);
        }

        [Test]
        public void SubscribeInvalidate_FiresOnSourceChange()
        {
            var p = new Prop<int>(0);
            var c = new Computed<int>(() => p.Value);
            _ = c.Value; // trigger lazy subscription to p
            bool invalidated = false;
            var sub = ((IReactiveSource)c).SubscribeInvalidate(() => invalidated = true);

            p.Value = 42;
            Assert.IsTrue(invalidated);
            sub.Dispose();
        }

        [Test]
        public void SubscribeInvalidate_DoesNotFireAfterDispose()
        {
            var p = new Prop<int>(0);
            var c = new Computed<int>(() => p.Value);

            int fireCount = 0;
            var sub = ((IReactiveSource)c).SubscribeInvalidate(() => fireCount++);
            sub.Dispose();

            p.Value = 99;
            Assert.AreEqual(0, fireCount);
        }

        [Test]
        public void ImplicitOperator_ReturnsValue()
        {
            var p = new Prop<string>("hello");
            var c = new Computed<string>(() => p.Value.ToUpper());

            string val = c;
            Assert.AreEqual("HELLO", val);
        }

        [Test]
        public void Refresh_RecomputesImmediately()
        {
            int callCount = 0;
            var p = new Prop<int>(1);
            var c = new Computed<int>(() => { callCount++; return p.Value; });

            _ = c.Value;
            p.Value = 2;
            c.Refresh();

            Assert.AreEqual(2, callCount);
            Assert.AreEqual(2, c.Value);
        }

        #region IReactiveSource on Prop (from former ReactiveEffectTests)

        [Test]
        public void IReactiveSource_SubscribeInvalidate_OnProp_ReturnsHandle()
        {
            var p = new Prop<int>(0);
            var handle = ((IReactiveSource)p).SubscribeInvalidate(() => { });
            Assert.IsNotNull(handle);
            handle.Dispose();
        }

        [Test]
        public void IReactiveSource_SubscribeInvalidate_OnProp_DisposeStops()
        {
            var p = new Prop<int>(0);
            int invalidationCount = 0;
            var handle = ((IReactiveSource)p).SubscribeInvalidate(() => invalidationCount++);

            p.Value = 1;
            Assert.AreEqual(1, invalidationCount);

            handle.Dispose();
            p.Value = 2;
            Assert.AreEqual(1, invalidationCount, "Should not fire after dispose");
        }

        #endregion
    }
}
