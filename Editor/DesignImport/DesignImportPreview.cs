using System;
using System.IO;
using System.Text;

namespace Sharq.Core.Editor.DesignImport
{
    /// <summary>
    /// Dry-run preview + apply helpers shared by Editor window and tests (ARCH §7.1c).
    /// Never patches shipped design-tokens.uss; never opens network.
    /// </summary>
    public static class DesignImportPreview
    {
        public const string DefaultOutDirAssets = "Assets/SusDesign";

        public sealed class PreviewResult
        {
            public bool Ok { get; set; }
            public ImportResult Import { get; set; }
            public string ExistingUss { get; set; } = "";
            public string UnifiedDiff { get; set; } = "";
            public string UssPath { get; set; } = "";
            public string MetaPath { get; set; } = "";
            public bool HasExisting { get; set; }
            public bool Unchanged { get; set; }
        }

        /// <summary>
        /// Dry-run import and build unified diff against existing USS in <paramref name="outDir"/> (if any).
        /// </summary>
        public static PreviewResult Preview(string jsonText, ImportOptions options = null)
        {
            options = options ?? new ImportOptions();
            options.DryRun = true;
            if (string.IsNullOrEmpty(options.OutDir))
                options.OutDir = DefaultOutDirAssets;

            var result = new PreviewResult
            {
                UssPath = Path.Combine(options.OutDir, options.UssFileName),
                MetaPath = Path.Combine(options.OutDir, options.MetaFileName)
            };

            result.Import = DesignImporter.Import(jsonText, options);
            result.Ok = result.Import != null && result.Import.Ok;
            if (!result.Ok)
                return result;

            if (File.Exists(result.UssPath))
            {
                result.HasExisting = true;
                result.ExistingUss = File.ReadAllText(result.UssPath, Encoding.UTF8);
            }

            var oldLabel = result.HasExisting
                ? "a/" + options.UssFileName
                : "a/" + options.UssFileName + " (new)";
            var newLabel = "b/" + options.UssFileName;

            if (!result.HasExisting)
            {
                // Treat empty old as create — still show + lines.
                result.UnifiedDiff = DesignDiff.Unified("", result.Import.Uss, oldLabel, newLabel);
                result.Unchanged = false;
            }
            else if (DesignImporter.UssEquals(result.ExistingUss, result.Import.Uss))
            {
                result.Unchanged = true;
                result.UnifiedDiff = string.Empty;
            }
            else
            {
                result.UnifiedDiff = DesignDiff.Unified(
                    result.ExistingUss, result.Import.Uss, oldLabel, newLabel);
            }

            return result;
        }

        /// <summary>
        /// Write override USS + meta sidecar. Does not touch design-tokens.uss.
        /// </summary>
        public static ImportResult Apply(string jsonText, ImportOptions options = null)
        {
            options = options ?? new ImportOptions();
            options.DryRun = false;
            if (string.IsNullOrEmpty(options.OutDir))
                options.OutDir = DefaultOutDirAssets;

            // Guard: refuse writing into paths that look like shipped SoT token sheets.
            var ussName = (options.UssFileName ?? "").ToLowerInvariant();
            if (ussName == "design-tokens.uss" || ussName.EndsWith("/design-tokens.uss"))
                throw new InvalidOperationException(
                    "Import refuses to write design-tokens.uss (SoT). Use imported-tokens.uss override.");

            var outFull = Path.GetFullPath(options.OutDir);
            if (outFull.IndexOf("design-tokens", StringComparison.OrdinalIgnoreCase) >= 0
                && ussName.Contains("design-tokens"))
            {
                throw new InvalidOperationException(
                    "Import refuses SoT design-tokens path. Write imported-tokens.uss under project SusDesign/.");
            }

            return DesignImporter.Import(jsonText, options);
        }

        /// <summary>
        /// Resolve project-relative Assets path to absolute (Editor CWD = project root).
        /// </summary>
        public static string ResolveOutDir(string outDir)
        {
            if (string.IsNullOrWhiteSpace(outDir))
                outDir = DefaultOutDirAssets;
            outDir = outDir.Replace('\\', '/').TrimEnd('/');
            if (Path.IsPathRooted(outDir))
                return outDir;
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), outDir));
        }
    }
}
