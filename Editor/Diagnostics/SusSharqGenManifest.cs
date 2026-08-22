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

        // ─── JSON fields (JsonUtility — only what Set Doctor needs) ────────
        public string generated;

        /// <summary>Where the generated companion <c>.g.uss</c> is ALSO copied to for runtime
        /// <c>Resources.Load</c> (e.g. <c>"Runtime/Resources/SusRuntime"</c>) — unlike
        /// <see cref="generated"/>, this zone ships even for a module whose <c>generated</c>
        /// zone is excluded from the classic set (skin, §5.5 D8): the purchaser needs the
        /// compiled stylesheet at runtime immediately, only the regenerable <c>.cs</c> source is
        /// held back. Used by <see cref="SusSetDoctor.DetectStaleGenerated"/> (T-1526) to judge
        /// the companion <c>.g.uss</c> copy here by the same sources-stem correspondence as the
        /// <see cref="generated"/> zone. Optional — null/blank when a module has no such copy
        /// step (e.g. this manifest's own module has none).</summary>
        public string resources;

        /// <summary>Directories (relative to the module's own folder, e.g.
        /// <c>["Components"]</c>) that hold this module's <c>.sharq</c> source files —
        /// searched recursively. T-1526: the set of stems this yields (one per
        /// <c>&lt;Stem&gt;.sharq</c> found anywhere under any of these) is the ONLY thing that
        /// can tell a real component's generated output apart from a stale one — <c>paths</c>
        /// says nothing (the whole generated zone is either all-known or all-excluded) and the
        /// zone folder itself doesn't remember which stems used to be valid. Optional — a
        /// manifest with no <c>sources</c> (or a sources list that resolves to nothing on disk)
        /// gives <see cref="SusSetDoctor.DetectStaleGenerated"/> no ground truth to compare
        /// against, so that module is silently skipped rather than guessed at.</summary>
        public string[] sources;

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

        /// <summary>Per-module ground truth for <see cref="SusSetDoctor.DetectStaleGenerated"/>
        /// (T-1526): the module's declared <see cref="generated"/>/<see cref="resources"/> zones
        /// (Assets-relative, same shape as <see cref="ResolveGeneratedZones"/>) plus the current
        /// stem set its <see cref="sources"/> dirs actually contain on disk RIGHT NOW. A module
        /// contributes no entry — the check silently skips it — when its descriptor is absent,
        /// unparsable, or has no non-blank <c>sources</c> entries: without a real
        /// <c>.sharq</c> to compare against there is no ground truth, only a guess (DoD п.4).</summary>
        internal readonly struct SusGenModuleInfo
        {
            internal readonly string GeneratedZone;
            internal readonly string ResourcesZone;
            internal readonly HashSet<string> SourceStems;

            internal SusGenModuleInfo(string generatedZone, string resourcesZone, HashSet<string> sourceStems)
            {
                GeneratedZone = generatedZone;
                ResourcesZone = resourcesZone;
                SourceStems = sourceStems;
            }
        }

        internal static Dictionary<SusModuleManifest, SusGenModuleInfo> ResolveModuleGenInfo(
            string assetsAbsPath, string root, IReadOnlyList<SusModuleManifest> presentModules)
        {
            var result = new Dictionary<SusModuleManifest, SusGenModuleInfo>();
            if (string.IsNullOrEmpty(root) || presentModules == null) return result;

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
                if (gen.sources == null || gen.sources.Length == 0) continue; // DoD п.4: no sources -> silent

                var genRel = gen.generated.Replace('\\', '/').Trim('/');
                var genZone = string.IsNullOrEmpty(genRel) ? null : $"{root}/{m.dir}/{genRel}";

                string resZone = null;
                if (!string.IsNullOrEmpty(gen.resources))
                {
                    var resRel = gen.resources.Replace('\\', '/').Trim('/');
                    if (!string.IsNullOrEmpty(resRel)) resZone = $"{root}/{m.dir}/{resRel}";
                }

                var stems = new HashSet<string>(StringComparer.Ordinal);
                foreach (var src in gen.sources)
                {
                    if (string.IsNullOrEmpty(src)) continue;
                    var srcAbs = Path.Combine(assetsAbsPath, root, m.dir, src.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(srcAbs)) continue;
                    foreach (var f in Directory.EnumerateFiles(srcAbs, "*.sharq", SearchOption.AllDirectories))
                        stems.Add(Path.GetFileNameWithoutExtension(f));
                }

                result[m] = new SusGenModuleInfo(genZone, resZone, stems);
            }
            return result;
        }
    }
}
