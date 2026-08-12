# 1. Quick start

## Minimum component

```xml
<!-- HelloWorld.sharq -->
<template>
<ui:Label $MainElement :text="Message" class="greeting"
          style="font-size: 24px; -unity-text-align: middle-center; color: white;" />
</template>

<script>
public string Message = "Hello SUS!";
</script>
```

Compiles to:

```csharp
[UxmlElement]
public partial class HelloWorld : SusComponent
{
    public string Message = "Hello SUS!";

    protected override void Build()
    {
        AddToClassList("greeting");
        this.AddToClassList("sharq-HelloWorld-s0");
        BindText(this, () => Message);
    }
}
```

Usage in UXML:

```xml
<sus:HelloWorld />
```

## Connecting to the scene (bootstrap)

Analogue of the Vue approach `createApp(App).mount('#app')`.

> **Order matters.** A `Mount<App>()` entry only compiles **after** `App.sharq` has been
> generated into `App.g.cs`. Create the component first (save the `.sharq` / run Setup Project),
> then add the entry — otherwise you hit `CS0246` (the chicken-and-egg rule, see
> [00-integration](./00-integration.md)).

**1. Create the root component** — `App.sharq` (the `<sus:MainMenu>` / `<sus:BattleHUD>` children
below are your own components; optional UI libraries are separate products at https://sus-ui.dev):

```xml
<template>
<ui:VisualElement $MainElement class="app-root" style="flex-grow: 1;">
    <sus:MainMenu v-if="CurrentScreen == 'menu'" />
    <sus:BattleHUD v-if="CurrentScreen == 'battle'" />
</ui:VisualElement>
</template>

<script>
public string CurrentScreen = "menu";
</script>
```

**2. Add the entry point.** **Prefer `SusApp`** — the documented entry point (TSS, token cascade,
OverlayHost, world panel, theme):

```csharp
using Sharq.Core;
using UnityEngine;
using UnityEngine.UIElements;

public class AppEntry : MonoBehaviour
{
    public UIDocument uiDocument;

    void Start()
    {
        SusApp.Create(uiDocument)
            .UseTheme(SusTheme.Dark)
            .Mount<App>();
    }
}
```

**Lower-level alternative** — `SusBootstrap.Mount<T>` (loads the token cascade, but **does not**
apply `SusDefault.tss` or set a theme — call `SusBootstrap.ApplyDefaultTSS(uiDocument)` and
`SusThemeService.Instance.SetTheme(root, SusTheme.Dark)` yourself if you need them):

```csharp
using Sharq.Core;
using UnityEngine;
using UnityEngine.UIElements;

public class AppEntry : MonoBehaviour
{
    public UIDocument uiDocument;

    void Start()
    {
        // Vue analogue: createApp(App).mount('#app')
        SusBootstrap.Mount<App>(uiDocument);
    }
}
```

**Without UIDocument** — to any `VisualElement`:

```csharp
SusBootstrap.Mount<App>(someVisualElement);
```

**Passing parameters:**

```csharp
var app = SusBootstrap.Mount<App>(uiDocument);
app.IsLoggedIn = true;
app.PlayerName = "Alice";
```

**Multiple independent trees:**

```csharp
public class MultiPanelEntry : MonoBehaviour
{
    public UIDocument leftPanel;
    public UIDocument rightPanel;

    void Start()
    {
        var sidebar = SusBootstrap.Mount<Sidebar>(leftPanel);
        sidebar.ActiveTab = "inventory";

        var details = SusBootstrap.Mount<ItemDetails>(rightPanel);
        details.ItemId = 42;

        // Components from different trees communicate through events:
        sidebar.On("tab-changed", (Action<string>)(tab => details.FilterByTab(tab)));
    }
}
```

> **EventSystem:** `SusBootstrap.Mount<T>()` / `SusApp` automatically create an `EventSystem`
> (GameObject with `EventSystem` only — no `StandaloneInputModule`) the first time they run.
> UI Toolkit input does not require the legacy Input Module.

**Design-token cascade** — loaded onto the container in this order:

`_palette` → `_font` → `_theme` → `design-tokens` → `_icon` → registered extras (L4/L5) → OverlayHost

`_global.uss` is **not** part of this cascade. It is applied via panel TSS (`SusDefault.tss` /
`SusBootstrap.ApplyDefaultTSS` / `SusApp.Create(UIDocument)`).

**Default font** is Montserrat. Comes with the package (`_font.uss`). Preferred override is
`SusApp.UseFonts(SusFontAsset)` (see [Design tokens §2](./DESIGN_TOKENS.md)). To override in USS
instead:
1. Create `Assets/Resources/SusRuntime/_font.uss`
2. Add `:root { -unity-font-definition: url("path/to/YourFont.asset"); }` (use `-unity-font-definition`
   with a Font Asset, **not** the legacy `-unity-font`)

## Installation

**Via Unity Package Manager (Git URL):**

<!-- sus:gen urls -->
```
https://github.com/antaresdk/sus-core.git#v1.0.12
```
<!-- /sus:gen -->

---

## Composition of components (parent → child)

```xml
<!-- ParentScreen.sharq -->
<template>
<ui:VisualElement $MainElement class="parent">
    <!-- Literal prop -->
    <sus:SusButton variant="primary" :text="BtnText" />

    <!-- Reactive prop - when Status.Value changes, the button will be updated -->
    <sus:SusButton :variant="Status.Value" text="Dynamic" />

    <!-- Slot: content between tags → in <slot> child -->
    <sus:SusCard>
        <ui:Label text="I'm in the #default slot!" />
    </sus:SusCard>
</ui:VisualElement>
</template>

<script>
public Prop<string> BtnText = new("Click Me");
public Prop<string> Status = new("primary");
</script>
```

**Configuration** — create `Assets/sus.config.json`:

```json
{
  "SharqDirectory": "Assets/SusUI",
  "GeneratedDirectory": "Assets/SusUI/Generated",
  "EnableValidation": true,
  "StrictVForKey": true,
  "LogGeneratedFiles": true,
  "HotReloadStatePreserve": true
}
```
