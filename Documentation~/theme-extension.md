# SusCore Theme System - connecting Kit themes

> **⚠️ Partially outdated — `Docs/DESIGN_TOKENS.md` (§5 themes) is the source of truth.**
> This note predates a few API/layout changes. Corrections already applied inline below:
> - **`SusTheme` is a `readonly struct`, not an enum** (predefined values `SusTheme.Dark` / `SusTheme.Light`; build custom themes as struct values — do **not** "add an enum value").
> - **L1 raw palette (`--base-*`) lives in `_palette.uss`**, not `_theme.uss`. `_theme.uss` holds L2 `--thm-*` aliases (Dark in `:root`/`.theme-dark`, Light in `.theme-light`).
> - **`SusThemeService.SetTheme` uses `ReplaceThemeClass`** (swaps the single active `.theme-*` class), not `EnableInClassList("theme-light", !isDark)`.
> - **Cascade order** (`SusBootstrap.LoadDesignTokenCascade`) is `_palette → _font → _theme → design-tokens → _icon → extras (L4/L5) → OverlayHost`. The order shown in §2.3 is historical.
>
> **For whom:** developers of sus-core, consumer applications and those who connect downstream library to their project.
>
> **What:** how sus-core provides an extensible theme system, and how downstream library builds on top of it without modifying core.

---

## 1. Architecture: four-layer cascade

Core themes are built on a cascade of CSS variables. Each layer links to the previous one - this allows you to switch the theme with one CSS class without editing components.

```
┌──────────────────────────────────────────────────────────────────┐
│ L4 --sk-* downstream-tokens.uss Kit-tokens │
│ They bridge --sus-* with fallbacks.                         │
│ Loaded ONLY if downstream library is installed.                  │
├──────────────────────────────────────────────────────────────────┤
│ L3 --sus-* design-tokens.uss Semantic core tokens │
│ Bridged to --thm-* → switched with the theme.         │
├──────────────────────────────────────────────────────────────────┤
│ L2 --thm-* _theme.uss Thematic aliases │
│ Defined in :root (Dark) and .theme-light (Light).           │
│ One file - two sets of values.                             │
├──────────────────────────────────────────────────────────────────┤
│  L1  --base-*      _palette.uss        Raw palette               │
│ Physical colors never change.                       │
│ Single source of truth for all topics.                         │
└──────────────────────────────────────────────────────────────────┘
```

### Chain example

User calls `SusThemeService.Instance.SetTheme(root, SusTheme.Light)`:

1. On `root` class is added `.theme-light`
2. `.theme-light` overrides `--thm-bg-surface` (was `rgb(20,20,20)`, became `rgb(255,255,255)`)
3. `--sus-bg-surface` refers to `--thm-bg-surface` → updated automatically
4. `--sk-color-surface` refers to `var(--sus-bg-surface, fallback)` → updated automatically
5. All kit components use `--sk-color-surface` → redrawn in light theme

**No component changes the code. Everything works through a CSS cascade.**

---

## 2. What sus-core provides

### 2.1 Services

| Service | Destination | API |
|--------|-----------|-----|
| **SusThemeService** | Switching Dark/Light | `Instance.SetTheme(root, theme)` |
| **SusBreakpointService** | Adaptability (sm/md/lg/xl) | `Attach(root)` — CSS classes `.breakpoint-*` |
| **SusDensityService** | UI Density (Compact/Comfortable/Default) | `Instance.SetDensity(root, density)` |
| **SusScaleService** | UI Scaling (0.75x–1.5x) | `Instance.SetScale(root, scale)` |

All services are singletons with reactive `Prop<T>` fields. Components can be signed:

```csharp
Watch(SusThemeService.Current, (_, theme) => OnThemeChanged(theme));
Watch(SusDensityService.Current, (_, d) => OnDensityChanged(d));
```

### 2.2 Style files (Resources/SusRuntime/)

| File | Layer | Contents |
|------|------|------------|
| `_palette.uss` | L1 | Raw palette (`--base-color-*`), spacing scale, radius |
| `_theme.uss` | L2 | Dark/Light aliases (`--thm-*`): `:root`/`.theme-dark` and `.theme-light` |
| `design-tokens.uss` | L3 | Semantic tokens (`--sus-*`), breakpoint overrides |
| `_font.uss` | — | `-unity-font-definition` + `url()` font resources, `--sus-font-family-*` bridges |
| `_icon.uss` | — | `.sus-icon-bg`, `.icon-*` utility classes |
| `_global.uss` | — | Resets, scrollbar, global utility |

### 2.3 SusBootstrap

Static class entry point. Two ways:

```csharp
// Path 1: Mount component (for standalone applications and examples)
SusBootstrap.Mount<MyScreen>(uiDocument);
// Automatically loads the ENTIRE cascade + services

// Path 2: Cascade only (for manual UI assembly)
SusBootstrap.LoadTokenCascade(container);
// Loads only styles + services, no component
```

**Loading order in `LoadDesignTokenCascade`** (`_global.uss` is applied via panel TSS, not the cascade):

```
_palette  →  _font  →  _theme  →  design-tokens  →  _icon
→  registered extras (L4 downstream-tokens / L5 suskit-base, if kit installed)
→  SusBreakpointService.Attach
→  SusDensityService.Attach
→  SusScaleService.Attach
→  GetOrCreateOverlay (OverlayHost, last child, receives the same cascade)
```

---

## 3. How downstream library extends core

### 3.1 Principle: add-on, not replacement

Kit **does not modify** any core. Instead:

1. **`downstream-tokens.uss`** - adds L4 layer. All `--sk-*` variables are bridged to `--sus-*`:

```css
:root {
    --sk-color-primary:  var(--sus-primary, rgb(0, 102, 255));
    --sk-color-surface:  var(--sus-bg-surface, rgb(31, 31, 31));
    --sk-color-text-primary: var(--sus-text-primary, rgb(240, 240, 240));
    /* ... ~40 more tokens ... */
}
```

2. **Density classes** - redefine size tokens on the same `:root`:

```css
.density-compact {
    --sk-form-height: 28px;
    --sk-row-height:  32px;
    --sk-space-8:     4px;
}
```

3. **`DownstreamLibThemeService`** (in downstream library, not in core) - atomically applies Theme + Density + Scale.

### 3.2 What kit does NOT do

- **Does not refer** to `--thm-*` directly (only through `--sus-*`)
- **Does not duplicate** definitions from `_theme.uss` or `design-tokens.uss`
- **Does not require** downstream library for core themes to work

### 3.3 Extension points for kit

| Dot | Where | How |
|-------|-----|-----|
| New color | `_theme.uss` L1 → `_theme.uss` L2 → `downstream-tokens.uss` L4 | Add `--base-color-*` → `--thm-*` → `--sk-color-*` |
| New spacing | `_theme.uss` L1 `--base-space-*` → `downstream-tokens.uss` `--sk-space-*` | Same chain |
| New theme | `.theme-*` block in `_theme.uss` + a `SusTheme` struct value | `SusTheme` is a struct — construct/expose a new value, then `SetTheme(root, yourTheme)` |
| New density level | `SusDensity` enum + `SusDensityService.SetDensity` + `.density-*` V `downstream-tokens.uss` | Add value to enum, class in USS |
| Custom scale mode | `SusScaleService` → `root.style.scale` | Expand Min/Max or add presets |

---

## 4. Connection in your Unity project

### 4.1 Via SusBootstrap (recommended)

```csharp
// Everything is automatic: cascade + services + OverlayHost
var root = GetComponent<UIDocument>().rootVisualElement;
SusBootstrap.Mount<MainScreen>(root);

// The default theme is Dark.
// Toggle:
SusThemeService.Instance.SetTheme(root, SusTheme.Light);
```

### 4.2 Manual connection (SusBootstrapper)

If you need full control (as in a production consumer app):

```csharp
// 1. Cascade (order is important!)
LoadUsStyleSheet(root, "SusRuntime/_theme");         // L1+L2
LoadUsStyleSheet(root, "SusRuntime/design-tokens");  // L3
LoadUsStyleSheet(root, "SusRuntime/downstream-tokens");  // L4 ← CRITICAL: to suskit-base
LoadUsStyleSheet(root, "SusRuntime/_font");
LoadUsStyleSheet(root, "SusRuntime/_icon");
LoadUsStyleSheet(root, "SusRuntime/_global");
LoadUsStyleSheet(root, "SusRuntime/suskit-base");    // L5

// 2. Services (the order is not important)
SusBreakpointService.Attach(root);
SusDensityService.Attach(root);
SusScaleService.Attach(root);

// 3. Topic
SusThemeService.Instance.SetTheme(root, SusTheme.Dark);
```

### 4.3 Pitfalls

- **downstream-tokens MUST be before suskit-base.** Otherwise the components will not see `--sk-*`.
- **downstream-tokens MUST come after design-tokens.** Otherwise `--sus-*` have not yet been determined.
- **OverlayHost must receive YOUR downstream-tokens.** `SusBootstrap.GetOrCreateOverlay` does this automatically.

---

## 5. Adding new colors to the palette

### 5.1 Add to L1 (`_palette.uss`)

```css
:root {
    /* Existing ... */
    --base-color-Rare:      rgb(255, 215,   0); /* Gold */
    --base-color-Epic:      rgb(170,  80, 240); /* Purple */
}
```

### 5.2 Add to L2 Dark (`_theme.uss`)

```css
:root, .theme-dark {
    /* Existing ... */
    --thm-rare: var(--base-color-Rare);
    --thm-epic: var(--base-color-Epic);
}
```

### 5.3 Add to L2 Light (`_theme.uss`)

```css
.theme-light {
    /* Existing ... */
    --thm-rare: var(--base-color-Rare);
    --thm-epic: var(--base-color-Epic);
}
```

### 5.4 Add to L4 (`downstream-tokens.uss`)

```css
:root {
    --sk-color-rare: var(--thm-rare, rgb(255, 215, 0));
    --sk-color-epic: var(--thm-epic, rgb(170, 80, 240));
}
```

After this, any kit component can use `var(--sk-color-rare)` — and the color will automatically switch when you change the theme.

---

## 6. Adding a third theme (HighContrast)

1. Add a CSS class to `_theme.uss`:

```css
.theme-highcontrast {
    --thm-bg-page:    rgb(0, 0, 0);
    --thm-text-primary: rgb(255, 255, 255);
    /* ... all --thm-* with their own values ​​... */
}
```

2. Expose a new `SusTheme` **struct** value for HighContrast (it is not an enum — construct a value that maps to the `.theme-highcontrast` class).

3. `SusThemeService.SetTheme` calls `ReplaceThemeClass(target, theme)`, which removes the previously active `.theme-*` class and adds the one for the requested theme — so a third theme works without touching the switch logic, as long as its struct value maps to `.theme-highcontrast`.

4. Kit tokens `--sk-*` will be picked up automatically - they refer to `--sus-*`, which refer to `--thm-*`.

---

## 7. Files and responsibility

| File | Package | Responsibility |
|------|-------|-----------------|
| `_palette.uss` | sus-core | L1 raw palette (`--base-*`) |
| `_theme.uss` | sus-core | L2 theme aliases (`--thm-*`, Dark/Light) |
| `design-tokens.uss` | sus-core | L3 semantic tokens + breakpoint overrides |
| `SusThemeService.cs` | sus-core | Switching Dark/Light via CSS classes |
| `SusDensityService.cs` | sus-core | Density classes + reactive prop |
| `SusScaleService.cs` | sus-core | Scale transform + reactive prop |
| `SusBootstrap.cs` | sus-core | Entry point: cascade + services + OverlayHost |
| `downstream-tokens.uss` | downstream library | L4 kit tokens (bridge to L3) |
| `DownstreamLibThemeService.cs` | downstream library | Bundle Theme+Density+Scale |
| `SusBootstrapper.cs` | your Unity project | Production connection |

---

## 8. Checklist when adding a new token

- [ ] L1: `--base-*` in `_palette.uss` (physical color)
- [ ] L2 Dark: `--thm-*` V `:root, .theme-dark` block `_theme.uss`
- [ ] L2 Light: `--thm-*` V `.theme-light` block `_theme.uss`
- [ ] L3: `--sus-*` V `design-tokens.uss` (if commonly used)
- [ ] L4: `--sk-*` V `downstream-tokens.uss` with fallback
- [ ] Components use the new token: `var(--sk-color-*, old-fallback)`
- [ ] Check: switching theme changes color
- [ ] Check: `Editor.log` — 0 errors
