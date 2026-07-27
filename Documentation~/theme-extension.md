# SusCore Theme System — extending the cascade

> **Source of truth:** [`internal-docs/ui/theme/DESIGN_TOKENS.md`](../../internal-docs/ui/theme/DESIGN_TOKENS.md) (§5 themes).
> Package stub: [`Docs/DESIGN_TOKENS.md`](../Docs/DESIGN_TOKENS.md).
>
> **For whom:** sus-core developers and apps that register their own L4/L5 token sheets.
>
> **What:** how sus-core provides an extensible theme cascade. Downstream UI packages
> register extras via `SusBootstrap.RegisterCascadeStyleSheet` — core never hardcodes them.

---

## 1. Architecture: layered cascade

Core themes are built on a cascade of CSS variables. Each layer links to the previous one —
switch theme with one CSS class without editing components.

```
┌──────────────────────────────────────────────────────────────────┐
│ L4/L5  --sk-* / base styles   (optional, downstream packages)    │
│ Registered via SusBootstrap.RegisterCascadeStyleSheet.           │
├──────────────────────────────────────────────────────────────────┤
│ L3     --sus-*                design-tokens.uss                  │
│ Bridged to --thm-* → switched with the theme.                    │
├──────────────────────────────────────────────────────────────────┤
│ L2     --thm-*                _theme.uss                         │
│ Defined in :root (Dark) and .theme-light (Light).                │
├──────────────────────────────────────────────────────────────────┤
│ L1     --base-*               _palette.uss                       │
│ Physical colors; single source of truth.                         │
└──────────────────────────────────────────────────────────────────┘
```

### Chain example

User calls `SusThemeService.Instance.SetTheme(root, SusTheme.Light)`:

1. On `root` class `.theme-light` is added
2. `.theme-light` overrides `--thm-bg-surface`
3. `--sus-bg-surface` refers to `--thm-bg-surface` → updates automatically
4. Downstream `--sk-*` tokens that bridge to `--sus-*` update automatically
5. Components consuming those tokens redraw in light theme

**No component changes code. Everything works through a CSS cascade.**

---

## 2. Registering downstream sheets

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void RegisterTokens()
{
    SusBootstrap.RegisterCascadeStyleSheet("my-tokens");  // L4
    SusBootstrap.RegisterCascadeStyleSheet("my-base");     // L5
}
```

Order rules:

- Downstream token sheets must come **after** `design-tokens` (core cascade does this).
- OverlayHost receives the same registered extras via `SusBootstrap`.

See also: [`internal-docs/ui/theme/DESIGN_TOKENS.md`](../../internal-docs/ui/theme/DESIGN_TOKENS.md).
