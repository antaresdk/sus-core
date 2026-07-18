using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Source of icon assets for <see cref="SusIconRegistry"/>. The registry owns
    /// alias resolution, weight-suffix detection and caching; a provider only has to
    /// turn an already-resolved base name (+ optional weight) into a
    /// <see cref="VectorImage"/>, and enumerate the names it can supply.
    ///
    /// This decouples the icon system from any specific icon set: the built-in
    /// <see cref="CoreIconProvider"/> reads the minimal core SVG set from Resources, the
    /// optional Phosphor package registers its own provider, and a game can register a
    /// custom provider (project SVGs / an icon-set asset) via
    /// <see cref="SusIconRegistry.RegisterProvider"/> and have it take priority.
    /// </summary>
    public interface ISusIconProvider
    {
        /// <summary>
        /// Returns the icon asset for a resolved base <paramref name="name"/> at the given
        /// <paramref name="weight"/>, or <c>null</c> if this provider has no such icon (the
        /// registry then falls through to the next provider). Providers whose set has no
        /// weights may ignore <paramref name="weight"/>.
        /// </summary>
        VectorImage Load(string name, SusIconWeight weight);

        /// <summary>All base names this provider can supply — for browsing / categories.</summary>
        IEnumerable<string> KnownNames { get; }

        /// <summary>Drops any cached lookups. Called from <see cref="SusIconRegistry.InvalidateCache"/>.</summary>
        void Invalidate();
    }
}
