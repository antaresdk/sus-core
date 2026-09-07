using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook
{
    /// <summary>How a state was put onto an instance — the receipt <see cref="SusStoryStates"/> returns.</summary>
    public enum SusStateForcing
    {
        /// <summary>Nothing was applied because nothing had to be: this IS the resting state.</summary>
        Rest = 0,

        /// <summary>A boolean prop of the component was set (<c>Disabled</c>, <c>Error</c>…).</summary>
        Prop = 1,

        /// <summary>
        /// A visual-state class was added (<c>&lt;prefix&gt;--disabled</c>,
        /// <c>&lt;prefix&gt;--focused</c>): the corpus has ZERO <c>:disabled</c> and ZERO
        /// <c>:focus</c> selectors, those states are class-driven already (plan §2.6).
        /// </summary>
        StateClass = 2,

        /// <summary>A pseudo-class twin was added (<see cref="SusStateTwins"/>).</summary>
        TwinClass = 3,

        /// <summary>
        /// The state COULD NOT be forced — a pseudo-class with no twin in the component's skin.
        /// The caller must not render a cell for it: the instance would show its rest appearance
        /// under a label that promises something else.
        /// </summary>
        Unsupported = 4,
    }

    /// <summary>
    /// The six columns of the state matrix and the only sanctioned way to force one of them onto
    /// a live instance (plan ARCH-20260907-STORYBOOK-ENGINE.md §4.5 p. 2, §0.3, card T-3038).
    ///
    /// Three levers, in this order of preference:
    /// <list type="number">
    /// <item>the component's own boolean prop, when it has one — then the component's own
    /// bindings put the class on, exactly as they do in a real app;</item>
    /// <item><see cref="SusComponent.SetVisualState"/>, which drives the
    /// <c>&lt;prefix&gt;--state</c> class group;</item>
    /// <item>a pseudo-class twin (<see cref="SusStateTwins"/>) — the only lever that exists for
    /// <c>hover</c> and <c>active</c>, and the one that may be missing.</item>
    /// </list>
    ///
    /// The <c>active</c> column deliberately does NOT look for a prop called <c>Active</c>. Several
    /// components have one, and it means "this item is the selected one", not "the pointer is
    /// pressing it" — using it would put a different state under the label.
    /// </summary>
    public static class SusStoryStates
    {
        public const string Rest = "rest";
        public const string Hover = "hover";
        public const string Focus = "focus";
        public const string Active = "active";
        public const string Disabled = "disabled";
        public const string Error = "error";

        /// <summary>The six columns, in the order the mock-up shows them.</summary>
        public static readonly IReadOnlyList<string> All =
            new[] { Rest, Hover, Focus, Active, Disabled, Error };

        static readonly string[] DisabledProps = { "Disabled" };
        static readonly string[] ErrorProps = { "Error", "HasError", "Invalid" };

        /// <summary>
        /// True when this state can be shown on this instance at all. Answered WITHOUT mutating
        /// the component, so the matrix can decide its columns before it builds a single cell.
        /// </summary>
        public static bool CanForce(SusComponent component, string state)
        {
            if (component == null) return false;
            return state switch
            {
                Hover => SusStateTwins.Has(component, SusStateTwins.HoverClass),
                Active => SusStateTwins.Has(component, SusStateTwins.ActiveClass),
                _ => true,
            };
        }

        /// <summary>
        /// Puts <paramref name="state"/> onto the instance and says how it did it. An unknown
        /// state, or a pseudo-state with no twin, returns <see cref="SusStateForcing.Unsupported"/>
        /// and changes nothing.
        /// </summary>
        public static SusStateForcing Force(SusComponent component, string state)
        {
            if (component == null) return SusStateForcing.Unsupported;

            switch (state)
            {
                case Rest:
                    return SusStateForcing.Rest;

                case Hover:
                    return Twin(component, SusStateTwins.HoverClass);

                case Active:
                    return Twin(component, SusStateTwins.ActiveClass);

                case Focus:
                    // No prop lever on purpose: Focus() needs a panel and a focus controller, and
                    // a matrix cell has neither. The corpus already styles focus with a class.
                    component.SetVisualState("focused");
                    return SusStateForcing.StateClass;

                case Disabled:
                    if (TrySetBool(component, DisabledProps)) return SusStateForcing.Prop;
                    component.SetVisualState("disabled");
                    return SusStateForcing.StateClass;

                case Error:
                    if (TrySetBool(component, ErrorProps)) return SusStateForcing.Prop;
                    component.SetVisualState("error");
                    return SusStateForcing.StateClass;

                default:
                    return SusStateForcing.Unsupported;
            }
        }

        static SusStateForcing Twin(VisualElement element, string twinClass)
        {
            if (!SusStateTwins.Has(element, twinClass)) return SusStateForcing.Unsupported;
            element.AddToClassList(twinClass);
            return SusStateForcing.TwinClass;
        }

        static bool TrySetBool(SusComponent component, IReadOnlyList<string> names)
        {
            var props = component.DescribeProps();
            for (int n = 0; n < names.Count; n++)
            {
                for (int i = 0; i < props.Count; i++)
                {
                    var p = props[i];
                    if (p.ReadOnly) continue;
                    if (p.ValueType != typeof(bool)) continue;
                    if (!string.Equals(p.Name, names[n], StringComparison.Ordinal)) continue;
                    if (p.TrySetValue(true)) return true;
                }
            }
            return false;
        }
    }
}
