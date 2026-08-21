using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>
    /// Set Doctor's own minimal reader for a module's <c>sharq.gen.json</c> descriptor — reads
    /// just the <see cref="generated"/> field, via plain <see cref="File"/> IO (no
    /// AssetDatabase/PackageManager), so it stays unit-testable with a temp dir the same way
    /// <see cref="SusSetDoctor.FindModuleManifests"/>/<see cref="SusSetDoctor.CollectActualPaths"/>
    /// already are. Deliberately separate from the packaging pipeline's own richer
    /// <c>SusPackageDescriptor</c>/<c>SusPackageRegistry</c> (which resolve UPM package roots and
    /// cache per domain reload) — Diagnostics has no dependency on Packaging.
    ///
    /// <b>D8 (ARCH-PACK-CLASSIC.md §5.5, T-1489).</b> A module with a generator (its own
    /// <c>sharq.gen.json</c>) ships in a classic set WITHOUT its <c>generated</c> folder — the
    /// packer excludes it (§1.1.b: zero <c>.cs</c> in the set tree, T-972 <c>excludeBySet</c>).
    /// The purchaser's own Generate (or <c>"watch": true</c> firing on the next import) then
    /// writes that folder AFTER install. Set Doctor's <c>known</c> set is normally exactly
    /// <c>paths</c> ∪ <c>sharedPaths</c> — code the purchaser just generated is in neither, so
    /// without this reader it comes back as <c>SetDoctor.Residual</c> with a "delete or
    /// reinstall" hint on the very code the purchaser was told to create. <see
    /// cref="ResolveGeneratedZones"/> reads each present module's own descriptor and reports its
    /// declared folder as a zone the module owns — <see cref="SusSetDoctor.ClassifyStrayPaths"/>
    /// treats every path under it as known, same as if it were in <c>paths</c>.
    /// </summary>
    [Serializable]
    internal sealed class SusSharqGenManifest
    {
        internal const string FileName = "sharq.gen.json";

        // ─── JSON field (JsonUtility — only what Set Doctor needs) ─────────
        public string generated;

        /// <summary>Parses just enough of a <c>sharq.gen.json</c> to resolve its generated
        /// folder. Returns null (never throws) on malformed JSON or a missing/blank
        /// <c>generated</c> field — a broken or foreign descriptor must never break Set
        /// Doctor; the caller simply treats that module as having no generated zone.</summary>
        internal static SusSharqGenManifest Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            SusSharqGenManifest m;
            try
            {
                m = JsonUtility.FromJson<SusSharqGenManifest>(json);
            }
            catch (Exception)
            {
                return null;
            }

            if (m == null || string.IsNullOrEmpty(m.generated)) return null;
            return m;
        }

        /// <summary>Every present module's generated zone, as an Assets-relative forward-slash
        /// path (<c>&lt;root&gt;/&lt;dir&gt;/&lt;generated&gt;</c>) — one entry per module that
        /// has its OWN <c>sharq.gen.json</c> right under <c>&lt;root&gt;/&lt;dir&gt;</c> with a
        /// non-blank <c>generated</c> field. A module with no descriptor (most of them — only
        /// generator-bearing modules like kit/game/skin have one), or one that fails to parse,
        /// contributes nothing — <see cref="SusSetDoctor.ClassifyStrayPaths"/> then falls back
        /// to its pre-D8 behaviour for that module's subtree, unchanged.</summary>
        internal static List<string> ResolveGeneratedZones(
            string assetsAbsPath, string root, IReadOnlyList<SusModuleManifest> presentModules)
        {
            var zones = new List<string>();
            if (string.IsNullOrEmpty(root) || presentModules == null) return zones;

            foreach (var m in presentModules)
            {
                if (string.IsNullOrEmpty(m?.dir)) continue;

                var descriptorAbs = Path.Combine(assetsAbsPath, root, m.dir, FileName);
                if (!File.Exists(descriptorAbs)) continue;

                string json;
                try { json = File.ReadAllText(descriptorAbs); }
                catch (IOException) { continue; } // transient (mid-import) — next trigger retries

                var gen = Parse(json);
                if (gen == null) continue;

                var genRel = gen.generated.Replace('\\', '/').Trim('/');
                if (string.IsNullOrEmpty(genRel)) continue;

                zones.Add($"{root}/{m.dir}/{genRel}");
            }
            return zones;
        }
    }
}
