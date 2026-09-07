using System;

namespace Sharq.Core
{
    /// <summary>
    /// Non-generic access to a <see cref="Prop{T}"/> for tooling that does not know
    /// <c>T</c> at compile time (introspection, generated control panels, probes).
    ///
    /// Implemented EXPLICITLY by <see cref="Prop{T}"/>, so the typed surface of a prop is
    /// unchanged: <c>((ISusPropAccess)prop).BoxedValue</c> is the only way in.
    ///
    /// Reading <see cref="BoxedValue"/> is deliberately "invisible": it neither registers a
    /// reactive dependency (<see cref="DependencyTracker"/>) nor counts towards
    /// <see cref="ReadCount"/>, so a tool that dumps every prop of a component cannot make a
    /// dead prop look alive. Writing it goes through the normal setter, so watchers fire.
    /// </summary>
    public interface ISusPropAccess : IReactiveSource
    {
        /// <summary>The <c>T</c> of <c>Prop&lt;T&gt;</c>.</summary>
        Type ValueType { get; }

        /// <summary>Current value, boxed. Get: untracked and uncounted. Set: normal setter.</summary>
        object BoxedValue { get; set; }

        /// <summary>
        /// How many times <c>Value</c> was READ through the tracked getter since construction.
        /// Zero plus <see cref="HasObservers"/> false is the "declared but nobody reads it"
        /// signal (see <c>SusPropInfo.Dead</c>).
        /// </summary>
        int ReadCount { get; }

        /// <summary>True while anything is subscribed to the value (Changed / binding / effect).</summary>
        bool HasObservers { get; }

        /// <summary>Subscribe to any value change; dispose to unsubscribe.</summary>
        IDisposable SubscribeChanged(Action onChanged);
    }
}
