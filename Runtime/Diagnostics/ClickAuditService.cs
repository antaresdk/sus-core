using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR || DEVELOPMENT_BUILD

namespace Sharq.Core.Diagnostics
{
    /// <summary>
    /// ClickAuditEntry — record of a registered clickable element.
    /// </summary>
    internal class ClickAuditEntry
    {
        public VisualElement Element;
        public string Description;
        public Rect LastBounds;
        public double LastWarnTime; // debounce
    }

    /// <summary>
    /// Singleton dev service: verifies that EVERY mouse click reaches
    /// registered clickable elements.
    ///
    /// Algorithm:
    /// 1. Intercept ClickEvent at panel level (TrickleDown)
    /// 2. panel.PickAll(point) → real element stack under the cursor
    /// 3. Compare with registered elements whose bounds contain the point
    /// 4. If a registered element is not in the PickAll stack → WARNING
    ///
    /// Activation: automatic in SusBootstrap.Mount() for Editor and Development Build.
    /// </summary>
    public class ClickAuditService
    {
        public static ClickAuditService Instance { get; } = new();

        private readonly List<ClickAuditEntry> _registry = new();
        private readonly HashSet<VisualElement> _transparentOverlays = new();
        private readonly HashSet<VisualElement> _ignoredElements = new();
        private bool _installed;
        private bool _suspended;

        /// <summary>Minimum interval between warnings for one element (seconds).</summary>
        public float DebounceSeconds = 2f;

        // ─── Install ──────────────────────────────────────────────────────────

        /// <summary>
        /// Activates the audit: attaches a global ClickEvent interceptor
        /// to panel.visualTree. Idempotent.
        /// </summary>
        public void Install(IPanel panel)
        {
            if (_installed || panel?.visualTree == null) return;
            _installed = true;

            panel.visualTree.RegisterCallback<ClickEvent>(OnAnyClick, TrickleDown.TrickleDown);
            SusLog.Verbose("[ClickAudit] Installed — will warn on blocked clicks.");
        }

        public void Suspend() => _suspended = true;
        public void Resume() => _suspended = false;

        // ─── Registration ─────────────────────────────────────────────────────

        /// <summary>
        /// Registers an element as "expecting clicks" with a human-readable description.
        /// </summary>
        public void Register(VisualElement element, string description)
        {
            if (element == null) return;

            // Deduplicate
            for (int i = 0; i < _registry.Count; i++)
            {
                if (_registry[i].Element == element) return;
            }

            var entry = new ClickAuditEntry
            {
                Element = element,
                Description = description,
                LastBounds = element.worldBound,
            };

            _registry.Add(entry);

            // Track geometry changes
            element.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            // Layout audit at registration time
            if (element.worldBound.width <= 0 || element.worldBound.height <= 0)
            {
                SusLog.Verbose($"[ClickAudit] '{description}' registered with zero bounds " +
                    $"({element.worldBound.width:F0}×{element.worldBound.height:F0}). " +
                    $"Layout may not be computed yet.");
            }
            if (element.pickingMode == PickingMode.Ignore)
            {
                SusLog.Verbose($"[ClickAudit] '{description}' has pickingMode=Ignore. " +
                    $"Clicks will NOT reach this element.");
            }
        }

        /// <summary>
        /// Removes an element from the audit.
        /// </summary>
        public void Unregister(VisualElement element)
        {
            if (element == null) return;
            element.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _registry.RemoveAll(e => e.Element == element);
        }

        /// <summary>
        /// Marks an overlay element as "transparent" (tooltip, hint).
        /// Such elements are excluded from the check — they are not considered blockers.
        /// </summary>
        public void RegisterTransparentOverlay(VisualElement element, string description)
        {
            if (element == null) return;
            _transparentOverlays.Add(element);
        }

        /// <summary>
        /// Excludes an element from all checks (e.g. modal scrim).
        /// </summary>
        public void IgnoreElement(VisualElement element)
        {
            if (element != null) _ignoredElements.Add(element);
        }

        // ─── Active audit (poll) ─────────────────────────────────────────────

        /// <summary>
        /// Active audit: for each registered element checks that
        /// panel.Pick(center) hits it (or a descendant).
        /// Useful to call periodically or on demand.
        /// </summary>
        public void RunActiveAudit(IPanel panel)
        {
            if (panel == null) return;

            for (int i = _registry.Count - 1; i >= 0; i--)
            {
                var entry = _registry[i];
                if (entry.Element?.panel == null) continue;

                var center = entry.LastBounds.center;
                if (center.x <= 0 && center.y <= 0) continue; // not laid out yet

                var picked = panel.Pick(center);
                bool reached = IsAncestorOf(picked, entry.Element);
                if (!reached)
                {
                    var reason = DiagnoseWhyBlocked(entry.Element, center, panel);
                    SusLog.Verbose($"[ClickAudit] Active: '{entry.Description}' blocked at center. {reason}");
                }
            }
        }

        // ─── Core click interception ─────────────────────────────────────────

        private void OnAnyClick(ClickEvent evt)
        {
            if (_suspended || _registry.Count == 0) return;

            var target = evt.target as VisualElement;
            var panel = target?.panel;
            if (panel == null) return;

            var point = (Vector2)((IPointerEvent)evt).position;

            // 1. Collect candidates — registered elements whose bounds contain the point
            var candidates = new List<ClickAuditEntry>();
            for (int i = 0; i < _registry.Count; i++)
            {
                var entry = _registry[i];
                if (entry.Element?.panel == null) continue;
                if (_ignoredElements.Contains(entry.Element)) continue;
                if (entry.LastBounds.Contains(point))
                    candidates.Add(entry);
            }

            if (candidates.Count == 0) return; // click in empty area — normal

            // 2. Real stack via PickAll
            var picked = new List<VisualElement>();
            panel.PickAll(point, picked);

            // 3. Compare: is the candidate (or its descendant) in the stack?
            var now = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                bool reached = false;
                for (int j = picked.Count - 1; j >= 0; j--)
                {
                    if (IsAncestorOf(picked[j], candidate.Element))
                    {
                        reached = true;
                        break;
                    }
                }

                if (!reached)
                {
                    // Debounce: at most once per DebounceSeconds for one element
                    if (now - candidate.LastWarnTime < DebounceSeconds) continue;
                    candidate.LastWarnTime = now;

                    var reason = DiagnoseWhyBlocked(candidate.Element, point, panel);
                    var topPicked = picked.Count > 0 ? picked[picked.Count - 1] : null;
                    SusLog.Warn(
                        $"[ClickAudit] Click at ({point.x:F0},{point.y:F0}) " +
                        $"did NOT reach '{candidate.Description}'. " +
                        $"Top: '{topPicked?.GetType().Name}' " +
                        $"(name='{topPicked?.name}', class='{topPicked?.GetClasses().FirstOrDefault()}'). " +
                        $"Reason: {reason}");
                }
            }
        }

        // ─── Diagnosis ───────────────────────────────────────────────────────

        private string DiagnoseWhyBlocked(VisualElement el, Vector2 point, IPanel panel)
        {
            if (el == null) return "Element is null";
            if (el.panel == null) return "Not attached to panel";

            var wb = el.worldBound;
            if (wb.width <= 0 || wb.height <= 0)
                return $"Zero-size bounds ({wb.width:F0}×{wb.height:F0}) — UITK skips ContainsPoint";

            if (!el.enabledInHierarchy)
                return "Disabled in hierarchy";

            if (el.resolvedStyle.display == DisplayStyle.None)
                return "display: none";

            if (!el.visible)
                return "visible: false";

            // Check if completely outside panel
            var panelBounds = panel.visualTree.worldBound;
            if (!panelBounds.Overlaps(wb))
                return $"Outside panel bounds (element: {wb}, panel: {panelBounds})";

            // Picking mode check (after all the above because Ignore with children still works)
            if (el.pickingMode == PickingMode.Ignore && el.childCount == 0)
                return "pickingMode=Ignore with no children — excluded from Pick";

            // Default: covered by another element
            var blocker = panel.Pick(point);
            if (blocker != null && !IsAncestorOf(blocker, el))
            {
                // Check if blocker is a known transparent overlay
                if (IsTransparentOverlay(blocker))
                    return $"Covered by transparent overlay '{blocker.GetType().Name}' (should be OK if children have pickingMode=Ignore)";

                return $"Covered by '{blocker.GetType().Name}' " +
                    $"(name='{blocker.name}', class='{blocker.GetClasses().FirstOrDefault()}')";
            }

            return "Unknown — not in PickAll stack, no obvious blocker";
        }

        private bool IsTransparentOverlay(VisualElement el)
        {
            var cur = el;
            while (cur != null)
            {
                if (_transparentOverlays.Contains(cur)) return true;
                cur = cur.parent;
            }
            return false;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            var changed = evt.target as VisualElement;
            if (changed == null) return;
            for (int i = 0; i < _registry.Count; i++)
            {
                if (_registry[i].Element == changed)
                    _registry[i].LastBounds = changed.worldBound;
            }
        }

        /// <summary>
        /// Checks whether container is an ancestor of target (or the same element).
        /// </summary>
        private static bool IsAncestorOf(VisualElement target, VisualElement container)
        {
            if (target == null || container == null) return false;
            var cur = target;
            while (cur != null)
            {
                if (cur == container) return true;
                cur = cur.parent;
            }
            return false;
        }

        // ─── Report ───────────────────────────────────────────────────────────

        public void DumpReport()
        {
            SusLog.Verbose($"[ClickAudit] === Report ===\n" +
                $"Registered clickables: {_registry.Count}\n" +
                $"Transparent overlays: {_transparentOverlays.Count}\n" +
                $"Ignored elements: {_ignoredElements.Count}\n" +
                $"Installed: {_installed}, Suspended: {_suspended}");

            for (int i = 0; i < _registry.Count; i++)
            {
                var e = _registry[i];
                var wb = e.LastBounds;
                SusLog.Verbose($"  [{i}] '{e.Description}' " +
                    $"bounds=({wb.x:F0},{wb.y:F0})-({wb.xMax:F0},{wb.yMax:F0}) " +
                    $"size={wb.width:F0}×{wb.height:F0} " +
                    $"pickingMode={e.Element?.pickingMode} " +
                    $"enabled={e.Element?.enabledInHierarchy}");
            }
        }
    }
}

#endif
