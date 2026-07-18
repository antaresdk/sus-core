using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Screens-layer container (framework primitive, sus-core).
    ///
    /// Mounts one active screen child at a time and maintains an LRU KeepAlive
    /// cache of detached screens keyed by an arbitrary string (e.g. a route path).
    /// It is <b>navigation-agnostic</b> — it knows nothing about routes/URLs; the
    /// router package (sus-router) drives it via a <c>SusRouteView</c> subclass.
    ///
    /// This is the "screens" counterpart to <see cref="OverlayHost"/> (overlays layer):
    /// both are physical layer containers owned by core so higher-level packages can
    /// only mount into the layers core declares.
    ///
    /// <typeparamref name="TScreen"/> is the concrete screen element type
    /// (e.g. router's <c>SusScreen</c>). Override <see cref="OnScreenEvicted"/> to run
    /// a teardown hook when a cached screen is evicted from the KeepAlive cache.
    /// </summary>
    public class SusScreenOutlet<TScreen> : SusLayer where TScreen : VisualElement
    {
        /// <summary>Maximum number of KeepAlive-cached screens (LRU eviction).</summary>
        public int MaxKeepAlive { get; set; } = 10;

        /// <summary>Current active screen mounted in this outlet.</summary>
        public TScreen CurrentScreen { get; protected set; }

        private readonly Dictionary<string, TScreen> _keepAliveCache = new();
        private readonly List<string> _keepAliveOrder = new(); // LRU: front = oldest

        /// <summary>
        /// USS: <c>.sus-screen-outlet</c> (flex-grow) / root route view uses
        /// <c>.sus-route-view--root</c> (absolute fill) — see SusRuntime/_global.uss.
        /// </summary>
        protected SusScreenOutlet()
        {
            AddToClassList("sus-screen-outlet");
        }

        /// <summary>
        /// Mounts a single child screen into this outlet, replacing the previously
        /// mounted child. Idempotent when the same screen is re-mounted.
        /// </summary>
        public void MountChild(TScreen screen)
        {
            if (screen == null) return;

            if (CurrentScreen != null && CurrentScreen != screen && CurrentScreen.parent == this)
                Remove(CurrentScreen);

            if (screen.parent != this)
            {
                Add(screen);
                screen.style.flexGrow = 1f;
            }
            CurrentScreen = screen;
        }

        /// <summary>Adds a screen directly, removing the current one first.</summary>
        public void StackScreen(TScreen screen)
        {
            if (screen == null) return;

            if (CurrentScreen != null && CurrentScreen.parent == this)
                Remove(CurrentScreen);

            Add(screen);
            screen.style.flexGrow = 1f;
            CurrentScreen = screen;
        }

        /// <summary>Removes a screen from the outlet.</summary>
        public void RemoveScreen(TScreen screen)
        {
            if (screen == null) return;
            if (screen.parent == this)
                Remove(screen);
            if (CurrentScreen == screen)
                CurrentScreen = null;
        }

        // ════════════════════════════════════════════════════════════════
        //  KeepAlive cache
        // ════════════════════════════════════════════════════════════════

        /// <summary>Tries to retrieve a cached KeepAlive screen (marks it most-recently-used).</summary>
        public bool TryGetKeepAliveScreen(string key, out TScreen screen)
        {
            if (!string.IsNullOrEmpty(key) && _keepAliveCache.TryGetValue(key, out screen))
            {
                _keepAliveOrder.Remove(key);
                _keepAliveOrder.Add(key);
                return true;
            }
            screen = null;
            return false;
        }

        /// <summary>Caches a screen for KeepAlive; evicts the oldest if over <see cref="MaxKeepAlive"/>.</summary>
        public void CacheKeepAliveScreen(string key, TScreen screen)
        {
            if (screen == null || string.IsNullOrEmpty(key)) return;

            if (_keepAliveCache.ContainsKey(key))
            {
                _keepAliveOrder.Remove(key);
            }
            else
            {
                while (_keepAliveOrder.Count >= MaxKeepAlive)
                {
                    var oldest = _keepAliveOrder[0];
                    _keepAliveOrder.RemoveAt(0);
                    if (_keepAliveCache.TryGetValue(oldest, out var oldScreen))
                    {
                        OnScreenEvicted(oldScreen);
                        _keepAliveCache.Remove(oldest);
                    }
                }
            }

            _keepAliveCache[key] = screen;
            _keepAliveOrder.Add(key);
        }

        /// <summary>Clears all cached KeepAlive screens (running the eviction hook for each).</summary>
        public void ClearKeepAliveCache()
        {
            foreach (var kv in _keepAliveCache)
                OnScreenEvicted(kv.Value);
            _keepAliveCache.Clear();
            _keepAliveOrder.Clear();
        }

        /// <summary>
        /// Called when a screen is evicted from the KeepAlive cache (LRU overflow or clear).
        /// Base implementation does nothing; subclasses run screen teardown (e.g. lifecycle Left()).
        /// </summary>
        protected virtual void OnScreenEvicted(TScreen screen) { }
    }
}
