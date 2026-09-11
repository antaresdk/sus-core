using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook
{
    /// <summary>What a role owes the matrix for one state.</summary>
    public enum SusStateDuty
    {
        /// <summary>The role does not have this state: a column for it is a FORGERY (plan D14).</summary>
        No = 0,

        /// <summary>The role owes this state; a missing one is a defect of the component.</summary>
        Required = 1,

        /// <summary>
        /// Owed by the VARIANT that declared interactivity, not by the component as such —
        /// the column is allowed, its absence is not judged.
        /// </summary>
        Variant = 2,
    }

    /// <summary>
    /// The role registry of the state contract (plan
    /// <c>docs-canon/plans/impl/ARCH-20260911-KIT-STATE-CONTRACT.md</c> §4.1–§4.2, decisions
    /// D1 <c>d:a2fe59</c>, D2 <c>d:44b2b4</c>, D5 <c>d:2fead6</c>, D13 <c>d:f5da73</c>,
    /// D14 <c>d:5ea31f</c>, D17 <c>d:083c3c</c>; cards T-3427 and T-3431).
    ///
    /// A state is owed to a component by its ROLE, not by the fact that the component exists.
    /// Seven roles, and the border between them is one question — who takes the focus: itself
    /// (<c>control</c>, <c>input</c>), its child (<c>group</c>), nobody (<c>feedback</c>,
    /// <c>display</c>), a child inside a trap (<c>overlay</c>); <c>surface</c> is a surface whose
    /// interactivity is a VARIANT.
    ///
    /// Why this exists at all: the matrix used to draw all six columns for everything it could
    /// force, so <c>SusDivider</c> carried a <c>disabled</c> column and <c>SusAlert</c> an
    /// <c>error</c> one. A column promising a state the role does not have lies exactly as much
    /// as a missing state the role owes — surplus equals shortfall (D14).
    ///
    /// The table below is a transcription of <see cref="DataSource"/>, which is the canon the
    /// rule <c>R145 state-contract</c> judges the corpus against; the plan sections named above
    /// are its prose. It is data — rows, not a chain of name checks — for the reason D17 gives:
    /// a list of names inside the logic goes stale on the first new component.
    /// </summary>
    public static class SusStateRoles
    {
        /// <summary>Canon of this table — the file the judge of R145 reads (D17).</summary>
        public const string DataSource = "state-roles.json";

        /// <summary>
        /// The class the corpus actually paints the focus ring with
        /// (<c>SusKeyboardFocus</c> of the component library). NOT a pseudo-class: UI Toolkit has no
        /// <c>:focus-visible</c>, so <c>:focus</c> cannot tell a keyboard focus from a mouse
        /// click and is forbidden by <c>KEYBOARD_CONTRACT.md</c>. Because the state
        /// is already class-driven, it is its OWN twin and no <c>sus-state-focus</c> is minted
        /// (D4 <c>d:da0868</c>).
        /// </summary>
        public const string KeyboardFocusClass = "keyboard-focus";

        public const string Control = "control";
        public const string Input = "input";
        public const string Group = "group";
        public const string Surface = "surface";
        public const string Overlay = "overlay";
        public const string Feedback = "feedback";
        public const string Display = "display";

        // ── the data ─────────────────────────────────────────────────────────
        // Rows, not code paths. Letters: r = required, v = variant, - = the role has no such
        // state. Column order is the tail of SusStoryStates.All (rest is not a state a role can
        // owe, it is the absence of one), asserted by SusStateRolesTests.

        static readonly string[] StateOrder =
        {
            SusStoryStates.Hover, SusStoryStates.Focus, SusStoryStates.Active,
            SusStoryStates.Disabled, SusStoryStates.Error,
        };

        static readonly string[] RoleRows =
        {
            "control  | r r r r -",
            "input    | r r v r r",
            "group    | r r r r -",
            "surface  | v v v v -",
            "overlay  | - - - - -",
            "feedback | - - - - -",
            "display  | - - - - -",
        };

        static readonly string[] ComponentRows =
        {
            "control  | SusButton SusCheckbox SusChip SusExpansionPanel SusHoldButton SusLink " +
            "SusRadio SusRepeatButton SusToggle",

            "input    | SusDropdown SusNumberInput SusRangeSlider SusRating SusSelect SusSlider " +
            "SusStepper SusTextfield SusWedgeSlider",

            "group    | SusBottomNav SusBreadcrumbs SusBtnToggle SusCarousel SusChipGroup " +
            "SusContextMenu SusDataTable SusExpansionPanels SusIconRail SusListGroup SusMenu " +
            "SusMenuButton SusPagination SusRadialMenu SusRadioGroup SusTable SusTabs SusTreeView",

            "surface  | SusAppBar SusCard SusDiagnosticsPanel SusEmptyState SusForm SusFormField " +
            "SusScrollView SusToolbar",

            "overlay  | SusBottomSheet SusChoiceDialog SusDialogueBox SusDrawer SusModal " +
            "SusPromptDialog SusSpotlightTour SusTutorialModal",

            "feedback | SusAlert SusBadge SusBanner SusLoadingScreen SusProgressCircular " +
            "SusProgressLinear SusSkeletonLoader SusSnackbar SusSnackbarQueue SusSpinner SusTooltip",

            "display  | SusAvatar SusAvatarGroup SusCellBar SusCooldownWipe SusDivider " +
            "SusFloatingDamage SusHealthBar SusIcon SusImg SusNameplate SusParallaxStack " +
            "SusPieChart SusSpeechBubble SusStatBar SusTextFx SusTimeline SusUnitNameplate",
        };

        /// <summary>
        /// Where the ring goes when it is NOT the root (D5 <c>d:2fead6</c>). For role
        /// <c>group</c> the focus rides a child — <c>SusTabs</c> paints
        /// <c>.sus-tabs__tab.keyboard-focus</c>, never the group box — so a matrix column that
        /// dressed the root would show a copy of rest again, with the right class this time.
        /// A blank target means the root. Groups whose ring is not drawn yet carry no row here:
        /// that is wave 2, not a silent default.
        /// </summary>
        static readonly string[] RingRows =
        {
            "SusListGroup  | sus-list-group__item",
            "SusMenuButton | sus-menu-button",
            "SusPagination | sus-pagination",
            "SusTabs       | sus-tabs__tab",
        };

        /// <summary>
        /// Per-component departures from the role's row. <c>SusFormField</c> is a surface that
        /// wraps a value, so <c>error</c> IS a state of it even though its role has none.
        /// </summary>
        static readonly string[] OverrideRows =
        {
            "SusFormField | error=r",
        };

        // ── the index ────────────────────────────────────────────────────────

        sealed class Entry
        {
            public string Role;
            public string RingTarget;                       // class name, no leading dot
            public Dictionary<string, SusStateDuty> Duties; // role row plus overrides
        }

        static Dictionary<string, Dictionary<string, SusStateDuty>> _roles;
        static Dictionary<string, Entry> _index;
        static readonly Dictionary<string, Entry> Declared = new(StringComparer.Ordinal);

        static void Build()
        {
            if (_index != null) return;

            _roles = new Dictionary<string, Dictionary<string, SusStateDuty>>(StringComparer.Ordinal);
            for (int i = 0; i < RoleRows.Length; i++)
            {
                var parts = RoleRows[i].Split('|');
                var duties = new Dictionary<string, SusStateDuty>(StringComparer.Ordinal);
                var letters = parts[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int s = 0; s < StateOrder.Length && s < letters.Length; s++)
                {
                    duties[StateOrder[s]] = letters[s] switch
                    {
                        "r" => SusStateDuty.Required,
                        "v" => SusStateDuty.Variant,
                        _ => SusStateDuty.No,
                    };
                }
                _roles[parts[0].Trim()] = duties;
            }

            _index = new Dictionary<string, Entry>(StringComparer.Ordinal);
            for (int i = 0; i < ComponentRows.Length; i++)
            {
                var parts = ComponentRows[i].Split('|');
                var role = parts[0].Trim();
                var names = parts[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int n = 0; n < names.Length; n++)
                {
                    _index[names[n]] = new Entry { Role = role, Duties = _roles[role] };
                }
            }

            for (int i = 0; i < RingRows.Length; i++)
            {
                var parts = RingRows[i].Split('|');
                if (_index.TryGetValue(parts[0].Trim(), out var e)) e.RingTarget = parts[1].Trim();
            }

            for (int i = 0; i < OverrideRows.Length; i++)
            {
                var parts = OverrideRows[i].Split('|');
                if (!_index.TryGetValue(parts[0].Trim(), out var e)) continue;
                var duties = new Dictionary<string, SusStateDuty>(e.Duties, StringComparer.Ordinal);
                var pairs = parts[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int p = 0; p < pairs.Length; p++)
                {
                    var kv = pairs[p].Split('=');
                    if (kv.Length != 2) continue;
                    duties[kv[0].Trim()] = kv[1].Trim() switch
                    {
                        "r" => SusStateDuty.Required,
                        "v" => SusStateDuty.Variant,
                        _ => SusStateDuty.No,
                    };
                }
                e.Duties = duties;
            }
        }

        static Entry Find(Type type)
        {
            if (type == null) return null;
            Build();
            for (var t = type; t != null; t = t.BaseType)
            {
                if (Declared.TryGetValue(t.Name, out var declared)) return declared;
                if (_index.TryGetValue(t.Name, out var e)) return e;
            }
            return null;
        }

        // ── what the shell and the tests read ────────────────────────────────

        /// <summary>Names of the seven roles, in the order §4.1 lists them.</summary>
        public static readonly IReadOnlyList<string> AllRoles =
            new[] { Control, Input, Group, Surface, Overlay, Feedback, Display };

        /// <summary>The five states a role can owe — <c>rest</c> is not one of them.</summary>
        public static IReadOnlyList<string> States => StateOrder;

        /// <summary>Every component name the registry knows, including test declarations.</summary>
        public static IEnumerable<string> Names
        {
            get
            {
                Build();
                foreach (var name in _index.Keys) yield return name;
            }
        }

        /// <summary>Role of the component, or null when the registry has never heard of it.</summary>
        public static string RoleOf(Type type) => Find(type)?.Role;

        /// <summary>
        /// Role registered under this exact type NAME, or null. The seam the packages' own tests
        /// use: this package does not reference the component library, so the registry it carries can
        /// only be asserted by name.
        /// </summary>
        public static string RoleOfName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            Build();
            if (Declared.TryGetValue(typeName, out var declared)) return declared.Role;
            return _index.TryGetValue(typeName, out var e) ? e.Role : null;
        }

        /// <summary>Component names carrying this role, in registry order.</summary>
        public static IReadOnlyList<string> NamesOf(string role)
        {
            Build();
            var list = new List<string>();
            foreach (var pair in _index)
            {
                if (string.Equals(pair.Value.Role, role, StringComparison.Ordinal)) list.Add(pair.Key);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>What the ROLE itself owes for a state, before per-component departures.</summary>
        public static SusStateDuty RoleDuty(string role, string state)
        {
            Build();
            if (role == null || !_roles.TryGetValue(role, out var duties)) return SusStateDuty.No;
            return duties.TryGetValue(state, out var duty) ? duty : SusStateDuty.No;
        }

        /// <summary>What the registry owes for a component addressed by NAME.</summary>
        public static SusStateDuty DutyOfName(string typeName, string state)
        {
            if (string.IsNullOrEmpty(typeName)) return SusStateDuty.Variant;
            Build();
            if (!Declared.TryGetValue(typeName, out var e) && !_index.TryGetValue(typeName, out e))
                return SusStateDuty.Variant;
            return e.Duties.TryGetValue(state, out var duty) ? duty : SusStateDuty.No;
        }

        /// <summary>Ring-target class declared for a component addressed by NAME, or null.</summary>
        public static string RingTargetClassOfName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            Build();
            if (Declared.TryGetValue(typeName, out var e)) return e.RingTarget;
            return _index.TryGetValue(typeName, out e) ? e.RingTarget : null;
        }

        /// <summary>Role of a live instance, or null.</summary>
        public static string RoleOf(SusComponent component) => RoleOf(component?.GetType());

        /// <summary>True when this type (or a base of it) carries a role.</summary>
        public static bool Knows(Type type) => Find(type) != null;

        /// <summary>
        /// What the role owes for this state. An UNKNOWN component answers
        /// <see cref="SusStateDuty.Variant"/>: the registry covers the first corpus today and
        /// the second one in wave 6, and a component outside it must keep the columns it had —
        /// silence of a registry is not a statement about a component.
        /// </summary>
        public static SusStateDuty Duty(Type type, string state)
        {
            var entry = Find(type);
            if (entry == null) return SusStateDuty.Variant;
            return entry.Duties.TryGetValue(state, out var duty) ? duty : SusStateDuty.No;
        }

        /// <summary>Convenience over <see cref="Duty(Type,string)"/> for a live instance.</summary>
        public static SusStateDuty Duty(SusComponent component, string state) =>
            Duty(component?.GetType(), state);

        /// <summary>True when the role owes or allows the state, i.e. a column may be drawn.</summary>
        public static bool Declares(Type type, string state) => Duty(type, state) != SusStateDuty.No;

        /// <summary>
        /// True when the role owes or allows at least one state. False is what makes the matrix
        /// disappear entirely for <c>display</c>, <c>feedback</c> and <c>overlay</c> (§4.6).
        /// </summary>
        public static bool DeclaresAnyState(Type type)
        {
            for (int i = 0; i < StateOrder.Length; i++)
            {
                if (Declares(type, StateOrder[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Where <see cref="KeyboardFocusClass"/> has to land on this instance: the declared
        /// child when the registry names one and the instance really has it, the root otherwise.
        /// Never null for a non-null instance — a focus column that found no target would be the
        /// copy of rest this whole contract is about.
        /// </summary>
        public static VisualElement RingTarget(SusComponent component)
        {
            if (component == null) return null;
            var target = Find(component.GetType())?.RingTarget;
            if (string.IsNullOrEmpty(target)) return component;
            if (component.ClassListContains(target)) return component;
            var child = component.Q(className: target);
            return child ?? component;
        }

        /// <summary>Class name of the declared ring target, or null when the ring rides the root.</summary>
        public static string RingTargetClass(Type type) => Find(type)?.RingTarget;

        // ── the test seam ────────────────────────────────────────────────────

        /// <summary>
        /// Declares a role for a type the shipped table does not carry — how a test gives an
        /// engine fixture a role without minting a kit component. Undone by <see cref="Reset"/>.
        /// </summary>
        public static void Declare(string typeName, string role, string ringTargetClass = null)
        {
            if (string.IsNullOrEmpty(typeName)) return;
            Build();
            if (!_roles.TryGetValue(role, out var duties)) return;
            Declared[typeName] = new Entry { Role = role, Duties = duties, RingTarget = ringTargetClass };
        }

        /// <summary>Drops every <see cref="Declare"/> — the reset a test needs.</summary>
        public static void Reset() => Declared.Clear();
    }
}
