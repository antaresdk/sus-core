using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Base for overlay-pinned content components (framework primitive, sus-core).
    ///
    /// Part of the C2 two-tier model: it IS a <see cref="SusComponent"/> (so the whole
    /// hierarchy stays uniform), but it fixes the <see cref="OverlayCategory"/> the
    /// component lives in. The layer is declared centrally per subclass and cannot be
    /// changed per-instance, so a modal can never end up in the tooltip layer, etc.
    ///
    /// Two mount mechanisms are provided:
    ///   - <see cref="MountSelfInOverlay"/> / <see cref="UnmountSelfFromOverlay"/> —
    ///     teleport THIS element into the overlay (used by modal, toast).
    ///   - child-teleport via <see cref="SusOverlayService"/> (used by tooltip, popup),
    ///     which keeps the activator inline and floats only a child card/popup.
    /// </summary>
    public abstract class SusOverlayComponent : SusComponent
    {
        /// <summary>
        /// The overlay layer this primitive is pinned to. Sealed by each concrete base
        /// (<see cref="SusModalBase"/> → Modal, etc.) so it cannot be overridden.
        /// </summary>
        protected abstract OverlayCategory Layer { get; }

        /// <summary>
        /// Reads the layer sealed by the concrete subclass (see <see cref="Layer"/>) for an
        /// external mounter that places THIS component into an <see cref="OverlayHost"/> from
        /// outside — e.g. <see cref="Sharq.Router.SusModalService"/> wrapping a
        /// <c>SusRouterModal</c> in a scrim/contentBox before calling
        /// <c>OverlayHost.AddToOverlay</c>. Such a mounter must put the wrapper in the SAME
        /// category the component itself is pinned to, so it reads this instead of
        /// duplicating the category as its own literal (the two could otherwise drift apart —
        /// see ARCH-20260903-OVERLAY-MOUNT §5 decision 4). Internal: not part of the public buyer API.
        /// </summary>
        internal OverlayCategory ResolvedLayer => Layer;

        private OverlayHost _selfHost;
        private OverlayEntry _selfEntry;
        private VisualElement _selfOriginalParent;

        /// <summary>
        /// Bumped on every mount/unmount state change (see <see cref="MountSelfInOverlay"/> /
        /// <see cref="UnmountSelfFromOverlay"/>). A deferred restore scheduled by an unmount
        /// captures the token at schedule time and only performs the reparent-back if the
        /// token is still current when the callback fires — see T-2174: a same-frame
        /// Cancel()+Start() (SusTutorialModal on step change) unmounts then immediately
        /// remounts into the overlay; the unmount's deferred restore must not run a frame
        /// later and rip the freshly-remounted element back out (that produced the
        /// "attached 6 times in 1s" RemountLoopAudit flood).
        /// </summary>
        private int _overlayMountToken;

        /// <summary>The OverlayHost resolved by the last self-mount, if any.</summary>
        protected OverlayHost ResolvedHost => _selfHost;

        /// <summary>True while this element is mounted into the overlay (self-mount).</summary>
        protected bool IsMountedInOverlay => _selfEntry != null;

        /// <summary>
        /// True while <see cref="MountSelfInOverlay"/>/<see cref="UnmountSelfFromOverlay"/> is
        /// in the middle of reparenting this element to/from its overlay host. UI Toolkit
        /// fires a real <c>DetachFromPanelEvent</c> (and therefore this component's own
        /// <c>Unmounted()</c>) as a side effect of the internal <c>RemoveFromHierarchy()</c>
        /// call that any reparent requires — subclasses (SusModal, ...) whose
        /// <c>Unmounted()</c> override runs real "closing" logic (e.g. <c>CloseOverlay()</c>)
        /// MUST check this flag first and return early when it's true. Otherwise the
        /// detach-for-relocation is misread as a genuine user-facing close: the very first
        /// <c>MountSelfInOverlay</c> call (element still parented inline) detaches from the
        /// inline parent to move into the host, which re-triggers the "closing" path
        /// mid-relocation and undoes the open before it ever finishes — reproduced
        /// empirically as silent display:None / no reparent, or the UI Toolkit "already
        /// being modified" error depending on internal timing (case 8, qa-4 2026-08-16).
        /// No amount of deferring the CALLER's open (schedule delay, GeometryChangedEvent)
        /// fixes this — the reentrancy happens one level down, inside AddToOverlay's own
        /// RemoveFromHierarchy(), regardless of when MountSelfInOverlay itself runs.
        /// </summary>
        protected bool IsRelocatingToOverlay { get; private set; }

        /// <summary>
        /// Tells <see cref="SusComponent.OnDetachFromPanelHandler"/> to skip
        /// <c>DisposeAllBindings()</c> while relocating — see <see cref="SusComponent.IsRelocating"/>
        /// for the full rationale.
        /// </summary>
        protected override bool IsRelocating => IsRelocatingToOverlay;

        /// <summary>
        /// True while the host this element sits in is being EMPTIED (<c>OverlayHost.ClearAll</c> /
        /// <c>ClearCategory</c>) rather than dismissing this one overlay — card T-3160.
        ///
        /// <see cref="UnmountSelfFromOverlay"/> answers a dismissal by scheduling a restore into the
        /// parent this element was teleported from, one frame later (it cannot reparent
        /// synchronously — see the comments there). Answering a TEARDOWN the same way puts the
        /// content back on screen a frame after the caller was told the host is empty, and it comes
        /// back live: re-parented means re-attached, re-attached means <c>Mounted()</c>, and a modal
        /// whose open prop is still true re-opens itself. That is how the previous Storybook story's
        /// modal ended up in four kit frames of the 2026-09-09 sweep (showcase-3) even though
        /// <c>SusStorybookHost.Unmount</c> had cleared every host it can reach (T-3131). A cleared
        /// host means gone: the element stays detached, and reopening it is the owner's call.
        /// </summary>
        private bool HostIsTearingDown()
        {
            if (_selfHost != null && _selfHost.IsClearing) return true;
            return parent is OverlayHost host && host.IsClearing;
        }

        /// <summary>
        /// Teleports THIS element into its pinned overlay layer, remembering the original
        /// parent for restore. Returns false if no OverlayHost was found (caller may fall
        /// back to inline display).
        /// </summary>
        protected bool MountSelfInOverlay(bool dismissOnClickOutside = false, Action onDismiss = null)
        {
            // Capture restore parent whenever we leave a non-host ancestor.
            // Previously only set when _selfHost was null — second open lost restore.
            if (parent != null && parent is not OverlayHost)
                _selfOriginalParent = parent;

            if (_selfHost == null)
            {
                // Shared ancestor-first resolution (T-3032). Never CREATES a host here: no host
                // above us means the caller falls back to inline display.
                _selfHost = SusBootstrap.FindOverlayHost(this);
                if (_selfHost == null && panel?.visualTree != null)
                    _selfHost = panel.visualTree.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            }

            if (_selfHost == null)
                return false;

            // Already on host — treat as mounted; do not reparent (AttachToPanel-safe).
            if (parent == _selfHost)
                return true;

            // Apply theme + tokens BEFORE reparenting so var() resolves in the overlay.
            SusThemeService.ApplyThemeClasses(this);
            IsRelocatingToOverlay = true;
            _overlayMountToken++;
            try
            {
                _selfEntry = _selfHost.AddToOverlay(this, Layer, dismissOnClickOutside, onDismiss);
            }
            finally
            {
                IsRelocatingToOverlay = false;
            }
            return true;
        }

        /// <summary>Removes this element from the overlay and restores it to its original parent.</summary>
        protected void UnmountSelfFromOverlay()
        {
            if (_selfEntry != null && _selfHost != null)
            {
                var entry = _selfEntry;
                _selfEntry = null;
                _overlayMountToken++;
                IsRelocatingToOverlay = true;
                try
                {
                    _selfHost.RemoveFromOverlay(entry);
                }
                finally
                {
                    IsRelocatingToOverlay = false;
                }
                if (HostIsTearingDown())
                {
                    // Cleared, not dismissed (card T-3160): drop the restore target the way a
                    // completed restore would, so a later reopen re-captures a live parent.
                    _selfOriginalParent = null;
                }
                else if (_selfOriginalParent != null)
                {
                    // this branch also runs when the HOST initiated the removal of THIS ONE
                    // overlay (OverlayHost.RemoveFromOverlay called directly, not through this
                    // component's own Close()/Model=false path; a host-wide ClearAll is a teardown
                    // and takes the branch above instead, card T-3160) — that path never sets
                    // IsRelocatingToOverlay before detaching, so the DetachFromPanelEvent
                    // this element's own RemoveFromHierarchy() fires reaches this method
                    // REENTRANT, synchronously, from inside UIR's own render-tree traversal
                    // (RepaintPanels -> ProcessChanges -> ... -> Unmounted() -> here). Adding
                    // this element back to its original parent SYNCHRONOUSLY in that window
                    // mutates the visual tree while UIR is still walking it and corrupts its
                    // internal bookkeeping (UpdateLocalFlipsWinding NullRef + repeated
                    // RepaintPanels assertion failures — reproduced live via ClearAll() on a
                    // still-open SusModal). Defer exactly like the "stale DOM" branch
                    // below already does for the identical underlying reason — safe
                    // because the next real frame is guaranteed to be outside any active
                    // traversal. A plain Close() click (IsRelocatingToOverlay never involved,
                    // no reentrancy) is delayed by one frame too, which every existing test
                    // already tolerates (asserts run after `yield return WaitFrame()`).
                    // Capture the mount token now — if MountSelfInOverlay runs again
                    // (same-frame Cancel()+Start()) before this callback fires, the token
                    // will have moved on and the stale restore below must no-op instead of
                    // ripping the freshly-remounted element back out (T-2174).
                    var restore = _selfOriginalParent;
                    _selfOriginalParent = null;
                    var restoreToken = _overlayMountToken;
                    restore.schedule.Execute(() =>
                    {
                        if (restoreToken == _overlayMountToken && parent != restore)
                            restore.Add(this);
                    }).ExecuteLater(0);
                }
            }
            else if (parent is OverlayHost && _selfOriginalParent != null && !HostIsTearingDown())
            {
                // Stale DOM on host without stack entry. Defer restore — Unmounted
                // may run inside DetachFromPanel where hierarchy mutation is illegal.
                _overlayMountToken++;
                var restore = _selfOriginalParent;
                _selfOriginalParent = null;
                var restoreToken = _overlayMountToken;
                restore.schedule.Execute(() =>
                {
                    if (restoreToken == _overlayMountToken && parent != restore)
                        restore.Add(this);
                }).ExecuteLater(0);
            }
        }

        /// <summary>
        /// Removes this element from the overlay for good: unlike <see cref="UnmountSelfFromOverlay"/>
        /// it does NOT go back to the parent it was teleported from. For transient content whose
        /// close means "gone" (a toast that timed out), not "hide until next time" (a modal).
        ///
        /// Restoring such content puts it back where it was declared still wearing its fade-out
        /// classes: opacity 0, but display/visibility/pickingMode untouched, so an invisible box
        /// keeps swallowing clicks meant for the UI beneath it. The same holds for an element
        /// that was added straight into the host (it has no stack entry, so
        /// <see cref="UnmountSelfFromOverlay"/> leaves it where it is): it is detached here too.
        /// Also cancels a restore an earlier unmount may still have pending.
        /// </summary>
        protected void DismissSelfFromOverlay()
        {
            _selfOriginalParent = null;
            _overlayMountToken++;
            if (_selfEntry != null && _selfHost != null)
            {
                var entry = _selfEntry;
                _selfEntry = null;
                // A real removal, not a relocation: IsRelocatingToOverlay stays false so the
                // subclass's Unmounted() and the binding teardown run as for any detach.
                _selfHost.RemoveFromOverlay(entry);
            }
            else if (parent is OverlayHost)
            {
                RemoveFromHierarchy();
            }
        }
    }

    /// <summary>
    /// Base for modal dialogs / drawers. Pinned to <see cref="OverlayCategory.Modal"/>.
    /// Self-teleports into the overlay and installs a focus trap. Router's
    /// <c>SusRouterModal</c> and downstream modal components both derive from this so every
    /// modal is a real <see cref="SusComponent"/> living in exactly the modal layer.
    /// </summary>
    public abstract class SusModalBase : SusOverlayComponent
    {
        protected sealed override OverlayCategory Layer => OverlayCategory.Modal;

        // ── The three overlay-role obligations (ARCH-20260911-KIT-STATE-CONTRACT D11/D12, T-3430) ──
        //
        //   1. ENTER   — on show, focus moves to the first focusable element INSIDE the overlay;
        //                if there is none, to the overlay itself (focusable, tabIndex -1) so that
        //                the trap becomes reachable at all.
        //   2. HOLD    — Tab / Shift+Tab cycle inside the overlay and never leave it.
        //   3. RETURN  — the element focused BEFORE the show gets focus back after the close.
        //
        // Why this lives in the base and not in each overlay component: a trap WITHOUT an initial
        // focus is INERT. It hangs on the overlay's KeyDownEvent, but key events go to the focused
        // element and bubble up from there — while focus is outside, neither Tab nor Escape ever
        // reaches the overlay. That was exactly the state of SusModal and SusSpotlightTour: the
        // trap was installed and the dialog still could not be operated from the keyboard. Enter,
        // hold and return are therefore one obligation with one home.
        //
        // The trap is installed IN THE CONSTRUCTOR: a subclass of the overlay base gets it by the
        // fact of inheritance rather than by a call someone can forget. Registering a callback
        // needs no panel, and an overlay with nothing focusable costs the trap nothing (zero
        // focusables — the handler returns immediately).

        /// <summary>
        /// Overlays that currently hold a focus session. Only ever holds elements that are on a
        /// panel (pruned on every claim); it exists so that "who is the presenter" can be decided
        /// by containment rather than by the order in which two nested overlays opened.
        /// </summary>
        private static readonly System.Collections.Generic.List<SusModalBase> ActiveFocusSessions
            = new System.Collections.Generic.List<SusModalBase>();

        private const int MaxFocusEnterAttempts = 12;

        private VisualElement _focusReturn;
        private bool _focusSession;
        private int _focusEnterAttempts;
        private IVisualElementScheduledItem _focusEnter;
        private EventCallback<KeyDownEvent> _escapeCallback;
        private Func<bool> _escapeGuard;
        private Action _escapeClose;

        protected SusModalBase()
        {
            InstallFocusTrapOn(this);
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                // Safety net for the return: a close that bypasses CloseFromOverlay
                // (OverlayHost.RemoveFromOverlay called directly, a service teardown, panel
                // destruction). A relocation into/out of the overlay is NOT a close — that is
                // what IsRelocatingToOverlay distinguishes.
                if (_focusSession && !IsRelocatingToOverlay)
                    EndOverlayFocus();
            });
        }

        /// <summary>True while this overlay holds the focus session (entered, not yet returned).</summary>
        protected bool HasOverlayFocusSession => _focusSession;

        /// <summary>
        /// Obligation 1, plus the capture for obligation 3. Called from the component's own POINT
        /// OF SHOW: a self-teleporting overlay gets it from <see cref="OpenInOverlay"/> (which
        /// calls this itself), an in-place one (drawer, bottom sheet, dialogue box) calls it from
        /// its own transition into the open state.
        ///
        /// Nesting: an overlay sitting INSIDE another overlay's content is not the presenter and
        /// holds no session of its own — otherwise the dialogue box inside the tutorial modal
        /// would pull focus off the modal's buttons onto itself. The outermost presenter always
        /// wins, whichever of the two opened first: an inner claim is refused, and an outer claim
        /// takes over a session already held by its own content (inheriting the return target the
        /// inner one had captured). Testing "is there a SusModalBase ancestor" instead would be
        /// wrong — a wrapper such as SusTutorialModal is such an ancestor while its inner modal
        /// falls back to inline display, and the real dialog would then get no initial focus.
        /// </summary>
        protected void BeginOverlayFocus()
        {
            if (_focusSession) return;

            PruneFocusSessions();

            for (int i = 0; i < ActiveFocusSessions.Count; i++)
            {
                var outer = ActiveFocusSessions[i];
                if (outer != this && outer.Contains(this))
                    return; // an enclosing overlay is the presenter
            }

            VisualElement inherited = null;
            for (int i = ActiveFocusSessions.Count - 1; i >= 0; i--)
            {
                var inner = ActiveFocusSessions[i];
                if (inner == this || !Contains(inner)) continue;
                inherited = inherited ?? inner._focusReturn;
                inner.CancelOverlayFocus();
            }

            _focusSession = true;
            _focusEnterAttempts = 0;
            ActiveFocusSessions.Add(this);

            if (inherited != null && inherited.panel != null && !Contains(inherited))
            {
                _focusReturn = inherited;
            }
            else
            {
                var focused = focusController?.focusedElement as VisualElement;
                if (focused != null && focused != this && !Contains(focused))
                    _focusReturn = focused;
            }

            ArmFocusEnter();
        }

        /// <summary>
        /// Arms the deferred initial focus. A component whose point of show runs while it is still
        /// DETACHED (a drawer opened by Model=true before it is added to a panel) cannot use the
        /// scheduler — the scheduler is panel-bound and the item would never tick — so the arming
        /// waits for the attach instead.
        /// </summary>
        private void ArmFocusEnter()
        {
            _focusEnter?.Pause();
            _focusEnter = null;

            if (panel == null)
            {
                UnregisterCallback<AttachToPanelEvent>(OnAttachArmFocus);
                RegisterCallback<AttachToPanelEvent>(OnAttachArmFocus);
                return;
            }

            _focusEnter = schedule.Execute(FocusFirstInside);
            _focusEnter.ExecuteLater(0);
        }

        private void OnAttachArmFocus(AttachToPanelEvent _)
        {
            UnregisterCallback<AttachToPanelEvent>(OnAttachArmFocus);
            if (_focusSession) ArmFocusEnter();
        }

        /// <summary>
        /// Drops the session without returning focus: used when an enclosing overlay takes the
        /// presentation over (the outer one owns the return target from then on).
        /// </summary>
        private void CancelOverlayFocus()
        {
            if (!_focusSession) return;
            _focusSession = false;
            ActiveFocusSessions.Remove(this);
            UnregisterCallback<AttachToPanelEvent>(OnAttachArmFocus);
            _focusEnter?.Pause();
            _focusEnter = null;
            _focusReturn = null;
        }

        private static void PruneFocusSessions()
        {
            for (int i = ActiveFocusSessions.Count - 1; i >= 0; i--)
            {
                var s = ActiveFocusSessions[i];
                if (s == null || s.panel == null)
                {
                    if (s != null) { s._focusSession = false; s._focusReturn = null; }
                    ActiveFocusSessions.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Obligation 3. Called from the point of close; <see cref="CloseFromOverlay"/> calls it
        /// itself. The return happens BEFORE the unmount — the element is still on the panel, so
        /// focus is moved synchronously with no frame of limbo in between.
        /// </summary>
        protected void EndOverlayFocus()
        {
            if (!_focusSession) return;
            _focusSession = false;
            ActiveFocusSessions.Remove(this);

            UnregisterCallback<AttachToPanelEvent>(OnAttachArmFocus);
            _focusEnter?.Pause();
            _focusEnter = null;

            var target = _focusReturn;
            _focusReturn = null;

            var focused = focusController?.focusedElement as VisualElement;
            if (focused != null && (focused == this || Contains(focused)))
                focused.Blur();

            if (target == null || target.panel == null) return;
            target.Focus();
        }

        /// <summary>
        /// Obligation 4 (Escape) as a single declaration: <paramref name="canClose"/> is the
        /// component's policy (Persistent / Permanent / "the step is still running"),
        /// <paramref name="close"/> its own close path. The mechanism — registration,
        /// StopPropagation, idempotence — lives here. Escape reaches the overlay at all only
        /// because obligation 1 moved focus inside it.
        /// </summary>
        protected void InstallEscapeClose(Func<bool> canClose, Action close)
        {
            if (close == null) return;
            _escapeGuard = canClose;
            _escapeClose = close;
            if (_escapeCallback != null) return;

            _escapeCallback = evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                if (_escapeGuard != null && !_escapeGuard()) return;
                _escapeClose?.Invoke();
                evt.StopPropagation();
            };
            RegisterCallback(_escapeCallback);
        }

        /// <summary>Opens the modal in the overlay (with focus trap). Falls back to inline display.</summary>
        protected bool OpenInOverlay(bool dismissOnClickOutside, Action onDismiss)
        {
            var mounted = MountSelfInOverlay(dismissOnClickOutside, onDismiss);
            BeginOverlayFocus();
            return mounted;
        }

        /// <summary>Closes the modal, restoring it to its original parent.</summary>
        protected void CloseFromOverlay()
        {
            EndOverlayFocus();
            UnmountSelfFromOverlay();
        }

        private void FocusFirstInside()
        {
            if (!_focusSession || panel == null) return;

            var first = FirstFocusableInside(this);
            if (first == null)
            {
                // Nothing focusable inside — the overlay itself takes focus. tabIndex -1 keeps it
                // out of the Tab cycle: it accepts focus programmatically but is never a Tab stop.
                focusable = true;
                tabIndex = -1;
            }

            var target = first ?? (VisualElement)this;
            target.Focus();

            // UI Toolkit refuses focus for an element that is not displayed yet, and an overlay
            // teleported into the OverlayHost resolves its style one frame AFTER the reparent —
            // so the first attempt legitimately lands nowhere and must be retried rather than
            // silently dropped (that is the whole defect this obligation exists to close).
            var landed = focusController?.focusedElement as VisualElement;
            if (landed != null && (landed == this || Contains(landed)))
            {
                _focusEnterAttempts = 0;
                return;
            }

            if (++_focusEnterAttempts >= MaxFocusEnterAttempts) return;
            ArmFocusEnter();
        }

        /// <summary>
        /// The Tab / Shift+Tab trap. The single implementation in the framework:
        /// <see cref="OverlayHost.InstallFocusTrap"/> delegates here so the traversal cannot drift
        /// apart in two places.
        /// </summary>
        public static void InstallFocusTrapOn(VisualElement overlayElement)
        {
            if (overlayElement == null) return;

            overlayElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Tab) return;

                var focusables = CollectFocusables(overlayElement);
                if (focusables.Count == 0) return;

                var current = overlayElement.focusController?.focusedElement as VisualElement;
                var currentIndex = focusables.IndexOf(current);

                VisualElement target;
                if (evt.shiftKey)
                    target = currentIndex <= 0 ? focusables[focusables.Count - 1] : focusables[currentIndex - 1];
                else
                    target = currentIndex >= focusables.Count - 1 ? focusables[0] : focusables[currentIndex + 1];

                target.Focus();
                evt.StopPropagation();
            }, TrickleDown.NoTrickleDown);
        }

        private static System.Collections.Generic.List<VisualElement> CollectFocusables(VisualElement root)
        {
            return root.Query<VisualElement>()
                .Where(e => e != root && e.focusable && e.tabIndex >= 0
                            && e.enabledInHierarchy && IsDisplayed(e, root))
                .ToList();
        }

        private static VisualElement FirstFocusableInside(VisualElement root)
        {
            var list = CollectFocusables(root);
            return list.Count > 0 ? list[0] : null;
        }

        private static bool IsDisplayed(VisualElement el, VisualElement root)
        {
            for (var e = el; e != null && e != root.parent; e = e.parent)
            {
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
                if (!e.visible) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Base for tooltips. Pinned to <see cref="OverlayCategory.Tooltip"/>. The activator
    /// stays inline; only a child card is floated via <see cref="SusOverlayService"/>.
    /// </summary>
    public abstract class SusTooltipBase : SusOverlayComponent
    {
        protected sealed override OverlayCategory Layer => OverlayCategory.Tooltip;

        /// <summary>Floats a child card into the tooltip layer next to an anchor.</summary>
        protected void ShowOverlayCard(VisualElement card, VisualElement anchor)
            => SusOverlayService.ShowTooltip(card, anchor);

        /// <summary>Removes the floated card, optionally restoring it to a parent.</summary>
        protected void HideOverlayCard(VisualElement card, VisualElement originalParent = null)
            => SusOverlayService.HideTooltip(card, originalParent);
    }

    /// <summary>
    /// Base for dropdown/select/menu popups. Pinned to <see cref="OverlayCategory.Dropdown"/>.
    /// The trigger stays inline; only a child popup is floated via <see cref="SusOverlayService"/>
    /// with full tracking (click-outside, scroll reposition, anchor detach).
    /// </summary>
    public abstract class SusPopupBase : SusOverlayComponent
    {
        protected sealed override OverlayCategory Layer => OverlayCategory.Dropdown;

        /// <summary>Floats a child popup below an anchor (dropdown layer), with tracking.</summary>
        protected void ShowPopup(VisualElement popup, VisualElement anchor, Action onClose = null)
            => SusOverlayService.Show(popup, anchor, onClose);

        /// <summary>Removes the floated popup, restoring it to its original parent.</summary>
        protected void HidePopup(VisualElement popup, VisualElement originalParent = null)
            => SusOverlayService.Hide(popup, originalParent);

        /// <summary>Repositions the popup relative to its anchor (with viewport clamping).</summary>
        protected void RepositionPopup(VisualElement popup, VisualElement anchor)
            => SusOverlayService.RepositionFloating(popup, anchor);
    }

    /// <summary>
    /// Base for toasts / snackbars. Pinned to <see cref="OverlayCategory.Toast"/> (above
    /// dropdowns, below console), so a transient notification is never hidden by an open
    /// popup. Self-teleports into the overlay.
    /// </summary>
    public abstract class SusToastBase : SusOverlayComponent
    {
        protected sealed override OverlayCategory Layer => OverlayCategory.Toast;

        /// <summary>Shows the toast in the overlay toast layer. Falls back to inline display.</summary>
        protected bool ShowToast(bool dismissOnClickOutside = false, Action onDismiss = null)
            => MountSelfInOverlay(dismissOnClickOutside, onDismiss);

        /// <summary>Hides the toast, restoring it to its original parent.</summary>
        protected void HideToast() => UnmountSelfFromOverlay();

        /// <summary>
        /// Removes the toast from the overlay without restoring it to its original parent —
        /// see <see cref="SusOverlayComponent.DismissSelfFromOverlay"/>.
        /// </summary>
        protected void DismissToast() => DismissSelfFromOverlay();
    }
}
