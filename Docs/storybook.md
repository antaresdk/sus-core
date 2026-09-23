# Storybook: a live playground for your own components

The storybook is a component browser built **into `sus-core`**: point it at a `UIDocument`, and
it lists every declared story, mounts a real instance of the component on a stage, and derives a
full control panel straight from that component's public `Prop<T>` fields — no hand-written
control list to keep in sync.

It ships as its own assembly, `com.sharq-it.sus.core.storybook`
(`Runtime/Storybook/com.sharq-it.sus.core.storybook.asmdef`), referencing only
`com.sharq-it.sus.core`. **It does not depend on `sus-router`** — navigation between stories is a
small self-contained layer (below), not a routed app. Nothing else in core references this
assembly (`autoReferenced: false`), so **a player build that never imports a stories sample does
not contain it**: no `.dll`, no stylesheet, 0 bytes. The assembly appears in a build the moment a
package's stories are imported and reference it — the cost is opt-in, per project, not per buyer.

## Opening it

1. Add a `UIDocument` to a scene and put `SusStorybookBehaviour` on the same GameObject
   (`Add Component → SUS → Storybook Host`). Enter Play (or Editor's UI Toolkit preview) — the
   shell mounts itself and lists every package that declared stories.
2. Downstream UI packages ship their own component stories the same way: a `Samples~` folder
   whose own `asmdef` references `com.sharq-it.sus.core.storybook` plus that package. Import the
   **`Stories`** sample from a package's Package Manager entry and open the one scene it drops
   (`UIDocument` + `SusStorybookBehaviour`) — that package's own docs name the sample and the
   scene file. The scene never lists stories itself, so it does not grow or go stale as the
   catalog does.
3. Every such package's stories arrive the same way — the sample is opt-in per package, and a
   project only pays for the ones it imports.

A package's `SusStoryAssembly` marker also says what the assembly is *for* (below), and the tab
follows from that:

- **Product, no stories declared** — the package still gets a tab, with a plate that says so,
  rather than vanishing silently, so "I imported the wrong sample" and "this package has no
  stories" read as two different things. This is where the plate earns its place: stories that
  arrive by hand-importing a `Samples~` folder (kit, game, a skin package). A project can also ask
  for this same tab and plate before the package ships any story at all, without any marker, via
  `SusStoryRegistry.DeclarePackage(key, packageId, version)`.
- **A test bench** (`Kind = SusStoryPackageKind.Fixture`) gets no tab and no plate by default —
  its stories exist to be reached by direct address from a suite. Hidden by default is not the
  same as gone: the `Window/SUS/Storybook/Show Engine Fixtures` menu lists every fixture package
  on that machine, and opening an address carrying `?fixtures=1` lists it too, so a link shared
  with someone whose menu toggle is off still opens on the bench tab.
- **No marker, and no `DeclarePackage` call for that key** — no tab.

## Writing your own story

```csharp
using Sharq.Core.Storybook;

[SusStory("myapp/atoms/price-tag", Name = "Price Tag", Purpose = "shows a price with currency")]
public sealed class PriceTagStory : ISusStory
{
    public SusComponent Create() => new PriceTag();

    public void Configure(SusStoryContext ctx)
    {
        var tag = (PriceTag)ctx.Component;
        tag.Amount.Value = 19.99f;
        // A prop this story deliberately leaves without a control MUST say why —
        // an exclusion without a reason is a hole, not a decision:
        ctx.Exclude(nameof(PriceTag.InternalSku), "debug-only, not buyer-facing");
    }
}
```

That is the whole contract: a class implementing `ISusStory`, marked with `[SusStory]`, in an
assembly that opts in with `[assembly: SusStoryAssembly]` (once per assembly, in an
`AssemblyInfo.cs`):

```csharp
[assembly: Sharq.Core.Storybook.SusStoryAssembly(Package = "myapp")]
```

`SusStoryAssembly` also carries `Kind`, defaulting to `SusStoryPackageKind.Product`. Leave it
alone for a package a buyer imports — that default is what earns the assembly its tab (and its
empty-package plate, above). A test bench that only exists to give a suite addressable stories
sets `Kind = SusStoryPackageKind.Fixture` instead: it drops out of the tab list, the tree and the
sweep by default, and its stories always resolve by direct address regardless — but "by default"
is not "never": the same menu toggle and `?fixtures=1` address described above put it back in the
listing.

The address `<package>/<group>/<slug>` is the story's permanent id — it is what deep links and
screenshots key on, so treat renaming it like renaming a public API. The registry finds stories by
scanning only assemblies carrying `SusStoryAssembly` (not every assembly in the project), and it
only ever builds a type it already has as a `System.Type` from that scan — never from a string —
so nothing about it depends on how a stripped/IL2CPP build resolves names.

If your stories are generated rather than one-class-per-story (skin presets, data-driven
compositions), implement `ISusStoryProvider.Enumerate()` instead — it is discovered the same way.

## How the control panel is derived

The panel does not know your component's type. It asks three questions that every `SusComponent`
answers about itself — `DescribeProps()`, `DescribeAllowed()`, `DescribeEvents()` — and builds a
control for each prop from its declared type:

| Prop shape | Control |
|---|---|
| `Prop<bool>` | toggle |
| `Prop<string>` with an allowed set (`UseAllowed`) | segmented buttons (≤ 5 values) or a dropdown |
| `Prop<string>` whose name ends in `Icon` | icon picker (grid of glyphs, searchable) |
| `Prop<string>`, anything else | text field |
| `Prop<int>` / `Prop<float>` | slider + numeric field, range from `[SusRange]` (0…100 if absent) |
| `Prop<Color>` | swatch, skin tokens first, arbitrary color second |
| `Prop<List<T>>` | row-count editor with expand |
| anything read-only / a plain object | value display only, updates live from events |
| an `On*` event field | a line in the event log (zone E) when it fires, with its argument |

The footer always shows **props N · controls M**. `M` below `N` without a matching
`ctx.Exclude(...)` reason is a story defect, not a cosmetic gap — the panel names it
(`dead props:`, `uncovered:`) instead of staying quiet about it.

### Ranges and dependencies

```csharp
[SusRange(0, 5, 0.5)]                      public Prop<float> Rating = new(0);
[SusRange(0, 100, "%")]                    public Prop<int>   Progress = new(0);
[SusDependsOn(nameof(ContentMode), "icon")] public Prop<string> Icon = new("");
[SusDependsOn(nameof(Clearable))]           public Prop<string> ClearIcon = new("x");
```

`[SusRange]` gives the slider real bounds instead of a guessed 0…100. `[SusDependsOn]` disables a
control and names the reason ("visible when ContentMode = icon") instead of rendering a control
that silently does nothing — several attributes on one field are combined with AND.

## Environment (zone B)

A row of chips lets you flip breakpoint, density, theme, scale and input device without leaving
the story — switching an axis never resets the controls you were driving. A skin package can
register its own axis (e.g. `skin`) through `SusStoryEnvAxisRegistry`; core ships no skin axis
itself, so the chip for it only appears once such a package registers one. The same is true of
`locale` — its provider is whichever localization package is present.

## Sharing a story: the address bar

The address of a story is `#/<package>/<group>/<slug>?Prop=val&env.axis=val` — the query carries
only what differs from the story's own defaults, so an untouched story has a short, stable link
and a shared one says exactly what changed. **Share** copies this text; **back / forward** (also
`Alt+←` / `Alt+→`) move through an in-memory history — none of it depends on a network round trip,
so it works the same in the Editor as it does in a browser build. Opening the site with a link
already in the address bar highlights the right entry in the sidebar and mounts the right story
without a page reload.

## Matrix and probe (zones C and E)

Below the control panel, a `Variant × State` matrix renders one real instance per cell (not a
drawing) for every value of the component's `Variant` axis against six columns: rest, hover,
active, focused, disabled, error. Only a component shipping a `sus-state-hover` /
`sus-state-active` twin class gets the hover and active columns — Unity UI Toolkit has no way to
force a pseudo-class from code, so a column without a twin is left out rather than shown wrong.
Above 24 cells, or on a story marked `Weight = SusStoryWeight.Heavy` (a data table, an inventory,
a battle grid), the matrix starts collapsed and builds nothing until you ask for it.

The probe strip under the live instance lists every event the component fired this session (with
its argument) and a health count from the same anomaly detector the MCP probe uses. **Frame
comparison prints `—`, not a false green:** canon-vs-live screenshot diffing is a capture pipeline
that plugs into this same contract but has not landed yet — the probe honestly says "don't know"
rather than reporting a comparison it cannot make.

## What this is not, today

- The **old per-package storybook shells** (`Samples~/Storybook` in kit and game) still exist
  side by side with this engine while stories are migrated onto it one package at a time — a
  sample named `Storybook` and one named `Stories` are not the same thing yet.
- **Phosphor's full icon set is not bundled.** The icon picker reads whatever is registered in
  `SusIconRegistry` — core's own ~127 icons always are; the 9,000+ Phosphor glyphs appear only
  after a project imports the separate `PhosphorIcons` sample from `sus-core`.
