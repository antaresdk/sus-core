using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    public abstract partial class SusComponent
    {
        /// <summary>
        /// Optional fail-fast when Provide overwrites an existing key without overwrite:true.
        /// </summary>
        public static System.Action<string, string> OnDuplicateProvide;

        /// <summary>
        /// Optional fail-fast when the same slot content/factory is registered twice.
        /// </summary>
        public static System.Action<string, string> OnDuplicateSlot;

        // ─── Provide / Inject ─────────────────────────────────────────────

        private Dictionary<string, object> _provided;

        /// <summary>
        /// Provide a value to all descendant components in the visual tree.
        /// Descendants call Inject&lt;T&gt;(key) to retrieve it.
        /// Works across any depth — no prop drilling.
        /// </summary>
        protected void Provide<T>(string key, T value, bool overwrite = false)
        {
            if (_provided == null)
                _provided = new Dictionary<string, object>();
            if (!overwrite && _provided.ContainsKey(key))
                OnDuplicateProvide?.Invoke(GetType().Name, key);
            _provided[key] = value;
        }

        /// <summary>
        /// Walks up the visual tree to find the first ancestor that provides the given key.
        /// Returns the provider component and value, or (null, null) if not found.
        /// </summary>
        private bool TryFindProvider(string key, out SusComponent provider, out object value)
        {
            VisualElement current = this;
            while (current != null)
            {
                if (current is SusComponent sc && sc._provided != null && sc._provided.TryGetValue(key, out value))
                {
                    provider = sc;
                    return true;
                }
                current = current.parent;
            }
            provider = null;
            value = null;
            return false;
        }

        /// <summary>
        /// Inject a value provided by an ancestor. Walks up the visual tree.
        /// Throws KeyNotFoundException if no ancestor provided this key.
        /// </summary>
        protected T Inject<T>(string key)
        {
            if (!TryFindProvider(key, out _, out var val))
                throw new KeyNotFoundException(
                    $"Injection key '{key}' not found in any ancestor. " +
                    $"Ensure a parent component calls Provide<{typeof(T).Name}>(\"{key}\", ...).");
            return (T)val;
        }

        /// <summary>
        /// Non-throwing version. Returns true if found.
        /// </summary>
        protected bool TryInject<T>(string key, out T value)
        {
            if (TryFindProvider(key, out _, out var obj))
            {
                value = (T)obj;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Returns true if this key is provided by any ancestor (or self).
        /// </summary>
        protected bool HasInjection(string key)
        {
            return TryFindProvider(key, out _, out _);
        }
    }
}
