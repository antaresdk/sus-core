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
        /// <c>&lt;prefix&gt;--error</c>): the corpus has ZERO <c>:disabled</c> selectors, those
        /// states are class-driven already (plan §2.6). Focus is class-driven too but through a
        /// different group — see <see cref="FocusClass"/>.
        /// </summary>
        StateClass = 2,

        /// <summary>A pseudo-class twin was added (<see cref="SusStateTwins"/>).</summary>
        TwinClass = 3,

        /// <summary>
        /// The focus ring class <see cref="SusStateRoles.KeyboardFocusClass"/> was added — to the
        /// root, or to the child the role registry declares as the ring target (card T-3427,
        /// plan D4 <c>d:da0868</c> and D5 <c>d:2fead6</c>). Told apart from
        /// <see cref="StateClass"/> because it is a DIFFERENT class group: the receipt says
        /// which lever fired, and the whole point of T-3427 was that the two had been confused.
        /// </summary>
        FocusClass = 5,

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
    /// Four levers, in this order of preference:
    /// <list type="number">
    /// <item>the component's own boolean prop, when it has one — then the component's own
    /// bindings put the class on, exactly as they do in a real app;</item>
    /// <item><see cref="SusComponent.SetVisualState"/>, which drives the
    /// <c>&lt;prefix&gt;--state</c> class group;</item>
    /// <item>for <c>focus</c> only, <see cref="SusStateRoles.KeyboardFocusClass"/> on the ring
    /// target the role registry names — the class the corpus paints the ring with (T-3427);</item>
    /// <item>a pseudo-class twin (<see cref="SusStateTwins"/>) — the only lever that exists for
    /// <c>hover</c> and <c>active</c>, and the one that may be missing.</item>
    /// </list>
    ///
    /// Whether a column is drawn at all is a question for <see cref="Declares"/>, and it is asked
    /// first: a state the component's ROLE does not have gets no column even when it could be
    /// forced (card T-3431).
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
        /// True when the component's ROLE has this state at all, i.e. when a column for it says
        /// something (card T-3431, plan §4.1/§4.6, decisions D13 <c>d:f5da73</c> and
        /// D14 <c>d:5ea31f</c>). <c>rest</c> is always declared — it is the absence of a state,
        /// not one of them.
        ///
        /// Surplus equals shortfall: a column labelled <c>error</c> over <c>SusAlert</c> promises
        /// a transition that does not exist (<c>sus-alert--error</c> is a colour VARIANT), and a
        /// column labelled <c>disabled</c> over <c>SusDivider</c> promises one nothing can reach.
        /// Both forge exactly as much as a missing state the role owes.
        ///
        /// Asked BEFORE <see cref="CanForce"/> and independently of it: this answers whether the
        /// column is meaningful, that one whether it is achievable.
        /// </summary>
        public static bool Declares(Type componentType, string state)
        {
            if (state == Rest) return true;
            if (componentType == null) return true;
            return SusStateRoles.Declares(componentType, state);
        }

        /// <summary>
        /// Same question asked of a live instance. The caller that has BOTH an instance and the
        /// story's declared component type should ask about the type it trusts: a probe that
        /// failed to build is null, and null must not be read as "every column" when the story
        /// said which component it is about (witness: <c>kit/world/floating-damage</c>, whose
        /// probe threw and whose matrix therefore came back with four columns over a display
        /// component — card T-3431).
        /// </summary>
        public static bool Declares(SusComponent component, string state) =>
            Declares(component == null ? null : component.GetType(), state);

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
                    // Card T-3427. No prop lever on purpose: Focus() needs a panel and a focus
                    // controller, and a matrix cell has neither. What this used to do instead was
                    // SetVisualState("focused") — the <prefix>--focused class group, which four
                    // kit components out of eighty style and three of those re-derive from a
                    // binding on the next render. The ring the corpus actually draws lives on
                    // .keyboard-focus (SusKeyboardFocus). The two sets did not intersect, which
                    // is the whole reason every focus column was pixel-for-pixel the rest column.
                    // The class goes on the target the role registry names — for a group the ring
                    // rides a child (.sus-tabs__tab), and dressing the root would repeat the same
                    // defect with a better class (D5).
                    SusStateRoles.RingTarget(component).AddToClassList(SusStateRoles.KeyboardFocusClass);
                    return SusStateForcing.FocusClass;

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
