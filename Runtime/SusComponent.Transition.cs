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
                lastShown = show;

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
