using System;
using UnityEngine;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>
    /// Parsed <c>sus-module.json</c> — the per-MODULE machine manifest a classic .unitypackage
    /// ships at <c>Assets/&lt;root&gt;/&lt;Module&gt;/sus-module.json</c>
    /// (ARCH-PACK-CLASSIC.md §2.3 D7 / §5.5). Replaces the pre- single
    /// <c>Assets/&lt;root&gt;/sus-set.json</c> as the source of truth for "what paths does this
    /// module own, at what version" — content does NOT depend on which set shipped it, so this
    /// file is byte-identical between kit-set and game-set for shared modules (core/router/kit).
    /// </summary>
    [Serializable]
    public sealed class SusModuleManifest
    {
        internal const string Schema = "sus-module/v1";

        public string schema;
        /// <summary>Manifest key, e.g. "core", "router", "kit", "game", "skin". For the first
        /// four <c>"com.sharq-it.sus." + id</c> happens to equal the UPM package name; a skin
        /// module's id is <c>skin</c> while its package is <c>com.sharq-it.sus.&lt;family&gt;.&lt;name&gt;</c>
        /// — always resolve the package through <see cref="package"/> (T-1334).</summary>
        public string id;
        /// <summary>Folder name under the set root, e.g. "Core".</summary>
        public string dir;
        /// <summary>Set root folder name under Assets/, e.g. "Sharq" (ARCH-PACK-CLASSIC.md §2.1 D1).</summary>
        public string root;
        /// <summary>UPM package name of the same module (<c>com.sharq-it.sus.core</c>,
        /// <c>com.sharq-it.sus.&lt;family&gt;.&lt;name&gt;</c>, …) — the source of truth for the UPM+classic
        /// collision check; may be null in manifests written before the field existed.</summary>
        public string package;
        public string version;
        public string sha;
        /// <summary>Every filesystem entry (folders and files, forward-slash, relative to
        /// Assets/) this module owns — its own subtree (<c>&lt;root&gt;/&lt;dir&gt;/**</c>) AND
        /// its samples subtree (<c>&lt;root&gt;/Samples/&lt;dir&gt;/**</c>) — plus this
        /// manifest file's own path. The source of truth for residual-file attribution
        /// (§5.5 "правило атрибуции").</summary>
        public string[] paths;

        /// <summary>
        /// Parses <c>sus-module.json</c> contents. Returns null (never throws) on malformed JSON,
        /// an unrecognized <see cref="schema"/>, or a structurally incomplete manifest — a
        /// foreign/corrupted/future-schema file must never break Set Doctor or domain reload; the
        /// caller reports it as a soft finding instead.
        /// </summary>
        public static SusModuleManifest Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            SusModuleManifest m;
            try
            {
                m = JsonUtility.FromJson<SusModuleManifest>(json);
            }
            catch (Exception)
            {
                return null;
            }

            if (m == null || !string.Equals(m.schema, Schema, StringComparison.Ordinal))
                return null;
            if (string.IsNullOrEmpty(m.id) || string.IsNullOrEmpty(m.dir) || string.IsNullOrEmpty(m.root) || m.paths == null)
                return null;

            return m;
        }
    }
}
