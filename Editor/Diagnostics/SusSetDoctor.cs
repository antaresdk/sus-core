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
    /// SUS Set Doctor (ARCH-PACK-CLASSIC.md §2.2, T-368) — detects three states a classic
    /// (.unitypackage) purchaser can silently end up in, because Unity's asset import only ADDS
    /// files, it never deletes or reconciles them:
    ///
    ///  1. <b>UPM + classic collision.</b> A UPM package for a module (Packages/) coexists with
    ///     the classic asset copy of the same module (Assets/&lt;root&gt;/&lt;Module&gt;) — same
    ///     asmdef name defined twice.
    ///  2. <b>Residual files.</b> Files from a previous set version remain on disk after
    ///     updating to a newer .unitypackage that no longer ships them (real case from the
    ///     audit: <c>SusIcon</c> → <c>SusIconElement</c>).
    ///  3. <b>Mixed module versions.</b> One module was updated (or manually replaced) while
    ///     others in the same set were not.
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
        public const string ManifestFileName = "sus-set.json";
        private const string PackageIdPrefix = "com.sharq-it.sus.";

        private static readonly Regex ChangelogVersionHeading =
            new(@"^##\s*\[([^\]]+)\]", RegexOptions.Multiline | RegexOptions.Compiled);

        [MenuItem("Window/SUS/Set Doctor")]
        public static void RunFromMenu()
        {
            var issues = RunAll();
            LogAndShow(issues, forceDialog: true);
        }

        /// <summary>Full check against the live project: finds the manifest (if any classic set
        /// is installed), gathers UPM/disk state and returns every finding. Returns an empty
        /// list — silently — when no <c>sus-set.json</c> is present (a plain UPM package
        /// development project is not a classic-set install and has nothing to check).</summary>
        public static List<SusValidationIssue> RunAll()
        {
            var issues = new List<SusValidationIssue>();

            var manifestAssetPath = FindManifestAssetPath();
            if (string.IsNullOrEmpty(manifestAssetPath)) return issues;

            string json;
            try
            {
                json = File.ReadAllText(manifestAssetPath);
            }
            catch (IOException)
            {
                return issues; // transient (mid-import) — the next trigger will retry
            }

            var manifest = SusSetManifest.Parse(json);
            if (manifest == null)
            {
                issues.Add(SusValidationIssue.Warning("SetDoctor",
                    $"'{manifestAssetPath}' exists but could not be parsed as a {ManifestFileName} manifest.",
                    "Reinstall the set from the Asset Store — the manifest may be truncated or hand-edited."));
                return issues;
            }

            var assetsAbs = Path.GetFullPath("Assets");
            var setRootAbs = Path.Combine(assetsAbs, manifest.root);

            var installedUpm = new HashSet<string>(
                PackageInfo.GetAllRegisteredPackages().Select(p => p.name),
                StringComparer.OrdinalIgnoreCase);

            var presentModuleDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in manifest.modules ?? Array.Empty<SusSetManifestModule>())
            {
                if (!string.IsNullOrEmpty(module.dir) && Directory.Exists(Path.Combine(setRootAbs, module.dir)))
                    presentModuleDirs.Add(module.dir);
            }

            issues.AddRange(DetectUpmCollisions(manifest, installedUpm, presentModuleDirs));

            var actualPaths = CollectActualPaths(assetsAbs, manifest.root);
            var residual = DetectResidualPaths(manifest, actualPaths);
            if (residual.Count > 0)
                issues.Add(BuildResidualIssue(manifest, residual));

            var actualVersions = new Dictionary<string, string>();
            foreach (var module in manifest.modules ?? Array.Empty<SusSetManifestModule>())
            {
                if (string.IsNullOrEmpty(module.dir)) continue;
                var changelogPath = Path.Combine(setRootAbs, module.dir, "CHANGELOG.md");
                if (!File.Exists(changelogPath)) continue;
                try
                {
                    var v = ExtractLatestChangelogVersion(File.ReadAllText(changelogPath));
                    if (!string.IsNullOrEmpty(v)) actualVersions[module.id] = v;
                }
                catch (IOException)
                {
                    // transient — skip this module for this run, next trigger will retry
                }
            }
            issues.AddRange(DetectVersionMismatches(manifest, actualVersions));

            return issues;
        }

        // ─── Pure classification (no Unity/IO — unit-testable directly) ──────────

        /// <summary>State (1): a module is both a registered UPM package AND present as a
        /// classic asset folder at the same time.</summary>
        internal static List<SusValidationIssue> DetectUpmCollisions(
            SusSetManifest manifest,
            ISet<string> installedUpmPackageNames,
            ISet<string> presentModuleDirs)
        {
            var issues = new List<SusValidationIssue>();
            foreach (var module in manifest.modules ?? Array.Empty<SusSetManifestModule>())
            {
                if (string.IsNullOrEmpty(module.id) || string.IsNullOrEmpty(module.dir)) continue;

                var packageName = PackageIdPrefix + module.id;
                var upmInstalled = installedUpmPackageNames.Contains(packageName);
                var assetPresent = presentModuleDirs.Contains(module.dir);
                if (!upmInstalled || !assetPresent) continue;

                issues.Add(SusValidationIssue.Error("SetDoctor.UpmCollision",
                    $"Both the UPM package '{packageName}' (Packages/) and the classic module " +
                    $"'{manifest.root}/{module.dir}' (Assets/) are installed at the same time — " +
                    "they define the same assembly and will not compile together.",
                    $"Remove ONE of the two: Package Manager -> remove '{packageName}', OR delete " +
                    $"the folder 'Assets/{manifest.root}/{module.dir}'. If you bought this set, " +
                    "keep the classic folder and remove the UPM package."));
            }
            return issues;
        }

        /// <summary>State (2): entries present on disk under the set root that the current
        /// manifest no longer declares. Collapsed to the shallowest offending path so a whole
        /// stale folder is reported once, not file-by-file.</summary>
        internal static List<string> DetectResidualPaths(SusSetManifest manifest, IEnumerable<string> actualPaths)
        {
            var manifestSet = new HashSet<string>(manifest.paths ?? Array.Empty<string>(), StringComparer.Ordinal);

            var extra = new List<string>();
            foreach (var p in actualPaths)
                if (!manifestSet.Contains(p)) extra.Add(p);
            extra.Sort(StringComparer.Ordinal);

            var collapsed = new List<string>();
            foreach (var p in extra)
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

        /// <summary>State (3): a module's manifest version disagrees with the version read back
        /// from its own CHANGELOG.md on disk. Modules with no readable changelog are skipped —
        /// silence, not a false positive, when there is no signal to compare against.</summary>
        internal static List<SusValidationIssue> DetectVersionMismatches(
            SusSetManifest manifest, IReadOnlyDictionary<string, string> actualVersionByModuleId)
        {
            var issues = new List<SusValidationIssue>();
            foreach (var module in manifest.modules ?? Array.Empty<SusSetManifestModule>())
            {
                if (string.IsNullOrEmpty(module.id)) continue;
                if (!actualVersionByModuleId.TryGetValue(module.id, out var actual) || string.IsNullOrEmpty(actual))
                    continue;
                if (string.Equals(actual, module.version, StringComparison.Ordinal)) continue;

                issues.Add(SusValidationIssue.Warning("SetDoctor.VersionMismatch",
                    $"Module '{module.dir}' on disk reports version {actual} (from its CHANGELOG.md), " +
                    $"but this project's {ManifestFileName} expects {module.version}.",
                    $"Likely a mixed-version install: re-import the '{manifest.displayName}' " +
                    $".unitypackage in full so every module under Assets/{manifest.root} matches, " +
                    "or check for a partial/interrupted update."));
            }
            return issues;
        }

        // ─── IO helpers (thin — file-walking only, unit-testable with a temp dir) ─

        /// <summary>Walks <c>&lt;assetsAbsPath&gt;/&lt;root&gt;</c> and returns every entry
        /// (folders and files, <c>.meta</c> excluded) as an Assets-relative forward-slash path
        /// — the same shape as <see cref="SusSetManifest.paths"/>, including the root folder
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

        private static string FindManifestAssetPath()
        {
            foreach (var guid in AssetDatabase.FindAssets("sus-set"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileName(assetPath), ManifestFileName, StringComparison.OrdinalIgnoreCase))
                    return assetPath;
            }
            return null;
        }

        private static SusValidationIssue BuildResidualIssue(SusSetManifest manifest, List<string> residual)
        {
            const int maxListed = 20;
            var listed = residual.Take(maxListed);
            var body = string.Join("\n   ", listed);
            if (residual.Count > maxListed) body += $"\n   ... and {residual.Count - maxListed} more";

            return SusValidationIssue.Warning("SetDoctor.Residual",
                $"{residual.Count} path(s) under 'Assets/{manifest.root}' are not part of the " +
                $"current '{manifest.displayName}' v{manifest.version} manifest — likely left over " +
                "from an older version (classic import only adds files, it never deletes):\n   " + body,
                "Delete the listed paths, or delete the whole set folder and re-import the current .unitypackage.");
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
