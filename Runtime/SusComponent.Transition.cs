using System;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    public abstract partial class SusComponent
    {
        /// <summary>
        /// v-if with enter/leave CSS transition (Vue &lt;Transition&gt; analogue).
        /// Applies preset classes from <see cref="SusTransition"/>; waits for
        /// <see cref="SusTransition.DefaultDurationMs"/> before removing from hierarchy on leave.
        /// Pass <c>transition="none"</c> / null to fall back to plain <see cref="BindVisibility"/>.
        /// </summary>
        protected WatchHandle BindTransitionVisibility(
            VisualElement el, Func<bool> getter, string transition = SusTransition.Fade)
        {
            if (el == null) throw new ArgumentNullException(nameof(el));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            if (string.IsNullOrEmpty(transition) || transition == SusTransition.None)
                return BindVisibility(el, getter);

            var preset = SusTransition.PresetClass(transition);
            if (preset != null)
                el.AddToClassList(preset);

            VisualElement rememberedParent = null;
            IVisualElementScheduledItem leaveJob = null;
            bool? lastShown = null;

            void ClearPhaseClasses()
            {
                el.RemoveFromClassList(SusTransition.EnterFrom);
                el.RemoveFromClassList(SusTransition.EnterActive);
                el.RemoveFromClassList(SusTransition.EnterTo);
                el.RemoveFromClassList(SusTransition.LeaveFrom);
                el.RemoveFromClassList(SusTransition.LeaveActive);
                el.RemoveFromClassList(SusTransition.LeaveTo);
            }

            void Enter()
            {
                leaveJob?.Pause();
                leaveJob = null;
                ClearPhaseClasses();

                if (el.parent == null && rememberedParent != null)
                    rememberedParent.Add(el);

                el.AddToClassList(SusTransition.EnterFrom);
                el.AddToClassList(SusTransition.EnterActive);

                // Next frame → enter-to (triggers USS transition)
                el.schedule.Execute(() =>
                {
                    el.RemoveFromClassList(SusTransition.EnterFrom);
                    el.AddToClassList(SusTransition.EnterTo);
                }).ExecuteLater(16);

                el.schedule.Execute(() =>
                {
                    ClearPhaseClasses();
                }).ExecuteLater(SusTransition.DefaultDurationMs + 16);
            }

            void Leave()
            {
                if (el.parent == null) return;
                rememberedParent = el.parent;

                ClearPhaseClasses();
                el.AddToClassList(SusTransition.LeaveFrom);
                el.AddToClassList(SusTransition.LeaveActive);

                el.schedule.Execute(() =>
                {
                    el.RemoveFromClassList(SusTransition.LeaveFrom);
                    el.AddToClassList(SusTransition.LeaveTo);
                }).ExecuteLater(16);

                leaveJob?.Pause();
                leaveJob = el.schedule.Execute(() =>
                {
                    ClearPhaseClasses();
                    // T-492: guard against a re-Enter() racing this delayed removal. Pause() above
                    // cancels the PREVIOUS job when Leave() is called again, but it does not protect
                    // against THIS job's own timer having already fired (UITK scheduler dispatch is
                    // in-flight) by the moment a later Enter() re-adds `el` — that Enter() cannot
                    // un-schedule a callback that is already executing/queued this tick, so the stale
                    // removal still runs and silently undoes the just-completed re-open (observed
                    // live: SusExpansionPanels close→reopen within ~200ms, e.g. SusUxDriver
                    // 01-closed→03-open-general). `lastShown` is the single source of truth for the
                    // CURRENT desired state — only remove if we are still actually meant to be hidden.
                    if (lastShown != false)
                        return;
                    if (el.parent != null)
                    {
                        rememberedParent = el.parent;
                        el.RemoveFromHierarchy();
                    }
                    leaveJob = null;
                }).StartingIn(SusTransition.DefaultDurationMs + 16);
            }

            var h = ReactiveEffect(() =>
            {
                bool show = getter();
                if (lastShown == show) return;
                bool isFirstRun = lastShown == null;
                lastShown = show;

                if (isFirstRun)
                {
                    // No enter/leave animation on initial mount (Vue <Transition> default:
                    // an element that starts v-if=false was never shown, so there is nothing
                    // to animate OUT of). Without this, the generator's Add-then-bind emission
                    // order (element is already parented when this effect first runs) made a
                    // closed-by-default panel play a 200ms+ Leave() — fully opaque
                    // (.sus-transition-leave-from = opacity:1) until the delayed removal
                    // fired — so collapsed content was fully visible/readable at mount, and a
                    // fast screenshot (or a click landing mid-leave) raced the pending removal
                    // job. Reflect the starting state synchronously instead (T-415, 2026-08-13
                    // — SusExpansionPanel body visible while collapsed / expand looked inert).
                    ClearPhaseClasses();
                    if (show)
                    {
                        if (el.parent == null && rememberedParent != null)
                            rememberedParent.Add(el);
                    }
                    else if (el.parent != null)
                    {
                        rememberedParent = el.parent;
                        el.RemoveFromHierarchy();
                    }
                    return;
                }

                if (show) Enter();
                else Leave();
            });

            TrackBinding(h);
            TrackBinding(new WatchHandle(() =>
            {
                leaveJob?.Pause();
                leaveJob = null;
            }));
            return h;
        }
    }
}
