# SUS — Design Tokens: fonts, colors, icons, themes

> Design token architecture for the new SUS.  
> Source of truth for themes, fonts and icons in SUS (StyleSheet cascade + Resources).
> **Principle:** everything is done through a CSS cascade and `var()` — the component does not know specific values, only semantic tokens.

---

## Content

1. [General architecture](#1-general-architecture)
2. [Fonts](#2-fonts)
3. [Colors: three-layer token system](#3-colors-three-layer-token-system)
4. [Icons: Phosphor Icons](#4-icons-phosphor-icons)
5. [Themes: switching Light/Dark](#5-themes-switching-light-dark)
6. [Screen resolutions (High/Low)](#6-screen-resolutions-high-low)
7. [Bootstrap: how everything is put together](#7-bootstrap-how-everything-is-put-together)
8. [File structure](#8-file-structure)
9. [Task list](#9-task-list)
10. [Appendix A: token summary table](#appendix-a-token-summary-table)
11. [Appendix B: key rules and patterns](#appendix-b-key-rules-and-patterns-identified-during-the-process)

---



## 1. General architecture



### 1.1 Principle

```
The component writes: And receives:
  color: var(--sus-text-primary) → rgba(255,255,255,1) (in dark theme)
  font-size: var(--sus-font-16) → 16px (on High resolution)
  -unity-font: var(--main-font-family) → Montserrat Regular
```

Tokens are resolved through a CSS cascade. Variables are defined in `:root` USS files that connect via direct ` root.styleSheets.Add()`.

### 1.2 Three layers of tokens

```
LAYER 1 — Raw values (ground truth)
  _palette.uss                     — --base-color-*, --base-space-*, --base-font-*, --base-radius-*
  _font.uss                        — --font-family-*, --font-heading, --font-body

LAYER 2 — Theme aliases (light/dark switch)
  _theme.uss (:root + .theme-dark)  — --thm-* (dark default)
  _theme.uss (.theme-light)         — --thm-* (light override)

LAYER 3 — Semantic UI tokens
  design-tokens.uss                 — --sus-* (delegates to --thm-*)
  _icon.uss                         — .sus-icon-bg utility class
```

**Load order** (container cascade via `SusBootstrap.LoadTokenCascade` / ` SusApp`):
`_palette` → `_font` → `_theme` → ` design-tokens` → `_icon` → registered extras (L4/L5) → OverlayHost.

Panel-level TSS (`SusDefault.tss` / ` ApplyDefaultTSS`) also includes `_palette`, `_font`, and
optional `_global` — `_global` is **not** part of the container cascade.

Theme is switched by adding/removing `.theme-dark` / `.theme-light` on the cascade root
(`SusThemeService.Instance.SetTheme(root, theme)`).



### 1.3 How it works in the old scheme

`SusDataManager.LoadUIResolutionThemes()` creates 6 theme presets (Dark/Light/CustomLight × High/Low), sets ` panel.themeStyleSheet` to one of `.tss`- entry points. `.tss` through `@import url(...)` builds a chain of three layers.

**In SUS this is simplified** - fewer presets (Dark/Light only), less `.tss`-files, ` ThemeStyleSheet` is replaced by directly adding USS to ` root.styleSheets` through ` SusBootstrap`.

---



## 2. Fonts



### 2.1 Current state

**Already made:** Montserrat (6 styles) comes with `sus-core`:

```
sus-core/Runtime/Resources/SusRuntime/
├── _font.uss
└── Fonts/Montserrat/
    ├── Montserrat-Regular.ttf    (400)
    ├── Montserrat-Medium.ttf     (500)
    ├── Montserrat-Bold.ttf       (700)
    ├── Montserrat-Black.ttf      (900)
    ├── Montserrat-Italic.ttf
    └── Montserrat-Light.ttf      (300)
```

`_font.uss` defines `:root { -unity-font: url("Fonts/Montserrat/Montserrat-Regular.ttf"); }` and CSS variables `--font-family-regular`, `--font-family-medium`, `--font-family-bold`, `--font-family-black`, `--font-family-italic`, `--font-family-light`.

`SusBootstrap.Mount<T>()` automatically downloads ` Resources/SusRuntime/_font.uss` with every mount.

### 2.2 What to do



#### 2.2.1 Add missing styles from the previous scheme

Now at `sus-core` 6 styles. Previously - 18 (9 styles + italic). Need to add:


| File | Weight | Destination |
| --------------------------- | ------ | ----------------- |
| `Montserrat-Thin.ttf`       | 100 | Emphasis Headings |
| `Montserrat-ExtraLight.ttf` | 200 | Small text |
| `Montserrat-SemiBold.ttf`   | 600 | Buttons, navigation |
| `Montserrat-ExtraBold.ttf`  | 800 | Hero titles |


Italic versions for new styles - optional, add if the project uses italics in the UI.

#### 2.2.2 Expand `_font.uss` - semantic variables

```css
/* sus-core/Runtime/Resources/SusRuntime/_font.uss */
:root {
    /* ── Font Families ─────────────────────────────── */
    --font-family-thin:       url("Fonts/Montserrat/Montserrat-Thin.ttf");
    --font-family-extra-light: url("Fonts/Montserrat/Montserrat-ExtraLight.ttf");
    --font-family-light:      url("Fonts/Montserrat/Montserrat-Light.ttf");
    --font-family-regular:    url("Fonts/Montserrat/Montserrat-Regular.ttf");
    --font-family-medium:     url("Fonts/Montserrat/Montserrat-Medium.ttf");
    --font-family-semi-bold:  url("Fonts/Montserrat/Montserrat-SemiBold.ttf");
    --font-family-bold:       url("Fonts/Montserrat/Montserrat-Bold.ttf");
    --font-family-extra-bold: url("Fonts/Montserrat/Montserrat-ExtraBold.ttf");
    --font-family-black:      url("Fonts/Montserrat/Montserrat-Black.ttf");
    --font-family-italic:     url("Fonts/Montserrat/Montserrat-Italic.ttf");

    /* ── Semantic font assignments ─────────────────── */
    --main-font-family: var(--font-family-regular);
    --font-heading:     var(--font-family-bold);
    --font-body:        var(--font-family-regular);
    --font-button:      var(--font-family-semi-bold);
    --font-caption:     var(--font-family-light);
    --font-mono: var(--font-family-regular);  /* replacement with monospace */

    -unity-font: var(--main-font-family);
}

* {
    -unity-font: var(--main-font-family);
}
```



#### 2.2.3 Font sizes

Font sizes are already in `_palette.uss` (Layer 1 — see section 6):

```css
--base-font-10: 10px;
--base-font-12: 12px;
--base-font-14: 14px;
--base-font-16: 16px;
--base-font-18: 18px;
--base-font-20: 20px;
--base-font-24: 24px;
--base-font-28: 28px;
--base-font-32: 32px;
```



#### 2.2.4 User Override

As it is now: the user creates `Assets/Resources/SusRuntime/_font.uss` — ` Resources.Load` prefers the draft version. Everything works without code.

---



## 3. Colors: three-layer token system



### 3.1 Layer 1 — Raw values (in `_palette.uss`)

The file that comes with `sus-core` in ` Resources/`. Basic colors without semantics:

```css
/* sus-core/Runtime/Resources/SusRuntime/_palette.uss — Layer 1 */
:root {
    /* Greys */
    --base-color-Grey98:    rgb(250, 250, 250);
    --base-color-Grey94:    rgb(240, 240, 240);
    --base-color-Grey90:    rgb(229, 229, 229);
    --base-color-Grey80:    rgb(204, 204, 204);
    --base-color-Grey60:    rgb(153, 153, 153);
    --base-color-Grey40:    rgb(102, 102, 102);
    --base-color-Grey30:    rgb( 77,  77,  77);
    --base-color-Grey24:    rgb( 61,  61,  61);
    --base-color-Grey16:    rgb( 41,  41,  41);
    --base-color-Grey12:    rgb( 31,  31,  31);
    --base-color-Grey08:    rgb( 20,  20,  20);
    --base-color-Grey04:    rgb( 10,  10,  10);
    --base-color-Grey02:    rgb(  5,   5,   5);

    /* Primary (Blue) */
    --base-color-Primary90: rgb(205, 225, 255);
    --base-color-Primary70: rgb(102, 163, 255);
    --base-color-Primary50: rgb(  0, 102, 255);
    --base-color-Primary30: rgb(  0,  61, 153);
    --base-color-Primary10: rgb(  0,  20,  51);

    /* Accent / Success / Warning / Error */
    --base-color-Success:   rgb( 76, 175,  80);
    --base-color-Warning:   rgb(255, 193,   7);
    --base-color-Error:     rgb(244,  67,  54);
}
```



### 3.2 Layer 2 — Theme aliases (Dark/Light in `_theme.uss`)

**Dark theme (default):**

```css
/* sus-core/Runtime/Resources/SusRuntime/_theme.uss — Layer 2 Dark */
:root,
.theme-dark {
    /* Surface */
    --thm-bg-page:           var(--base-color-Grey04);
    --thm-bg-surface:        var(--base-color-Grey08);
    --thm-bg-surface-raised: var(--base-color-Grey12);
    --thm-bg-surface-overlay:var(--base-color-Grey16);

    /* Text */
    --thm-text-primary:      var(--base-color-Grey94);
    --thm-text-secondary:    var(--base-color-Grey80);
    --thm-text-disabled:     var(--base-color-Grey60);
    --thm-text-on-primary:   var(--base-color-Grey98);

    /* Borders */
    --thm-border:            var(--base-color-Grey24);
    --thm-border-hover:      var(--base-color-Grey40);
    --thm-border-focus:      var(--base-color-Primary50);

    /* Primary */
    --thm-primary:           var(--base-color-Primary50);
    --thm-primary-hover:     var(--base-color-Primary70);
    --thm-primary-pressed:   var(--base-color-Primary30);

    /* Semantic colors */
    --thm-success:           var(--base-color-Success);
    --thm-warning:           var(--base-color-Warning);
    --thm-error:             var(--base-color-Error);
}
```

**Light theme** (overrides light/dark):

```css
/* sus-core/Runtime/Resources/SusRuntime/_theme.uss — Layer 2 Light */
.theme-light {
    --thm-bg-page:           var(--base-color-Grey98);
    --thm-bg-surface:        rgb(255, 255, 255);
    --thm-bg-surface-raised: var(--base-color-Grey94);
    --thm-bg-surface-overlay:var(--base-color-Grey90);

    --thm-text-primary:      var(--base-color-Grey04);
    --thm-text-secondary:    var(--base-color-Grey30);
    --thm-text-disabled:     var(--base-color-Grey60);
    --thm-text-on-primary:   var(--base-color-Grey98);

    --thm-border:            var(--base-color-Grey90);
    --thm-border-hover:      var(--base-color-Grey60);
    --thm-border-focus:      var(--base-color-Primary50);

    --thm-primary:           var(--base-color-Primary50);
    --thm-primary-hover:     var(--base-color-Primary70);
    --thm-primary-pressed:   var(--base-color-Primary30);

    --thm-success:           var(--base-color-Success);
    --thm-warning:           var(--base-color-Warning);
    --thm-error:             var(--base-color-Error);
}
```



### 3.3 Layer 3 — Semantic UI tokens (`design-tokens.uss`)

The file that the components actually use:

```css
/* sus-core/Runtime/Resources/SusRuntime/design-tokens.uss */
:root {
    /* ── Text ──────────────────────────────────────── */
    --sus-text-primary:    var(--thm-text-primary);
    --sus-text-secondary:  var(--thm-text-secondary);
    --sus-text-disabled:   var(--thm-text-disabled);
    --sus-text-inverse:    var(--thm-text-inverse);
    --sus-text-link:       var(--thm-primary);

    /* ── Backgrounds ───────────────────────────────── */
    --sus-bg:              var(--thm-bg);
    --sus-bg-elevated:     var(--thm-bg-elevated);
    --sus-bg-overlay:      var(--thm-bg-overlay);
    --sus-surface:         var(--thm-surface);
    --sus-surface-hover:   var(--thm-surface-hover);

    /* ── Borders ───────────────────────────────────── */
    --sus-border:          var(--thm-border);
    --sus-border-hover:    var(--thm-border-hover);
    --sus-divider:         var(--thm-divider);
    --sus-radius-sm:       4px;
    --sus-radius-md:       8px;
    --sus-radius-lg:       12px;
    --sus-radius-xl:       16px;
    --sus-radius-full:     20px;

    /* ── Status ────────────────────────────────────── */
    --sus-success:         var(--thm-success);
    --sus-fail:            var(--thm-fail);
    --sus-warning:         var(--thm-warning);
    --sus-info:            var(--thm-info);

    /* ── Interactive ───────────────────────────────── */
    --sus-btn-primary-bg:        var(--thm-primary);
    --sus-btn-primary-bg-hover:  var(--thm-primary-hover);
    --sus-btn-primary-text:      var(--thm-text-inverse);
    --sus-btn-secondary-bg:      var(--thm-surface);
    --sus-btn-secondary-bg-hover: var(--thm-surface-hover);
    --sus-btn-secondary-text:    var(--thm-text-primary);
    --sus-btn-ghost-bg:          transparent;
    --sus-btn-ghost-bg-hover:    var(--thm-primary-muted, var(--base-color-primary-muted));
    --sus-btn-disabled-bg:       var(--thm-surface);
    --sus-btn-disabled-text:     var(--thm-text-disabled);

    /* ── Inputs ────────────────────────────────────── */
    --sus-input-bg:         var(--thm-surface);
    --sus-input-border:     var(--thm-border);
    --sus-input-border-focus: var(--thm-primary);
    --sus-input-text:       var(--thm-text-primary);
    --sus-input-placeholder: var(--thm-text-disabled);

    /* ── Overlays ──────────────────────────────────── */
    --sus-overlay-backdrop: var(--thm-bg-overlay);
    --sus-modal-bg:         var(--thm-bg-elevated);
    --sus-tooltip-bg:       var(--thm-bg-elevated);
    --sus-tooltip-text:     var(--thm-text-primary);

    /* ── Scrollbar ─────────────────────────────────── */
    --sus-scrollbar-track:  var(--thm-bg);
    --sus-scrollbar-thumb:  var(--thm-border);
    --sus-scrollbar-thumb-hover: var(--thm-border-hover);

    /* ── Font sizes (semantic aliases) ─────────────── */
    --sus-font-10:  var(--base-font-10);
    --sus-font-12:  var(--base-font-12);
    --sus-font-14:  var(--base-font-14);
    --sus-font-16:  var(--base-font-16);
    --sus-font-18:  var(--base-font-18);
    --sus-font-20:  var(--base-font-20);
    --sus-font-24:  var(--base-font-24);
    --sus-font-28:  var(--base-font-28);
    --sus-font-32:  var(--base-font-32);

    /* ── Spacing ───────────────────────────────────── */
    --sus-space-0:  0px;
    --sus-space-4:  var(--base-space-4);
    --sus-space-8:  var(--base-space-8);
    --sus-space-12: var(--base-space-12);
    --sus-space-16: var(--base-space-16);
    --sus-space-24: var(--base-space-24);
    --sus-space-32: var(--base-space-32);
    --sus-space-48: var(--base-space-48);
    --sus-space-64: var(--base-space-64);
}
```



### 3.4 Use in components

```xml
<!-- Card.sharq -->
<style scoped>
.card {
    background-color: var(--sus-surface);
    border-width: 1px;
    border-color: var(--sus-border);
    border-radius: var(--sus-radius-md);
    color: var(--sus-text-primary);
    padding: var(--sus-space-16);
}
.card__title {
    font-size: var(--sus-font-20);
    -unity-font: var(--font-heading);
    color: var(--sus-text-primary);
}
.card__body {
    font-size: var(--sus-font-14);
    color: var(--sus-text-secondary);
    margin-top: var(--sus-space-8);
}
</style>
```

---



## 4. Icons: Phosphor Icons

SUS ships **[Phosphor Icons](https://phosphoricons.com/)** (MIT) as VectorImage SVGs — not Bootstrap Icons.
There is no `Icons/bootstrap/` tree anymore.

| Set | Path | Size | Role |
|-----|------|------|------|
| **Core** | `Resources/SusRuntime/Icons/core/{regular,fill}/` | ~70 SVGs | Minimal built-in set so SUS works even if Phosphor is trimmed |
| **Phosphor** | `Resources/SusRuntime/Icons/phosphor/{thin,light,regular,bold,fill,duotone}/` | **1512 names × 6 weights ≈ 9000 SVGs** | Full chrome icon library |

**Weights** (`SusIconWeight`): ` Thin`, ` Light`, ` Regular` (default), ` Bold`, ` Fill`, ` Duotone`.

**Status:** ✅ runtime + editor auto-register Phosphor; downstream UI packages can use the same registry.

### 4.1 Layout on disk

```
sus-core/Runtime/Resources/SusRuntime/Icons/
├── core/
│   ├── regular/     star.svg, gear.svg, …
│   └── fill/        star-fill.svg, …          (weight folder + optional -fill suffix)
└── phosphor/
    ├── thin/
    ├── light/
    ├── regular/     {name}.svg
    ├── bold/        {name}-bold.svg
    ├── fill/        {name}-fill.svg
    └── duotone/     {name}-duotone.svg
```

Loader convention (`ResourcesFolderIconProvider`): resources path  
`SusRuntime/Icons/{collection}/{weightFolder}/{fileName}`  
where `fileName` is `{name}` for `regular`, else `{name}-{weight}`.

Two constructors:

- `new ResourcesFolderIconProvider("app")` — **project-local** overload (no package id). Serves
  icons from **any** `Assets/**/Resources/SusRuntime/Icons/app/…` folder (including the wizard's
  `Customization/Icons/Resources/…`). Use this for your app's own icons.
- `new ResourcesFolderIconProvider("com.my.game", "app")` — **packaged** overload. The first
  argument is the **UPM package id** whose `Runtime/Resources/SusRuntime/Icons/app/…` holds the
  icons (editor scan + `.meta` repair are resolved against that package). Use this only when you
  ship icons inside your own package. Do **not** pass `"com.sharq-it.sus.core"` for your project
  icons — that points the scan at the core package.

### 4.2 How resolution works

```
SusIcon / SusIconRegistry.Load(alias, weight)
  → resolve semantic aliases (e.g. "settings" → "gear")
  → optional name suffix "-fill" / "-bold" / … overrides weight
  → query ISusIconProvider list (first hit wins):
       1. Project providers (SusApp.UseIcons / RegisterProvider highest priority)
       2. CoreIconProvider          (Icons/core)
       3. PhosphorIconProvider      (Icons/phosphor, auto via PhosphorIconBootstrap)
```

`PhosphorIconBootstrap` registers Phosphor at **SubsystemRegistration** / editor load with
`asHighestPriority: false`, so core + project icons keep precedence for overlapping names.

### 4.3 API (current)

```csharp
// Weight enum — not the old SusIconStyle Outline/Filled
public enum SusIconWeight { Thin, Light, Regular, Bold, Fill, Duotone }

// Core primitive (also used under the hood)
var icon = new SusIcon("gear");                      // Regular
var star = new SusIcon("star", SusIconWeight.Fill);
icon.Name.Value = "x";                               // reactive

VectorImage img = SusIconRegistry.Load("star", SusIconWeight.Fill);
SusIconRegistry.AddAlias("settings", "gear");
SusIconRegistry.RegisterProvider(myProvider, asHighestPriority: true);

// SusApp — project icons win over Phosphor
SusApp.Create(uiDocument)
    .UseIcons(new ResourcesFolderIconProvider("com.my.game", "app"))
    .Mount<HomeScreen>();
```

Downstream UI packages may expose a higher-level icon component:

```xml
<sus:SusIcon Icon="star" Weight="Fill" Size="md" />
```

`Kind=phosphor|game|portrait|auto` — game/portrait use the consumer media bridge; see your optional component package documentation.

### 4.4 SVG import settings (IMPORTANT)

Every `.svg.meta` should use UI Toolkit Vector Image + antialiased tessellation:

```yaml
svgType: 3                          # UI Toolkit Vector Image
tessellationMode: 1                 # Antialiased Arc Encoding (NOT 0)
textureSize: 256
sampleCount: 4
```

Without `tessellationMode: 1`, curves look pixelated.

### 4.5 Auxiliary USS

**`_icon.uss`** — global class for plain ` VisualElement` / core ` SusIcon` (`.sus-icon-bg`):

```css
.sus-icon-bg {
    background-size: contain;
    background-repeat: no-repeat;
    background-position: center;
    -unity-background-image-tint-color: rgb(255, 255, 255);
    flex-shrink: 0;
}
```

Loaded with the token cascade (`SusBootstrap` / ` SusApp`).

### 4.6 USS vs C# for icons

| What | Where | Why |
|-----|-----|--------|
| `background-size/position/repeat`, ` flex-shrink` | USS (`.sus-icon-bg` / companion) | Static |
| Default tint | USS | Theme can override |
| `backgroundImage` | C# via ` SusIconRegistry.Load` | Runtime asset |
| Size / weight / name | Props / C# | Dynamic |

### 4.7 Custom / project icons

Do **not** drop files into a fictional `Icons/bootstrap/custom/`. Prefer:

1. **`ResourcesFolderIconProvider`** pointing at your collection under  
   `Resources/SusRuntime/Icons/{yourCollection}/{weight}/`, then  
   `SusApp.UseIcons(...)` or ` SusIconRegistry.RegisterProvider(...)`.
2. Or **`SusIconSetAsset`** + ` SusApp.UseIcons(iconSet)`.
3. Semantic aliases: `SusIconRegistry.AddAlias("my-settings", "gear")`.

Setup Project scaffolding uses `Customization/Icons/.../app/` + `ResourcesFolderIconProvider("app")` (the project-local overload) as the project override layer.

---



## 5. Themes: switching Light/Dark



### 5.1 How it works in the previous scheme

- `UIResolutionThemes` — ScriptableObject with an array of 6 ` StyleTheme` (Dark/Light/CustomLight × High/Low)
- `panel.themeStyleSheet` = ` ThemeStyleSheet` (`.tss`), which through `@import` pulls the USS chain
- `PollScreenWidth()` in ` Update` — switches High/Low when crossing 1600px
- `SetStyleTheme()` — switches Dark/Light/CustomLight



### 5.2 SUS — current API

**No `ThemeStyleSheet` for theme switching** — USS sheets are added to the container via
`LoadTokenCascade` / ` SusApp`. Theme variants are CSS classes on the cascade root.

**Layer files:**
- `_palette.uss` — L1 `--base-*` (raw values)
- `_theme.uss` — L2 `--thm-*` for `.theme-dark` / `.theme-light`

**Runtime API** (`SusTheme` is a ` readonly struct`; service is a singleton):

```csharp
// sus-core/Runtime/SusTheme.cs + Runtime/Services/SusThemeService.cs
namespace Sharq.Core
{
    public readonly struct SusTheme
    {
        public string Name { get; }
        public SusTheme(string name);
        public static SusTheme Dark => new("dark");
        public static SusTheme Light => new("light");
        public string CssClass => $"theme-{Name}";  // "theme-dark", "theme-light", …
    }

    public class SusThemeService
    {
        public static SusThemeService Instance { get; }
        public static Prop<SusTheme> Current { get; }

        // Applies .theme-{name} on the cascade root + OverlayHost
        public void SetTheme(VisualElement root, SusTheme theme);
    }
}
```

Usage:

```csharp
SusThemeService.Instance.SetTheme(root, SusTheme.Dark);
SusThemeService.Instance.SetTheme(root, SusTheme.Light);
SusThemeService.Instance.SetTheme(root, new SusTheme("midnight"));  // custom → .theme-midnight

Watch(SusThemeService.Current, (_, next) => AdaptToTheme(next));
```

CSS switching through a class on the root — already in `_theme.uss` (see 3.2).



### 5.3 SusBootstrap / SusApp

`SusApp.Create(...).UseTheme(...).Mount/Run` applies the theme **last** so OverlayHost
receives the theme class. Prefer that path over manual `styleSheets.Add`.

`SusBootstrap.Mount<T>()` / ` LoadTokenCascade` load the full cascade
(`_palette` → `_font` → `_theme` → ` design-tokens` → `_icon` → extras + OverlayHost).

---



## 6. Screen resolutions (High/Low)



### 6.1 Breakpoint

**1600px** (`SusResolutionService.ThresholdHigh`) — scale UI for wide (High) vs narrower (Low).

### 6.2 Mechanics

`SusResolutionService` (sus-core) only **toggles the class** `resolution-high` / `resolution-low`
on the cascade root. The actual scaling **USS rules may live in a downstream UI package**
(Layer 4 token sheets) — core's `_palette.uss` defines the base `--base-*` values but does
**not** ship `.resolution-*` overrides. So:

- **With a downstream UI package installed:** the classes scale `--base-*` automatically (rules below).
- **Core-only:** the classes are added but nothing changes unless you define your own
  `.resolution-low` / `.resolution-high` rules in a project USS.

```css
/* Example downstream package: downstream-tokens.uss — Layer 4 (base values come from core _palette.uss) */
:root {
    /* HIGH (>=1600px) — default */
    --base-space-4:   4px;
    --base-space-8:   8px;
    --base-space-16:  16px;
    /* ... font sizes, radii ... */
}

/* LOW (<1600px) — scaled through the class on root */
.resolution-low {
    --base-space-4:   3px;
    --base-space-8:   6px;
    --base-space-16:  12px;
    /* ... font sizes, radii — scaled ... */
}
```

> Not to be confused with `SusBreakpointService` (sm/md/lg/xl `.breakpoint-*` classes, see
> [06-responsive.md](./06-responsive.md)). Resolution = one 1600px High/Low axis for global UI
> scale; breakpoints = layout adaptation at multiple widths.

In C# — `SusResolutionService` (singleton; no ` IsHighResolution` prop):

```csharp
// sus-core/Runtime/SusResolutionService.cs
namespace Sharq.Core
{
    public class SusResolutionService
    {
        public static readonly SusResolutionService Instance = new();
        public const float ThresholdHigh = 1600f;

        public void Update(VisualElement root, float logicalWidth)
        {
            if (logicalWidth >= ThresholdHigh)
            {
                root.RemoveFromClassList("resolution-low");
                root.AddToClassList("resolution-high");
            }
            else
            {
                root.RemoveFromClassList("resolution-high");
                root.AddToClassList("resolution-low");
            }
        }
    }
}
```

`SusComponent.Updated()` calls
`SusResolutionService.Instance.Update(cascadeRoot, cascadeRoot.resolvedStyle.width)` —
automatic switching. Manual call:

```csharp
SusResolutionService.Instance.Update(root, root.resolvedStyle.width);
```

---



## 7. Bootstrap: how everything is put together



### 7.1 Boot order (prefer `SusApp`)

```
SusApp.Create(uiDocument)
  ├─ ApplyDefaultTSS (panel: _palette + _font + _global via SusDefault.tss)
  ├─ EnsureEventSystem
  ├─ UseIcons → SusIconRegistry
  ├─ LoadTokenCascade (container):
  │     _palette → _font → _theme → design-tokens → _icon → extras
  │     + Breakpoint/Density/Scale services + OverlayHost
  ├─ EnsureWorldSpacePanel (if UseWorldSpace && playing)
  ├─ UseFonts / UseCustomStyles
  ├─ Configure callbacks (router / manual UI)
  ├─ Mount<T> (optional)
  └─ SusThemeService.Instance.SetTheme(root, theme)   ← last
```

**Important:** the container cascade always includes `_palette` and `_theme` (not only
`design-tokens`). Prefer `SusApp` over hand-rolled `root.styleSheets.Add(...)`.

> **World-space panel caveat.** `EnsureWorldSpacePanel` gives the world panel its own token
> cascade (`_palette → _font → _theme → design-tokens`), but **`UseFonts`, `UseCustomStyles`
> (branding) and `SetTheme` are applied only to the screen root + OverlayHost** — not to the
> world panel. So world UI (healthbars/nameplates from a downstream UI package) renders with the **default** Dark theme,
> default fonts and no branding override. If you need brand colors / a light theme / custom fonts
> on world UI, apply them to `app.WorldPanel` / `WorldSpaceService.Default.WorldSpacePanel`
> yourself (e.g. `SusThemeService.Instance.SetTheme(worldRoot, theme)` and load your branding USS
> onto it).



### 7.2 What each layer defines

```
_palette.uss     — L1: --base-color-*, --base-space-*, --base-font-*, --base-radius-*
_font.uss        — font families (--sus-font-family-* bridges) + -unity-font-definition
_theme.uss       — L2: --thm-* for .theme-dark / .theme-light
design-tokens.uss— L3: --sus-* semantic tokens
_icon.uss        — .sus-icon-bg utility
```

### 7.3 Minimal example

```csharp
public class AppEntry : MonoBehaviour
{
    public UIDocument uiDocument;

    void Start()
    {
        SusApp.Create(uiDocument)
            .UseTheme(SusTheme.Dark)
            .Mount<App>();
        // Fonts, colors, icons, OverlayHost, theme — all wired.
    }
}
```

Lower-level alternative (manual cascade):

```csharp
void Start()
{
    SusBootstrap.ApplyDefaultTSS(uiDocument);
    var root = uiDocument.rootVisualElement;
    SusBootstrap.LoadTokenCascade(root);
    SusBootstrap.Mount<App>(root);
    SusThemeService.Instance.SetTheme(root, SusTheme.Dark);
}
```

---



## 8. File structure



### 8.1 In `sus-core` (comes with package)

```
sus-core/
├── Docs/
│   └── DESIGN_TOKENS.md          ← this document
│
├── Runtime/
│   ├── Resources/SusRuntime/
│   │   ├── _palette.uss          ← L1 --base-*                  ✅
│   │   ├── _font.uss             ← Fonts (Montserrat)           ✅
│   │   ├── _theme.uss            ← L2 --thm-*                   ✅
│   │   ├── design-tokens.uss     ← L3 --sus-*                   ✅
│   │   ├── _icon.uss             ← .sus-icon-bg                 ✅
│   │   ├── Fonts/Montserrat/     ← 6 .ttf
│   │   └── Icons/
│   │       ├── core/{regular,fill}/
│   │       └── phosphor/{thin,light,regular,bold,fill,duotone}/
│   ├── SusTheme.cs / Services/SusThemeService.cs
│   ├── SusResolutionService.cs
│   ├── SusIconRegistry.cs + icon providers
│   ├── SusIcon.cs
│   ├── SusApp.cs
│   └── SusBootstrap.cs
```

**Versioning note:** package version is tracked in `package.json` (currently **`1.0.87`**).
For every `.cs` change in `sus-core`, bump `package.json` (pre-push hook requires it).



### 8.2 In a user project (optional override)

```
Assets/Resources/SusRuntime/
├── _font.uss      ← font override
├── _palette.uss   ← L1 palette override (optional)
├── _theme.uss     ← L2 theme override (optional)
└── Icons/
    └── {yourCollection}/{regular|fill|…}/  ← via ResourcesFolderIconProvider
```

Or use `SusApp.UseIcons(...)` / ` SusIconSetAsset` — see §4.7.

---



## 9. Task list



### Phase A: Fonts


| # | Problem | Files | Status |
| --- | ------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- | ------ |
| A1 | Add 4 missing Montserrat styles to `sus-core`: Thin, ExtraLight, SemiBold, ExtraBold                    | ` sus-core/Runtime/Resources/SusRuntime/Fonts/Montserrat/*.ttf` | ⬜      |
| A2 | Expand `_font.uss` - semantic variables `--font-heading`, `--font-body`, `--font-button`, `--font-caption` | ` sus-core/.../SusRuntime/_font.uss`                            | ⬜      |




### Phase B: Colors


| # | Problem | Files | Status |
| --- | -------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------- | -------- |
| B1 | Create `_palette.uss` (L1) + `_theme.uss` (L2 Dark/Light) | `…/_palette.uss`, `…/_theme.uss` | ✅ |
| B2 | Create `design-tokens.uss` — Layer 3: `--sus-*` tokens for components | ` sus-core/.../SusRuntime/design-tokens.uss`             | ✅ v1.0.19 |
| B3 | Create `SusThemeService` - SetTheme with class `.theme-light`/`.theme-dark`                          | ` sus-core/Runtime/SusThemeService.cs`                     | ✅ v1.0.19 |




### Phase C: Dimensions and resolutions


| # | Problem | Files | Status |
| --- | ----------------------------------------------------------------------------------------------------------- | --------------------------------------------------- | -------- |
| C1 | Spacing, font-size, radius — in `_palette.uss` Layer 1 | ` sus-core/.../SusRuntime/_palette.uss` | ✅ |
| C2 | Create `SusResolutionService` — ` ThresholdHigh` 1600, `.resolution-low`/`.resolution-high` | ` SusResolutionService.cs` | ✅ |
| C3 | Integrate `SusResolutionService.Instance.Update(root, width)` in ` SusComponent.Updated()` | ` SusComponent.cs` | ✅ |




### Phase D: Icons


| # | Problem | Files | Status |
| --- | ----------------------------------------------------------------- | ------------------------------------------- | -------- |
| D1 | Ship Phosphor (1512×6) + core subset under `Icons/phosphor`, ` Icons/core` | ` sus-core/.../SusRuntime/Icons/` | ✅ |
| D2 | `SusIconRegistry` + ` SusIconWeight` + aliases + provider chain | ` SusIconRegistry.cs`, `*IconProvider.cs` | ✅ |
| D3 | Downstream `SusIcon` component (phosphor/game/portrait) | optional component package | ✅ |
| D4 | `_icon.uss` — global `.sus-icon-bg` | ` sus-core/.../SusRuntime/_icon.uss` | ✅ |
| D5 | `tessellationMode: 1` on SVG metas | ` Icons/**/*.svg.meta` | ✅ |
| D6 | `PhosphorIconBootstrap` auto-register | ` PhosphorIconBootstrap.cs` | ✅ |
| D7 | Project overrides via `SusApp.UseIcons` / ` ResourcesFolderIconProvider` | Setup Project Customization/Icons | ✅ |




### Phase E: Bootstrap


| # | Problem | Files | Status |
| --- | ------------------------------------------------------------------------- | ---------------------------------- | -------- |
| E1  | `SusBootstrap` / ` SusApp` — full cascade + OverlayHost | ` SusBootstrap.cs`, ` SusApp.cs` | ✅ |
| E2 | Cascade on container: `_palette` → `_font` → `_theme` → ` design-tokens` → `_icon` | ` LoadDesignTokenCascade` | ✅ |
| E3 | `SusResolutionService.Instance.Update` in ` SusComponent.Updated()` | ` SusComponent.cs` | ✅ |




### Phase F: Documentation


| # | Problem | Files | Status |
| --- | -------------------------------------------------------------------------- | --------------------------------- | -------- |
| F1  | `DESIGN_TOKENS.md` - current code, rules, structure | ` sus-core/Docs/DESIGN_TOKENS.md` | ✅ current |
| F2 | ~~Update `ARCHITECTURE.md` — section Design Tokens~~ (the file has been deleted, everything is separated into separate docks) | — | ❌ removed |
| F3 | Update `README.md` — SusApp quick start with cascade | ` sus-core/README.md` | ✅ |


---



## Appendix A: Token Summary Table


| Token | Destination | Layer |
| ------------------------ | ------------------------ | -------- |
| `--font-family-regular`  | Basic font | Font |
| `--font-heading`         | Headings (Bold) | Font |
| `--font-body`            | Body text (Regular) | Font |
| `--sus-font-16`          | Text size 16px | Size |
| `--sus-space-16`         | Margin 16px | Size |
| `--sus-radius-md`        | Fillet 8px | Size |
| `--sus-text-primary`     | Body text | Color L3 |
| `--sus-text-secondary`   | Secondary text | Color L3 |
| `--sus-bg`               | Page background | Color L3 |
| `--sus-surface`          | Card/Panel Background | Color L3 |
| `--sus-border`           | Borders | Color L3 |
| `--sus-btn-primary-bg`   | Button - background | Color L3 |
| `--sus-btn-primary-text` | Button - text | Color L3 |
| `--sus-success`          | Color of success | Color L3 |
| `--sus-fail`             | Error color | Color L3 |

---

## Appendix B: Key rules and patterns (identified during the process)

### B.1 Statics in USS, dynamics in C#

```
┌─────────────┬────────────────────────┬──────────────────────────┐
│ Where │ Examples │ Rule │
├─────────────┼────────────────────────┼──────────────────────────┤
│ USS (static)│ background-size, │ Do not change in runtime │
│ │ flex-shrink, │ from props - write in USS │
│             │ margin-left, color,    │                          │
│             │ font-size (layout)     │                          │
├─────────────┼────────────────────────┼──────────────────────────┤
│ C# (dynamics) │ width/height from Size, │ Change from props or │
│ │ tint from TintColor, │ runtime logic - in code │
│             │ backgroundImage        │                          │
│             │ (Resources.Load)       │                          │
└─────────────┴────────────────────────┴──────────────────────────┘
```

**Motivation:** The main source of change is props. By changing the props, we only change the dynamic attribute in C#. The statics are in USS and are not spread out in the code. This makes it easier to read and debug.

**Example (good):**

```csharp
// C# - only dynamic from props
el.style.width = size;                     // ← from prop Size
el.style.height = size;
el.AddToClassList("icon-row");             // ← statics in USS

/* USS:
.icon-row { flex-direction: row; align-items: center; margin-bottom: 6px; } */
```

**Example (bad - antipattern):**

```csharp
el.style.flexDirection = FlexDirection.Row;   // ← static in C# (migrate to USS)
el.style.alignItems = Align.Center;
el.style.color = new StyleColor(Color.white);
```

### B.2 Sharq bare `<style>` — how selectors work

```
Source (.sharq) Compiled (.uss) Does root match?
─────────────────────────────────────────────────────────────────────
<style> .sus-icon { ✅ YES
    flex-shrink: 0;            flex-shrink: 0;
</style>                   }

<style> .sus-icon .sus-icon { ❌ NO
.sus-icon { flex-shrink: 0;            (.sus-icon inside .sus-icon)
    flex-shrink: 0;        }
}
</style>

<style> .sus-icon .child { ✅ YES (if child is inside)
.child {                        ...                    }
    ...
}
</style>
```

**Rule:** if you need to style the ROOT of a component, write the properties directly in `<style>` without selector. If you need to style child elements, use their classes (without the component prefix).

In special cases (self-target, container-target - see rule `sharq-css-scoping.mdc`) — move the rule to companion `.uss` as unscoped.

### B.3 `backgroundImage` re-assert pattern

UI Toolkit Known Issue: `VectorImage` Sometimes it doesn't render on the first frame. Proven pattern (from old ` SizedIcon`):

```csharp
el.style.backgroundImage = new StyleBackground(vec);
el.MarkDirtyRepaint();
el.schedule.Execute(() =>
{
    el.style.backgroundImage = new StyleBackground(vec);
    el.MarkDirtyRepaint();
}).ExecuteLater(0);
el.schedule.Execute(() =>
{
    el.style.backgroundImage = new StyleBackground(vec);
    el.MarkDirtyRepaint();
}).ExecuteLater(16);  // next frame
```

Reinstalling `backgroundImage` three times: immediately, after 0 frames, after ~1 frame. This ensures that ` VectorImage` will not be “lost” when the tree is rebuilt.

### B.4 SVG Import: tessellationMode

| Parameter | Meaning | Why |
|-------------------|----------|--------|
| `svgType`         | `3`      | UI Toolkit Vector Image (not Texture2D) |
| `tessellationMode`| `1`      | Antialiased Arc Encoding - **smooth edges** on arcs |
| `textureSize`     | `256`    | Enough for icons up to 48px |
| `sampleCount`     | `4`      | Multisampling for subpixel AA |

**Never** use `tessellationMode: 0` (Basic Triangulation) - Gives pixelated/jagged edges on SVG curves.

### B.5 USS loading: prefer SusApp / LoadTokenCascade

```csharp
// ✅ Correct: SusApp (or LoadTokenCascade) on the document root
SusApp.Create(uiDocument)
    .UseTheme(SusTheme.Dark)
    .Mount<App>();
// Cascade order: _palette → _font → _theme → design-tokens → _icon → extras + OverlayHost
// Panel TSS (_global) via ApplyDefaultTSS / SusDefault.tss

// ✅ Lower-level
SusBootstrap.ApplyDefaultTSS(uiDocument);
SusBootstrap.LoadTokenCascade(uiDocument.rootVisualElement);

// ❌ Incorrect: hand-add only design-tokens without _palette/_theme
root.styleSheets.Add(designTokensSheet);
```

### B.6 Versioning sus-core

For any change to `.cs` in ` sus-core`:
1. Increment the version in `sus-core/package.json` (patch version)
2. Commit with version tag
3. Push → cache in Unity Package Manager
4. Clear `Library/PackageCache/com.sharq-it.sus.core/` in the client
5. Delete `Packages/packages-lock.json`
6. Refresh Unity → the package will be resolved again

Script (`.uss`, `.svg`, `.meta`) changes do not require a manual cache flush.
