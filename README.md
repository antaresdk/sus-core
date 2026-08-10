# SusCore

**Foundation of the SUS UI system.** Analogue of Vue.js — reactivity, SFC compiler, directives, slots, CSS scoped, themes, breakpoints, world-space, console.

<!-- sus:gen ver pkg=sus-core -->
**Package:** `com.sharq-it.sus.core` (current version — `1.0.6`)
<!-- /sus:gen -->

---

## What's inside

| Subsystem | Files | Status |
|---|---|---|
| **Reactivity** | `Prop<T>`, `Computed<T>`, `ReactiveEffect`, `DependencyTracker` | ✅ |
| **Component Model** | `SusComponent`, `Watch()`, `WatchEffect()`, lifecycle hooks | ✅ |
| **Bindings** | `BindText`, `BindShow`, `BindVisibility`, `BindClass`, `BindList`, `BindModel` | ✅ |
| **Themes** | `SusThemeService` + `SusTheme` (`readonly struct`) + `.theme-*` classes | ✅ |
| **Colors (3 layers)** | `_palette.uss` (L1 `--base-*`), `_theme.uss` (L2 `--thm-*`), `design-tokens.uss` (L3 `--sus-*`) | ✅ |
| **Fonts** | `_font.uss` (Montserrat + override), `--sus-font-*` tokens | ✅ |
| **Icons** | Phosphor (~1512×6) + core subset, `SusIconRegistry` / providers, `SusIcon`, theme tint | ✅ |
| **Breakpoints** | `SusBreakpointService`, `Prop<Breakpoint>`, `.breakpoint-*` classes on root | ✅ |
| **OverlayHost** | Portal container, layers by `OverlayCategory`, z-order via DOM | ✅ |
| **World-space** | `WorldSpaceService` (separate world panel preferred; OverlayCategory.World fallback) | ✅ |
| **Console** | `SusConsoleService` + `SusConsoleDriver` (hotkey `~`, filter, search, Tab-completion) | ✅ |
| **Compiler** | Sharq SFC → C#, scoped CSS, validator, incremental compilation | ✅ |
| **Audit (Debug/QA)** | 21 modules + ScreenAudit (text screen dumps): ClickAudit, BoundsAudit, CallbackAudit, OverlayAudit, StateAudit, LifecycleAudit, NavigationAudit, PerformanceAudit, DebounceAudit, ClickTargetSizeAudit, StackDepthAudit, GuardAudit, ModalStackAudit, EmptyStateAudit, RemountLoopAudit, OverflowAudit, DeadRouteAudit, SusTable StateAudit, LayoutReentryAudit, IdleGuardAudit, FocusTrapAudit | ✅ |

## Quick start

```csharp
// MonoBehaviour on stage — prefer SusApp:
public class AppEntry : MonoBehaviour
{
    public UIDocument uiDocument;
    void Start()
    {
        SusApp.Create(uiDocument)
            .UseTheme(SusTheme.Dark)
            .Mount<AppScreen>();
        // Finalize order: icons → cascade → world → fonts → custom → configure → mount → theme
        // Cascade: _palette → _font → _theme → design-tokens → _icon → extras + OverlayHost
        // (_global via SusDefault.tss / ApplyDefaultTSS)
    }
}
```

## Installation

<!-- sus:gen urls -->
```
https://github.com/antaresdk/sus-core.git#v1.0.6
```
<!-- /sus:gen -->

Configuration (`Assets/sus.config.json`):

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

## What replaces

| Old (v1) | New (v2) |
|---|---|
| `sus` (UPM `com.sus.sfc`) | `com.sharq-it.sus.core` |
| `sharq-ui-system` (SusCompiler.exe, LibSassHost) | `SharqFileImporter` (AssetPostprocessor) |
| `ElementBase` — reflective | `SusComponent : VisualElement` |
| `compiled ui/` — a mixture of manual and auto | `generated/` — auto, `.gitignored` |

## Place in the ecosystem

```
sus-core (this package)
    ├── sus-router — navigation (Push/Replace/Back, screens, modals, KeepAlive)
    └── your Unity project — consumer app
```

## Documentation

Full documentation: [`Docs/README.md`](./Docs/README.md)

Roadmaps and plans: [`roadmap/`](./roadmap/)
