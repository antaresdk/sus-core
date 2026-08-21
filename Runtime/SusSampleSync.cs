#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace Sharq.Core
{
    /// <summary>
    /// Editor-only sample-tree sync. UPM copies a package sample into
    /// <c>Assets/Samples/&lt;displayName&gt;/&lt;version&gt;/&lt;sample&gt;</c> once at import and never
    /// again; the package "Refresh … From Package" menus used to re-copy a HARD-CODED list of
    /// file names, so every file outside that list (new stories, QA fixtures, tests, a new
    /// sample file) silently never reached the copy the showcase drivers and live tests run
    /// against — three times in a row.
    ///
    /// This helper syncs the WHOLE tree recursively and verifies a copy against its source:
    /// <list type="bullet">
    /// <item>code/text files (<see cref="CodeExtensions"/>) are compared as EOL-normalized text
    /// (workspace <c>Samples~</c> is LF, PackageCache pins are often CRLF — R39);</item>
    /// <item>serialized Unity assets (<c>.unity</c>, <c>.asset</c>, <c>.prefab</c>, …) are copied
    ///   only when the copy lacks them — the editor rewrites their YAML under its own version,
    ///   overwriting an open scene from disk is never what "Refresh" means (R39 S3 soft);</item>
    /// <item><c>.meta</c> files are copied only for files that are new in the copy — existing
    ///   GUIDs stay stable, nothing is re-imported for no reason;</item>
    /// <item>files present in the copy but gone from the source are deleted (with their
    ///   <c>.meta</c>), except local drivers matching <see cref="DefaultSkipLocal"/>
    /// (<c>*ShotAll.cs</c> live in the copy ON PURPOSE —).</item>
    /// </list>
    /// Lives in the runtime assembly under <c>UNITY_EDITOR</c> so that both package Editor menus
    /// and PlayMode tests / drivers (which have no editor asmdef reference) can call
    /// <see cref="GuardCopyFresh"/> before they trust the copy.
    /// </summary>
    public static class SusSampleSync
    {
        /// <summary>Compared as normalized text; everything else is a serialized asset (soft).</summary>
        public static readonly HashSet<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".uss", ".uxml", ".asmdef", ".asmref", ".json", ".sharq", ".txt", ".md", ".tss",
        };

        /// <summary>Local driver files that live only in the copy and must survive a sync.</summary>
        public static readonly string[] DefaultSkipLocal = { "*ShotAll.cs" };

        /// <summary>Outcome of <see cref="SyncTree"/>; relative paths use '/' separators.</summary>
        public sealed class SyncResult
        {
            public readonly List<string> Copied = new List<string>();
            public readonly List<string> Unchanged = new List<string>();
            public readonly List<string> Deleted = new List<string>();
            public readonly List<string> KeptLocal = new List<string>();
            public readonly List<string> SkippedLocked = new List<string>();
            public readonly List<string> SoftKept = new List<string>();

            public int Skipped => SkippedLocked.Count;

            public override string ToString()
                => $"copied {Copied.Count} · unchanged {Unchanged.Count} · deleted {Deleted.Count}"
                   + $" · kept-local {KeptLocal.Count} · soft-kept {SoftKept.Count} · locked {SkippedLocked.Count}";
        }

        /// <summary>
        /// Copies <paramref name="srcDir"/> into <paramref name="destDir"/> recursively and removes
        /// files that disappeared from the source. <paramref name="copyFile"/> may wrap the raw copy
        /// (retry/lock handling); it returns false when the destination is locked.
        /// </summary>
        public static SyncResult SyncTree(string srcDir, string destDir,
            string[] skipLocalPatterns = null, bool deleteExtra = true,
            Func<string, string, bool> copyFile = null)
        {
            if (string.IsNullOrEmpty(srcDir) || !Directory.Exists(srcDir))
                throw new DirectoryNotFoundException($"Sample source not found: {srcDir}");
            srcDir = Path.GetFullPath(srcDir);
            destDir = Path.GetFullPath(destDir);
            skipLocalPatterns ??= DefaultSkipLocal;
            copyFile ??= DefaultCopy;

            var result = new SyncResult();
            Directory.CreateDirectory(destDir);

            var srcFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var abs in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                var rel = Rel(srcDir, abs);
                srcFiles.Add(rel);
                if (IsMeta(rel)) continue; // handled together with its base file

                var dest = Path.Combine(destDir, rel);
                var destExists = File.Exists(dest);
                if (destExists && !IsCode(rel))
                {
                    // Serialized asset already in the copy — the editor owns its YAML.
                    result.SoftKept.Add(rel);
                    continue;
                }
                if (destExists && SameContent(abs, dest, rel))
                {
                    result.Unchanged.Add(rel);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? destDir);
                if (!copyFile(abs, dest))
                {
                    result.SkippedLocked.Add(rel);
                    continue;
                }
                result.Copied.Add(rel);

                // New file → bring its .meta along so the GUID matches the package's (scenes in
                // the sample reference scripts by GUID). Existing files keep the copy's GUID.
                if (!destExists)
                {
                    var srcMeta = abs + ".meta";
                    var destMeta = dest + ".meta";
                    if (File.Exists(srcMeta) && !File.Exists(destMeta))
                        copyFile(srcMeta, destMeta);
                }
            }

            if (deleteExtra)
            {
                foreach (var abs in Directory.GetFiles(destDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Rel(destDir, abs);
                    if (srcFiles.Contains(rel)) continue;
                    if (rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsMeta(rel))
                    {
                        // A .meta is judged by its base file: keep it while the base stays.
                        var baseRel = rel.Substring(0, rel.Length - ".meta".Length);
                        if (File.Exists(Path.Combine(destDir, baseRel)) || Directory.Exists(Path.Combine(destDir, baseRel)))
                            continue;
                        if (srcFiles.Contains(baseRel)) continue;
                        TryDelete(abs);
                        continue;
                    }
                    if (MatchesAny(Path.GetFileName(rel), skipLocalPatterns))
                    {
                        result.KeptLocal.Add(rel);
                        continue;
                    }
                    TryDelete(abs);
                    TryDelete(abs + ".meta");
                    result.Deleted.Add(rel);
                }
            }

            return result;
        }

        /// <summary>
        /// Compares a copy with its source the way the workspace sample-sync gate (R39) does: code/text
        /// files as EOL-normalized text (S1 stale), missing source files (S2 absent). Serialized
        /// assets, <c>.meta</c> and local drivers are not judged. Returns relative paths prefixed
        /// with <c>S1 </c> / <c>S2 </c>; empty when the copy is fresh.
        /// </summary>
        public static List<string> Verify(string srcDir, string destDir, string[] skipLocalPatterns = null)
        {
            var drift = new List<string>();
            if (string.IsNullOrEmpty(srcDir) || !Directory.Exists(srcDir)) return drift;
            srcDir = Path.GetFullPath(srcDir);
            destDir = Path.GetFullPath(destDir);
            foreach (var abs in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                var rel = Rel(srcDir, abs);
                if (IsMeta(rel) || !IsCode(rel)) continue;
                var dest = Path.Combine(destDir, rel);
                if (!File.Exists(dest)) drift.Add("S2 absent: " + rel);
                else if (!SameContent(abs, dest, rel)) drift.Add("S1 stale: " + rel);
            }
            return drift;
        }

        /// <summary>
        /// Pre-step for drivers/tests that run against a sample COPY: compares the copy that
        /// contains the calling file with the package's <c>Samples~/&lt;sampleFolder&gt;</c> and
        /// returns a human-readable error (with the menu route) when the copy is stale, or
        /// <c>null</c> when it is fresh OR the source cannot be located (classic install, no
        /// package — nothing to compare against). <paramref name="dirsUpToSampleRoot"/> is how many
        /// directories separate the calling file from the sample root (0 = file sits in the root,
        /// 1 = in <c>Tests/</c>).
        /// </summary>
        public static string GuardCopyFresh(string packageId, string sampleFolder, int dirsUpToSampleRoot,
            string menuRoute, [CallerFilePath] string callerFile = "")
        {
            if (!TryFindPackageSampleDir(packageId, sampleFolder, out var srcDir)) return null;
            if (string.IsNullOrEmpty(callerFile)) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(callerFile));
            for (var i = 0; i < dirsUpToSampleRoot && dir != null; i++) dir = Path.GetDirectoryName(dir);
            if (dir == null || !Directory.Exists(dir)) return null;
            if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(srcDir), StringComparison.OrdinalIgnoreCase))
                return null; // running straight from Samples~ (read-only open) — nothing to drift
            var drift = Verify(srcDir, dir);
            if (drift.Count == 0) return null;
            var sb = new StringBuilder();
            sb.Append($"[SusSampleSync] sample copy is STALE vs package source — {drift.Count} file(s): ");
            sb.Append(string.Join(", ", drift.GetRange(0, Math.Min(drift.Count, 6))));
            if (drift.Count > 6) sb.Append(" …");
            sb.Append($". Copy: {dir} · source: {srcDir}. Refresh first: {menuRoute} (R39) — shooting/testing a stale copy silently reports the OLD UI.");
            return sb.ToString();
        }

        /// <summary>
        /// Locates <c>Samples~/&lt;sampleFolder&gt;</c> of an installed package: the resolved
        /// <c>Packages/&lt;id&gt;</c> path (file:/embedded/PackageCache via PackageInfo) first, then
        /// a PackageCache scan.
        /// </summary>
        public static bool TryFindPackageSampleDir(string packageId, string sampleFolder, out string absDir)
        {
            absDir = null;
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageId);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    var c = Path.Combine(info.resolvedPath, "Samples~", sampleFolder);
                    if (Directory.Exists(c)) { absDir = Path.GetFullPath(c); return true; }
                }
            }
            catch { /* PackageInfo unavailable in odd contexts — fall through */ }

            var viaPackages = Path.Combine(Path.GetFullPath($"Packages/{packageId}"), "Samples~", sampleFolder);
            if (Directory.Exists(viaPackages)) { absDir = viaPackages; return true; }

            var cache = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/PackageCache"));
            if (!Directory.Exists(cache)) return false;
            foreach (var dir in Directory.GetDirectories(cache, packageId + "*"))
            {
                var c = Path.Combine(dir, "Samples~", sampleFolder);
                if (Directory.Exists(c)) { absDir = c; return true; }
            }
            return false;
        }

        // ─── internals ───────────────────────────────────────────────

        static bool IsMeta(string rel) => rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        static bool IsCode(string rel) => !IsMeta(rel) && CodeExtensions.Contains(Path.GetExtension(rel));
        static string Rel(string root, string abs) => Path.GetRelativePath(root, abs).Replace('\\', '/');

        static bool SameContent(string a, string b, string rel)
        {
            if (IsCode(rel))
                return NormalizeText(File.ReadAllText(a)) == NormalizeText(File.ReadAllText(b));
            var ba = File.ReadAllBytes(a);
            var bb = File.ReadAllBytes(b);
            if (ba.Length != bb.Length) return false;
            for (var i = 0; i < ba.Length; i++) if (ba[i] != bb[i]) return false;
            return true;
        }

        static string NormalizeText(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

        static bool DefaultCopy(string src, string dest)
        {
            try { File.Copy(src, dest, overwrite: true); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }

        /// <summary>Minimal glob: <c>*</c> (any run) and <c>?</c> (one char), case-insensitive, on the file name.</summary>
        public static bool MatchesAny(string fileName, string[] patterns)
        {
            if (patterns == null) return false;
            foreach (var p in patterns)
                if (GlobMatch(fileName, p)) return true;
            return false;
        }

        static bool GlobMatch(string s, string p)
        {
            int si = 0, pi = 0, star = -1, mark = 0;
            while (si < s.Length)
            {
                if (pi < p.Length && (p[pi] == '?' || char.ToLowerInvariant(p[pi]) == char.ToLowerInvariant(s[si]))) { si++; pi++; }
                else if (pi < p.Length && p[pi] == '*') { star = pi++; mark = si; }
                else if (star >= 0) { pi = star + 1; si = ++mark; }
                else return false;
            }
            while (pi < p.Length && p[pi] == '*') pi++;
            return pi == p.Length;
        }
    }
}
#endif
