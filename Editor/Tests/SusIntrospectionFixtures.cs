using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>Plain model object — the "bound data" case of the prop table (§4.3).</summary>
    public sealed class IntrospectionModel
    {
        public string Id;
        public int Count;
        public override string ToString() => Id + "#" + Count;
    }

    /// <summary>Enum prop case.</summary>
    public enum IntrospectionMode
    {
        Compact,
        Cozy,
        Roomy,
    }

    /// <summary>
    /// Fixture 1 of 3 for the introspection contract (T-3031): one component carrying every prop
    /// shape the control table of ARCH-20260907-STORYBOOK-ENGINE §4.3 names — bool (state and
    /// behaviour), string, icon-string, enum, int, float, colour, list, model — plus both
    /// <c>UseAllowed</c> forms, both attributes and all three event shapes.
    /// </summary>
    public class SusIntrospectionFixture : SusComponent
    {
        // ── state / behaviour bools ──────────────────────────────────────
        public Prop<bool> Disabled = new(false);
        public Prop<bool> Clearable = new(false);

        // ── content ──────────────────────────────────────────────────────
        public Prop<string> Text = new("hello");
        public Prop<List<string>> Items = new(new List<string> { "a", "b" });

        // ── axes ─────────────────────────────────────────────────────────
        public Prop<string> Size = new("md");                 // allowed set, static
        public Prop<string> Variant = new("solid");           // axis by name, no set
        public Prop<IntrospectionMode> Mode = new(IntrospectionMode.Cozy);
        public Prop<Color> Accent = new(Color.red);
        public Prop<int> PageSize = new(10);                  // allowed set, dynamic

        // ── icon + its declared dependency ───────────────────────────────
        public Prop<string> ContentMode = new("text");

        [SusDependsOn(nameof(ContentMode), "icon")]
        public Prop<string> Icon = new("star");

        // ── numeric ranges ───────────────────────────────────────────────
        [SusRange(0, 10, 0.5)]
        public Prop<float> Value = new(3f);

        [SusRange(0, 100, "%")]
        public Prop<int> Progress = new(0);

        public Prop<int> DebounceMs = new(120);               // numeric, behaviour by name

        // ── data ─────────────────────────────────────────────────────────
        public Prop<IntrospectionModel> Model = new(null);

        // ── declared and never read by anyone (the R124-family signal) ───
        public Prop<string> DeadProp = new("");

        // ── events ───────────────────────────────────────────────────────
        public Action OnPing;
        public Action<string> OnTextChanged;
        public Action<int> OnCount;

        /// <summary>Not an event: wrong delegate shape. Must be ignored by DescribeEvents.</summary>
        public Func<int> NotAnEvent;

        /// <summary>Not an event: right shape, wrong name prefix. Must be ignored.</summary>
        public Action Handler;

        /// <summary>Backing list of the dynamic allowed set — swapped by tests.</summary>
        public List<int> PageSizes = new() { 10, 25, 50 };

        protected override void Build()
        {
            UseAllowed(
                Size,
                new[] { "sm", "md", "lg" },
                "md",
                new Dictionary<string, string> { ["small"] = "sm", ["large"] = "lg" },
                propName: "SusIntrospectionFixture.Size");

            UseAllowed(PageSize, () => PageSizes, 10, "SusIntrospectionFixture.PageSize");
        }
    }

    /// <summary>
    /// Fixture 2 of 3: a component with no props, no allowed sets and no events — the empty
    /// answer must be an empty list, never null and never a throw.
    /// </summary>
    public sealed class SusIntrospectionEmptyFixture : SusComponent
    {
        protected override void Build() { }
    }

    /// <summary>
    /// Fixture 3 of 3: inheritance. Props and events declared on the BASE component belong to the
    /// derived component's public API too — a control panel that only saw the leaf type would
    /// lose most of a real kit component, whose axes sit on shared bases.
    /// </summary>
    public sealed class SusIntrospectionDerivedFixture : SusIntrospectionFixture
    {
        public Prop<string> Extra = new("x");
        public Action<bool> OnToggled;

        protected override void Build()
        {
            base.Build();
            UseAllowed(Extra, new[] { "x", "y" }, "x", propName: "Derived.Extra");
        }
    }
}
