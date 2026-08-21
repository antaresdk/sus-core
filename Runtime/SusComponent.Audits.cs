using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Dev-only audit hooks for <see cref="SusComponent"/> (Editor / Development Build).
    /// Kept in a partial so the main constructor stays short.
    /// </summary>
    public abstract partial class SusComponent
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private string _clickAuditDescription;
        private double _lastClickTime;
        private int _remountCount;
        private double _remountWindowStart;
        private int _layoutReentryCount;
        private double _layoutReentryWindow;
        private bool _layoutReentryWarnedThisWindow;
#endif

        // ─── Click Audit API (always present; bodies Editor / Development Build only) ─

        /// <summary>
        /// Sets a human-readable description of the component for ClickAuditService.
        /// Call once in Created(). When set, every ClickEvent on this element
        /// is checked by the audit (Editor / Development Build only).
        ///
        /// Example:
        /// <code>
        /// protected override void Created()
        /// {
        ///     SetClickAuditDescription("MainMenu.FightButton");
        ///     // ...
        /// }
        /// </code>
        /// </summary>
        protected void SetClickAuditDescription(string description)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _clickAuditDescription = description;
#endif
        }

        /// <summary>
        /// Logs a warning if the click was blocked by a guard condition.
        /// Call inside a ClickEvent handler on early return.
        /// No-op outside Editor / Development Build.
        ///
        /// Example:
        /// if (Disabled.Value) {
        ///     AuditClickBlocked("Disabled");
        ///     return;
        /// }
        /// </summary>
        protected void AuditClickBlocked(string reason)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var desc = _clickAuditDescription ?? GetType().Name;
            SusLog.Verbose($"[CallbackAudit] '{desc}' click blocked: {reason}");
#else
            _ = reason;
#endif
        }

        /// <summary>
        /// Marks the start of a click handler for duration audit.
        /// Returns 0 outside Editor / Development Build.
        /// </summary>
        protected double AuditClickStart()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return UnityEngine.Time.realtimeSinceStartupAsDouble;
#else
            return 0d;
#endif
        }

        /// <summary>
        /// Logs if the handler ran but took longer than the threshold ms.
        /// No-op outside Editor / Development Build.
        /// </summary>
        protected void AuditClickEnd(double startTime, string action = "OnClick")
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var elapsed = (UnityEngine.Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;
            if (elapsed > 50.0)
            {
                var desc = _clickAuditDescription ?? GetType().Name;
                SusLog.Verbose($"[CallbackAudit] '{desc}' {action} took {elapsed:F1}ms");
            }
#else
            _ = startTime;
            _ = action;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Registers constructor-time dev audits (bounds, tap target, perf, debounce,
        /// idle guard, overflow). Called from the SusComponent constructor.
        /// </summary>
        private void RegisterDevAudits()
        {
            // Auto-register for click audit if description was set in Build()
            if (!string.IsNullOrEmpty(_clickAuditDescription))
                Diagnostics.ClickAuditService.Instance.Register(this, _clickAuditDescription);

            // Deferred bounds audit — check that element has non-zero size after layout
            schedule.Execute(() =>
            {
                if (panel == null) return;
                var wb = worldBound;
                if (wb.width <= 0 || wb.height <= 0)
                {
                    // Decorative (Ignore) or collapsed (display:none self/ancestor) — not a hit-target bug.
                    if (pickingMode == PickingMode.Ignore) return;
                    if (resolvedStyle.display == DisplayStyle.None) return;
                    for (var p = parent; p != null; p = p.parent)
                    {
                        if (p.resolvedStyle.display == DisplayStyle.None)
                            return;
                    }
                    SusLog.Verbose($"[BoundsAudit] '{GetType().Name}' has zero bounds after mount " +
                        $"({wb.width:F0}×{wb.height:F0}). display={resolvedStyle.display}, " +
                        $"visible={visible}, pickingMode={pickingMode}. " +
                        $"Clicks will NOT reach this element.");
                }

                // ClickTargetSizeAudit: for interactive elements, check minimum tap target
                if (!string.IsNullOrEmpty(_clickAuditDescription))
                {
                    if (wb.width > 0 && wb.height > 0 && (wb.width < 30 || wb.height < 30))
                        SusLog.Verbose($"[ClickTargetAudit] '{_clickAuditDescription}' " +
                            $"tap target is small ({wb.width:F0}×{wb.height:F0}px). " +
                            $"HIG recommends ≥44×44. Consider padding or min-size.");
                }
            }).StartingIn(150); // 2-3 frames for layout

            // PerformanceAudit — warn if element tree is too deep / too many children
            schedule.Execute(() =>
            {
                if (panel == null) return;
                var count = this.Query<VisualElement>().Build().ToList().Count;
                if (count > 500)
                    SusLog.Verbose($"[PerfAudit] '{GetType().Name}' has {count} VisualElements. " +
                        $"Consider virtualization (SusTable) or paging.");
            }).StartingIn(500);

            // DebounceAudit — warns about double-clicks (<300ms) on any interactive element
            // Registered in the constructor → fires BEFORE handlers in Created() (UITK order is guaranteed)
            this.RegisterCallback<ClickEvent>(_ =>
            {
                if (string.IsNullOrEmpty(_clickAuditDescription)) return;
                var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
                var elapsed = (now - _lastClickTime) * 1000.0;
                _lastClickTime = now;
                if (elapsed < 300.0 && elapsed >= 0)
                    SusLog.Verbose($"[DebounceAudit] '{_clickAuditDescription}' " +
                        $"rapid double-click ({elapsed:F0}ms). Possible unintended double-submit.");
            });

            // IdleGuardAudit — warn if interactive element is visible but never clicked
            if (!string.IsNullOrEmpty(_clickAuditDescription))
            {
                schedule.Execute(() =>
                {
                    if (panel == null || !visible || resolvedStyle.display == DisplayStyle.None) return;
                    if (_lastClickTime == 0)
                        SusLog.Verbose($"[IdleGuardAudit] '{_clickAuditDescription}' " +
                            $"visible but never clicked. " +
                            $"pickingMode={pickingMode}, worldBound=({worldBound.x:F0},{worldBound.y:F0} " +
                            $"{worldBound.width:F0}×{worldBound.height:F0}).");
                }).StartingIn(30000);
            }

            // OverflowAudit — check if any child exceeds parent bounds (Unity doesn't clip!)
            schedule.Execute(() =>
            {
                if (panel == null) return;
                var parentBounds = worldBound;
                if (parentBounds.width <= 0 || parentBounds.height <= 0) return;

                var overflowCount = 0;
                this.Query<VisualElement>().ForEach(child =>
                {
                    if (child == this) return;
                    var cb = child.worldBound;
                    if (cb.xMax > parentBounds.xMax + 2 || cb.yMax > parentBounds.yMax + 2 ||
                        cb.xMin < parentBounds.xMin - 2 || cb.yMin < parentBounds.yMin - 2)
                        overflowCount++;
                });
                if (overflowCount > 0)
                    SusLog.Verbose($"[OverflowAudit] '{GetType().Name}' has " +
                        $"{overflowCount} child(ren) exceeding parent bounds " +
                        $"({parentBounds.width:F0}×{parentBounds.height:F0}). " +
                        $"Unity UITK does NOT clip overflow (no overflow:hidden).");
            }).StartingIn(200);
        }

        /// <summary>RemountLoopAudit: detect rapid attach/detach cycles (&gt;5 in 1 second).</summary>
        private void RunRemountLoopAudit()
        {
            var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now - _remountWindowStart > 1.0)
            {
                _remountCount = 0;
                _remountWindowStart = now;
            }
            _remountCount++;
            if (_remountCount > 5)
            {
                // F3.4: escalate to Error so STRICT OnLog → BattleFailFast CreationFlood
                SusLog.Error($"[RemountLoopAudit] '{GetType().Name}' " +
                    $"attached {_remountCount} times in 1s. Possible Reactivity loop " +
                    $"(WatchEffect modifying visibility/layout).");
            }
        }

        /// <summary>LayoutReentryAudit: warn on geometry thrash (&gt;20 changes in 500ms).</summary>
        private void RunLayoutReentryAudit()
        {
            var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now - _layoutReentryWindow > 0.5)
            {
                _layoutReentryCount = 0;
                _layoutReentryWindow = now;
                _layoutReentryWarnedThisWindow = false;
            }
            _layoutReentryCount++;
            // Once per 500ms window — continuous warn flooded battle logs (GAP-20260729-001 T09).
            if (_layoutReentryCount > 20 && !_layoutReentryWarnedThisWindow)
            {
                _layoutReentryWarnedThisWindow = true;
                SusLog.Verbose($"[LayoutReentryAudit] '{GetType().Name}' " +
                    $"{_layoutReentryCount}+ geometry changes in 500ms. " +
                    $"Possible layout loop — WatchEffect modifying size/position.");
            }
        }
#endif
    }
}
