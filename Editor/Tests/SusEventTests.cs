using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    public class SusEventTests
    {
        [Test]
        public void SubscribeAndEmit_HandlerCalledWithArgs()
        {
            var evt = new SusEvent<ClickArgs>();
            ClickArgs received = default;
            var comp = new DummySusComponent();
            evt.Subscribe(args => received = args);

            evt.Emit(new ClickArgs(comp));

            Assert.AreEqual(comp, received.Target);
        }

        [Test]
        public void Emit_MultipleSubscribers_AllCalled()
        {
            var evt = new SusEvent<int>();
            int sum = 0;
            evt.Subscribe(v => sum += v);
            evt.Subscribe(v => sum += v);

            evt.Emit(5);

            Assert.AreEqual(10, sum);
        }

        [Test]
        public void Unsubscribe_StopsHandler()
        {
            var evt = new SusEvent<string>();
            int callCount = 0;
            void Handler(string _) => callCount++;
            evt.Subscribe(Handler);

            evt.Emit("a");
            Assert.AreEqual(1, callCount);

            evt.Unsubscribe(Handler);
            evt.Emit("b");
            Assert.AreEqual(1, callCount, "Should not fire after unsubscribe");
        }

        [Test]
        public void Unit_Emit_NoArgs_Works()
        {
            var evt = new SusEvent<Unit>();
            int callCount = 0;
            evt.Subscribe(_ => callCount++);

            evt.Emit();

            Assert.AreEqual(1, callCount);
        }

        private class DummySusComponent : SusComponent
        {
            protected override void Build() { }
        }
    }
}
