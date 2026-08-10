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

## Built on sus-core

`sus-core` itself has no visible widgets — it's the reactivity engine, theming cascade, OverlayHost and
compiler that everything else in SUS renders through. Here's what that looks like once
`downstream library` components sit on top of it:

<table>
<tr>
<td><img src="Documentation~/images/kit-app-bar.png" width="260" alt="Theme tokens"><br><sub>Theme tokens (`_theme.uss` / `design-tokens.uss`) — SusAppBar</sub></td>
<td><img src="Documentation~/images/kit-diagnostics-panel.png" width="260" alt="Audit tooling"><br><sub>Audit tooling (`SusDiagnosticsPanel`) inspecting the reactive tree</sub></td>
<td><img src="Documentation~/images/kit-avatar-group.png" width="260" alt="Bindings"><br><sub>`Prop&lt;T&gt;` / bindings driving a `SusAvatarGroup`</sub></td>
</tr>
</table>

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

