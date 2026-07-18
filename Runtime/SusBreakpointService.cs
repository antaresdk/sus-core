using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Reactive breakpoint service — tracks a root's logical width and updates
    /// Current (Prop&lt;Breakpoint&gt;) + IsMobile/IsTablet/IsDesktop (Computed&lt;bool&gt;).
    ///
    /// Two consumption paths, both share the same per-root instance (keyed by
    /// the cascade root from <see cref="SusBootstrap.LoadTokenCascade"/>):
    ///   • Components inject reactive state via <see cref="For(SusComponent)"/>.
    ///   • <see cref="Attach(VisualElement)"/> (called from SusBootstrap.Mount)
    ///     wires GeometryChangedEvent and swaps the .breakpoint-* class on the
    ///     root so USS token overrides (.breakpoint-sm { --sk-* }) apply live.
    /// </summary>
    public class SusBreakpointService
    {
        private static readonly Dictionary<VisualElement, SusBreakpointService> s_cache = new();
        private static readonly SusBreakpointService s_nullFallback = new();

        /// <summary>Get-or-create the shared service for a root VisualElement.</summary>
        public static SusBreakpointService For(VisualElement root)
        {
            if (root == null)
                return s_nullFallback; // shared fallback — never creates orphans

            if (s_cache.TryGetValue(root, out var existing))
                return existing;

            // Prefer already-attached cascade root on the same panel.
            var cascaded = SusBootstrap.TokenCascadeRoot;
            if (cascaded != null && cascaded.panel != null
                && (root.panel == null || root.panel == cascaded.panel)
                && s_cache.TryGetValue(cascaded, out var onCascade))
                return onCascade;

            var bp = new SusBreakpointService();
            s_cache[root] = bp;
            return bp;
        }

        /// <summary>Get-or-create the shared service for a component's cascade root.</summary>
        public static SusBreakpointService For(SusComponent component)
        {
            if (component == null)
                return s_nullFallback;

            // Walk up — Attach caches the cascade root (UIDocument.root), not panel.visualTree.
            for (VisualElement el = component; el != null; el = el.parent)
            {
                if (s_cache.TryGetValue(el, out var existing))
                    return existing;
            }

            var cascaded = SusBootstrap.TokenCascadeRoot;
            if (cascaded != null && component.panel != null && cascaded.panel == component.panel
                && s_cache.TryGetValue(cascaded, out var onCascade))
                return onCascade;

            if (component.panel == null)
                return s_nullFallback;

            // UIDocument content root is usually a direct child of visualTree.
            var vt = component.panel.visualTree;
            if (vt != null)
            {
                for (int i = 0; i < vt.hierarchy.childCount; i++)
                {
                    var child = vt.hierarchy[i];
                    if (s_cache.TryGetValue(child, out var onChild))
                        return onChild;
                }
                if (s_cache.TryGetValue(vt, out var onVt))
                    return onVt;
            }

            return s_nullFallback;
        }

        /// <summary>
        /// Bind the service to a root: track width via GeometryChangedEvent and
        /// keep a .breakpoint-* class in sync so USS token overrides apply.
        /// Idempotent. Returns the shared per-root service.
        /// </summary>
        public static SusBreakpointService Attach(VisualElement root)
        {
            var svc = For(root);
            svc.Bind(root);
            return svc;
        }

        /// <summary>Stop tracking a root and drop it from the cache.</summary>
        public static void Detach(VisualElement root)
        {
            if (root == null) return;
            if (s_cache.TryGetValue(root, out var svc))
            {
                svc.Unbind();
                s_cache.Remove(root);
            }
        }

        public Prop<Breakpoint> Current { get; } = new(Breakpoint.Xxl);

        public Computed<bool> IsMobile  { get; }
        public Computed<bool> IsTablet  { get; }
        public Computed<bool> IsDesktop { get; }

        private VisualElement _root;

        public SusBreakpointService()
        {
            IsMobile  = new Computed<bool>(() => Current.Value <= Breakpoint.Md);
            IsTablet  = new Computed<bool>(() => Current.Value == Breakpoint.Md || Current.Value == Breakpoint.Lg);
            IsDesktop = new Computed<bool>(() => Current.Value >= Breakpoint.Xl);
        }

        /// <summary>
        /// Update breakpoint from layout width. When bound to a cascade root, always
        /// uses that root's width (never a child component's width — would flip USS
        /// .breakpoint-* incorrectly).
        /// </summary>
        public void UpdateFromElement(VisualElement el)
        {
            if (_root != null)
            {
                var w = _root.resolvedStyle.width;
                if (!float.IsNaN(w) && w > 0f)
                    Update(w);
                return;
            }

            if (el?.panel == null) return;
            Update(el.resolvedStyle.width);
        }

        /// <summary>Map a logical width to a breakpoint and sync the root class.</summary>
        public void Update(float logicalWidth)
        {
            Current.Value = logicalWidth switch
            {
                <= 640  => Breakpoint.Sm,
                <= 1024 => Breakpoint.Md,
                <= 1440 => Breakpoint.Lg,
                <= 1920 => Breakpoint.Xl,
                _       => Breakpoint.Xxl
            };
            ApplyClass();
        }

        /// <summary>USS class name for a breakpoint (Tailwind naming, Xxl → "2xl").</summary>
        public static string ClassFor(Breakpoint bp) => bp switch
        {
            Breakpoint.Sm => "breakpoint-sm",
            Breakpoint.Md => "breakpoint-md",
            Breakpoint.Lg => "breakpoint-lg",
            Breakpoint.Xl => "breakpoint-xl",
            _             => "breakpoint-2xl"
        };

        // ─── Root binding (class-swap) ───────────────────────────────────

        private void Bind(VisualElement root)
        {
            if (root == null) return;
            if (_root == root)
            {
                RefreshFromRoot();
                return;
            }

            Unbind();
            _root = root;
            // Keep cache key == bound root (For may have created under another key first).
            s_cache[root] = this;

            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            root.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            RefreshFromRoot();
        }

        private void Unbind()
        {
            if (_root == null) return;
            _root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _root.UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            _root.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            s_cache.Remove(_root);
            _root = null;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.target is VisualElement)
                RefreshFromRoot();
        }

        private void OnAttachToPanel(AttachToPanelEvent evt) => RefreshFromRoot();

        private void OnDetachFromPanel(DetachFromPanelEvent evt) => Unbind();

        private void RefreshFromRoot()
        {
            if (_root == null) return;
            var w = _root.resolvedStyle.width;
            if (!float.IsNaN(w) && w > 0f)
                Update(w);
            else
                ApplyClass(); // default until first layout pass
        }

        private void ApplyClass()
        {
            if (_root == null) return;

            foreach (Breakpoint bp in Enum.GetValues(typeof(Breakpoint)))
                _root.RemoveFromClassList(ClassFor(bp));

            _root.AddToClassList(ClassFor(Current.Value));
        }
    }

    public enum Breakpoint : int
    {
        Sm  = 640,
        Md  = 1024,
        Lg  = 1440,
        Xl  = 1920,
        Xxl = 2560
    }
}
