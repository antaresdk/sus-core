using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    public class SusObjectPoolTests
    {
        private class Poolable
        {
            public bool ResetCalled;
            public bool Deactivated;
        }

        [Test]
        public void Get_ReturnsNewInstance_WhenPoolIsEmpty()
        {
            int created = 0;
            var pool = new SusObjectPool<Poolable>(
                createFunc: () => { created++; return new Poolable(); });

            var item = pool.Get();

            Assert.IsNotNull(item);
            Assert.AreEqual(1, created);
            Assert.AreEqual(1, pool.CountAll);
            Assert.AreEqual(0, pool.CountInactive);
        }

        [Test]
        public void Release_ReturnsToPool()
        {
            var pool = new SusObjectPool<Poolable>(() => new Poolable());
            var item = pool.Get();

            pool.Release(item);

            Assert.AreEqual(0, pool.CountAll);
            Assert.AreEqual(1, pool.CountInactive);
        }

        [Test]
        public void Get_AfterRelease_ReusesInstance()
        {
            var pool = new SusObjectPool<Poolable>(() => new Poolable());
            var first = pool.Get();

            pool.Release(first);
            var second = pool.Get();

            Assert.AreSame(first, second, "Pool should reuse released instances");
        }

        [Test]
        public void Release_BeyondMaxSize_DoesNotPool()
        {
            var pool = new SusObjectPool<Poolable>(
                createFunc: () => new Poolable(),
                maxSize: 2);

            var a = pool.Get();
            var b = pool.Get();
            var c = pool.Get();

            // Pool capacity = 2. Release 3 items.
            pool.Release(a);
            pool.Release(b);
            pool.Release(c);

            Assert.LessOrEqual(pool.CountInactive, 2,
                "Should not pool more than maxSize items");
        }

        [Test]
        public void Clear_EmptiesThePool()
        {
            var pool = new SusObjectPool<Poolable>(() => new Poolable());
            var item = pool.Get();
            pool.Release(item);

            Assert.Greater(pool.CountInactive, 0);

            pool.Clear();

            Assert.AreEqual(0, pool.CountInactive);
        }

        [Test]
        public void OnGetAction_FiresOnGet()
        {
            Poolable resetTarget = null;
            var pool = new SusObjectPool<Poolable>(
                createFunc: () => new Poolable(),
                onGet: obj => resetTarget = obj);

            var item = pool.Get();

            Assert.AreSame(item, resetTarget);
        }

        [Test]
        public void OnReleaseAction_FiresOnRelease()
        {
            Poolable deactivatedTarget = null;
            var pool = new SusObjectPool<Poolable>(
                createFunc: () => new Poolable(),
                onRelease: obj => deactivatedTarget = obj);

            var item = pool.Get();
            pool.Release(item);

            Assert.AreSame(item, deactivatedTarget);
        }
    }
}
