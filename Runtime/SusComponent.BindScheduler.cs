using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Batches bind-update actions (WatchEffect / Bind* re-applies) once per frame, plus the
    /// binding-lifecycle helpers (tracking, disposal, relocation flag) the scheduler and
    /// SusComponent's constructor/attach/detach handlers share.
    /// </summary>
    public abstract partial class SusComponent
    {
        // ─── Batched bind update scheduling ────────────────────────────────

        private readonly HashSet<Action> _pendingBindActions = new();
        private IVisualElementScheduledItem _bindScheduleItem;

        /// <summary>
        /// Batches a bind-update action to execute once per frame.
        /// De-duplicates: same Action is only queued once.
        /// </summary>
        /// <remarks>
        /// UITK <see cref="VisualElement.schedule"/> is a no-op until the element is
        /// attached to a panel. Actions are still queued in <see cref="_pendingBindActions"/>
        /// and flushed from <see cref="OnAttachToPanelHandler"/> so Prop changes during
        /// Build / SetChildProp before <c>parent.Add()</c> still reach Bind*.
        /// </remarks>
        private void ScheduleBindUpdate(Action apply)
        {
            _pendingBindActions.Add(apply);
            if (panel == null)
                return;
            _bindScheduleItem ??= schedule.Execute(ApplyAllBindUpdates);
            _bindScheduleItem.ExecuteLater(0);
        }

        /// <summary>Safety bound for the re-entrant drain loop below — see remarks.
        /// A real WatchEffect-writes-derived-Prop chain settles in 1-2 iterations; this only
        /// exists to turn an actual A-writes-B-writes-A cycle into a logged warning instead
        /// of an infinite loop.</summary>
        private const int MaxSteadyStateFlushIterations = 100;

        /// <remarks>
        /// (2026-08-20): this used to be a single snapshot-and-clear pass. That silently
        /// dropped updates in the STEADY-STATE case (same failure class fixed for the
        /// attach-time path, but that fix did not cover this one): when a bind action run
        /// from this loop synchronously writes a Prop that ANOTHER Bind*/WatchEffect on the SAME
        /// component reads — e.g. <c>Mounted()</c>'s <c>WatchEffect(ApplyVisual)</c> sets a
        /// derived <c>HpText</c> Prop that <c>Build()</c>'s <c>BindText(HpCounterLabel, () =&gt;
        /// HpText.Value)</c> reads — the write's <c>Prop.Value</c> setter synchronously invokes
        /// <see cref="ScheduleBindUpdate"/> for the BindText effect. We are INSIDE this very
        /// scheduled item's dispatch (<c>_bindScheduleItem</c>'s <c>Execute</c> callback is on
        /// the stack right now), so that nested call's <c>_bindScheduleItem.ExecuteLater(0)</c>
        /// is a reentrant re-arm of the SAME scheduled item while UITK's scheduler is mid-
        /// dispatch for it — and per Unity's scheduler silently drops that re-arm. The
        /// action lands (harmlessly) back in <c>_pendingBindActions</c> for "next time", but
        /// there never IS a next time: nothing else ever re-triggers that scheduled item again,
        /// so the bound Label.text is stuck one generation behind forever.
        ///
        /// Fix: loop here instead of doing one pass. Newly-queued actions (from a bind action's
        /// own synchronous side effects) are applied immediately, within the SAME dispatch, so
        /// we never depend on a reentrant re-arm succeeding. Bounded by
        /// <see cref="MaxSteadyStateFlushIterations"/> so an actual A-writes-B-writes-A Prop
        /// cycle logs a warning and yields instead of hanging. Regression:
        /// SteadyStateBindCascadeTests (WatchEffect writes a derived Prop that a same-instance
        /// BindText/BindClass reads — must re-render every generation, not just the first).
        /// </remarks>
        private void ApplyAllBindUpdates()
        {
            var iterations = 0;
            while (_pendingBindActions.Count > 0)
            {
                if (++iterations > MaxSteadyStateFlushIterations)
                {
                    SusLog.Warn(
                        $"[SusComponent] ApplyAllBindUpdates on {GetType().Name} exceeded " +
                        $"{MaxSteadyStateFlushIterations} re-entrant iterations in one flush — " +
                        "likely a bind/WatchEffect cycle (a Prop write re-triggers itself, " +
                        "directly or via another Prop, on the same component). Remaining " +
                        "actions deferred to the next tick.");
                    break;
                }

                // Snapshot and clear to allow re-registration during execution
                var actions = new List<Action>(_pendingBindActions);
                _pendingBindActions.Clear();
                foreach (var action in actions)
                    action();
            }
        }

        /// <summary>
        /// Catch up pending bind actions right on panel attach. Queued invalidations that
        /// arrived while detached never ran (schedule was a no-op — see <see cref="ScheduleBindUpdate"/>).
        /// </summary>
        /// <remarks>
        /// (2026-08-17): this used to defer the catch-up via
        /// <c>schedule.Execute(ApplyAllBindUpdates).ExecuteLater(0)</c>, i.e. it queued a NEW
        /// one-shot scheduled item on <c>this</c> element's panel scheduler instead of just
        /// running the pending actions directly. That is harmless for a single component
        /// attaching on its own — the one-shot item fires next tick and nothing else competes
        /// for it — but it silently dropped the flush when SEVERAL freshly-built reactive
        /// siblings attach in the SAME synchronous cascade (e.g. a list of rows built off-panel
        /// then added to an already-mounted host in one call — SusProfileScreenContent.SetFriends
        /// building N SusChip rows with Label/Color set before Add()). Every sibling's
        /// OnAttachToPanelHandler calls <c>schedule.Execute(...)</c> reentrantly on the SAME
        /// panel scheduler within the SAME editor tick while that scheduler is itself mid-dispatch
        /// for the ongoing attach; some of those one-shot items never got a chance to fire, so the
        /// affected sibling's BindClass/BindVisibility never applied — permanently, since nothing
        /// else was pending to retrigger it (confirmed live via Unity MCP: forcing the same Prop
        /// again on the broken chip minutes later did not self-heal it either, because
        /// re-triggering ALSO goes through <c>ScheduleBindUpdate</c> → the very same scheduler
        /// call that was already dropping items for that element). There is no reason for this
        /// specific catch-up to go through the scheduler at all: we are already inside the
        /// synchronous AttachToPanelEvent dispatch, so applying pending actions directly is both
        /// simpler and immune to sibling scheduler contention. Regression tests:
        /// PreAttachBindFlushTests.TwoSiblings_PlainHost_PropsSetBeforeAdd_BothApplyOnSharedAttachBatch
        /// / ThreeSiblings_SusComponentHost_PropsSetBeforeAdd_AllApplyOnSharedAttachBatch.
        /// </remarks>
        // ─── bounded re-entrant flush (trampoline) ──────────────────
        // A bind action applied by ApplyAllBindUpdates() can itself perform a structural
        // change (BindVisibility re-Insert, a WatchEffect that Add()s a freshly-built
        // child) that attaches MORE elements to an already-on-panel parent — and UITK
        // dispatches THAT attach synchronously, nested inside the very call we are in.
        // If that nested element ALSO has a pending flush (very common: freshly-built
        // reactive content, props set before Add — exactly the pattern), calling
        // ApplyAllBindUpdates() directly here would recurse the SAME small chain of
        // frames (OnAttachToPanelHandler → FlushPendingBindUpdatesOnAttach →
        // ApplyAllBindUpdates → action) once per nesting level. For a static handful of
        // levels that's invisible; for a screen whose reveal cascades several levels
        // deep in one synchronous mount (GameFlow app boot building the whole initial
        // route tree) it compounds into a deep, repeating call pattern — harmless on
        // Mono's generous Editor stack, but exactly the shape of the WebGL wasm
        // "RangeError: Maximum call stack size exceeded" from (small wasm stack,
        // interpreted invoke_iii/invoke_ii trampolines burn more stack per managed
        // frame than native Mono).
        //
        // Fix: cap re-entrant synchronous flushes to depth 1. Anything nested deeper
        // is queued instead of executed inline, and drained ITERATIVELY by the
        // outermost (depth-0) caller once its own ApplyAllBindUpdates() returns — so
        // the whole cascade still fully flushes within the SAME tick (nobody observes
        // an unflushed frame), but the C# call stack used by SUS's own flush machinery
        // stays O(1) regardless of how many levels re-enter, instead of O(depth).
        //
        // Deliberately NOT falling back to schedule.Execute(...).ExecuteLater(0) for the
        // deferred case — that is the exact mechanism removed (reentrant
        // registration on the panel's scheduler while it is mid-dispatch silently drops
        // some one-shot items). The queue below is plain, private, and drained by a
        // normal loop, so it carries none of that race.
        [ThreadStatic] private static int _flushDepth;
        [ThreadStatic] private static Queue<SusComponent> _deferredFlushQueue;
        private bool _flushQueuedPending;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Test-only count of flushes that were re-entrant (would have added a
        /// stack frame on top of an in-progress flush and were queued instead). A deep
        /// synchronous reveal cascade drives this above 0 — proving the guard actually
        /// intercepted nesting, not just that nesting never happens to occur. Regression
        /// tests reset this to 0 and assert on it (see).</summary>
        internal static int DebugInterceptedReentrantFlushCount;
#endif

        private void FlushPendingBindUpdatesOnAttach()
        {
            if (_pendingBindActions.Count == 0) return;

            if (_flushDepth > 0)
            {
                // Re-entrant: some ancestor's bind action is still on the stack, mid-flush,
                // and just attached us as a side effect. Don't add another frame on top of
                // that chain — queue for the outermost caller to drain after it returns.
                if (!_flushQueuedPending)
                {
                    _flushQueuedPending = true;
                    (_deferredFlushQueue ??= new Queue<SusComponent>()).Enqueue(this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    DebugInterceptedReentrantFlushCount++;
#endif
                }
                return;
            }

            _flushDepth = 1;
            try
            {
                ApplyAllBindUpdates();
                DrainDeferredFlushQueue();
            }
            finally
            {
                _flushDepth = 0;
            }
        }

        private static void DrainDeferredFlushQueue()
        {
            var queue = _deferredFlushQueue;
            if (queue == null) return;
            while (queue.Count > 0)
            {
                var comp = queue.Dequeue();
                comp._flushQueuedPending = false;
                comp.ApplyAllBindUpdates();
            }
        }

        /// <summary>
        /// Creates a WatchHandle for manual subscription tracking.
        /// Use when you need to TrackBinding() for non-standard callbacks
        /// (e.g. Controller.Errors.Changed) that can't use Bind* methods.
        /// </summary>
        protected static WatchHandle CreateWatchHandle(Action unsubscribe)
        {
            return new WatchHandle(unsubscribe);
        }

        /// Registers a binding's WatchHandle so it can be disposed on detach.
        /// Called by all Bind* methods and Watch/WatchEffect.
        /// </summary>
        protected void TrackBinding(WatchHandle h)
        {
            if (h != null) _bindings.Add(h);
        }

        private void DisposeAllBindings()
        {
            for (int i = _bindings.Count - 1; i >= 0; i--)
                _bindings[i].Dispose();
            _bindings.Clear();
            _eventHandlers?.Clear();
        }

        /// <summary>
        /// True while a subclass is synchronously relocating this element to a different
        /// parent WITHIN THE SAME PANEL — e.g. <see cref="SusOverlayComponent.IsRelocatingToOverlay"/>
        /// during <c>MountSelfInOverlay</c>/<c>UnmountSelfFromOverlay</c>. That relocation's own
        /// <c>RemoveFromHierarchy()</c>/<c>Add()</c> calls trigger a REAL Attach/DetachFromPanelEvent
        /// as an implementation detail of UI Toolkit reparenting, even though the element never
        /// truly leaves the visual tree. <see cref="OnDetachFromPanelHandler"/> checks this to skip
        /// <see cref="DisposeAllBindings"/> during a pure reparent — without it, every
        /// <c>Watch()</c>/<c>WatchEffect()</c> registered in <c>Created()</c> (which runs once, in
        /// the constructor) gets permanently unsubscribed the first time an overlay-hosted component
        /// (Modal/Toast) opens, silently breaking all future reactivity to that Prop (e.g. SusModal's
        /// <c>Watch(Model,...)</c> never firing again after the first open).
        /// </summary>
        protected virtual bool IsRelocating => false;
    }
}
