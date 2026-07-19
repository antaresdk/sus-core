using System;
using System.Collections.Generic;
using System.Reflection;
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
    ///     wires geometry + a light poll so USS .breakpoint-* classes stay in sync
    ///     when the Game view / panel is resized (Editor often skips GeometryChanged
    ///     on the content root alone).
    /// </summary>
    public class SusBreakpointService
    {
        private static readonly Dictionary<VisualElement, SusBreakpointService> s_cache = new();
        private static readonly SusBreakpointService s_nullFallback = new();

        /// <summary>Get-or-create the shared service for a root VisualElement.</summary>
        public static SusBreakpointService For(VisualElement root)
        {
            if (root == null)
                return s_nullFallback;

            if (s_cache.TryGetValue(root, out var existing))
                return existing;

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

            for (VisualElement el = component; el != null; el = el.parent)
            {
                if (s_cache.TryGetValue(el, out var existing))
                    return existing;
            }

            var cascaded = SusBootstrap.TokenCascadeRoot;
            if (cascaded != null && component.panel != null && cascaded.panel == component.panel)
            {
                if (s_cache.TryGetValue(cascaded, out var onCascade))
                    return onCascade;
                // Heal: cascade root exists but Bind was lost (e.g. Detach) — re-attach.
                return Attach(cascaded);
            }

            if (component.panel == null)
                return s_nullFallback;

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
        /// Bind the service to a root: track width and keep .breakpoint-* in sync.
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

        /// <summary>When set, width polling is ignored and this breakpoint is forced.</summary>
        public Breakpoint? Override { get; private set; }

        private VisualElement _root;
        private VisualElement _panelTree;
        private IVisualElementScheduledItem _poll;
        private float _lastWidth = float.NaN;

        public SusBreakpointService()
        {
            IsMobile  = new Computed<bool>(() => Current.Value <= Breakpoint.Md);
            IsTablet  = new Computed<bool>(() => Current.Value == Breakpoint.Md || Current.Value == Breakpoint.Lg);
            IsDesktop = new Computed<bool>(() => Current.Value >= Breakpoint.Xl);
        }

        /// <summary>
        /// Update breakpoint from layout width. When bound to a cascade root, always
        /// uses that root's width (never a child component's width — would flip USS
        /// .breakpoint-* incorrectly). Same source path as the old SusResolutionService:
        /// cascadeRoot.resolvedStyle.width on GeometryChanged.
        /// </summary>
        public void UpdateFromElement(VisualElement el)
        {
            if (_root != null)
            {
                var w = _root.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 0f)
                    w = _root.layout.width;
                if (!float.IsNaN(w) && w > 0f)
                    Update(w);
                else
                    RefreshFromRoot();
                return;
            }

            // Heal unbound service (Detach used to kill ApplyClass while Resolution
            // kept working because it always received the cascade root explicitly).
            var cascade = SusBootstrap.TokenCascadeRoot
                ?? (el != null ? SusThemeService.ResolveCascadeRoot(el) : null);
            if (cascade != null)
            {
                Attach(cascade);
                return;
            }

            if (el?.panel == null) return;
            var ew = el.resolvedStyle.width;
            if (!float.IsNaN(ew) && ew > 0f)
                Update(ew);
        }

        /// <summary>
        /// Force a breakpoint (Storybook / QA). Pass <c>null</c> to resume auto from width.
        /// </summary>
        public void SetOverride(Breakpoint? breakpoint)
        {
            Override = breakpoint;
            if (breakpoint.HasValue)
            {
                var prev = Current.Value;
                Current.Value = breakpoint.Value;
                _lastWidth = float.NaN;
                ApplyClass(force: true);
                if (prev != Current.Value)
                    Debug.Log($"[SusBreakpoint] override {prev} → {Current.Value}");
            }
            else
            {
                _lastWidth = float.NaN;
                RefreshFromRoot();
            }
        }

        /// <summary>Map a logical width to a breakpoint and sync the root class.</summary>
        public void Update(float logicalWidth)
        {
            if (float.IsNaN(logicalWidth) || logicalWidth <= 0f)
                return;

            if (Override.HasValue)
            {
                if (Current.Value != Override.Value || _root == null
                    || !_root.ClassListContains(ClassFor(Override.Value)))
                {
                    Current.Value = Override.Value;
                    ApplyClass(force: true);
                }
                return;
            }

            // Ignore sub-pixel noise from layout thrash.
            if (!float.IsNaN(_lastWidth) && Mathf.Abs(_lastWidth - logicalWidth) < 0.5f
                && _root != null && _root.ClassListContains(ClassFor(Current.Value)))
                return;

            _lastWidth = logicalWidth;

            var prev = Current.Value;
            Current.Value = logicalWidth switch
            {
                <= 640  => Breakpoint.Sm,
                <= 1024 => Breakpoint.Md,
                <= 1440 => Breakpoint.Lg,
                <= 1920 => Breakpoint.Xl,
                _       => Breakpoint.Xxl
            };
            ApplyClass();

            if (prev != Current.Value)
                Debug.Log($"[SusBreakpoint] {prev} → {Current.Value} @ {logicalWidth:F0}px (class={ClassFor(Current.Value)})");
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
                EnsurePanelHook();
                EnsurePoll();
                RefreshFromRoot();
                return;
            }

            Unbind();
            _root = root;
            s_cache[root] = this;

            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            root.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            // Do NOT Unbind on DetachFromPanel — Editor/UIDocument briefly detaches
            // during domain reload / panel rebuild; Unbind left the service dead until
            // the next Mount. Detach() is the explicit cleanup path.

            EnsurePanelHook();
            EnsurePoll();
            RefreshFromRoot();
        }

        private void Unbind()
        {
            if (_poll != null)
            {
                _poll.Pause();
                _poll = null;
            }

            if (_panelTree != null)
            {
                _panelTree.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
                _panelTree = null;
            }

            if (_root == null) return;
            _root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _root.UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            s_cache.Remove(_root);
            _root = null;
            _lastWidth = float.NaN;
        }

        private void EnsurePanelHook()
        {
            if (_root?.panel == null) return;
            var vt = _root.panel.visualTree;
            if (vt == null || vt == _root || vt == _panelTree) return;

            if (_panelTree != null)
                _panelTree.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);

            _panelTree = vt;
            // Panel visualTree is what actually resizes with the Game view in Editor.
            _panelTree.RegisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
        }

        private void EnsurePoll()
        {
            if (_root == null || _poll != null) return;
            // Fallback: Game view drag often skips GeometryChanged on content root.
            _poll = _root.schedule.Execute(RefreshFromRoot).Every(50);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // Prefer authoritative Game/Screen width over this element's rect —
            // cascade root rect can stay stale while Game view changes.
            RefreshFromRoot();
        }

        private void OnPanelGeometryChanged(GeometryChangedEvent evt) => RefreshFromRoot();

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            EnsurePanelHook();
            EnsurePoll();
            RefreshFromRoot();
        }

        private void RefreshFromRoot()
        {
            if (_root == null) return;

            float w = ReadLogicalWidth();
            if (!float.IsNaN(w) && w > 0f)
                Update(w);
            else
                ApplyClass();
        }

        /// <summary>
        /// Width source — match deleted SusResolutionService:
        /// primary = cascade root <c>resolvedStyle.width</c> (updates with panel layout).
        /// Screen / GameView only when root not laid out yet.
        /// </summary>
        private float ReadLogicalWidth()
        {
            if (_root != null)
            {
                float w = _root.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 0f)
                    w = _root.layout.width;
                if (!float.IsNaN(w) && w > 0f)
                    return w;
            }

            if (_panelTree != null)
            {
                float w = _panelTree.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 0f)
                    w = _panelTree.layout.width;
                if (!float.IsNaN(w) && w > 0f)
                    return w;
            }

            float gv = TryGetEditorGameViewWidth();
            if (!float.IsNaN(gv) && gv > 0f)
                return gv;

            if (Application.isPlaying && Screen.width > 0)
                return Screen.width;

            return float.NaN;
        }

        private static float TryGetEditorGameViewWidth()
        {
#if UNITY_EDITOR
            try
            {
                // Unity 2022.2+ — actual Game view render resolution while playing.
                var playModeWindow = Type.GetType("UnityEditor.PlayModeWindow,UnityEditor");
                var getRes = playModeWindow?.GetMethod(
                    "GetRenderingResolution",
                    BindingFlags.Public | BindingFlags.Static);
                if (getRes != null)
                {
                    object[] args = { 0u, 0u };
                    getRes.Invoke(null, args);
                    if (args[0] is uint uw && uw > 0)
                        return uw;
                }

                var gameView = Type.GetType("UnityEditor.GameView,UnityEditor");
                var getSize = gameView?.GetMethod(
                    "GetSizeOfMainGameView",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (getSize != null)
                {
                    var size = (Vector2)getSize.Invoke(null, null);
                    if (size.x > 0f)
                        return size.x;
                }
            }
            catch
            {
                // Reflection best-effort — fall through to Screen / layout.
            }
#endif
            return float.NaN;
        }

        private void ApplyClass(bool force = false)
        {
            if (_root == null) return;

            var next = ClassFor(Current.Value);
            if (!force && _root.ClassListContains(next))
                return;

            foreach (Breakpoint bp in Enum.GetValues(typeof(Breakpoint)))
                _root.RemoveFromClassList(ClassFor(bp));

            _root.AddToClassList(next);

            // Nudge style resolution so var(--sk-*) consumers pick up new tokens.
            _root.MarkDirtyRepaint();
            ForceCustomPropertyRefresh();
        }

        private void ForceCustomPropertyRefresh()
        {
            if (_root == null) return;

            // Toggle a no-op class so UITK re-evaluates inherited custom properties.
            const string nudge = "sus-bp-nudge";
            _root.AddToClassList(nudge);
            _root.RemoveFromClassList(nudge);

            // Re-touch density classes (same element) — forces another style pass.
            SusDensityService.Attach(_root);
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
