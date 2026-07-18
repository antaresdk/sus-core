# 12. Running samples (Samples~)

The package contains 3 standalone examples in `Samples~/`.

## Import

`Window` → ` Package Manager` → select ` SusCore` → button ` Import`in the Samples section.
After import: `Assets/Samples/SusCore/<version>/`.

## Scene setup

1. Create empty `GameObject` → Add Component → ` UIDocument`
2. Add Component → example script (`SusBootstrapExample`, ` SusKeepAliveExample`or ` CompExample`)
3. `[RequireComponent(typeof(UIDocument))]` guarantees the presence of a UIDocument
4. The scene must have `EventSystem`

## Comp Example (v1.0.49+)

**Goals:**
- `SetChildProp` — transfer of literal prop (case-insensitive, mutates `.Value`)
- `BindChildProp` — reactive prop (`:variant="expr"` in `.sharq`)
- Checking that `Prop<T>` child is not replaced by a new copy

**In code:** parent (`CompScreen`) creates a child ` MockChild`and sends props.
In `.sharq` this is equivalent to `<sus:SusButton variant="secondary" :label="LabelProp" />`.

## SusBootstrap Example

**Goals:**
- `SusBootstrap.Mount<T>()` — analog of ` createApp().mount('#app')`
- `GetOrCreateOverlay()` — portal render
- `SusThemeService.Instance.SetTheme(root, theme)` — dark/light theme class on the cascade root

**Controls:** keys **D** (Dark), **L** (Light), **T** (Show Tooltip), **O** (Toggle Overlay).

## SusKeepAlive Example

**Goals:**
- `SusKeepAlive.Wrap(element)` — DOM caching
- `Active` toggle — hide through `display:none`

**No control** — works automatically. Every second:
```
[KeepAlive] Active=True (visible)
[KeepAlive] Active=False (hidden — DOM preserved)
```

## Diagnostics

| Symptom | Reason | Solution |
|---|---|---|
| Keys don't work | Focus is not in Game view | Click in Game view |
| Nothing is shown | UIDocument without PanelSettings | Scripts expose themselves |
| The example is not imported | Package Manager Cache | Remove → Install again |
