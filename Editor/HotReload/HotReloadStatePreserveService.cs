#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// E2b: preserve SusComponent Prop state across domain reload while Playing.
    /// Requires Unity preference: Edit → Preferences → General →
    /// "Script Changes While Playing" = Recompile And Continue Playing.
    ///
    /// Captures UIDocument scene trees and EditorWindow Sus trees (same roots as USS hot reload).
    /// Opt-out via Assets/sus.config.json: <c>"HotReloadStatePreserve": false</c>
    /// </summary>
    [InitializeOnLoad]
    public static class HotReloadStatePreserveService
    {
        const string SessionKey = "Sharq.HotReload.PropSnapshot";

        static HotReloadStatePreserveService()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
        }

        static bool IsEnabled => SusConfig.Instance.HotReloadStatePreserve;

        static void OnBeforeReload()
        {
            if (!IsEnabled || !EditorApplication.isPlaying)
            {
                SessionState.EraseString(SessionKey);
                return;
            }

            try
            {
                var snap = CaptureAll();
                if (snap.Count == 0)
                {
                    SessionState.EraseString(SessionKey);
                    return;
                }

                SessionState.SetString(SessionKey, SusComponentSnapshot.SerializeEntries(snap));
                Debug.Log($"[HotReloadStatePreserve] captured {snap.Count} component(s) " +
                          "(UIDocument + EditorWindow) before domain reload");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HotReloadStatePreserve] capture failed: {ex.Message}");
                SessionState.EraseString(SessionKey);
            }
        }

        static void OnAfterReload()
        {
            if (!IsEnabled) return;
            EditorApplication.delayCall += RestoreDeferred;
        }

        static void RestoreDeferred()
        {
            if (!EditorApplication.isPlaying) return;

            var json = SessionState.GetString(SessionKey, "");
            SessionState.EraseString(SessionKey);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var snap = SusComponentSnapshot.DeserializeEntries(json);
                RestoreAll(snap);
                Debug.Log($"[HotReloadStatePreserve] restored {snap.Count} component(s) after domain reload");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HotReloadStatePreserve] restore failed: {ex.Message}");
            }
        }

        static List<SusComponentSnapshot.Entry> CaptureAll()
        {
            var snap = SusComponentSnapshot.CaptureAllDocuments();
            foreach (var root in EnumerateEditorWindowRoots())
                snap.AddRange(SusComponentSnapshot.Capture(root));
            return snap;
        }

        static void RestoreAll(List<SusComponentSnapshot.Entry> snap)
        {
            SusComponentSnapshot.RestoreAllDocuments(snap);
            foreach (var root in EnumerateEditorWindowRoots())
                SusComponentSnapshot.Restore(root, snap);
        }

        static IEnumerable<VisualElement> EnumerateEditorWindowRoots()
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in windows)
            {
                var root = window?.rootVisualElement;
                if (root != null)
                    yield return root;
            }
        }
    }
}
#endif
