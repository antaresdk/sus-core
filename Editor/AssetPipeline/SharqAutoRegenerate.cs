using System.IO;
using System.Linq;
using UnityEditor;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Auto-triggers Sharq regeneration on domain reload.
    /// Uses timestamp (fast path) + content hash (authoritative) for freshness.
    /// </summary>
    [InitializeOnLoad]
    public static class SharqAutoRegenerate
    {
        private static string SharqDir => SusConfig.Instance.SharqDirectory;
        private static string GeneratedDir => SusConfig.Instance.GeneratedDirectory;

        static SharqAutoRegenerate()
        {
            EditorApplication.delayCall += RegenIfSharqExists;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            RegenIfSharqExists();
        }

        private static void RegenIfSharqExists()
        {
            if (!AnySharqFilesExist()) return;
            if (GeneratedIsFresh()) return;
            SharqFileImporter.RegenerateAll();
        }

        private static bool GeneratedIsFresh()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), SharqDir);
            if (!Directory.Exists(path)) return true;

            var sharqFiles = Directory.GetFiles(path, "*.sharq", SearchOption.AllDirectories)
                .Where(f => !SharqFileImporter.IsUnderGenerated(f))
                .ToArray();

            if (sharqFiles.Length == 0) return true;

            var generatedDir = Path.Combine(Directory.GetCurrentDirectory(), GeneratedDir);
            if (!Directory.Exists(generatedDir)) return false;

            foreach (var sharq in sharqFiles)
            {
                var className = Path.GetFileNameWithoutExtension(sharq);
                var genCsPath = Path.Combine(generatedDir, $"{className}.g.cs");
                if (!File.Exists(genCsPath)) return false;

                // Fast path: timestamp (covers most cases)
                var sharqTime = File.GetLastWriteTimeUtc(sharq);
                if (File.GetLastWriteTimeUtc(genCsPath) < sharqTime) return false;

                // Authoritative: content hash
                if (!SharqFileImporter.IsHashMatch(sharq, GetSharqHash(sharq))) return false;

                // USS freshness (timestamp only — USS is regenerated when C# is)
                var genUssPath = Path.Combine(generatedDir, $"{className}_scoped.g.uss");
                if (File.Exists(genUssPath) && File.GetLastWriteTimeUtc(genUssPath) < sharqTime) return false;
                var genGlobalUssPath = Path.Combine(generatedDir, $"{className}.g.uss");
                if (File.Exists(genGlobalUssPath) && File.GetLastWriteTimeUtc(genGlobalUssPath) < sharqTime) return false;
            }

            return true;
        }

        private static string GetSharqHash(string sharqPath)
        {
            var content = File.ReadAllText(sharqPath);
            return SharqFileImporter.ComputeHash(content);
        }

        private static bool AnySharqFilesExist()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), SharqDir);
            if (!Directory.Exists(path)) return false;
            return Directory.GetFiles(path, "*.sharq", SearchOption.AllDirectories)
                .Any(f => !SharqFileImporter.IsUnderGenerated(f));
        }
    }
}
