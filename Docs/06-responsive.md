# 6. Adaptive layout (breakpoints)

The payoff: one `.sharq` screen ships on desktop and mobile without a separate build. Point a
screen at the breakpoint axis below and its tokens reflow the layout for a narrow panel — instead
of hand-rebuilding the screen for mobile.

Screen-size adaptation uses **one** axis: `SusBreakpointService`.

There is no High/Low resolution service (`SusResolutionService` was removed) and no
automatic panel scale tied to monitor size. Density
(`.density-compact` / `.density-comfortable`) is a **manual product preset**, not
screen-size adaptation.

PanelSettings for samples use `ConstantPixelSize` so the breakpoint width tracks
the panel / Game view width (no Unity `ScaleWithScreenSize` auto-scale).

```csharp
// Inside SusComponent (injected automatically):
BreakpointService.Current.Value  // Prop<Breakpoint>
BreakpointService.IsMobile       // Computed<bool> — width ≤ 1024
BreakpointService.IsTablet       // Computed<bool> — Md | Lg
BreakpointService.IsDesktop      // Computed<bool> — width ≥ 1920
```

## How width is measured

Classes (`.breakpoint-*`) always sit on the **token cascade root**
(`SusBootstrap.TokenCascadeRoot` — the same element that holds theme/density).

Width source (same path the old resolution service used):

1. **Primary:** `cascadeRoot.resolvedStyle.width` (and `layout.width` if needed)
2. **Fallback:** panel `visualTree` size
3. **Last resort:** Editor Game view size / `Screen.width` (only when root is not laid out yet)

Updates are driven by:

- `SusBreakpointService.Attach(root)` from `LoadTokenCascade` / `Mount`
- Every `SusComponent` `GeometryChangedEvent` → push cascade-root width
  (explicit `Attach(cascadeRoot).Update(width)`, same pattern as deleted
  `SusResolutionService.Update(cascadeRoot, width)`)
- Light poll + panel `visualTree` geometry hook (Editor Game view drag often
  skips geometry on the content root alone)

Do **not** use a child component’s own width to pick the root class — that would
flip tokens for the whole tree incorrectly.

### Editor tip

If Game view is locked to a fixed resolution (e.g. 1920×1080), panel width may
not change when you only resize the docked window. Use **Free Aspect** and drag
width across the thresholds below, or force a breakpoint in Storybook
(`Breakpoint` select → `sm` / `md` / …).

## Breakpoints

| Name | Width | Root class |
|------|--------|------------|
| `Sm` | ≤ 640px | `.breakpoint-sm` |
| `Md` | ≤ 1024px | `.breakpoint-md` |
| `Lg` | ≤ 1440px | `.breakpoint-lg` |
| `Xl` | ≤ 1920px | `.breakpoint-xl` |
| `Xxl` | > 1920px | `.breakpoint-2xl` |

## Tokens (`--sk-*`)

UI packages built on core (kit, game) resize spacing / heights / fonts under `.breakpoint-*`
through their own token sheet. Components already use `var(--sk-button-height)`,
`var(--sk-space-16)`, `var(--sk-font-body)`, etc. — they react automatically when
the root class changes.

Not every `--sk-*` token moves with the breakpoint. Sizes split into families — spacing,
control height, font size and a few others resize; border width, radius (except a pill's,
which follows its own height), letter spacing, 9-slice borders and small optical-alignment
nudges stay fixed on every breakpoint, on purpose. See
[Design tokens §6.1](https://sus-ui.dev/docs/guide/DESIGN_TOKENS) for the full family list
and the reasoning behind each invariant one.

Breakpoint and density (`.density-compact` / `.density-comfortable`) both narrow down "how
much room is there", so a project should be able to combine them and get one predictable
result rather than have one silently override the other.

> **Status:** combining breakpoint and density predictably, and collecting every breakpoint ×
> density combination into one generated token sheet, is part of an upcoming kit release — see
> the kit changelog. Today the two axes can still compete on some breakpoints; this note will be
> replaced with a plain statement of the new behavior once that release ships.

## Use in a component

```csharp
public class ResponsivePanel : SusComponent
{
    public Prop<float> PanelWidth = new(300f);

    protected override void Created()
    {
        Watch(BreakpointService.Current, (old, bp) =>
        {
            PanelWidth.Value = bp >= Breakpoint.Xl ? 400f : 300f;
        });
    }
}
```

## USS: resize through a token, not a breakpoint selector

Prefer a token over hand-writing a `.breakpoint-*` selector on a project class — the token
already carries the right value for every breakpoint, and it keeps working once breakpoint and
density are resolved together (see the status note above):

```css
/* preferred — the token already varies by breakpoint */
.responsive-panel { width: var(--sk-tooltip-max-width, 300px); }
```

```css
/* still parses, but a project override of this shape can stop winning once breakpoint and
   density combine with higher specificity — override the token instead */
.responsive-panel { width: 300px; }
.breakpoint-xl .responsive-panel { width: 400px; }
```

## API

```csharp
public class SusBreakpointService
{
    public Prop<Breakpoint> Current { get; }
    public Computed<bool> IsMobile { get; }   // ≤ 1024
    public Computed<bool> IsTablet { get; }   // Md | Lg
    public Computed<bool> IsDesktop { get; }  // ≥ 1920

    /// <summary>When set, width polling is ignored (Storybook / QA).</summary>
    public Breakpoint? Override { get; }

    public static SusBreakpointService Attach(VisualElement root);
    public static SusBreakpointService For(VisualElement root);
    public static SusBreakpointService For(SusComponent component);

    public void Update(float logicalWidth);
    public void UpdateFromElement(VisualElement el);
    public void SetOverride(Breakpoint? breakpoint); // null = resume auto
}
```

`Attach` is idempotent and is the correct entry point (there is no
`SusBreakpointService.Instance`).
