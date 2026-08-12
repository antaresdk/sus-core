using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Project icon set — assign aliased VectorImages in the Inspector.
    /// Create via <c>Assets → Create → SUS → Icon Set</c>.
    /// Pass to <c>SusApp.UseIcons(set)</c> to register as highest-priority provider.
    /// </summary>
    [CreateAssetMenu(menuName = "SUS/Icon Set", fileName = "SusIconSet", order = 201)]
    public class SusIconSetAsset : ScriptableObject
    {
        /// <summary>
        /// One icon entry: alias ↔ VectorImage.
        /// Alias is case-insensitive — used in <c>SusIconRegistry.Load(alias)</c>.
        /// </summary>
        [Serializable]
        public struct IconEntry
        {
            [Tooltip("Alias for SusIconRegistry.Load(alias), e.g. \"settings\", \"close\", \"add\".")]
            public string Alias;

            [Tooltip("The VectorImage asset (SVG imported as UI Toolkit vector).")]
            public VectorImage Image;
        }

        [Header("Icons")]
        [Tooltip("List of named icons. Each entry overrides or extends the default Phosphor set.")]
        public List<IconEntry> Icons = new();

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Ensure no duplicate aliases
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Icons.Count; i++)
            {
                var alias = Icons[i].Alias;
                if (string.IsNullOrEmpty(alias)) continue;
                if (!seen.Add(alias))
                    SusLog.Warn($"[SusIconSetAsset] Duplicate alias '{alias}' at index {i} in {name}.");
            }
        }
#endif
    }
}
