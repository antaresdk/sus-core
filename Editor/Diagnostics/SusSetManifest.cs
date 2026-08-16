using System;
using UnityEngine;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>
    /// Parsed <c>sus-set.&lt;set&gt;.json</c> — the per-SET identity descriptor a classic
    /// .unitypackage ships at <c>Assets/&lt;root&gt;/sus-set.&lt;set&gt;.json</c>
    /// (ARCH-PACK-CLASSIC.md §2.3 D7 / §5.5, T-556/T-557). Unlike the pre-T-556
    /// <c>Assets/&lt;root&gt;/sus-set.json</c> this replaces, its name is per-set (so two sets can
    /// be installed side by side without one overwriting the other's descriptor) and it carries
    /// NO module paths and NO module versions — those live in each module's own
    /// <see cref="SusModuleManifest"/> now, precisely so that an out-of-date descriptor (a
    /// purchaser updated one set but not a sibling one) can never mask a real residual file by
    /// being wrongly treated as "the" list of everything under the root.
    /// </summary>
    [Serializable]
    public sealed class SusSetManifest
    {
        internal const string Schema = "sus-set/v2";

        public string schema;
        public string set;
        public string displayName;
        /// <summary>Version of the set's lead package (§4.2) — NOT a per-module version.</summary>
        public string version;
        /// <summary>Manifest key (into <c>modules</c>) of this set's lead package.</summary>
        public string lead;
        /// <summary>Set root folder name under Assets/, e.g. "Sharq" — contractually fixed
        /// (ARCH-PACK-CLASSIC.md §2.1 D1), never renamed after first publish.</summary>
        public string root;
        /// <summary>Module ids this set ships (e.g. <c>["core","router","kit"]</c>) — ids only;
        /// each module's own paths/dir/version live in its <see cref="SusModuleManifest"/>.</summary>
        public string[] modules;
        /// <summary>Paths this SET (not any one module) owns: the root folder, the generated
        /// root files (README/LICENSE/Third-Party Notices), the shared Samples node, and this
        /// descriptor itself.</summary>
        public string[] sharedPaths;

        /// <summary>
        /// Parses <c>sus-set.&lt;set&gt;.json</c> contents. Returns null (never throws) on
        /// malformed JSON, an unrecognized <see cref="schema"/>, or a structurally incomplete
        /// manifest — a foreign/corrupted/pre-T-556/future-schema file must never break Set
        /// Doctor or domain reload; the caller reports it as a soft finding instead.
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

            if (m == null || !string.Equals(m.schema, Schema, StringComparison.Ordinal))
                return null;
            if (string.IsNullOrEmpty(m.set) || string.IsNullOrEmpty(m.root) || m.modules == null)
                return null;

            return m;
        }
    }
}
