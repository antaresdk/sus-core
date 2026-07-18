using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Editmode tests for WatchEffect (protected on SusComponent).
    /// Full auto-tracking + playmode re-run behavior is in playmode tests (F6.7).
    /// </summary>
    public class WatchEffectTests
    {
        private class TestComponent : SusComponent
        {
            public int RunCount { get; private set; }
            public WatchHandle Handle { get; private set; }

            public void Run(Prop<int> p)
            {
                Handle = WatchEffect(() =>
                {
                    _ = p.Value;
                    RunCount++;
                });
            }

            protected override void Build() { }
        }

        [Test]
        public void WatchEffect_RunsImmediately_OnRegistration()
        {
            var p = new Prop<int>(5);
            var comp = new TestComponent();

            comp.Run(p);

            Assert.AreEqual(1, comp.RunCount);
        }

        [Test]
        public void WatchEffect_Handle_IsNotNull()
        {
            var p = new Prop<int>(0);
            var comp = new TestComponent();

            comp.Run(p);

            Assert.IsNotNull(comp.Handle);
        }

        [Test]
        public void WatchEffect_Dispose_IsIdempotent()
        {
            var p = new Prop<int>(0);
            var comp = new TestComponent();

            comp.Run(p);
            comp.Handle.Dispose();
            comp.Handle.Dispose(); // Second dispose should not throw
        }

        [Test]
        public void WatchEffect_AfterDispose_NoReRun_EvenIfPropChanges()
        {
            var p = new Prop<int>(0);
            var comp = new TestComponent();

            comp.Run(p);
            int countBefore = comp.RunCount;
            comp.Handle.Dispose();

            p.Value = 99;

            Assert.AreEqual(countBefore, comp.RunCount,
                "After dispose, WatchEffect should not fire even if dependency changes");
        }
    }
}
