using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Wraps a <see cref="SusIconSetAsset"/> as an <see cref="ISusIconProvider"/>,
    /// so the icon set integrates with <see cref="SusIconRegistry"/>'s provider chain.
    /// Register via <c>SusIconRegistry.RegisterProvider(new AssetIconProvider(set))</c>
    /// or <c>SusApp.UseIcons(set)</c> for fluent startup registration.
    /// </summary>
    public class AssetIconProvider : ISusIconProvider
    {
        private readonly Dictionary<string, VectorImage> _lookup;
        private readonly string _setName;

        /// <param name="iconSet">The asset whose icons to expose. Null = empty provider.</param>
        public AssetIconProvider(SusIconSetAsset iconSet)
        {
            _setName = iconSet != null ? iconSet.name : "(null)";
            _lookup = new Dictionary<string, VectorImage>();

            if (iconSet == null || iconSet.Icons == null) return;

            foreach (var entry in iconSet.Icons)
            {
                if (string.IsNullOrEmpty(entry.Alias) || entry.Image == null) continue;
                var key = entry.Alias.ToLowerInvariant();
                _lookup[key] = entry.Image;
            }
        }

        /// <inheritdoc />
        public VectorImage Load(string alias, SusIconWeight weight)
        {
            if (string.IsNullOrEmpty(alias)) return null;
            _lookup.TryGetValue(alias.ToLowerInvariant(), out var img);
            return img;
        }

        /// <inheritdoc />
        public bool Has(string alias) =>
            !string.IsNullOrEmpty(alias) && _lookup.ContainsKey(alias.ToLowerInvariant());

        /// <inheritdoc />
        public IEnumerable<string> KnownNames => _lookup.Keys;

        /// <inheritdoc />
        public void Invalidate() { /* Asset-based provider doesn't cache */ }

        /// <inheritdoc />
        public override string ToString() => $"AssetIconProvider({_setName}, {_lookup.Count} icons)";
    }
}
