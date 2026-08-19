# 00. SUS integration from scratch

> **Goal:** raise an empty Unity project to the first `.sharq` screen without accessing other documents.

---

## Critical: `SusApp` + generated files

**`Sharq.Core.SusApp` is the documented application entry point.** Prefer it over a hand-rolled
`Start()` that only calls `styleSheets.Add` / `Mount`. It applies TSS, token cascade, theme,
ensures **`OverlayHost`** (last child of the screen root), and creates a
**`SusWorldSpacePanel`** so world UI (`BindToWorld`, healthbars) has a known mount target.

### What sits where (after bootstrap)

`SusApp` is a **fluent bootstrap**, not a VisualElement with three child slots. The live tree:

```
Camera
└── __SusWorldSpacePanel__ (SusWorldSpacePanel + UIDocument)   ← UNDER all screen UI
    └── healthbars / nameplates / floating damage
        (WorldSpaceService.Default → BindToWorld)

Screen UIDocument  ← SusApp.Create(uiDocument) targets this root
└── rootVisualElement
    ├── screen content (Mount<T> or SusRouteView)           ← screens
    └── OverlayHost                                         ← last child: modals, tooltips,
                                                              dropdowns, toasts, console
```

World-space is **not** plugged into OverlayHost. After `Mount` / `Run`, use:

```csharp
WorldSpaceService.BindToWorld(healthBar, unit.transform, offset: new Vector3(0, 2f, 0));
// or: app.WorldPanel / WorldSpaceService.Default.WorldSpacePanel
```

Opt out: `.UseWorldSpace(false)` for pure 2D apps. Details: [07-overlayhost](./07-overlayhost.md).

### Chicken-and-egg: keep generated `.g.cs` in sync

Sharq sources (`.sharq`) are **not** C#. The compiler writes partials into
`GeneratedDirectory` (e.g. `Assets/SusUI/Generated/*.g.cs`). Your entry
(`MyApp` / Setup wizard starter) typically does `Mount<HomeScreen>()` — that type
**only exists after** generation.

**Setup Project copies a pre-baked starter from sus-core git** (`Editor/Setup/Starter~/`):

| Copied file | Role |
|---|---|
| `HomeScreen.sharq` | Source SFC |
| `Generated/HomeScreen.g.cs` | Already generated — project compiles immediately |
| `{AppName}.cs` | Calls `Sharq.Core.SusApp.Create(...).Mount<HomeScreen>()` |

**No first `RegenerateAll` is required for the default starter** — the wizard copies the
committed `.g.cs`, so the project compiles the moment the files land. `RegenerateAll` runs
only if you enable the customization scaffold **with** a downstream component package (`AppButton.sharq`),
which needs generation. Maintainers refresh the committed starter `.g.cs` with
`Tools~/refresh-starter-generated.ps1` or `Window → SUS → Setup → Refresh Starter Generated`,
then commit in **sus-core**.

| Bad state | What happens |
|---|---|
| `.sharq` exists, `Generated/` missing or empty | Project does not compile → Unity AssetPostprocessor / Sharq may not run → **compiler cannot heal itself** |
| Starter references a screen that was never generated | CS0246 / domain stuck broken |
| Delete `Generated/` “to clean up” without regenerating | Same chicken-and-egg |

**How to avoid breakage:**

1. Run **`Window → SUS → Setup Project`** once — copies config, SusApp starter, `HomeScreen.sharq` + **pre-generated** `HomeScreen.g.cs`. This is the recommended path (see Step 2).
2. Or, for a hand-made screen: create `sus.config.json` + `.sharq`, then save the `.sharq` (or **`Window → SUS → Sharq → Regenerate All Prototype Components`**) so `.g.cs` appear **before** (or together with) code that references those types.
3. Outside Unity (CI / fresh clone of a consumer app): CLI `sus-core/Tools~/SharqBootstrap` generates `.g.cs` without needing a compiled Editor.
4. Do **not** hand-edit `*.g.cs` / `*.g.uss` — edit `.sharq` and regenerate.
5. Consumer project `Generated/` is usually gitignored; **package repos** with optional component libraries and **core Starter~** ship generated sources so UPM / Setup compile out of the box.

`Window → SUS → Validate Setup` warns when `.sharq` exists but `Generated/` does not.

---

## Step 1. Install the package

Add to `Packages/manifest.json`:

<!-- sus:gen urls -->
```json
{
  "dependencies": {
    "com.sharq-it.sus.core": "https://github.com/antaresdk/sus-core.git#v1.0.19"
  }
}
```
<!-- /sus:gen -->

Unity will automatically pick up the package when the editor has focus. It will appear in Package Manager as **"SusCore"**.

> For local development you can use `"file:../sus-core"` next to your project. For production — git URL or npm registry.

---

## Step 2. Recommended path — Setup Project

**`Window → SUS → Setup Project`** does everything below in one click:

- checks the environment (Unity version, UI Toolkit, packages),
- writes `sus.config.json`,
- creates a scene with `UIDocument` + `PanelSettings`,
- **copies the pre-baked starter** (`HomeScreen.sharq` + `Generated/HomeScreen.g.cs` + `{AppName}.cs`),
- optionally scaffolds the `Customization/` folder.

Because the starter ships with its `.g.cs` already generated, the project compiles immediately —
**no first generation step is needed**. After the wizard finishes and Unity recompiles, jump
straight to **Step 7 (Play)**.

> Prefer this path. Steps 3–6 below describe the **manual** setup for a hand-made screen without
> the wizard — useful for understanding the pipeline or adding screens by hand.

---

## Step 3. First `.sharq` component (manual)

Create `Assets/SusUI/HelloScreen.sharq`:

```xml
<template>
    <ui:VisualElement $MainElement style="flex-grow: 1; align-items: center; justify-content: center;">
        <ui:Label text="Hello SUS!" style="font-size: 28px; color: white; -unity-text-align: middle-center;" />
    </ui:VisualElement>
</template>

<script>
$using Sharq.Core;
</script>
```

> In `<script>` use `$using` (Sharq directive), **not** a plain `using`. `Sharq.Core` is imported
> automatically, so an empty `<script>` (or none) works too — the `$using` above is just an example.

---

## Step 4. `sus.config.json`

Create `Assets/sus.config.json`:

```json
{
  "SharqDirectory": "Assets/SusUI",
  "GeneratedDirectory": "Assets/SusUI/Generated",
  "ResourcesDirectory": "Assets/SusUI/Generated/Resources/SusRuntime",
  "LogGeneratedFiles": true
}
```

| Field | Description |
|------|-----------|
| `SharqDirectory` | Where your `.sharq` files live |
| `GeneratedDirectory` | Where to write `.g.cs` and `.g.uss` |
| `ResourcesDirectory` | Where to write `Resources/` mirrors of `.uss` |
| `LogGeneratedFiles` | `true` (default) — log each generated file; set `false` to silence |

> If there is no file, the Sharq compiler uses the default paths (`Assets/SusUI`).

---

## Step 5. Generation of Sharq (manual path)

After saving `.sharq` the Sharq compiler automatically generates:

- `Assets/SusUI/Generated/HelloScreen.g.cs` — C# partial class
- `Assets/SusUI/Generated/HelloScreen_static.g.uss` — inline `style="…"` extracted to USS
  (a global `HelloScreen.g.uss` / scoped `HelloScreen_scoped.g.uss` appear only when the
  component has a `<style>` block)

**Order matters:** generate (or wait for import) **before** a `Mount<HelloScreen>()` entry
compiles. If the domain is already broken, use Setup Project / `Regenerate All Prototype
Components` / CLI bootstrap — see [Critical](#critical-susapp-generated-files) above.

Check the Unity console: `Window → SUS → Validate Setup` — the diagnostics will tell you if everything is OK.

---

## Step 6. Entry point — `SusApp` (manual path)

Create `MyApp.cs` and hang it on the GameObject with `UIDocument`:

```csharp
using Sharq.Core;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class MyApp : MonoBehaviour
{
    // OnEnable mirrors the Setup Project starter; Start() also works.
    private void OnEnable()
    {
        // Prefer Sharq.Core.SusApp — avoids clash with a legacy global SusApp type.
        Sharq.Core.SusApp.Create(GetComponent<UIDocument>())
            .UseTheme(SusTheme.Dark)
            .Mount<HelloScreen>();
    }
}
```

`SusApp.Create(UIDocument)` then `Mount` / `Run` — full Finalize order:

1. **Create** — applies `SusDefault.tss` to PanelSettings (`_palette` + `_font` + `_global` at panel level)
2. **Icons** — `UseIcons` providers registered (highest priority)
3. **Token cascade** — `_palette` → `_font` → `_theme` → `design-tokens` → `_icon` → L4/L5 extras + **`OverlayHost`** (last child)
4. **World** — `EnsureWorldSpacePanel` + `WorldSpaceService.Default` (unless `UseWorldSpace(false)`)
5. **Fonts** — `UseFonts` on root + OverlayHost
6. **Custom styles** — `UseCustomStyles` on root + OverlayHost (top override layer)
7. **Configure** — callbacks (router registration, manual UI)
8. **Mount** — root component (if `Mount<T>`)
9. **Theme last** — `SusThemeService.Instance.SetTheme(root, theme)` so OverlayHost inherits theme classes

This is the supported way to start a SUS app. Prefer Setup Project so the starter + first
`.g.cs` are created together.

---

## Step 7. Play

Click Play → white text “Hello SUS!” on a dark background.

---

## Alternative bootstrap — `SusBootstrap.Mount<T>()`

If `SusApp` is not suitable (for example, manual UI without themes):

```csharp
void Start()
{
    // Apply the default TSS + create OverlayHost.
    // NOTE: Mount<T> does NOT call ApplyDefaultTSS for you — do it explicitly here.
    SusBootstrap.ApplyDefaultTSS(_uiDocument);
    var root = _uiDocument.rootVisualElement;

    // Load the token cascade manually (_palette → _font → _theme → design-tokens → _icon).
    SusBootstrap.LoadTokenCascade(root);

    // Mount the component
    SusBootstrap.Mount<HelloScreen>(root);
}
```

You still need generated `.g.cs` for `HelloScreen`. Skipping `SusApp` does **not** remove
the chicken-and-egg rule. This path applies the cascade but **not** the theme —
call `SusThemeService.Instance.SetTheme(root, SusTheme.Dark)` yourself if you need it.

---

## Connecting sus-router

Open-core (free) packages from public GitHub:

<!-- sus:gen urls -->
```json
{
  "dependencies": {
    "com.sharq-it.sus.core":   "https://github.com/antaresdk/sus-core.git#v1.0.19",
    "com.sharq-it.sus.router": "https://github.com/antaresdk/sus-router.git#v1.0.11"
  }
}
```
<!-- /sus:gen -->

Optional UI component libraries are separate products — see https://sus-ui.dev.

With router (`using Sharq.Router;`). Register every screen you navigate to — the example uses
`HomeScreen` and `SettingsScreen`, so both must have `.sharq` sources (and generated `.g.cs`):

```csharp
SusApp.Create(_uiDocument)
    .UseTheme(SusTheme.Dark)
    .UseRouter(
        new SusRouter(),
        r =>
        {
            r.Register("/", typeof(HomeScreen));
            r.Register("/settings", typeof(SettingsScreen));
        },
        initialPath: "/")
    .Run();
```

---

## Setup Wizard and Customization/ folder

Everything above can be obtained with one button: **`Window → SUS → Setup Project`** — the wizard
checks the environment, writes `sus.config.json`, creates a scene with `UIDocument` + `PanelSettings`,
and **copies the pre-baked starter** (`HomeScreen.sharq` + already-generated `HomeScreen.g.cs` +
`{AppName}.cs`). No first generation is triggered for the default starter, so it compiles out of the
box (solves chicken-and-egg).

With the **Create customization scaffold** option (enabled by default), the wizard also creates
`{UI-root}/Customization/` — designated place for project customization with a live
example for each axis; everything is already connected in the starter via one `Use*` line:

| Axis | File | Line in starter |
|---|---|---|
| Colors/sizes (tokens) | `Customization/Theme/Resources/branding.uss` | `.UseCustomStyles("branding")` |
| Fonts | `Customization/Fonts/AppFonts.asset` | `.UseFonts(_fonts)` |
| Icons | `Customization/Icons/Resources/SusRuntime/Icons/app/…` | `.UseIcons(new ResourcesFolderIconProvider(<yourPackageOrCollection>, "app"))` |
| Your component | `Customization/Components/AppButton.sharq` (optional downstream package) | `<sus:AppButton>` in `HomeScreen.sharq` |

> **Icons — first argument caveat.** `ResourcesFolderIconProvider(editorPackagePath, collection)`
> resolves the editor scan against `Packages/{editorPackagePath}/Runtime/Resources/SusRuntime/Icons/{collection}`
> (with a fallback to `Assets/Resources/SusRuntime/Icons/{collection}`). For a **consumer app**
> shipping its own package, pass **your** package id (e.g. `"com.my.game"`); for icons under
> `Assets/.../Resources` pass an id whose path doesn't exist so the `Assets/Resources` fallback is
> used. Do **not** copy `"com.sharq-it.sus.core"` verbatim — that points the scan at the core
> package, not your icons. See [Design tokens §4](./DESIGN_TOKENS.md).

Card “I want to change X → edit file Y” — in the created `Customization/README.md`.
Running the wizard again does not erase edited files (only missing ones are created).

---

## Diagnostics

`Window → SUS → Validate Setup` checks:

- Unity version (≥6000.0) <!-- sus:ok min version documented in package.json -->
- UI Toolkit
- `sus.config.json` and paths
- Availability of `.sharq` and `.g.cs`
- `PanelSettings`
- Availability of packages

---

## Further

- [Quick start](./01-quickstart.md) — more about `.sharq`, compositions, props
- [OverlayHost](./07-overlayhost.md) — screen overlay layers vs world-space
- [Design tokens](./DESIGN_TOKENS.md) — theming: colors, fonts, icons
- Theme extension notes — `Documentation~/theme-extension.md` in the package repo
