using System;
using UnityEditor;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Batch-generates <c>.sharq</c> → <c>.g.cs</c>/USS for every UPM package that
    /// declares a <c>sharq.gen.json</c> descriptor (see <see cref="SusPackageRegistry"/>).
    /// The compilation core is the shared <see cref="SharqBatchCompiler"/> — the same
    /// pipeline as the project importer, so package output is identical byte-for-byte.
    /// </summary>
    public static class SusPackageGenerator
    {
        [MenuItem("Window/SUS/Sharq/Generate All Packages", false, 200)]
        public static void GenerateAll()
        {
            SusPackageRegistry.Refresh();

            if (SusPackageRegistry.Packages.Count == 0)
            {
                Debug.LogWarning(
                    "[SusPackages] No packages with a 'sharq.gen.json' descriptor found.");
                return;
            }

            foreach (var d in SusPackageRegistry.Packages)
                GenerateInternal(d, refresh: false);

            AssetDatabase.Refresh();
        }

        [MenuItem("Window/SUS/Sharq/Generate Package…", false, 201)]
        public static void OpenWindow() => SusPackageGeneratorWindow.Open();

        /// <summary>Generates a single package and imports the results.</summary>
        public static SharqBatchCompiler.Result Generate(SusPackageDescriptor descriptor)
        {
            var result = GenerateInternal(descriptor, refresh: true);
            return result;
        }

        private static SharqBatchCompiler.Result GenerateInternal(
            SusPackageDescriptor d, bool refresh)
        {
            var total = new SharqBatchCompiler.Result();

            try
            {
                foreach (var src in d.AbsSourceDirs)
                {
                    var r = SharqBatchCompiler.CompileDirectory(
                        src, d.AbsGeneratedDir, d.AbsResourcesDir, log: false);
                    total.Compiled += r.Compiled;
                    total.Failed += r.Failed;
                }

                if (total.Failed > 0)
                    Debug.LogWarning(
                        $"[SusPackages] {d.displayName}: {total.Compiled} compiled, {total.Failed} FAILED.");
                else
                    Debug.Log($"[SusPackages] {d.displayName}: {total.Compiled} compiled, 0 failed.");
            }
            catch (Exception ex)
            {
                // One broken package must not block the rest of a GenerateAll run.
                total.Failed++;
                Debug.LogError($"[SusPackages] {d.displayName}: generation threw — {ex.Message}");
            }

            if (refresh)
                AssetDatabase.Refresh();

            return total;
        }
    }

    /// <summary>
    /// Minimal per-package generation window (Sharq → Generate Package…):
    /// one row per discovered descriptor + a registry refresh button.
    /// </summary>
    internal sealed class SusPackageGeneratorWindow : EditorWindow
    {
        public static void Open()
        {
            var w = GetWindow<SusPackageGeneratorWindow>(utility: false, title: "Sharq Packages");
            w.minSize = new Vector2(340, 120);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Packages with sharq.gen.json", EditorStyles.boldLabel);

            var packages = SusPackageRegistry.Packages;
            if (packages.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No mutable packages with a 'sharq.gen.json' descriptor found.",
                    MessageType.Info);
            }

            foreach (var d in packages)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(d.displayName, d.PackageName), GUILayout.MinWidth(120));
                    EditorGUILayout.LabelField(
                        $"{string.Join(", ", d.sources)} → {d.generated}",
                        EditorStyles.miniLabel);
                    if (GUILayout.Button("Generate", GUILayout.Width(80)))
                        SusPackageGenerator.Generate(d);
                }
            }

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh registry"))
                    SusPackageRegistry.Refresh();
                if (GUILayout.Button("Generate All"))
                    SusPackageGenerator.GenerateAll();
            }
        }
    }
}
