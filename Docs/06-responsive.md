# 6. Adaptive layout (breakpoints)

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

Downstream UI packages override spacing / heights / fonts under `.breakpoint-*`
(e.g. a downstream `*-tokens.uss` sheet). Components already use `var(--sk-button-height)`,
`var(--sk-space-16)`, `var(--sk-font-body)`, etc. — they react automatically when
the root class changes.

Breakpoint token blocks are ordered **after** density in the kit token sheet so
overlapping keys (heights, spacing) prefer the active breakpoint when both
classes are present.

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

## USS via breakpoint classes

```css
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
