using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Strips Unity built-in USS classes from all <see cref="Label"/> elements
    /// and replaces them with <c>sus-label</c> (see <c>_text.uss</c>).
    ///
    /// Unity TSS attaches classes like <c>unity-label</c>, <c>unity-text-element</c>,
    /// which fight theme tokens. We do not keep those classes — layout/color come
    /// from <c>.sus-label</c> (height follows text; flex-shrink:0).
    ///
    /// AttachToPanelEvent is sent ONLY to the attaching element (no bubble/trickle),
    /// so we register a hook on every VisualElement in the watched subtree.
    /// </summary>
    public static class SusLabelClassService
    {
        private static readonly HashSet<VisualElement> s_attachedRoots = new();
        private static readonly HashSet<VisualElement> s_hookedElements = new();
        private static readonly HashSet<VisualElement> s_scanRoots = new();

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_attachedRoots.Clear();
            s_hookedElements.Clear();
            s_scanRoots.Clear();
        }
#endif

        /// <summary>
        /// Attach stripper to a root. Idempotent — safe to call multiple times.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (root == null) return;
            if (!s_attachedRoots.Add(root)) return;

            InstallHooksRecursive(root);

            root.RegisterCallback<DetachFromPanelEvent>(OnRootDetachFromPanel);
            EnsurePeriodicScan(root);
        }

        /// <summary>Stop watching a root (does not unhook individual elements).</summary>
        public static void Detach(VisualElement root)
        {
            if (root == null) return;
            if (!s_attachedRoots.Remove(root)) return;

            root.UnregisterCallback<DetachFromPanelEvent>(OnRootDetachFromPanel);
            s_scanRoots.Remove(root);
        }

        /// <summary>
        /// Walk a subtree, hook every VisualElement, strip all Labels.
        /// Call after building dynamic UI (menus, tables, runtime lists).
        /// </summary>
        public static void InstallHooksRecursive(VisualElement root)
        {
            if (root == null) return;

            EnsureHook(root);

            if (root is Label label)
                StripUnityClasses(label);

            int count = root.hierarchy.childCount;
            for (int i = 0; i < count; i++)
                InstallHooksRecursive(root.hierarchy[i]);
        }

        /// <summary>
        /// Remove all <c>unity-*</c> classes from a single label and attach
        /// <c>sus-label</c> so layout/theme come from SUS USS (height by text,
        /// flex-shrink:0) — not from Unity Default Theme.
        /// </summary>
        public static void StripUnityClasses(Label label)
        {
            if (label == null) return;

            var toRemove = new List<string>();
            foreach (var cls in label.GetClasses())
            {
                if (cls.StartsWith("unity-"))
                    toRemove.Add(cls);
            }

            foreach (var cls in toRemove)
                label.RemoveFromClassList(cls);

            label.AddToClassList("sus-label");
        }

        private static void EnsureHook(VisualElement ve)
        {
            if (ve == null) return;
            if (!s_hookedElements.Add(ve)) return;

            ve.RegisterCallback<AttachToPanelEvent>(OnElementAttachToPanel);
        }

        private static void OnElementAttachToPanel(AttachToPanelEvent evt)
        {
            if (evt.target is not VisualElement ve) return;

            // Element just entered a panel — hook its subtree and strip labels.
            InstallHooksRecursive(ve);
        }

        private static void OnRootDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (evt.target is VisualElement ve && s_attachedRoots.Contains(ve))
                Detach(ve);
        }

        /// <summary>
        /// Catches Labels added at runtime after initial hook pass
        /// (AttachToPanelEvent does not bubble to ancestors).
        /// </summary>
        private static void EnsurePeriodicScan(VisualElement root)
        {
            if (root == null) return;
            if (!s_scanRoots.Add(root)) return;

            root.schedule.Execute(() =>
            {
                if (root.panel == null) return;
                ScanUnhookedLabels(root);
            }).Every(64);
        }

        private static void ScanUnhookedLabels(VisualElement ve)
        {
            if (ve == null) return;

            if (ve is Label label && !s_hookedElements.Contains(ve))
            {
                EnsureHook(ve);
                StripUnityClasses(label);
            }

            int count = ve.hierarchy.childCount;
            for (int i = 0; i < count; i++)
                ScanUnhookedLabels(ve.hierarchy[i]);
        }
    }
}
