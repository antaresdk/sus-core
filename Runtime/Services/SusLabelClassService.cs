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
        /// <summary>The one product class this service paints onto a label.</summary>
        public const string LabelClass = "sus-label";

        private static readonly HashSet<VisualElement> s_attachedRoots = new();
        private static readonly HashSet<VisualElement> s_hookedElements = new();
        private static readonly HashSet<VisualElement> s_scanRoots = new();

        // Subtree boundaries (card T-3388). CONFIG, not panel state: registered once at load by
        // the owner of the subtree (see SusStorybookShellIsolation) and deliberately NOT cleared
        // by ResetStatics - that reset runs on every play start, its order against other
        // SubsystemRegistration hooks is undefined, and a cleared boundary would silently start
        // painting the shell again on a play without domain reload.
        private static readonly HashSet<string> s_excludedClasses = new();
        private static readonly HashSet<string> s_islandClasses = new();

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_attachedRoots.Clear();
            s_hookedElements.Clear();
            s_scanRoots.Clear();
            // s_excludedClasses / s_islandClasses are config, not state - see the field comment.
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

        // --- Subtree boundaries (card T-3388) ---------------------------------

        /// <summary>
        /// Declares a subtree the service must NOT paint: under an element carrying
        /// <paramref name="ussClass"/> a label still loses its <c>unity-*</c> classes (Unity
        /// Default Theme must not reach in either) but does NOT get <see cref="LabelClass"/> -
        /// so that subtree carries no product class at all.
        ///
        /// This exists for a host that is NOT the product on display but merely frames it: the
        /// storybook shell. The shell has its own stylesheet on its own names, and a kit/core
        /// class on a shell element kept the kit cascade physically able to reach the viewer
        /// (314 of 517 shell elements, report ux-reviewer 2026-09-11, plan
        /// ARCH-20260911-STORYBOOK-SHELL D2/D3/D20).
        ///
        /// Additive: with no class registered every walk behaves exactly as before. Idempotent.
        /// </summary>
        public static void ExcludeSubtreeClass(string ussClass)
        {
            if (string.IsNullOrEmpty(ussClass)) return;
            s_excludedClasses.Add(ussClass);
        }

        /// <summary>
        /// Declares a PRODUCT ISLAND inside an excluded subtree - the storybook stage canvas,
        /// where the component on display lives. Inside the island painting resumes, because
        /// what is mounted there IS the product and must look exactly like it looks in an app.
        ///
        /// A <see cref="SusComponent"/> is an island by construction (it paints its own labels
        /// from its constructor); this registry covers what a story puts on the stage AROUND the
        /// instance - raw labels of a decoration, and a story whose subject is not a component.
        /// </summary>
        public static void IncludeSubtreeClass(string ussClass)
        {
            if (string.IsNullOrEmpty(ussClass)) return;
            s_islandClasses.Add(ussClass);
        }

        /// <summary>Drops every registered boundary (tests).</summary>
        public static void ClearSubtreeBoundaries()
        {
            s_excludedClasses.Clear();
            s_islandClasses.Clear();
        }

        /// <summary>
        /// True when <paramref name="element"/> sits in a subtree the service does not paint.
        /// NEAREST boundary wins: a product island (or any <see cref="SusComponent"/>) below an
        /// excluded root is painted again.
        /// </summary>
        public static bool IsInExcludedSubtree(VisualElement element)
        {
            if (element == null) return false;
            if (s_excludedClasses.Count == 0) return false;

            for (var p = element; p != null; p = p.hierarchy.parent)
            {
                if (IsIsland(p)) return false;
                if (HasAnyClass(p, s_excludedClasses)) return true;
            }
            return false;
        }

        private static bool IsIsland(VisualElement ve) =>
            ve is SusComponent || (s_islandClasses.Count != 0 && HasAnyClass(ve, s_islandClasses));

        private static bool HasAnyClass(VisualElement ve, HashSet<string> classes)
        {
            foreach (var cls in classes)
            {
                if (ve.ClassListContains(cls)) return true;
            }
            return false;
        }

        /// <summary>Context of a CHILD, given the context of its parent (one step of the walk).</summary>
        private static bool NextContext(VisualElement child, bool excluded)
        {
            if (excluded) return !IsIsland(child);
            return s_excludedClasses.Count != 0 && HasAnyClass(child, s_excludedClasses);
        }

        /// <summary>
        /// Walk a subtree, hook every VisualElement, strip all Labels.
        /// Call after building dynamic UI (menus, tables, runtime lists).
        ///
        /// The walk carries the boundary context (card T-3388): an EXPLICIT call starts as
        /// product unless the element itself sits under an excluded root, so a component asking
        /// for its own labels always gets them - wherever it is mounted.
        /// </summary>
        public static void InstallHooksRecursive(VisualElement root)
        {
            if (root == null) return;
            InstallHooksRecursive(root, IsInExcludedSubtree(root));
        }

        private static void InstallHooksRecursive(VisualElement root, bool excluded)
        {
            if (root == null) return;

            EnsureHook(root);

            if (root is Label label)
                ApplyLabelClasses(label, excluded);

            int count = root.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = root.hierarchy[i];
                InstallHooksRecursive(child, NextContext(child, excluded));
            }
        }

        /// <summary>
        /// Remove all <c>unity-*</c> classes from a single label and attach
        /// <c>sus-label</c> so layout/theme come from SUS USS (height by text,
        /// flex-shrink:0) — not from Unity Default Theme.
        /// </summary>
        public static void StripUnityClasses(Label label) => ApplyLabelClasses(label, false);

        /// <summary>
        /// Removes every <c>unity-*</c> class, then either adds <see cref="LabelClass"/> (product)
        /// or removes it (<paramref name="excluded"/> - a subtree declared via
        /// <see cref="ExcludeSubtreeClass"/>). Removing is not cosmetic: it makes the pass
        /// idempotent for a label that was painted before its boundary was declared.
        /// </summary>
        private static void ApplyLabelClasses(Label label, bool excluded)
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

            if (excluded)
                label.RemoveFromClassList(LabelClass);
            else
                label.AddToClassList(LabelClass);
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

            // Element just entered a panel — hook its subtree and strip labels. The context is
            // resolved from its ANCESTORS: this event carries no walk state, and without the
            // lookup a shell label that arrives late would be painted as product (T-3388).
            InstallHooksRecursive(ve, IsInExcludedSubtree(ve));
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
                ScanUnhookedLabels(root, IsInExcludedSubtree(root));
            }).Every(64);
        }

        private static void ScanUnhookedLabels(VisualElement ve, bool excluded)
        {
            if (ve == null) return;

            if (ve is Label label && !s_hookedElements.Contains(ve))
            {
                EnsureHook(ve);
                ApplyLabelClasses(label, excluded);
            }

            int count = ve.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = ve.hierarchy[i];
                ScanUnhookedLabels(child, NextContext(child, excluded));
            }
        }
    }
}
