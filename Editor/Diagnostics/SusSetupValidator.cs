using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Diagnostics
{
    public enum SusValidationSeverity { Error, Warning, Info }

    public class SusValidationIssue
    {
        public SusValidationSeverity Severity;
        public string Category;
        public string Message;
        public string FixHint;

        public static SusValidationIssue Error(string cat, string msg, string fix = null) =>
            new() { Severity = SusValidationSeverity.Error, Category = cat, Message = msg, FixHint = fix };
        public static SusValidationIssue Warning(string cat, string msg, string fix = null) =>
            new() { Severity = SusValidationSeverity.Warning, Category = cat, Message = msg, FixHint = fix };
        public static SusValidationIssue Info(string cat, string msg) =>
            new() { Severity = SusValidationSeverity.Info, Category = cat, Message = msg };

        public override string ToString()
        {
            var icon = Severity switch
            {
                SusValidationSeverity.Error => "\u274C",
                SusValidationSeverity.Warning => "\u26A0\uFE0F",
                SusValidationSeverity.Info => "\u2139\uFE0F",
                _ => "?"
            };
            var sb = new StringBuilder();
            sb.AppendLine($"{icon} [{Category}] {Message}");
            if (!string.IsNullOrEmpty(FixHint))
                sb.AppendLine($"   \u2192 {FixHint}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Diagnostics for SUS setup — checks config, generated files, PanelSettings,
    /// package availability. Run via <c>Tools → SUS → Validate Setup</c>.
    /// </summary>
    public static class SusSetupValidator
    {
        private const string MenuPath = "Window/SUS/Validate Setup";

        [MenuItem(MenuPath)]
        public static void ValidateSetup()
        {
            var issues = new List<SusValidationIssue>();
            ValidateAll(issues);

            if (issues.Count == 0)
            {
                Debug.Log("<color=green>\u2714 SUS Setup - everything is fine.</color>");
                EditorUtility.DisplayDialog("SUS Setup", "No issues found.", "OK");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== SUS Setup Validation === {issues.Count} issue(s)");
            sb.AppendLine();

            int errors = 0, warnings = 0, infos = 0;
            foreach (var issue in issues)
            {
                sb.AppendLine(issue.ToString());
                switch (issue.Severity)
                {
                    case SusValidationSeverity.Error: errors++; break;
                    case SusValidationSeverity.Warning: warnings++; break;
                    case SusValidationSeverity.Info: infos++; break;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"\u274C {errors} errors  \u26A0\uFE0F {warnings} warnings  \u2139\uFE0F {infos} info");

            var summary = sb.ToString();
            Debug.Log(summary);

            var firstError = issues.Find(i => i.Severity == SusValidationSeverity.Error);
            var firstWarning = issues.Find(i => i.Severity == SusValidationSeverity.Warning);
            var displayIssue = firstError ?? firstWarning ?? issues[0];
            EditorUtility.DisplayDialog("SUS Setup", summary, "OK");
        }

        public static void ValidateAll(List<SusValidationIssue> issues)
        {
            CheckUnityVersion(issues);
            CheckUiToolkit(issues);
            CheckSusConfigPaths(issues);
            CheckSharqFiles(issues);
            CheckGeneratedFiles(issues);
            CheckResourcesUss(issues);
            CheckPanelSettings(issues);
            CheckCorePackage(issues);
            CheckPackages(issues);
        }

        // ── Individual checks ──────────────────────────────────

        private static void CheckUnityVersion(List<SusValidationIssue> issues)
        {
            // Unity versions look like "6000.3.17f1" — System.Version rejects the letter suffix.
            if (!TryParseUnityVersion(Application.unityVersion, out var ver))
            {
                issues.Add(SusValidationIssue.Warning("Unity", $"Failed to parse version '{Application.unityVersion}'"));
                return;
            }
            if (ver.Major < 6000)
                issues.Add(SusValidationIssue.Error("Unity", $"Requires Unity 6000.0+, current: {Application.unityVersion}",
                    "Update the editor to 6000.0 LTS or later"));
        }

        /// <summary>Parses "6000.3.17f1" / "2022.3.10f1" into a comparable Version (major.minor.build).</summary>
        internal static bool TryParseUnityVersion(string raw, out Version ver)
        {
            ver = null;
            if (string.IsNullOrEmpty(raw)) return false;
            var end = 0;
            while (end < raw.Length && (char.IsDigit(raw[end]) || raw[end] == '.'))
                end++;
            var numeric = end > 0 ? raw.Substring(0, end).TrimEnd('.') : raw;
            return Version.TryParse(numeric, out ver);
        }

        private static void CheckUiToolkit(List<SusValidationIssue> issues)
        {
            var type = Type.GetType("UnityEngine.UIElements.UIDocument, UnityEngine.UIElementsModule");
            if (type == null)
                issues.Add(SusValidationIssue.Error("UIToolkit", "UI Toolkit not detected - UIDocument does not resolve",
                    "Check for the presence of the UnityEngine.UIElementsModule module"));
        }

        private static void CheckSusConfigPaths(List<SusValidationIssue> issues)
        {
            // Same editor assembly as SusConfig (internal) — no reflection needed.
            var cfg = SusConfig.Instance;
            var sharqDir = cfg?.SharqDirectory ?? "Assets/SusUI";
            var genDir = cfg?.GeneratedDirectory ?? "Assets/SusUI/Generated";

            if (!string.IsNullOrEmpty(sharqDir) && !Directory.Exists(sharqDir))
                issues.Add(SusValidationIssue.Error("Config", $"SharqDirectory does not exist: '{sharqDir}'",
                    "Create a folder of Sharq files or run Tools → SUS → Setup Project"));

            if (!string.IsNullOrEmpty(genDir) && !Directory.Exists(genDir))
                issues.Add(SusValidationIssue.Warning("Config", $"GeneratedDirectory does not exist: '{genDir}'",
                    "The folder will be created automatically during the first Sharq compilation"));

            if (cfg == null)
                issues.Add(SusValidationIssue.Info("Config", "SusConfig not found - working with default paths"));
            else if (!File.Exists(SusConfig.ConfigFilePath))
                issues.Add(SusValidationIssue.Info("Config", "Assets/sus.config.json is missing - SusConfig defaults are used"));
        }

        private static void CheckSharqFiles(List<SusValidationIssue> issues)
        {
            var sharqDir = SusConfig.Instance?.SharqDirectory ?? "Assets/SusUI";

            if (!Directory.Exists(sharqDir))
                return; // Already reported in CheckSusConfigPaths

            var files = Directory.GetFiles(sharqDir, "*.sharq", SearchOption.AllDirectories);
            if (files.Length == 0)
                issues.Add(SusValidationIssue.Info("Sharq", $"No .sharq files in '{sharqDir}' - if this is a new project, create the first component"));
        }

        private static void CheckGeneratedFiles(List<SusValidationIssue> issues)
        {
            var cfg = SusConfig.Instance;
            var genDir = cfg?.GeneratedDirectory ?? "Assets/SusUI/Generated";
            var resourcesDir = cfg?.ResourcesDirectory ?? "Assets/SusUI/Generated/Resources/SusRuntime";
            var sharqDir = cfg?.SharqDirectory ?? "Assets/SusUI";

            if (Directory.Exists(genDir))
            {
                var csFiles = Directory.GetFiles(genDir, "*.g.cs", SearchOption.AllDirectories).Length;
                var ussFiles = Directory.GetFiles(genDir, "*.g.uss", SearchOption.AllDirectories).Length;
                if (csFiles == 0 && ussFiles == 0)
                    issues.Add(SusValidationIssue.Warning("Generated", $"Folder '{genDir}' is empty - no .g.cs/.g.uss",
                        "Run Window/SUS/Sharq/Generate All Packages or import .sharq files"));
            }
            else if (Directory.Exists(sharqDir) && Directory.GetFiles(sharqDir, "*.sharq", SearchOption.AllDirectories).Length > 0)
            {
                issues.Add(SusValidationIssue.Error("Generated", $"There is .sharq in '{sharqDir}' but Generated/ does not exist",
                    "Run Window/SUS/Sharq/Generate All Packages"));
            }

            if (!string.IsNullOrEmpty(resourcesDir) && !Directory.Exists(resourcesDir))
                issues.Add(SusValidationIssue.Warning("Resources", $"ResourcesDirectory does not exist: '{resourcesDir}'",
                    "Will be created during the first Sharq compilation"));
        }

        private static void CheckResourcesUss(List<SusValidationIssue> issues)
        {
            var resourcesDir = SusConfig.Instance?.ResourcesDirectory
                               ?? "Assets/SusUI/Generated/Resources/SusRuntime";

            var requiredFiles = new[] { "SusDefault.tss", "_palette.uss", "_font.uss", "_theme.uss", "design-tokens.uss" };
            foreach (var file in requiredFiles)
            {
                var fullPath = Path.Combine(resourcesDir, file);
                if (File.Exists(fullPath)) continue;

                var resName = "SusRuntime/" + Path.GetFileNameWithoutExtension(file);
                var found = file.EndsWith(".tss", StringComparison.OrdinalIgnoreCase)
                    ? Resources.Load<ThemeStyleSheet>(resName) != null
                    : Resources.Load<StyleSheet>(resName) != null;

                if (!found)
                    issues.Add(SusValidationIssue.Warning("Resources", $"'{file}' not found in '{resourcesDir}' and in Resources",
                        "Check the installation of the sus-core package"));
            }
        }

        private static void CheckPanelSettings(List<SusValidationIssue> issues)
        {
            // Setup Wizard writes SusPanelSettings next to the UI root; samples use Resources/.
            var cfgRoot = SusConfig.Instance?.SharqDirectory?.TrimEnd('/', '\\') ?? "Assets/SusUI";
            var knownPaths = new[]
            {
                "Assets/Resources/PanelSettings.asset",
                cfgRoot + "/SusPanelSettings.asset",
            };

            var hasAsset = false;
            foreach (var psPath in knownPaths)
            {
                if (File.Exists(psPath) || AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath) != null)
                {
                    hasAsset = true;
                    break;
                }
            }
            if (!hasAsset && Resources.Load<PanelSettings>("PanelSettings") == null)
            {
                issues.Add(SusValidationIssue.Info("PanelSettings",
                    "Explicit PanelSettings.asset not found - SusBootstrap will create it on the fly. Setup puts SusPanelSettings in UI root."));
            }

            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(UnityEngine.FindObjectsSortMode.None);
            int withoutPanelSettings = 0;
            foreach (var doc in docs)
            {
                if (doc.panelSettings == null) withoutPanelSettings++;
            }
            if (withoutPanelSettings > 0)
            {
                var warn = $"On stage {withoutPanelSettings} UIDocument without PanelSettings";
                if (docs.Length == withoutPanelSettings)
                    warn += "(All)";
                issues.Add(SusValidationIssue.Warning("PanelSettings", warn,
                    "SusBootstrap.ApplyDefaultTSS will create PanelSettings on the fly. For a production scene, assign it manually."));
            }
        }

        private static void CheckCorePackage(List<SusValidationIssue> issues)
        {
            // Editor references com.sharq-it.sus.core — if this method compiles/runs, SusApp is present.
            // Old check used Type.GetType("…, sus-core.Runtime") which always failed (wrong asm name).
            _ = typeof(SusApp);
        }

        private static void CheckPackages(List<SusValidationIssue> issues)
        {
            if (!TypeExists("Sharq.Router.SusRouter"))
                issues.Add(SusValidationIssue.Info("Packages", "sus-router is not detected - navigation will be unavailable"));
        }

        static bool TypeExists(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(fullName, throwOnError: false); }
                catch { continue; }
                if (t != null) return true;
            }
            return false;
        }
    }
}
