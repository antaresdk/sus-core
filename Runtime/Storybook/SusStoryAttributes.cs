using System;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// Marks a class as ONE story of the storybook engine
    /// (plan ARCH-20260907-STORYBOOK-ENGINE.md §4.1).
    ///
    /// The class must implement <see cref="ISusStory"/> and have a public parameterless
    /// constructor — the registry never builds a type from a STRING name, only from the
    /// <see cref="Type"/> the attribute was found on (§9 risk 3: IL2CPP / managed stripping).
    ///
    /// <code>
    /// [SusStory("kit/atoms/select", Name = "Select", Purpose = "choose from a list")]
    /// public sealed class SelectStory : ISusStory { … }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class SusStoryAttribute : Attribute
    {
        /// <summary>Address of the story: <c>&lt;package&gt;/&lt;group&gt;/&lt;slug&gt;</c>.</summary>
        public string Id { get; }

        /// <summary>
        /// Human name shown in zone A and in the header (<c>Select</c>). Defaults to the slug
        /// with the first letter capitalised when not given.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Alias of <see cref="Name"/> kept because plan §4.1 spells the property <c>Title</c>
        /// while card T-3033 spells it <c>Name</c>. Both write the same field, so a story
        /// written against either text compiles.
        /// </summary>
        public string Title
        {
            get => Name;
            set => Name = value;
        }

        /// <summary>One line of "what this is for" — shown next to the name (buyer-facing).</summary>
        public string Purpose { get; set; }

        /// <summary>
        /// The catalogue component this story is the story OF (plan §4.1a, decision D19):
        /// <c>[SusStory("game/inventory/shell", Component = typeof(SusInventory))]</c>.
        ///
        /// Before this property the link "story ↔ component" was GUESSED from the first word of
        /// <see cref="Purpose"/>, and two whole rule layers (R134 <c>story-orphan</c> and
        /// <c>ledger-ghost</c>) judged 17 catalogue holes by the first word of an English
        /// sentence — a sentence that legitimately starts with <c>Layout</c>, <c>Preset</c> or
        /// <c>Same</c>. A type is the machine side of that link: it is renamed by the compiler,
        /// not by a proof-reader, and it cannot resolve to a word that is not a component.
        ///
        /// A story that shows no single catalogue component (a service demo, a showcase set, a
        /// screen assembled in C# with no <c>.sharq</c> face) leaves this null and says WHY in
        /// <see cref="NoComponent"/> — "no face" must be a RECORD, not a silence, or the layer
        /// cannot tell it from "the face was never named".
        /// </summary>
        public Type Component { get; set; }

        /// <summary>
        /// Why this story names no <see cref="Component"/>. One line, in the same buyer-facing
        /// English as <see cref="Purpose"/>: what the stage actually shows instead of one
        /// catalogue component.
        /// </summary>
        public string NoComponent { get; set; }

        /// <summary>Sort key inside its group; equal orders fall back to <see cref="Name"/>.</summary>
        public int Order { get; set; }

        /// <summary>
        /// Prop that carries the CLOSED axis of variants (plan ARCH-20260911-STORYBOOK-SHELL.md
        /// D26, card T-3379). Defaults to <see cref="SusStoryAxis.DefaultPropName"/>; name another
        /// prop only when the variants of this component genuinely live somewhere else.
        /// </summary>
        public string Axis { get; set; }

        /// <summary>
        /// The legal values of <see cref="Axis"/>, in the order the matrix should draw its rows
        /// and zone D its buttons:
        /// <c>[SusStory("kit/atoms/alert", AxisValues = new[]{"tonal","outlined","text","flat"})]</c>.
        ///
        /// This is the PRODUCER half of the closed axis (<see cref="SusStoryAxis"/>). It is for
        /// the case where only the STORY knows the enumeration: the component declares
        /// <c>Prop&lt;string&gt; Variant</c> and branches on four literals in its own template,
        /// but never clamped the prop with <c>UseAllowed</c> - so the engine could not tell the
        /// four apart from any other string, printed one matrix row, and gave the buyer a text
        /// field to guess the spelling in.
        ///
        /// When the component DOES clamp the prop, that set wins and this one is only checked
        /// against it (a mismatch is logged, not silently preferred): the runtime coerces to the
        /// component's set no matter what a story says, so a story that disagrees is wrong, and
        /// hiding that would move the guess from the buyer to the author.
        /// </summary>
        public string[] AxisValues { get; set; }

        /// <summary>
        /// How expensive one instance of this component is to build (plan §0.3). A
        /// <see cref="SusStoryWeight.Heavy"/> story keeps its state matrix COLLAPSED regardless of
        /// the cell budget: a table, an inventory, a battle grid or a minimap costs more per
        /// instance than the matrix is worth, and the storybook must open instantly on every
        /// story, not only on the cheap ones.
        /// </summary>
        public SusStoryWeight Weight { get; set; } = SusStoryWeight.Normal;   // card T-3038

        public SusStoryAttribute(string id)
        {
            Id = id;
        }
    }

    /// <summary>
    /// Opt-in marker on an assembly that CAN carry stories. The registry scans only assemblies
    /// carrying this attribute, so a project with 200 assemblies does not pay a full
    /// <c>GetTypes()</c> sweep on every start (§4.1).
    ///
    /// Declaring the attribute WITHOUT any <c>[SusStory]</c> type is legal and meaningful: the
    /// package gets a tab in zone A with the "no stories declared" plate instead of vanishing
    /// silently — "the package is loaded, its story provider is missing" is the message the
    /// mock-up asks for, and it can only be said about a package that announced itself.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
    public sealed class SusStoryAssemblyAttribute : Attribute
    {
        /// <summary>
        /// Short package key used as the first segment of story ids and as the tab label
        /// (<c>kit</c>, <c>game</c>, <c>router</c>). Optional — when omitted the keys are taken
        /// from the ids of the stories found in this assembly.
        /// </summary>
        public string Package { get; set; }

        /// <summary>
        /// Full package id (<c>com.sharq-it.sus.core</c>) shown on the empty-package plate.
        /// Optional: in the Editor it is resolved from the assembly's <c>PackageInfo</c>.
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// Package version shown at the bottom of zone A. Optional and only a FALLBACK: in the
        /// Editor the live <c>PackageInfo.version</c> wins, because a literal here goes stale
        /// the moment release bumps the package (facts are derived, not declared).
        /// </summary>
        public string Version { get; set; }
    }
}
