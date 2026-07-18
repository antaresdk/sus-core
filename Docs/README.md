# SusCore - documentation

> **Package:** `com.sharq-it.sus.core`
> **The foundation of the SUS UI system** is an analogue of Vue.js: reactivity, SFC compiler, directives, slots, CSS scoped

## Contents

| # | Document | Contents |
|---|---|---|
| 0 | [Integration from scratch](./00-integration.md) 🆕 | Installation, scene, first .sharq, SusApp, sus.config.json, Sharq generation |
| 1 | [Quick start](./01-quickstart.md) | Minimal component, bootstrap, installation, configuration |
| 2 | [`.sharq` and directives](./02-sharq-format.md) | SFC-format, $using, $MainElement, v-if/v-show/v-for/:text/@click/style |
| 3 | [Reactivity](./03-reactivity.md) | Prop&lt;T&gt;, Computed&lt;T&gt;, Watch&lt;T&gt;, DependencyTracker, P/C helpers |
| 4 | [Slots](./04-slots.md) | Named slots, default slot, SlotPropMap |
| 5 | [CSS Scoping](./05-css-scoping.md) | scoped/global, hash classes, Sharq restrictions |
| 6 | [Adaptive layout](./06-responsive.md) | SusBreakpointService, breakpoint classes, Watch |
| 7 | [OverlayHost and portals](./07-overlayhost.md) | Portal container, OverlayCategory, z-order |
| 8 | [Events](./08-events.md) | Emit/On, communication via Prop&lt;T&gt; |
| 9 | [Compilation and generation](./09-compilation.md) | Pipeline, incremental compilation, SharqValidator |
| 10 | [Configuration](./10-configuration.md) | sus.config.json |
| 11 | [API Reference](./11-api-reference.md) | SusApp, SusComponent, Bind helpers, SusBootstrap |
| 12 | [Running examples](./12-examples.md) | SusBootstrap + SusKeepAlive examples from Samples~ |
| 13 | [Built-in audits (Debug / QA)](./13-audits.md) | ClickAudit, BoundsAudit, CallbackAudit, OverlayAudit, StateAudit, LifecycleAudit, … |
| 14 | [Roadmap / Plans](./../roadmap/ROADMAP.md) | Tooltip, WorldSpace (healthbar/nameplate/floating damage) |
| 17 | [Dev console](./17-console.md) | SusConsoleService, hotkey `~`, OverlayCategory.Console |
| — | [Design tokens](./DESIGN_TOKENS.md) | Palette / theme / semantic tokens, fonts, icons, SusApp cascade |
| — | [Audit vs Vue](./VUE_NOTES.md) | Feature parity notes (WatchEffect, Devtools, …) |

## Quick start

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

```csharp
// MonoBehaviour on stage — prefer SusApp.
// Mount the component you defined above (HelloWorld); its .g.cs must be generated first.
public class AppEntry : MonoBehaviour
{
    public UIDocument uiDocument;
    void Start() => SusApp.Create(uiDocument).UseTheme(SusTheme.Dark).Mount<HelloWorld>();
}
```

## Where to look for what

- **I want to start from scratch** → [00-integration.md](./00-integration.md)
- **I want to understand the syntax of `.sharq`** → [02-sharq-format.md](./02-sharq-format.md)
- **I want to understand Prop&lt;T&gt; and Computed&lt;T&gt;** → [03-reactivity.md](./03-reactivity.md)
- **I want to make a modal/tooltip** → [07-overlayhost.md](./07-overlayhost.md)
- **I want the full API** → [11-api-reference.md](./11-api-reference.md)
- **I want to run an example** → [12-examples.md](./12-examples.md)
- **I want to know about built-in audits (Debug/QA)** → [13-audits.md](./13-audits.md)
- **I want to configure the compiler** → [10-configuration.md](./10-configuration.md)
- **I want themes / tokens / icons** → [DESIGN_TOKENS.md](./DESIGN_TOKENS.md)
- **I want the in-game console** → [17-console.md](./17-console.md)

## Related documents

- [sus-router](https://github.com/antaresdk/sus-router) — navigation and modals (MIT, open-core companion)
- [Integration from scratch](./00-integration.md) - installation and first screen
- [Design tokens](./DESIGN_TOKENS.md) - theming: colors, fonts, icons
- [OverlayHost and portals](./07-overlayhost.md) - overlays: tooltips, popups, modals
- [Audit vs Vue](./VUE_NOTES.md) - Vue feature parity
