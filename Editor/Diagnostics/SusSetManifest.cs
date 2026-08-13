using System;
using UnityEngine;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>One entry of <see cref="SusSetManifest.modules"/> — a module shipped inside a
    /// classic set (ARCH-PACK-CLASSIC.md §5.3).</summary>
    [Serializable]
    public sealed class SusSetManifestModule
    {
        /// <summary>Manifest key, e.g. "core", "router", "kit", "game" — matches the UPM
        /// package name via <c>"com.sharq-it.sus." + id</c>.</summary>
        public string id;
        /// <summary>Folder name under the set root, e.g. "Core".</summary>
        public string dir;
        public string version;
        public string sha;
    }

    /// <summary>
    /// Parsed <c>sus-set.json</c> — the per-set machine manifest a classic .unitypackage ships
    /// at <c>Assets/&lt;root&gt;/sus-set.json</c> (ARCH-PACK-CLASSIC.md §2.1/§5.3). Consumed by
    /// <see cref="SusSetDoctor"/> to detect collisions, residual files and version drift.
    /// </summary>
    [Serializable]
    public sealed class SusSetManifest
    {
        public string set;
        public string displayName;
        public string version;
        /// <summary>Set root folder name under Assets/, e.g. "Sharq" — contractually fixed
        /// (ARCH-PACK-CLASSIC.md §2.1 D1), never renamed after first publish.</summary>
        public string root;
        public SusSetManifestModule[] modules;
        /// <summary>Every filesystem entry (folders and files, forward-slash, relative to
        /// Assets/) this version of the set ships — the source of truth for residual-file
        /// detection.</summary>
        public string[] paths;

        /// <summary>
        /// Parses <c>sus-set.json</c> contents. Returns null (never throws) on malformed JSON
        /// or a structurally incomplete manifest — a foreign/corrupted file must never break
        /// Set Doctor or domain reload; the caller reports it as a soft finding instead.
        /// </summary>
        public static SusSetManifest Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            SusSetManifest m;
            try
            {
                m = JsonUtility.FromJson<SusSetManifest>(json);
            }
            catch (Exception)
            {
                return null;
            }

            if (m == null || string.IsNullOrEmpty(m.root) || m.modules == null)
                return null;

            return m;
        }
    }
}
