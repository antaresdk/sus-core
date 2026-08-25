using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>
    /// SUS Set Doctor v2 (ARCH-PACK-CLASSIC.md §2.3 D7 / §5.5) — detects several
    /// states a classic (.unitypackage) purchaser can silently end up in, because Unity's asset
    /// import only ADDS files, it never deletes or reconciles them:
    ///
    ///  1. <b>UPM + classic collision</b> (<see cref="DetectUpmCollisions"/>) — a UPM package for
    ///     a module (Packages/) coexists with the classic asset copy of the same module
    ///     (Assets/&lt;root&gt;/&lt;Module&gt;) — same asmdef name defined twice.
    ///  2. <b>Residual files</b> (<see cref="ClassifyStrayPaths"/>) — files from a previous
    ///     module version remain on disk after updating to a newer .unitypackage that no longer
    ///     ships them (real case from the audit: <c>SusIcon</c> → <c>SusIconElement</c>).
    ///  3. <b>Mixed module versions</b> (<see cref="DetectVersionMismatches"/>) — one module was
    ///     updated (or manually replaced) while others were not.
    ///  4. <b>Unattributed files</b>, <b>missing module manifests</b>, <b>incomplete sets</b> and
    ///     <b>relocated folders</b> — see the "правило атрибуции" below.
    /// 5. <b>Root file provenance</b> (<see cref="DetectRootFileProvenance"/>) — the
    ///     generated <c>README.txt</c>/<c>LICENSE.txt</c>/<c>Third-Party Notices.txt</c> share ONE
    ///     path per set root; re-importing a smaller set on top of an already-installed larger one
    ///     silently overwrites them with the smaller set's content (e.g. Complete's demo-art
    ///     notices quietly dropped out of the combined Third-Party Notices.txt after a Kit
    ///     re-import). A second branch covers <b>disjoint</b> co-installed sets (no shared
    ///     modules, no strict nesting either way): last import still wins the three shared
    ///     paths, so the combined notice loses the other set's attributions —
    ///     <c>SetDoctor.RootFileProvenanceDisjoint</c> (R33/I15(5)).
    ///
    /// <b>Правило атрибуции (§2.3 D7).</b> Before a single shared
    /// <c>Assets/&lt;root&gt;/sus-set.json</c> was overwritten by WHICHEVER set was imported
    /// last, so its "everything under the root" semantics made the sibling set's own modules
    /// look like residue of an old version — Set Doctor would tell a Complete owner who
    /// re-imported Kit on top to delete their own Game module. Since each module
    /// owns its own manifest (<see cref="SusModuleManifest"/>, at
    /// <c>Assets/&lt;root&gt;/&lt;Module&gt;/sus-module.json</c>) that ships and is overwritten
    /// together with the module's own files — it structurally cannot go stale relative to a
    /// SIBLING module the way one shared file could. The rule this class follows everywhere:
    /// <b>a path is only ever called residual (and only THIS class of finding carries a "delete"
    /// hint) when it can be positively attributed to a module whose OWN manifest is present and
    /// does not list it.</b> Anything else under the set root that isn't explicitly known
    /// (a purchaser's own file, or the remnant of a module whose manifest was itself removed) is
    /// reported without ever suggesting deletion.
    ///
    /// <b>Documented boundary (not solved, by design):</b> when the colliding module is the one
    /// hosting this class (<c>com.sharq-it.sus.core</c>) and BOTH copies are present from the
    /// very first import of a fresh project — neither copy has ever compiled — Unity's asmdef
    /// pipeline rejects both definitions of <c>com.sharq-it.sus.core.editor</c> before any of
    /// its code, including this class, ever runs (verified against a live collision repro: the
    /// Editor log shows only Unity's own "Assembly with name '...' already exists" + "Scripts
    /// have compiler errors" — Doctor never gets a chance to print anything in that exact
    /// scenario). Doctor reliably catches: (a) a collision on router/kit/game — their asmdef
    /// failure never touches core.editor, so Doctor keeps running; (b) a core collision
    /// introduced incrementally, i.e. added to a project where core already had one
    /// successfully-compiled state to run Doctor from (its AssetPostprocessor hook still
    /// executes using the last-good assembly on the very same import batch that introduces the
    /// collision, which is before <c>CompilationPipeline</c> attempts — and fails — the
    /// duplicate-assembly compile). This is why Doctor lives in core: it is the module least
    /// likely to be the one that takes itself down.
    /// </summary>
    public static class SusSetDoctor
    {
        /// <summary>Filename of a per-module manifest — one lives inside EACH module's own
        /// folder (<c>Assets/&lt;root&gt;/&lt;Module&gt;/sus-module.json</c>).</summary>
        public const string ModuleManifestFileName = "sus-module.json";
        private const string SetDescriptorPrefix = "sus-set.";
        private const string SetDescriptorSuffix = ".json";
        private const string PackageIdPrefix = "com.sharq-it.sus.";

        /// <summary>Generated root files whose CONTENT is shared/overwritable per set root (they
        /// are listed in a set descriptor's <c>sharedPaths</c>, not owned by any one module) —
        /// mirrors the equivalent list in the set packaging tool that generates these files
        /// (§5.5 point 10 / risk R11).</summary>
        internal static readonly string[] RootFileNames = { "README.txt", "LICENSE.txt", "Third-Party Notices.txt" };

        private static readonly Regex ChangelogVersionHeading =
            new(@"^##\s*\[([^\]]+)\]", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Matches the set packaging tool's deterministic provenance marker
        /// (<c>Generated for: &lt;set&gt; v&lt;version&gt;</c>, no dates) against a single
        /// trimmed line.</summary>
        private static readonly Regex RootFileProvenanceMarker =
            new(@"^Generated for:\s*(?<set>\S+)\s+v(?<version>.+)$", RegexOptions.Compiled);

        /// <summary>True for a per-SET descriptor filename, i.e. <c>sus-set.&lt;set&gt;.json</c>
        /// (the old single <c>sus-set.json</c> does NOT match — that name is never written
        /// again, §2.3 D7 п.3 / инвариант I15(4), and there are zero live purchasers on the old
        /// format to stay compatible with, R12).</summary>
        internal static bool IsSetDescriptorFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            if (!fileName.StartsWith(SetDescriptorPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!fileName.EndsWith(SetDescriptorSuffix, StringComparison.OrdinalIgnoreCase)) return false;
            return fileName.Length > SetDescriptorPrefix.Length + SetDescriptorSuffix.Length;
        }

        [MenuItem("Window/SUS/Set Doctor")]
        public static void RunFromMenu()
        {
            var issues = RunAll();
            LogAndShow(issues, forceDialog: true);
        }

        /// <summary>Full check against the live project: finds every present module manifest and
        /// set descriptor, gathers UPM/disk state and returns every finding. Returns an empty
        /// list — silently — when no classic-set manifest of any kind is present (a plain UPM
        /// package development project is not a classic-set install and has nothing to check).</summary>
        public static List<SusValidationIssue> RunAll()
        {
            var issues = new List<SusValidationIssue>();

            var moduleFinds = FindModuleManifests();
            var setFinds = FindSetDescriptors();
            if (moduleFinds.Count == 0 && setFinds.Count == 0) return issues;

            foreach (var f in moduleFinds.Where(x => x.manifest == null))
                issues.Add(SusValidationIssue.Warning("SetDoctor",
                    $"'{f.assetPath}' exists but could not be parsed as a {ModuleManifestFileName} manifest.",
                    "Reinstall the set that ships this module — the manifest may be truncated, hand-edited, or from an incompatible SUS version."));
            foreach (var f in setFinds.Where(x => x.manifest == null))
                issues.Add(SusValidationIssue.Warning("SetDoctor",
                    $"'{f.assetPath}' exists but could not be parsed as a set descriptor.",
                    "Reinstall the set from the Asset Store — the manifest may be truncated, hand-edited, or from an incompatible SUS version."));

            var presentModules = moduleFinds.Where(x => x.manifest != null).Select(x => x.manifest).ToList();
            var presentDescriptors = setFinds.Where(x => x.manifest != null).Select(x => x.manifest).ToList();
            if (presentModules.Count == 0 && presentDescriptors.Count == 0) return issues;

            var root = ResolveRoot(moduleFinds, setFinds);
            if (string.IsNullOrEmpty(root)) return issues;

            issues.AddRange(DetectRelocatedModules(moduleFinds.Where(x => x.manifest != null)
                .Select(x => (x.manifest, x.actualRoot, x.actualDir)).ToList()));
            issues.AddRange(DetectRelocatedDescriptors(setFinds.Where(x => x.manifest != null)
                .Select(x => (x.manifest, x.actualRoot)).ToList()));

            var assetsAbs = Path.GetFullPath("Assets");
            var actualPaths = CollectActualPaths(assetsAbs, root);
            var generatedZones = SusSharqGenManifest.ResolveGeneratedZones(assetsAbs, root, presentModules);
            var moduleGenInfo = SusSharqGenManifest.ResolveModuleGenInfo(assetsAbs, root, presentModules);
            issues.AddRange(DetectStaleGenerated(assetsAbs, moduleGenInfo));

            var installedUpm = new HashSet<string>(
                PackageInfo.GetAllRegisteredPackages().Select(p => p.name),
                StringComparer.OrdinalIgnoreCase);
            issues.AddRange(DetectUpmCollisions(presentModules, installedUpm));

            issues.AddRange(ClassifyStrayPaths(root, presentModules, presentDescriptors, actualPaths, generatedZones));
            issues.AddRange(DetectModuleManifestMissing(root, presentDescriptors, presentModules, actualPaths));
            issues.AddRange(DetectIncompleteSets(root, presentDescriptors, presentModules, actualPaths));

            var setRootAbs = Path.Combine(assetsAbs, root);
            var actualVersionByModuleId = new Dictionary<string, string>();
            foreach (var m in presentModules)
            {
                if (string.IsNullOrEmpty(m.dir)) continue;
                var changelogPath = Path.Combine(setRootAbs, m.dir, "CHANGELOG.md");
                if (!File.Exists(changelogPath)) continue;
                try
                {
                    var v = ExtractLatestChangelogVersion(File.ReadAllText(changelogPath));
                    if (!string.IsNullOrEmpty(v)) actualVersionByModuleId[m.id] = v;
                }
                catch (IOException)
                {
                    // transient — skip this module for this run, next trigger will retry
                }
            }
            issues.AddRange(DetectVersionMismatches(presentModules, actualVersionByModuleId));

            var rootFileMarkers = ReadRootFileProvenanceMarkers(setRootAbs);
            issues.AddRange(DetectRootFileProvenance(root, presentDescriptors, rootFileMarkers));

            return issues;
        }

        // ─── Pure classification (no Unity/IO — unit-testable directly) ──────────

        /// <summary>State (1): a module is both a registered UPM package AND present as a
        /// classic asset folder at the same time. Only needs the modules we actually found a
        /// manifest for — since a module's own <c>sus-module.json</c> survives on disk
        /// regardless of which sibling set was imported on top (it lives under that module's own
        /// folder, which a sibling set's packer never touches), so this no longer needs a
        /// separate "present module folders" disk scan the way the old single-manifest
        /// version did (that scan existed only to work around a manifest that could forget a
        /// module entirely — see the class doc's правило атрибуции).</summary>
        internal static List<SusValidationIssue> DetectUpmCollisions(
            IReadOnlyList<SusModuleManifest> presentModules,
            ISet<string> installedUpmPackageNames)
        {
            var issues = new List<SusValidationIssue>();
            foreach (var m in presentModules)
            {
                if (string.IsNullOrEmpty(m.id) || string.IsNullOrEmpty(m.dir)) continue;

                var packageName = ResolveUpmPackageName(m);
                if (string.IsNullOrEmpty(packageName) || !installedUpmPackageNames.Contains(packageName)) continue;

                issues.Add(SusValidationIssue.Error("SetDoctor.UpmCollision",
                    $"Both the UPM package '{packageName}' (Packages/) and the classic module " +
                    $"'{m.root}/{m.dir}' (Assets/) are installed at the same time — " +
                    "they define the same assembly and will not compile together.",
                    $"Remove ONE of the two: Package Manager -> remove '{packageName}', OR delete " +
                    $"the folder 'Assets/{m.root}/{m.dir}'. If you bought this set, " +
                    "keep the classic folder and remove the UPM package."));
            }
            return issues;
        }

        /// <summary>UPM package name a module collides with: the <c>package</c> field of its
        /// own <c>sus-module.json</c> (the set packer writes it for every module) — the only
        /// correct source for a skin module, whose <c>id</c> is <c>skin</c> while its package is
        /// <c>com.sharq-it.sus.&lt;family&gt;.&lt;name&gt;</c> (ARCH-SKIN §4.1 two-forms contract, T-1334);
        /// falls back to <c>com.sharq-it.sus.&lt;id&gt;</c> for manifests that predate the field
        /// (core/router/kit/game, where the two happen to coincide).</summary>
        internal static string ResolveUpmPackageName(SusModuleManifest m)
        {
            if (m == null) return null;
            var explicitName = m.package?.Trim();
            if (!string.IsNullOrEmpty(explicitName)) return explicitName;
            return string.IsNullOrEmpty(m.id) ? null : PackageIdPrefix + m.id;
        }

        /// <summary>States (2)+(4a): splits every actual path under the set root that ISN'T
        /// explicitly known (not in any present module's own <c>paths</c>, not in any present
        /// set descriptor's <c>sharedPaths</c>, not under a present module's own generated zone
        /// — <paramref name="generatedZones"/>, D8/T-1489) into <c>SetDoctor.Residual</c>
        /// (attributable to a present module's own subtree —
        /// <c>&lt;root&gt;/&lt;dir&gt;/**</c> or <c>&lt;root&gt;/Samples/&lt;dir&gt;/**</c> —
        /// the ONLY case that gets a "delete" hint) or <c>SetDoctor.Unattributed</c> (everything
        /// else: a purchaser's own file, or the remnant of a module whose OWN manifest was
        /// itself removed — never a "delete" hint, §5.5 point 5). Collapsed to the shallowest
        /// offending ancestor so a whole stray folder is reported once, not file-by-file.</summary>
        internal static List<SusValidationIssue> ClassifyStrayPaths(
            string root,
            IReadOnlyList<SusModuleManifest> presentModules,
            IReadOnlyList<SusSetManifest> presentDescriptors,
            IEnumerable<string> actualPaths,
            IReadOnlyList<string> generatedZones = null)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in presentModules)
                foreach (var p in m.paths ?? Array.Empty<string>())
                    known.Add(p);
            foreach (var d in presentDescriptors)
                foreach (var p in d.sharedPaths ?? Array.Empty<string>())
                    known.Add(p);
            var zones = generatedZones ?? Array.Empty<string>();

            var stray = new List<string>();
            foreach (var p in actualPaths)
            {
                if (string.Equals(p, root, StringComparison.Ordinal)) continue; // root itself — always valid
                if (known.Contains(p)) continue;
                if (IsUnderAnyZone(p, zones)) continue; // D8: purchaser's own Generate output
                stray.Add(p);
            }
            var collapsed = CollapseToAncestors(stray);

            var residualByModule = new Dictionary<SusModuleManifest, List<string>>();
            var unattributed = new List<string>();
            foreach (var p in collapsed)
            {
                var owner = FindOwningModule(root, presentModules, p);
                if (owner != null)
                {
                    if (!residualByModule.TryGetValue(owner, out var list))
                        residualByModule[owner] = list = new List<string>();
                    list.Add(p);
                }
                else
                {
                    unattributed.Add(p);
                }
            }

            var issues = new List<SusValidationIssue>();
            foreach (var kv in residualByModule)
                issues.Add(BuildResidualIssue(root, kv.Key, kv.Value, presentDescriptors.Count));
            if (unattributed.Count > 0)
                issues.Add(BuildUnattributedIssue(root, unattributed));
            return issues;
        }

        /// <summary>True when <paramref name="path"/> IS a generated zone or lives under one
        /// (D8/T-1489: <c>&lt;root&gt;/&lt;dir&gt;/&lt;generated&gt;</c> and everything below,
        /// including the zone folder itself — the purchaser's own Generate output, never a
        /// residual — AND the intermediate directories BETWEEN the module's own dir and the
        /// zone (e.g. <c>&lt;root&gt;/&lt;dir&gt;/Runtime</c> above
        /// <c>&lt;root&gt;/&lt;dir&gt;/Runtime/Generated</c>): those aren't in <c>paths</c>
        /// either when the packer's exclude cuts the whole subtree, so without this they'd
        /// collapse to their own stray "shallowest ancestor" and get flagged instead of the
        /// leaf, per §5.5 algorithm step 3 "вместе с промежуточными каталогами до
        /// &lt;root&gt;/&lt;dir&gt;").</summary>
        private static bool IsUnderAnyZone(string path, IReadOnlyList<string> zones)
        {
            foreach (var z in zones)
            {
                if (string.IsNullOrEmpty(z)) continue;
                if (string.Equals(path, z, StringComparison.Ordinal) ||
                    path.StartsWith(z + "/", StringComparison.Ordinal) || // inside the zone
                    z.StartsWith(path + "/", StringComparison.Ordinal))  // an ancestor of the zone
                    return true;
            }
            return false;
        }

        private static List<string> CollapseToAncestors(List<string> paths)
        {
            var sorted = new List<string>(paths);
            sorted.Sort(StringComparer.Ordinal);
            var collapsed = new List<string>();
            foreach (var p in sorted)
            {
                var coveredByAncestor = false;
                foreach (var a in collapsed)
                {
                    if (p.StartsWith(a + "/", StringComparison.Ordinal)) { coveredByAncestor = true; break; }
                }
                if (!coveredByAncestor) collapsed.Add(p);
            }
            return collapsed;
        }

        private static SusModuleManifest FindOwningModule(string root, IReadOnlyList<SusModuleManifest> presentModules, string path)
        {
            foreach (var m in presentModules)
            {
                if (string.IsNullOrEmpty(m.dir)) continue;
                var modRoot = $"{root}/{m.dir}";
                var samplesRoot = $"{root}/Samples/{m.dir}";
                if (string.Equals(path, modRoot, StringComparison.Ordinal) || path.StartsWith(modRoot + "/", StringComparison.Ordinal))
                    return m;
                if (string.Equals(path, samplesRoot, StringComparison.Ordinal) || path.StartsWith(samplesRoot + "/", StringComparison.Ordinal))
                    return m;
            }
            return null;
        }

        /// <summary>State (4b): a present set descriptor lists a module id whose OWN manifest is
        /// missing, but whose folder (<c>&lt;root&gt;/&lt;X&gt;</c>, matched case-insensitively
        /// against the id — there is nowhere else to read <c>dir</c> from when exactly the file
        /// that would carry it is the thing that's missing) is still on disk.</summary>
        internal static List<SusValidationIssue> DetectModuleManifestMissing(
            string root,
            IReadOnlyList<SusSetManifest> presentDescriptors,
            IReadOnlyList<SusModuleManifest> presentModules,
            IEnumerable<string> actualPaths)
        {
            var presentIds = new HashSet<string>(presentModules.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
            var actualPathSet = new HashSet<string>(actualPaths, StringComparer.Ordinal);
            var issues = new List<SusValidationIssue>();
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in presentDescriptors)
            {
                foreach (var id in d.modules ?? Array.Empty<string>())
                {
                    if (presentIds.Contains(id)) continue;
                    if (!reported.Add(id)) continue;
                    var dir = FindDirForModuleId(root, id, actualPathSet);
                    if (dir == null) continue; // folder absent entirely -> DetectIncompleteSets' territory
                    issues.Add(SusValidationIssue.Warning("SetDoctor.ModuleManifestMissing",
                        $"'Assets/{root}/{dir}' belongs to module '{id}' (listed by set '{d.set}'), but its " +
                        $"{ModuleManifestFileName} is missing.",
                        $"Reimport the set that ships module '{id}' ('{d.displayName}')."));
                }
            }
            return issues;
        }

        /// <summary>State (4c): a present set descriptor lists a module that is present neither
        /// as files nor as a manifest — a partial/deselected import.</summary>
        internal static List<SusValidationIssue> DetectIncompleteSets(
            string root,
            IReadOnlyList<SusSetManifest> presentDescriptors,
            IReadOnlyList<SusModuleManifest> presentModules,
            IEnumerable<string> actualPaths)
        {
            var presentIds = new HashSet<string>(presentModules.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
            var actualPathSet = new HashSet<string>(actualPaths, StringComparer.Ordinal);
            var issues = new List<SusValidationIssue>();
            foreach (var d in presentDescriptors)
            {
                var missing = new List<string>();
                foreach (var id in d.modules ?? Array.Empty<string>())
                {
                    if (presentIds.Contains(id)) continue;
                    if (FindDirForModuleId(root, id, actualPathSet) != null) continue;
                    missing.Add(id);
                }
                if (missing.Count == 0) continue;
                issues.Add(SusValidationIssue.Warning("SetDoctor.IncompleteSet",
                    $"Set '{d.displayName}' ('{d.set}') lists module(s) {string.Join(", ", missing)}, but " +
                    "neither their files nor manifests are present.",
                    "Likely a partial or interrupted import — re-import the full .unitypackage for this set."));
            }
            return issues;
        }

        private static string FindDirForModuleId(string root, string id, HashSet<string> actualPathSet)
        {
            var prefix = root + "/";
            foreach (var p in actualPathSet)
            {
                if (!p.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var rest = p.Substring(prefix.Length);
                if (rest.IndexOf('/') >= 0) continue; // not an immediate child of root
                if (string.Equals(rest, id, StringComparison.OrdinalIgnoreCase)) return rest;
            }
            return null;
        }

        /// <summary>State (3): a module's manifest version disagrees with the version read back
        /// from its own CHANGELOG.md on disk. Modules with no readable changelog are skipped —
        /// silence, not a false positive, when there is no signal to compare against.</summary>
        internal static List<SusValidationIssue> DetectVersionMismatches(
            IReadOnlyList<SusModuleManifest> presentModules, IReadOnlyDictionary<string, string> actualVersionByModuleId)
        {
            var issues = new List<SusValidationIssue>();
            foreach (var m in presentModules)
            {
                if (string.IsNullOrEmpty(m.id)) continue;
                if (!actualVersionByModuleId.TryGetValue(m.id, out var actual) || string.IsNullOrEmpty(actual))
                    continue;
                if (string.Equals(actual, m.version, StringComparison.Ordinal)) continue;

                issues.Add(SusValidationIssue.Warning("SetDoctor.VersionMismatch",
                    $"Module '{m.dir}' on disk reports version {actual} (from its CHANGELOG.md), " +
                    $"but its {ModuleManifestFileName} expects {m.version}.",
                    $"Likely a mixed-version install: re-import the module '{m.id}' belongs to in full " +
                    $"so every path under Assets/{m.root}/{m.dir} matches, or check for a partial/interrupted update."));
            }
            return issues;
        }

        /// <summary>State (10, §5.5 point 10 / risk R11): the generated root files (README.txt,
        /// LICENSE.txt, Third-Party Notices.txt) are a SET-level shared path
        /// (<c>sharedPaths</c>), not owned by any module — a classic re-import of a SMALLER set
        /// on top of an ALREADY-INSTALLED LARGER one silently overwrites them with the smaller
        /// set's content (same path, last import wins; Unity's importer has no notion of "these
        /// three files should be a union"). The real-world case that motivated this: a Complete
        /// owner re-imports/updates Kit, and the combined <c>Third-Party Notices.txt</c> quietly
        /// drops Game's demo-art attributions — a legal document silently regressing, not
        /// cosmetics. The packer stamps a deterministic marker
        /// (<c>Generated for: &lt;set&gt; v&lt;version&gt;</c>, no dates) as the last non-empty
        /// line of each such file; this compares the set it names against every OTHER present
        /// set descriptor and flags it when that other set is a STRICT superset of the
        /// marker's module list. A separate branch (<c>SetDoctor.RootFileProvenanceDisjoint</c>,
        /// R33/I15(5)) fires when another present descriptor shares <b>zero</b> modules with the
        /// marker's set (no nesting either way): last import still overwrites the shared root
        /// files, so the other set's attributions vanish. Never a "delete" hint (§5.5 point 10 /
        /// I15(5)): advise reimport of the larger set, or of <b>both</b> disjoint sets.</summary>
        internal static List<SusValidationIssue> DetectRootFileProvenance(
            string root,
            IReadOnlyList<SusSetManifest> presentDescriptors,
            IReadOnlyDictionary<string, (string set, string version)> rootFileMarkers)
        {
            var issues = new List<SusValidationIssue>();
            if (rootFileMarkers == null || rootFileMarkers.Count == 0) return issues;
            if (presentDescriptors == null || presentDescriptors.Count < 2) return issues; // nothing to be a subset OF

            // One re-import normally stamps every generated root file with the SAME marker — group
            // by (markerSet -> otherSet) so that produces one issue naming all affected files,
            // not one issue per file. Nested (strict-superset) and disjoint pairs are separate
            // categories; a marker that has a present strict superset takes that path only.
            var nestedGroups = new Dictionary<(string markerSet, string supersetSet), List<string>>();
            var disjointGroups = new Dictionary<(string markerSet, string otherSet), List<string>>();
            foreach (var kv in rootFileMarkers)
            {
                var fileName = kv.Key;
                var markerSet = kv.Value.set;
                var markerDesc = presentDescriptors.FirstOrDefault(d => string.Equals(d.set, markerSet, StringComparison.Ordinal));
                if (markerDesc == null) continue; // marker names a set Doctor has no present descriptor for — nothing to compare against

                var superset = presentDescriptors.FirstOrDefault(d =>
                    !string.Equals(d.set, markerSet, StringComparison.Ordinal) && IsStrictModuleSuperset(d.modules, markerDesc.modules));
                if (superset != null)
                {
                    var nestedKey = (markerDesc.set, superset.set);
                    if (!nestedGroups.TryGetValue(nestedKey, out var nestedFiles))
                        nestedGroups[nestedKey] = nestedFiles = new List<string>();
                    nestedFiles.Add(fileName);
                    continue;
                }

                // No nesting either way: flag every OTHER present descriptor whose modules share
                // nothing with the marker's set (skin-set ⇄ game-set). Partial overlap without
                // nesting stays silent — same as T-561 sibling case.
                foreach (var other in presentDescriptors)
                {
                    if (string.Equals(other.set, markerSet, StringComparison.Ordinal)) continue;
                    if (IsStrictModuleSuperset(markerDesc.modules, other.modules)) continue; // marker is the larger set
                    if (!AreModulesDisjoint(markerDesc.modules, other.modules)) continue;

                    var disjointKey = (markerDesc.set, other.set);
                    if (!disjointGroups.TryGetValue(disjointKey, out var disjointFiles))
                        disjointGroups[disjointKey] = disjointFiles = new List<string>();
                    disjointFiles.Add(fileName);
                }
            }

            foreach (var kv in nestedGroups)
            {
                var markerDesc = presentDescriptors.First(d => string.Equals(d.set, kv.Key.markerSet, StringComparison.Ordinal));
                var supersetDesc = presentDescriptors.First(d => string.Equals(d.set, kv.Key.supersetSet, StringComparison.Ordinal));
                issues.Add(BuildRootFileProvenanceIssue(root, markerDesc, supersetDesc, kv.Value));
            }
            foreach (var kv in disjointGroups)
            {
                var markerDesc = presentDescriptors.First(d => string.Equals(d.set, kv.Key.markerSet, StringComparison.Ordinal));
                var otherDesc = presentDescriptors.First(d => string.Equals(d.set, kv.Key.otherSet, StringComparison.Ordinal));
                issues.Add(BuildRootFileProvenanceDisjointIssue(root, markerDesc, otherDesc, kv.Value));
            }
            return issues;
        }

        private static bool IsStrictModuleSuperset(IReadOnlyList<string> maybeSuperset, IReadOnlyList<string> maybeSubset)
        {
            var superset = new HashSet<string>(maybeSuperset ?? Array.Empty<string>(), StringComparer.Ordinal);
            var subset = new HashSet<string>(maybeSubset ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (superset.Count <= subset.Count) return false;
            foreach (var id in subset)
                if (!superset.Contains(id)) return false;
            return true;
        }

        /// <summary>True when the two module-id lists share no id (I15(5) disjoint pair).</summary>
        private static bool AreModulesDisjoint(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            var left = new HashSet<string>(a ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var id in b ?? Array.Empty<string>())
                if (left.Contains(id)) return false;
            return true;
        }

        private static SusValidationIssue BuildRootFileProvenanceIssue(
            string root, SusSetManifest markerDesc, SusSetManifest supersetDesc, List<string> files)
        {
            var sortedFiles = files.OrderBy(f => f, StringComparer.Ordinal).ToList();
            var fileList = string.Join(", ", sortedFiles.Select(f => $"'{f}'"));
            var verb = sortedFiles.Count == 1 ? "was" : "were";
            return SusValidationIssue.Warning("SetDoctor.RootFileProvenance",
                $"{fileList} in 'Assets/{root}' {verb} generated for '{markerDesc.displayName}' ('{markerDesc.set}'), " +
                $"but the larger installed set '{supersetDesc.displayName}' ('{supersetDesc.set}') is also present — " +
                "content only the larger set adds (e.g. its own demo-art third-party notices) is missing from the " +
                "combined file(s).",
                $"Reimport '{supersetDesc.displayName}' to regenerate these files for the full set.");
        }

        /// <summary>I15(5) / <c>RootFileProvenanceDisjoint</c>: both sets installed, modules
        /// share nothing, root files describe only the last-imported set. Never suggest delete —
        /// reimport <b>both</b> so each set's attributions land again.</summary>
        private static SusValidationIssue BuildRootFileProvenanceDisjointIssue(
            string root, SusSetManifest markerDesc, SusSetManifest otherDesc, List<string> files)
        {
            var sortedFiles = files.OrderBy(f => f, StringComparer.Ordinal).ToList();
            var fileList = string.Join(", ", sortedFiles.Select(f => $"'{f}'"));
            var verb = sortedFiles.Count == 1 ? "was" : "were";
            return SusValidationIssue.Warning("SetDoctor.RootFileProvenanceDisjoint",
                $"{fileList} in 'Assets/{root}' {verb} generated for '{markerDesc.displayName}' ('{markerDesc.set}'), " +
                $"but the disjoint installed set '{otherDesc.displayName}' ('{otherDesc.set}') is also present — " +
                "the sets share no modules, so neither reimport alone restores the other's attributions in the " +
                "combined root file(s).",
                $"Reimport both '{markerDesc.displayName}' and '{otherDesc.displayName}' " +
                "(order does not matter for detection; reimporting only one leaves the other's notices missing).");
        }

        /// <summary>Parses the packer's deterministic provenance marker from the LAST non-empty
        /// line of a generated root file's text. Returns null when there is no such line (the
        /// file is hand-edited, foreign, or from a version of the packer before) — silence,
        /// not a false positive, when there is no signal to compare against.</summary>
        internal static (string set, string version)? ParseRootFileProvenanceMarker(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                var m = RootFileProvenanceMarker.Match(line);
                if (!m.Success) return null;
                return (m.Groups["set"].Value, m.Groups["version"].Value.Trim());
            }
            return null;
        }

        /// <summary>Reads and parses every present <see cref="RootFileNames"/> entry under the
        /// set root. A file that's missing, unreadable (transient mid-import), or carries no
        /// marker is silently absent from the result — <see cref="DetectRootFileProvenance"/>
        /// only ever compares markers it actually found.</summary>
        private static Dictionary<string, (string set, string version)> ReadRootFileProvenanceMarkers(string setRootAbs)
        {
            var result = new Dictionary<string, (string set, string version)>();
            foreach (var fileName in RootFileNames)
            {
                var path = Path.Combine(setRootAbs, fileName);
                if (!File.Exists(path)) continue;
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; } // transient (mid-import) — next trigger retries
                var marker = ParseRootFileProvenanceMarker(text);
                if (marker.HasValue) result[fileName] = marker.Value;
            }
            return result;
        }

        /// <summary>State (4d): a module manifest's declared root/dir disagree with where the
        /// file actually is — the purchaser (or some other tool) renamed/moved the folder.</summary>
        internal static List<SusValidationIssue> DetectRelocatedModules(
            IReadOnlyList<(SusModuleManifest manifest, string actualRoot, string actualDir)> found)
        {
            var issues = new List<SusValidationIssue>();
            foreach (var (m, actualRoot, actualDir) in found)
            {
                if (string.Equals(m.root, actualRoot, StringComparison.Ordinal) &&
                    string.Equals(m.dir, actualDir, StringComparison.Ordinal))
                    continue;
                issues.Add(SusValidationIssue.Warning("SetDoctor.Relocated",
                    $"Module '{m.id}' manifest declares 'Assets/{m.root}/{m.dir}', but was found at " +
                    $"'Assets/{actualRoot}/{actualDir}'.",
                    "This module folder appears to have been renamed or moved — Set Doctor's other " +
                    "checks may be unreliable until it's restored to its original location."));
            }
            return issues;
        }

        /// <summary>Same as <see cref="DetectRelocatedModules"/> but for a set descriptor's
        /// declared root.</summary>
        internal static List<SusValidationIssue> DetectRelocatedDescriptors(
            IReadOnlyList<(SusSetManifest manifest, string actualRoot)> found)
        {
            var issues = new List<SusValidationIssue>();
            foreach (var (m, actualRoot) in found)
            {
                if (string.Equals(m.root, actualRoot, StringComparison.Ordinal)) continue;
                issues.Add(SusValidationIssue.Warning("SetDoctor.Relocated",
                    $"Set '{m.set}' descriptor declares root 'Assets/{m.root}', but was found under " +
                    $"'Assets/{actualRoot}'.",
                    "This set's root folder appears to have been renamed or moved — Set Doctor's other " +
                    "checks may be unreliable until it's restored to its original location."));
            }
            return issues;
        }

        /// <summary>Extracts the newest released version from a Keep-a-Changelog file — the
        /// first <c>## [x.y.z]</c> heading, skipping <c>[Unreleased]</c>. Returns null when no
        /// such heading exists.</summary>
        internal static string ExtractLatestChangelogVersion(string changelogText)
        {
            if (string.IsNullOrEmpty(changelogText)) return null;

            foreach (Match m in ChangelogVersionHeading.Matches(changelogText))
            {
                var v = m.Groups[1].Value.Trim();
                if (string.Equals(v, "Unreleased", StringComparison.OrdinalIgnoreCase)) continue;
                return v;
            }
            return null;
        }

        // ─── IO helpers (thin — file-walking only, unit-testable with a temp dir) ─

        /// <summary>Walks <c>&lt;assetsAbsPath&gt;/&lt;root&gt;</c> and returns every entry
        /// (folders and files, <c>.meta</c> excluded) as an Assets-relative forward-slash path
        /// — the same shape as <see cref="SusModuleManifest.paths"/>, including the root folder
        /// itself as the first entry.</summary>
        internal static List<string> CollectActualPaths(string assetsAbsPath, string root)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(root)) return results;

            var rootAbs = Path.Combine(assetsAbsPath, root);
            if (!Directory.Exists(rootAbs)) return results;

            results.Add(root);
            WalkForPaths(rootAbs, root, results);
            return results;
        }

        private static void WalkForPaths(string dirAbs, string relPrefix, List<string> results)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dirAbs).OrderBy(e => e, StringComparer.Ordinal))
            {
                if (entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                var rel = relPrefix + "/" + Path.GetFileName(entry);
                results.Add(rel);
                if (Directory.Exists(entry)) WalkForPaths(entry, rel, results);
            }
        }

        /// <summary>Every <c>sus-module.json</c> in the project, with the root/dir the asset
        /// path itself implies (NOT the manifest's own declared root/dir — that comparison is
        /// exactly what <see cref="DetectRelocatedModules"/> needs). <c>manifest</c> is null for
        /// a file that exists but didn't parse.</summary>
        private static List<(string assetPath, string actualRoot, string actualDir, SusModuleManifest manifest)> FindModuleManifests()
        {
            var results = new List<(string, string, string, SusModuleManifest)>();
            foreach (var guid in AssetDatabase.FindAssets("sus-module"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileName(assetPath), ModuleManifestFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryParseModulePath(assetPath, out var actualRoot, out var actualDir)) continue;
                string json;
                try { json = File.ReadAllText(assetPath); }
                catch (IOException) { continue; } // transient (mid-import) — next trigger retries
                results.Add((assetPath, actualRoot, actualDir, SusModuleManifest.Parse(json)));
            }
            return results;
        }

        /// <summary>Splits a <c>sus-module.json</c> asset path into the root/dir its LOCATION
        /// implies — <c>"Assets/&lt;root&gt;/&lt;dir&gt;/sus-module.json"</c> where
        /// <c>&lt;dir&gt;</c> may itself contain slashes (a skin module's dir is nested two
        /// levels deep, e.g. <c>Assets/Sharq/Themes/Example/sus-module.json</c> → root
        /// <c>"Sharq"</c>, dir <c>"Themes/Example"</c>, T-1488 — the old <c>parts[len-3]</c>
        /// arithmetic assumed a single-segment dir and misread the second segment as the root,
        /// producing a false <c>SetDoctor.Relocated</c> on every skin-set install). Returns
        /// false for anything shorter than <c>Assets/&lt;root&gt;/&lt;dir&gt;/sus-module.json</c>
        /// (needs at least one dir segment) — the caller skips those, they can't be ours.</summary>
        internal static bool TryParseModulePath(string assetPath, out string actualRoot, out string actualDir)
        {
            actualRoot = null;
            actualDir = null;
            if (string.IsNullOrEmpty(assetPath)) return false;
            var parts = assetPath.Split('/');
            if (parts.Length < 4) return false; // not "Assets/<root>/<dir...>/sus-module.json"
            actualRoot = parts[1];
            actualDir = string.Join("/", parts, 2, parts.Length - 3);
            return true;
        }

        /// <summary>Every <c>sus-set.&lt;set&gt;.json</c> in the project, with the root the
        /// asset path itself implies. <c>manifest</c> is null for a file that exists but didn't
        /// parse.</summary>
        private static List<(string assetPath, string actualRoot, SusSetManifest manifest)> FindSetDescriptors()
        {
            var results = new List<(string, string, SusSetManifest)>();
            foreach (var guid in AssetDatabase.FindAssets("sus-set"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsSetDescriptorFileName(Path.GetFileName(assetPath))) continue;
                var parts = assetPath.Split('/');
                if (parts.Length < 3) continue; // not "Assets/<root>/sus-set.<set>.json"
                var actualRoot = parts[parts.Length - 2];
                string json;
                try { json = File.ReadAllText(assetPath); }
                catch (IOException) { continue; }
                results.Add((assetPath, actualRoot, SusSetManifest.Parse(json)));
            }
            return results;
        }

        /// <summary>The set root to run the disk walk against: the most common actual location
        /// among every found manifest (in the overwhelming normal case they all agree — every
        /// set the packer builds shares one contractually-fixed root, §2.1 D1). Ties break
        /// alphabetically so the result is deterministic, not IO-order-dependent.</summary>
        private static string ResolveRoot(
            List<(string assetPath, string actualRoot, string actualDir, SusModuleManifest manifest)> moduleFinds,
            List<(string assetPath, string actualRoot, SusSetManifest manifest)> setFinds)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            void Count(string r)
            {
                if (string.IsNullOrEmpty(r)) return;
                counts[r] = counts.TryGetValue(r, out var c) ? c + 1 : 1;
            }
            foreach (var f in moduleFinds) Count(f.actualRoot);
            foreach (var f in setFinds) Count(f.actualRoot);
            if (counts.Count == 0) return null;
            return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
        }

        private static SusValidationIssue BuildResidualIssue(string root, SusModuleManifest owner, List<string> paths, int presentDescriptorCount)
        {
            const int maxListed = 20;
            var listed = paths.Take(maxListed);
            var body = string.Join("\n   ", listed);
            if (paths.Count > maxListed) body += $"\n   ... and {paths.Count - maxListed} more";

            var fix = $"Delete the listed path(s), or reinstall the set that ships module '{owner.id}' " +
                      $"(folder 'Assets/{root}/{owner.dir}').";
            // §5.5 point 4: "удалить всю папку набора" is only safe to say when exactly one
            // descriptor is present — with two (e.g. Kit + Complete side by side) it would be
            // telling the purchaser to delete a sibling set they paid for and still want.
            if (presentDescriptorCount == 1)
                fix += $" If you're not sure, delete the whole 'Assets/{root}' folder and re-import the current .unitypackage.";

            return SusValidationIssue.Warning("SetDoctor.Residual",
                $"{paths.Count} path(s) under 'Assets/{root}/{owner.dir}' belong to module '{owner.id}' " +
                $"(v{owner.version}) but are not listed in its {ModuleManifestFileName} — likely left " +
                "over from an older version (classic import only adds files, it never deletes):\n   " + body,
                fix);
        }

        private static SusValidationIssue BuildUnattributedIssue(string root, List<string> paths)
        {
            const int maxListed = 20;
            var listed = paths.Take(maxListed);
            var body = string.Join("\n   ", listed);
            if (paths.Count > maxListed) body += $"\n   ... and {paths.Count - maxListed} more";

            return SusValidationIssue.Warning("SetDoctor.Unattributed",
                $"{paths.Count} path(s) under 'Assets/{root}' could not be attributed to any " +
                "installed module or set:\n   " + body,
                "If these are your own files, leave them where they are. If they're left over from a " +
                "set whose module manifest was removed, reinstall that set.");
        }

        /// <summary>The boundary §5.5 D8 documents as sitting outside its own reach (T-1526,
        /// ARCH-PACK-CLASSIC.md §5.5 D8 last paragraph): T-1489's <c>generatedZones</c> makes
        /// EVERY path under a module's declared generated zone unconditionally "known" to
        /// <see cref="ClassifyStrayPaths"/> — that's correct for a purchaser's own fresh
        /// Generate output, but it also means a genuinely stale generat (<c>CsX.g.cs</c> from a
        /// component the module no longer ships, e.g. renamed/removed) is now invisible: it
        /// can't be Residual (it's under the zone, D8 says "known") and it can't be judged from
        /// <c>paths</c> alone (the whole zone is either all-known or, pre-purchase-Generate,
        /// entirely absent from disk). The only ground truth left is stem correspondence against
        /// the module's OWN current <c>.sharq</c> sources (<see
        /// cref="SusSharqGenManifest.ResolveModuleGenInfo"/>, <c>sources</c> field, T-1489's
        /// sibling field added here) — a <c>&lt;Stem&gt;.g.cs</c>/<c>&lt;Stem&gt;.g.uss</c> under
        /// the generated zone, or a companion <c>&lt;Stem&gt;.g.uss</c> copy under the
        /// <c>resources</c> zone (T-1526's DoD explicitly), whose stem matches no
        /// <c>&lt;Stem&gt;.sharq</c> anywhere under <c>sources</c> right now.
        ///
        /// Warning only, and the hint below NEVER says "reinstall the set" — reinstalling a
        /// classic set only ADDS files (§5.5's central invariant running through this whole
        /// class), so it would leave the exact same stale generat behind. Deleting the file and
        /// re-running Generate is the only fix that actually removes it.</summary>
        internal static List<SusValidationIssue> DetectStaleGenerated(
            string assetsAbsPath,
            IReadOnlyDictionary<SusModuleManifest, SusSharqGenManifest.SusGenModuleInfo> moduleGenInfo)
        {
            var issues = new List<SusValidationIssue>();
            if (moduleGenInfo == null) return issues;

            foreach (var kv in moduleGenInfo)
            {
                var owner = kv.Key;
                var info = kv.Value;
                var stale = new List<string>();

                CollectStaleGeneratedInZone(assetsAbsPath, info.GeneratedZone, GeneratedZoneSuffixes, info.SourceStems, stale);
                CollectStaleGeneratedInZone(assetsAbsPath, info.ResourcesZone, ResourcesZoneSuffixes, info.SourceStems, stale);

                if (stale.Count == 0) continue;
                stale.Sort(StringComparer.Ordinal);
                issues.Add(BuildStaleGeneratedIssue(owner, stale));
            }
            return issues;
        }

        /// <summary>Suffixes judged in a module's own <c>generated</c> zone: the generat
        /// C# and its co-located <c>.g.uss</c> (the zone is currently flat and holds a
        /// <c>.sections.json</c> per stem too, but that file carries no compiled output worth a
        /// "delete and Generate" hint on its own — its stem is still covered because the .g.cs
        /// in the same zone already flags the stem).</summary>
        private static readonly string[] GeneratedZoneSuffixes = { ".g.cs", ".g.uss" };

        /// <summary>Suffixes judged in a module's <c>resources</c> zone: only the runtime
        /// <c>Resources.Load</c> copy of the compiled stylesheet — DoD п.3: a hand-authored
        /// PLAIN <c>.uss</c> living alongside it (e.g. shared tokens/breakpoints files that were
        /// never generated from any <c>.sharq</c>) does not end in <c>.g.uss</c>, so it never
        /// matches this suffix and can never be flagged — no stem comparison needed to exclude
        /// it, the suffix filter already does.</summary>
        private static readonly string[] ResourcesZoneSuffixes = { ".g.uss" };

        private static void CollectStaleGeneratedInZone(
            string assetsAbsPath, string zoneRelPath, string[] suffixes, HashSet<string> knownStems, List<string> outStale)
        {
            if (string.IsNullOrEmpty(zoneRelPath) || knownStems == null) return;
            var zoneAbs = Path.Combine(assetsAbsPath, zoneRelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(zoneAbs)) return;

            foreach (var file in Directory.EnumerateFiles(zoneAbs, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                string matchedSuffix = null;
                foreach (var suf in suffixes)
                {
                    if (!fileName.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) continue;
                    matchedSuffix = suf;
                    break;
                }
                if (matchedSuffix == null) continue; // not a generat file this zone is judged by (DoD п.3)

                var stem = fileName.Substring(0, fileName.Length - matchedSuffix.Length);
                if (knownStems.Contains(stem)) continue; // has a live .sharq source — not stale

                var relFromZone = Path.GetRelativePath(zoneAbs, file).Replace('\\', '/');
                outStale.Add($"{zoneRelPath}/{relFromZone}");
            }
        }

        private static SusValidationIssue BuildStaleGeneratedIssue(SusModuleManifest owner, List<string> stalePaths)
        {
            const int maxListed = 20;
            var listed = stalePaths.Take(maxListed);
            var body = string.Join("\n   ", listed);
            if (stalePaths.Count > maxListed) body += $"\n   ... and {stalePaths.Count - maxListed} more";

            var verb = stalePaths.Count == 1 ? "has" : "have";
            return SusValidationIssue.Warning("SetDoctor.StaleGenerated",
                $"{stalePaths.Count} generated file(s) under module '{owner.id}' (v{owner.version}) {verb} " +
                "no matching .sharq source anymore — likely a component that was renamed or removed from a " +
                "newer version of this module (Generate output, unlike the rest of an import, is never " +
                "reconciled automatically):\n   " + body,
                "Delete the listed file(s), then run Generate again. A classic re-import only ADDS files " +
                "and never ships Generate output in the first place, so updating the module will not " +
                "remove them on its own.");
        }

        // ─── Output (shared formatting with SusSetupValidator's convention) ──────

        internal static void LogAndShow(List<SusValidationIssue> issues, bool forceDialog)
        {
            if (issues.Count == 0)
            {
                if (forceDialog)
                {
                    Debug.Log("<color=green>✔ SUS Set Doctor - no issues found.</color>");
                    EditorUtility.DisplayDialog("SUS Set Doctor", "No issues found.", "OK");
                }
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== SUS Set Doctor === {issues.Count} issue(s)");
            sb.AppendLine();

            int errors = 0, warnings = 0;
            foreach (var issue in issues)
            {
                sb.AppendLine(issue.ToString());
                if (issue.Severity == SusValidationSeverity.Error) errors++;
                else if (issue.Severity == SusValidationSeverity.Warning) warnings++;
            }
            sb.AppendLine();
            sb.AppendLine($"❌ {errors} errors  ⚠️ {warnings} warnings");

            var summary = sb.ToString();
            if (errors > 0) Debug.LogError(summary);
            else Debug.LogWarning(summary);

            if (forceDialog) EditorUtility.DisplayDialog("SUS Set Doctor", summary, "OK");
        }
    }
}
