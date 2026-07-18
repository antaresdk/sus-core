# 6. Adaptive layout

`SusBreakpointService` is a reactive service that tracks screen width.

```csharp
// Inside SusComponent (injected automatically):
BreakpointService.Current.Value  // Prop<Breakpoint>
BreakpointService.IsMobile // Computed<bool> - width ≤ 1024
BreakpointService.IsTablet       // Computed<bool> — 1025–1440
BreakpointService.IsDesktop // Computed<bool> - width ≥ 1920
```

## Breakpoints

| Name | Width |
|-----|--------|
| `Sm` | ≤ 640px |
| `Md` | ≤ 1024px |
| `Lg` | ≤ 1440px |
| `Xl` | ≤ 1920px |
| `Xxl` | > 1920px |

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

The root element gets a class`breakpoint-{bp}`:

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
    public Computed<bool> IsTablet { get; }   // 1025–1440
    public Computed<bool> IsDesktop { get; }  // ≥ 1920
}
```
