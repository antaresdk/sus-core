using System;
using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    public class EffectScopeTests
    {
        #region Basic scope lifecycle

        [Test]
        public void Dispose_UnsubscribesAllRegisteredHandles()
        {
            var scope = new EffectScope();
            var p1 = new Prop<int>(0);
            var p2 = new Prop<int>(0);
            int fireCount1 = 0, fireCount2 = 0;

            var h1 = scope.Watch(p1, (_, _) => fireCount1++);
            var h2 = scope.Watch(p2, (_, _) => fireCount2++);

            p1.Value = 1;
            p2.Value = 1;
            Assert.AreEqual(1, fireCount1);
            Assert.AreEqual(1, fireCount2);

            scope.Dispose();

            p1.Value = 2;
            p2.Value = 2;
            Assert.AreEqual(1, fireCount1, "Should NOT fire after dispose");
            Assert.AreEqual(1, fireCount2, "Should NOT fire after dispose");
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var scope = new EffectScope();
            var p = new Prop<int>(0);
            int fireCount = 0;
            scope.Watch(p, (_, _) => fireCount++);

            scope.Dispose();
            scope.Dispose(); // second call should NOT throw
            scope.Dispose(); // third call should NOT throw
        }

        [Test]
        public void Dispose_RunsOnDisposeActions()
        {
            var scope = new EffectScope();
            int cleanup1 = 0, cleanup2 = 0;

            scope.OnDispose(() => cleanup1 = 1);
            scope.OnDispose(() => cleanup2 = 2);

            Assert.AreEqual(0, cleanup1);
            Assert.AreEqual(0, cleanup2);

            scope.Dispose();

            Assert.AreEqual(1, cleanup1);
            Assert.AreEqual(2, cleanup2);
        }

        [Test]
        public void OnDispose_AfterDispose_Throws()
        {
            var scope = new EffectScope();
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.OnDispose(() => { }));
        }

        [Test]
        public void Register_AfterDispose_Throws()
        {
            var scope = new EffectScope();
            var p = new Prop<int>(0);
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.Watch(p, (_, _) => { }));
        }

        #endregion

        #region Standalone usage (no SusComponent)

        [Test]
        public void Standalone_Watch_PropagatesChanges()
        {
            var scope = new EffectScope();
            var p = new Prop<string>("a");
            string lastVal = null;
            scope.Watch(p, (_, v) => lastVal = v);

            p.Value = "b";
            Assert.AreEqual("b", lastVal);

            scope.Dispose();
            p.Value = "c";
            Assert.AreEqual("b", lastVal, "Should not update after dispose");
        }

        [Test]
        public void Standalone_DisposeAll_StopsAllSubscriptions()
        {
            var scope = new EffectScope();
            var p1 = new Prop<int>(0);
            var p2 = new Prop<int>(0);
            var p3 = new Prop<int>(0);
            int totalCalls = 0;

            scope.Watch(p1, (_, _) => totalCalls++);
            scope.Watch(p2, (_, _) => totalCalls++);
            scope.Watch(p3, (_, _) => totalCalls++);

            p1.Value = 1;
            p2.Value = 1;
            Assert.AreEqual(2, totalCalls);

            scope.Dispose();

            p1.Value = 2;
            p2.Value = 2;
            p3.Value = 2;
            Assert.AreEqual(2, totalCalls, "No further calls after dispose");
        }

        #endregion
    }
}
