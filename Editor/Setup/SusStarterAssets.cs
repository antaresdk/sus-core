using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Locates and copies the pre-baked Setup starter, either from
    /// <c>Editor/Setup/Starter~</c> (UPM channel, committed in sus-core git with Generated/)
    /// or from <c>Editor/Setup/StarterAssets</c> (classic .unitypackage channel — <c>~</c>
    /// folders never survive an AssetDatabase export, see ARCH-PACK-CLASSIC.md §3 T2/T5).
    /// In the classic layout the shipped <c>.sharq</c>/<c>.g.cs</c> additionally carry a
    /// trailing <c>.txt</c> so they don't self-compile / self-generate on import — the same
    /// suffix trick already used for <c>MyApp.*.cs.txt</c> in this folder.
    /// </summary>
    internal static class SusStarterAssets
    {
        public const string StarterFolderName = "Starter~";
        public const string StarterFolderNameClassic = "StarterAssets";

        private static readonly string[] StarterFolderNames =
            { StarterFolderName, StarterFolderNameClassic };

        /// <summary>Absolute path to the starter folder inside the sus-core package/module,
        /// whichever layout is present (<c>Starter~</c> or <c>StarterAssets</c>).</summary>
        public static string GetStarterRoot()
        {
            // Fully qualify: UnityEditor.PackageInfo also exists (ambiguous with PackageManager).
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(SusStarterAssets).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
            {
                var found = ResolveStarterFolder(Path.Combine(info.resolvedPath, "Editor", "Setup"));
                if (found != null) return found;
            }

            // Fallback: walk up from this source file (file: / embedded / classic Assets layouts).
            var guids = AssetDatabase.FindAssets("SusSetupWizard t:MonoScript");
            foreach (var guid in guids)
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(scriptPath) || !scriptPath.Contains("SusSetupWizard"))
                    continue;
                var setupDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
                var found = ResolveStarterFolder(setupDir ?? "");
                if (found != null) return found;
            }

            throw new DirectoryNotFoundException(
                "[SusSetup] Starter assets not found in sus-core (Editor/Setup/Starter~ or Editor/Setup/StarterAssets).");
        }

        /// <summary>Returns the first existing starter folder under <paramref name="editorSetupDir"/>
        /// (<c>Starter~</c> checked before <c>StarterAssets</c>), or null if neither exists.</summary>
        internal static string ResolveStarterFolder(string editorSetupDir)
        {
            foreach (var name in StarterFolderNames)
            {
                var candidate = Path.Combine(editorSetupDir, name);
                if (Directory.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// Resolves a starter file under <paramref name="starterRoot"/> that may ship with a
        /// trailing <c>.txt</c> suffix (classic channel — see ARCH-PACK-CLASSIC.md §3 T5).
        /// Returns the actual on-disk path (with or without <c>.txt</c>), or null if neither exists.
        /// </summary>
        internal static string ResolveStarterFile(string starterRoot, string relPath)
        {
            var direct = Path.Combine(starterRoot, relPath);
            if (File.Exists(direct)) return direct;
            var txt = direct + ".txt";
            return File.Exists(txt) ? txt : null;
        }

        public static void CopyHomeScreen(string projectUiRootAbs)
        {
            var starter = GetStarterRoot();
            var sharqSrc = ResolveStarterFile(starter, "HomeScreen.sharq");
            var gcsSrc = ResolveStarterFile(starter, Path.Combine("Generated", "HomeScreen.g.cs"));
            if (sharqSrc == null || gcsSrc == null)
                throw new FileNotFoundException(
                    "[SusSetup] Starter assets are incomplete — run Tools~/refresh-starter-generated.ps1 in sus-core.");

            // Destination name is always the bare (non-.txt) name — this is what strips the
            // classic-channel suffix, the same pattern CopyAppEntry below already relies on.
            WriteIfMissing(Path.Combine(projectUiRootAbs, "HomeScreen.sharq"), File.ReadAllText(sharqSrc));

            var genDir = Path.Combine(projectUiRootAbs, "Generated");
            Directory.CreateDirectory(genDir);
            WriteIfMissing(Path.Combine(genDir, "HomeScreen.g.cs"), File.ReadAllText(gcsSrc, Encoding.UTF8));
        }

        public static void CopyAppEntry(string projectUiRootAbs, string className, bool withExample, bool withCustomization)
        {
            var starter = GetStarterRoot();
            string templateName;
            if (!withExample)
                templateName = "MyApp.Run.cs.txt";
            else if (withCustomization)
                templateName = "MyApp.Customization.cs.txt";
            else
                templateName = "MyApp.Mount.cs.txt";

            var templatePath = Path.Combine(starter, templateName);
            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"[SusSetup] Missing starter template: {templateName}");

            var text = File.ReadAllText(templatePath, Encoding.UTF8)
                .Replace("{{CLASS_NAME}}", className);
            WriteIfMissing(Path.Combine(projectUiRootAbs, className + ".cs"), text);
        }

        /// <summary>
        /// Regenerates <c>&lt;starter&gt;/Generated/HomeScreen.g.cs</c> from the committed .sharq
        /// (maintainers). Same generator as the AssetPostprocessor. Raskladko-neutral: works
        /// against <c>Starter~</c> (UPM) or <c>StarterAssets</c> (classic, where both files
        /// carry a <c>.txt</c> suffix — the output keeps whatever suffix the source had).
        /// </summary>
        [MenuItem("Window/SUS/Setup/Refresh Starter Generated", false, 61)]
        public static void RefreshStarterGeneratedMenu()
        {
            try
            {
                var starter = GetStarterRoot();
                var sharqPath = ResolveStarterFile(starter, "HomeScreen.sharq");
                if (sharqPath == null)
                {
                    EditorUtility.DisplayDialog("SUS", "HomeScreen.sharq (or .sharq.txt) not found in starter root.", "OK");
                    return;
                }

                var content = File.ReadAllText(sharqPath);
                var model = SharqFileParser.Parse(content, sharqPath);
                if (string.IsNullOrEmpty(model.TemplateXml))
                {
                    EditorUtility.DisplayDialog("SUS", "HomeScreen.sharq has no <template>.", "OK");
                    return;
                }

                var csharp = BuildMethodGenerator.Generate(model);
                // Stable comment path for git diffs
                csharp = System.Text.RegularExpressions.Regex.Replace(
                    csharp,
                    @"// Auto-generated by SharqSourceGenerator from .*",
                    "// Auto-generated by SharqSourceGenerator from Editor/Setup/Starter~/HomeScreen.sharq");

                var outDir = Path.Combine(starter, "Generated");
                Directory.CreateDirectory(outDir);
                var suffix = sharqPath.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase) ? ".txt" : "";
                var outPath = Path.Combine(outDir, "HomeScreen.g.cs" + suffix);
                File.WriteAllText(outPath, csharp, new UTF8Encoding(false));
                Debug.Log($"[SusSetup] Refreshed starter Generated: {outPath}");
                EditorUtility.DisplayDialog("SUS",
                    "Starter Generated/HomeScreen.g.cs refreshed.\nCommit it in the sus-core repo.",
                    "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SusSetup] Refresh Starter Generated failed: {ex}");
                EditorUtility.DisplayDialog("SUS", "Refresh failed — see Console.", "OK");
            }
        }

        static void WriteIfMissing(string absPath, string content)
        {
            if (File.Exists(absPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
            File.WriteAllText(absPath, content, new UTF8Encoding(false));
        }
    }
}
