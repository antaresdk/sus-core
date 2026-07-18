#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Registers a StyleSheet-from-USS factory for <see cref="SusRuntimeHotReload"/>
    /// using Unity's internal <c>StyleSheetImporterImpl</c> (same path as UI test framework).
    /// </summary>
    [InitializeOnLoad]
    public static class SusUssFromString
    {
        static SusUssFromString()
        {
            SusRuntimeHotReload.StyleSheetFromUss = Create;
        }

        /// <summary>Build an in-memory StyleSheet from USS text.</summary>
        public static StyleSheet Create(string ussText, string sheetName = "hotreload.g")
        {
            if (string.IsNullOrEmpty(ussText))
                throw new ArgumentException("ussText is empty");

            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            sheet.hideFlags = HideFlags.HideAndDontSave | HideFlags.NotEditable;
            sheet.name = string.IsNullOrEmpty(sheetName) ? "hotreload.g" : sheetName;

            // Prefer StyleSheetImporterImpl.Import(StyleSheet, string) via reflection —
            // public in UnityEditor.UIElements.StyleSheets but not always in our asm refs.
            if (!TryImportViaImporterImpl(sheet, ussText, $"Assets/{sheet.name}.uss"))
            {
                // Fallback: write temp file under Library and import through AssetDatabase.
                ImportViaTempAsset(sheet, ussText, sheet.name);
            }

            return sheet;
        }

        static bool TryImportViaImporterImpl(StyleSheet sheet, string ussText, string fakePath)
        {
            try
            {
                var importerType = Type.GetType(
                    "UnityEditor.UIElements.StyleSheets.StyleSheetImporterImpl, UnityEditor.UIElementsModule")
                    ?? Type.GetType(
                        "UnityEditor.UIElements.StyleSheets.StyleSheetImporterImpl, UnityEditor");

                if (importerType == null) return false;

                var importer = Activator.CreateInstance(importerType);
                var import = importerType.GetMethod(
                    "Import",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(StyleSheet), typeof(string) },
                    null);

                if (import == null) return false;

                // Set m_AssetPath if present (avoids null path warnings)
                var pathField = importerType.GetField("m_AssetPath",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                pathField?.SetValue(importer, fakePath);

                import.Invoke(importer, new object[] { sheet, ussText });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SusUssFromString] ImporterImpl failed: {ex.Message}");
                return false;
            }
        }

        static void ImportViaTempAsset(StyleSheet target, string ussText, string sheetName)
        {
            var dir = "Library/SharqHotReload";
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var safe = Sanitize(sheetName);
            var path = $"{dir}/{safe}.uss";
            System.IO.File.WriteAllText(path, ussText, Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (loaded == null)
                throw new InvalidOperationException($"Failed to import temp USS at {path}");

            // Copy rules by re-serializing is not available; swap by using loaded sheet
            // directly — caller uses returned sheet from Create, so replace target content
            // by returning loaded. We mutate: destroy empty target usage by copying name.
            // Actually Create already returned `target` — replace via EditorUtility.CopySerialized
            EditorUtility.CopySerialized(loaded, target);
            target.name = sheetName;
            target.hideFlags = HideFlags.HideAndDontSave | HideFlags.NotEditable;
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "hotreload";
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-' ? c : '_');
            return sb.ToString();
        }
    }
}
#endif
