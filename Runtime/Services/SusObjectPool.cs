using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace Sharq.Core
{
    /// <summary>
    /// Generic typed object pool backed by Unity's ObjectPool.
    ///
    /// Usage:
    /// <code>
    /// var pool = new SusObjectPool<MyType>(
    ///     createFunc: () => new MyType(),
    ///     onGet: obj => obj.Reset(),
    ///     onRelease: obj => obj.Deactivate(),
    ///     onDestroy: obj => obj.Dispose());
    ///
    /// var item = pool.Get();
    /// pool.Release(item);
    /// pool.Clear();
    /// </code>
    /// </summary>
    public class SusObjectPool<T> where T : class
    {
        private readonly ObjectPool<T> _pool;

        public int CountInactive => _pool.CountInactive;
        public int CountAll { get; private set; }

        public SusObjectPool(
            Func<T> createFunc = null,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            int defaultCapacity = 10,
            int maxSize = 100)
        {
            _pool = new ObjectPool<T>(
                createFunc ?? (() => default),
                onGet,
                onRelease,
                onDestroy,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize);
        }

        public T Get()
        {
            var item = _pool.Get();
            CountAll++;
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
            _pool.Release(item);
            CountAll--;
        }

        public void Clear() => _pool.Clear();
    }
}
