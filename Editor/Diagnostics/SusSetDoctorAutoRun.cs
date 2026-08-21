using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>
    /// Auto-triggers <see cref="SusSetDoctor"/> without any manual step: on every domain reload
    /// (project open, script recompile), and right after an asset-import batch that either
    /// touches a module manifest (<c>sus-module.json</c>) or a set descriptor
    /// (<c>sus-set.&lt;set&gt;.json</c>), or changes which Sharq UPM packages are registered —
    /// the events that can introduce the UPM/classic collision (ARCH-PACK-CLASSIC.md §2.2) or
    /// change what Doctor knows is present (§2.3 D7).
    ///
    /// Package-registration changes are detected via <c>PackageInfo.GetAllRegisteredPackages()</c>
    /// name diffing (cheap, in-memory, no filesystem walk) rather than by scanning imported asset
    /// paths for a literal package-root prefix — a purchaser's classic install must never contain
    /// a source-code assumption about where UPM packages live (that literal is exactly what T6 of
    /// ARCH-PACK-CLASSIC.md §3 forbids: it is meaningless once resources move to Assets/, and is
    /// the class of bug this very file exists to catch elsewhere).
    ///
    /// The <see cref="AssetPostprocessor"/> hook runs once at the end of the SAME import batch
    /// that resolves GUID conflicts for a newly-added colliding copy — i.e. after Unity's own
    /// (unavoidable, happens deeper than any public Editor API) GUID reassignment for that batch,
    /// but BEFORE <c>CompilationPipeline</c> attempts to compile the now-duplicated assembly and
    /// fails outright. That is the earliest a plain Editor script can plausibly warn — see the
    /// boundary note on <see cref="SusSetDoctor"/> for when this window isn't reachable at all
    /// (the collision is on core itself, on a fresh project that never compiled either copy).
    /// </summary>
    [InitializeOnLoad]
    internal static class SusSetDoctorAutoRun
    {
        private const string SusPackageNamePrefix = "com.sharq-it.sus.";

        private static HashSet<string> s_lastKnownSusPackageNames = new(StringComparer.OrdinalIgnoreCase);

        static SusSetDoctorAutoRun()
        {
            EditorApplication.delayCall += RunQuiet;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() => RunQuiet();

        private static void RunQuiet()
        {
            s_lastKnownSusPackageNames = CurrentSusPackageNames();
            var issues = SusSetDoctor.RunAll();
            SusSetDoctor.LogAndShow(issues, forceDialog: false);
        }

        private static HashSet<string> CurrentSusPackageNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in PackageInfo.GetAllRegisteredPackages())
                if (p.name.StartsWith(SusPackageNamePrefix, StringComparison.OrdinalIgnoreCase))
                    names.Add(p.name);
            return names;
        }

        /// <summary>True when a changed asset path is a module manifest OR a set descriptor —
        /// the cheap, filesystem-only half of the re-check trigger (the other half is the UPM
        /// package-name diff in <see cref="CurrentSusPackageNames"/>). Both names matter since
        /// D7: a module manifest changing can flip Residual/UpmCollision/VersionMismatch
        /// findings on its own module, a set descriptor changing can flip
        /// ModuleManifestMissing/IncompleteSet findings that read its <c>modules</c> list.</summary>
        internal static bool IsManifestPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            var name = Path.GetFileName(assetPath);
            return string.Equals(name, SusSetDoctor.ModuleManifestFileName, StringComparison.OrdinalIgnoreCase)
                || SusSetDoctor.IsSetDescriptorFileName(name);
        }

        private sealed class Postprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                var manifestTouched =
                    AnyManifestPath(importedAssets) || AnyManifestPath(deletedAssets) || AnyManifestPath(movedAssets);

                if (!manifestTouched && SetEquals(CurrentSusPackageNames(), s_lastKnownSusPackageNames))
                    return;

                RunQuiet();
            }

            private static bool AnyManifestPath(string[] paths)
            {
                if (paths == null) return false;
                foreach (var p in paths)
                    if (IsManifestPath(p)) return true;
                return false;
            }

            private static bool SetEquals(HashSet<string> a, HashSet<string> b) => a.SetEquals(b);
        }
    }
}
