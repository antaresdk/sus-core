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
    /// SUS Set Doctor v2 (ARCH-PACK-CLASSIC.md §2.3 D7 / §5.5, T-556/T-557) — detects several
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
    ///
    /// <b>Правило атрибуции (§2.3 D7).</b> Before T-556 a single shared
    /// <c>Assets/&lt;root&gt;/sus-set.json</c> was overwritten by WHICHEVER set was imported
    /// last, so its "everything under the root" semantics made the sibling set's own modules
    /// look like residue of an old version — Set Doctor would tell a Complete owner who
    /// re-imported Kit on top to delete their own Game module (T-550). Since T-556 each module
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

        private static readonly Regex ChangelogVersionHeading =
            new(@"^##\s*\[([^\]]+)\]", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>True for a per-SET descriptor filename, i.e. <c>sus-set.&lt;set&gt;.json</c>
        /// (the pre-T-556 single <c>sus-set.json</c> does NOT match — that name is never written
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

            var installedUpm = new HashSet<string>(
                PackageInfo.GetAllRegisteredPackages().Select(p => p.name),
                StringComparer.OrdinalIgnoreCase);
            issues.AddRange(DetectUpmCollisions(presentModules, installedUpm));

            issues.AddRange(ClassifyStrayPaths(root, presentModules, presentDescriptors, actualPaths));
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

            return issues;
        }

        // ─── Pure classification (no Unity/IO — unit-testable directly) ──────────

        /// <summary>State (1): a module is both a registered UPM package AND present as a
        /// classic asset folder at the same time. Only needs the modules we actually found a
        /// manifest for — since T-556, a module's own <c>sus-module.json</c> survives on disk
        /// regardless of which sibling set was imported on top (it lives under that module's own
        /// folder, which a sibling set's packer never touches), so this no longer needs a
        /// separate "present module folders" disk scan the way the pre-T-556 single-manifest
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

                var packageName = PackageIdPrefix + m.id;
                if (!installedUpmPackageNames.Contains(packageName)) continue;

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

        /// <summary>States (2)+(4a): splits every actual path under the set root that ISN'T
        /// explicitly known (not in any present module's own <c>paths</c>, not in any present
        /// set descriptor's <c>sharedPaths</c>) into <c>SetDoctor.Residual</c> (attributable to a
        /// present module's own subtree — <c>&lt;root&gt;/&lt;dir&gt;/**</c> or
        /// <c>&lt;root&gt;/Samples/&lt;dir&gt;/**</c>, T-534 — the ONLY case that gets a "delete"
        /// hint) or <c>SetDoctor.Unattributed</c> (everything else: a purchaser's own file, or
        /// the remnant of a module whose OWN manifest was itself removed — never a "delete"
        /// hint, §5.5 point 5). Collapsed to the shallowest offending ancestor so a whole stray
        /// folder is reported once, not file-by-file.</summary>
        internal static List<SusValidationIssue> ClassifyStrayPaths(
            string root,
            IReadOnlyList<SusModuleManifest> presentModules,
            IReadOnlyList<SusSetManifest> presentDescriptors,
            IEnumerable<string> actualPaths)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in presentModules)
                foreach (var p in m.paths ?? Array.Empty<string>())
                    known.Add(p);
            foreach (var d in presentDescriptors)
                foreach (var p in d.sharedPaths ?? Array.Empty<string>())
                    known.Add(p);

            var stray = new List<string>();
            foreach (var p in actualPaths)
            {
                if (string.Equals(p, root, StringComparison.Ordinal)) continue; // root itself — always valid
                if (known.Contains(p)) continue;
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
                var parts = assetPath.Split('/');
                if (parts.Length < 4) continue; // not "Assets/<root>/<dir>/sus-module.json" — can't be ours
                var actualRoot = parts[parts.Length - 3];
                var actualDir = parts[parts.Length - 2];
                string json;
                try { json = File.ReadAllText(assetPath); }
                catch (IOException) { continue; } // transient (mid-import) — next trigger retries
                results.Add((assetPath, actualRoot, actualDir, SusModuleManifest.Parse(json)));
            }
            return results;
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
