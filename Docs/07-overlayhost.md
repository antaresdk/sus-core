# 7. OverlayHost and portals

OverlayHost is a portal container for tooltips, popups, modals, dropdowns, toasts, and the
dev console. It renders **above route screens** (last sibling in the screen-space DOM —
UI Toolkit has no `z-index`).

**World-space UI (health bars, nameplates) — always UNDER screens, never in this host.**
World markers belong to 3D objects, so a menu or HUD must be able to cover them — they must
**never** live in the OverlayHost (the top-most layer). Two modes, both below the screens:
- *Variant A* (default — `SusApp` / `SusBootstrap.EnsureWorldSpacePanel`): a **separate** panel
  (`SusWorldSpacePanel`, `PanelSettings.renderMode = WorldSpace`) the camera draws under all screen UI.
- *Variant B* (fallback, no world-space panel): `WorldSpaceService` renders flat screen-space markers
  into a dedicated **`WorldMarkerLayer`** — the **first** child of the screen root (lowest z, below
  screens **and** this OverlayHost) — repositioned per frame via `WorldToScreenPoint`.

See `WorldSpaceService` in the API reference and [11-api-reference](./11-api-reference.md).

`SusApp` always builds a fixed three-layer scaffold on the UIDocument root — world markers stay
under screens, overlays stay on top, and all app content mounts into the middle `ScreenHost`:

```
World-space panel (separate UIDocument)          ← variant A, UNDER screens
└── healthbar / nameplate / floating damage

Screen-space UIDocument (root)
├── WorldMarkerLayer  ← first child = lowest (variant-B world markers)
├── ScreenHost        ← app content: Mount<T> component / router SusRouteView
└── OverlayHost       ← last child = always on top of screens
     ├── transition (Transition = 10)
     ├── modal      (Modal      = 20)
     ├── tooltip    (Tooltip    = 30)  ← above modals
     ├── dropdown   (Dropdown   = 40)  ← above modals
     ├── toast      (Toast      = 45)
     └── console    (Console    = 50)  ← topmost
```

Tooltips and dropdowns sit **above** modals on purpose: a modal can contain a Select,
menu, or control with a hover tooltip — those popups must not paint under the dialog.

## Creation

`AddToOverlay` / `RemoveFromOverlay` are **methods on `OverlayHost`** (not on `SusComponent`).
Get the host via `SusBootstrap`:

```csharp
var overlay = SusBootstrap.GetOrCreateOverlay(root);

var popup = BuildMyPopup();
overlay.AddToOverlay(popup, OverlayCategory.Dropdown, dismissOnClickOutside: true);
// ...
overlay.RemoveFromOverlay(popup);

overlay.AddToOverlay(myTooltip, OverlayCategory.Tooltip);
overlay.AddToOverlay(myModal, OverlayCategory.Modal);
```

## OverlayCategory — z-order

Higher value = drawn later = on top (no USS `z-index`). Values from
`sus-core/Runtime/OverlayCategory.cs`:

| Category | Value | Role |
|---|---|---|
| `World` | 0 | **Legacy / internal.** World markers do **not** belong in this host (they'd paint over screens). They render **under** screens — variant A in a separate `SusWorldSpacePanel`, variant B in a dedicated `WorldMarkerLayer` (first child of the root). This value survives only for the deprecated `WorldSpaceService.OverlayHost` fallback. |
| `Transition` | 10 | Route transition curtain |
| `Modal` | 20 | Dialogs, drawers |
| `Tooltip` | 30 | Hover hints (above modals) |
| `Dropdown` | 40 | Menus, autocomplete (above modals) |
| `Toast` | 45 | Snackbars |
| `Console` | 50 | Dev console / error overlays (topmost) |

Within one category, the last added entry is on top (modal stack, toast stack, …).

## OverlayEntry

```csharp
var entry = overlay.AddToOverlay(dialog, OverlayCategory.Modal);
// entry.Element — the dialog itself
// entry.Category — category
// entry.DismissOnClickOutside — false (can be set at creation)
// entry.OnDismiss — callback
```

## Cleaning

```csharp
overlay.ClearCategory(OverlayCategory.Tooltip);  // remove all tooltips
overlay.ClearAll();                               // remove everything
```

## Convenience methods

For common patterns you don't need to position elements by hand:

```csharp
// Tooltip anchored to an element (position: "top" | "bottom" | "left" | "right").
// Added to the Tooltip category, dismissOnClickOutside: true.
overlay.ShowTooltip(anchor, tooltipContent, position: "top");

// Dropdown below an anchor; auto-flips above when there's no room. Dropdown category.
overlay.ShowDropdown(anchor, menuContent);

// One-time Escape handler: dismisses the topmost dismissable overlay. Idempotent.
overlay.InstallEscapeHandler();

// Trap Tab / Shift+Tab focus within a modal element.
overlay.InstallFocusTrap(myModalRoot);
```

## Theming (does the theme reach overlays?)

**Yes.** The OverlayHost is the last child of the screen root and receives the full design-token
cascade during bootstrap (`SusApp` / `SusBootstrap.GetOrCreateOverlay`), plus:

- `SusApp.UseCustomStyles(...)` (branding) and `UseFonts(...)` are applied to the OverlayHost as
  well as the screen root, so popups match your brand colors and typeface.
- `SusThemeService.Instance.SetTheme(root, theme)` also swaps the `.theme-*` class **on the
  OverlayHost and on every currently open overlay child**, so reparented popups repaint with the
  active theme.

You normally don't do anything — mount via `SusApp` and overlays inherit tokens + theme + branding
automatically. Manual bootstraps must call `SetTheme` after building the overlay for it to pick up
a non-default theme.

## Related (Core)

| Piece | Status | Notes |
|---|---|---|
| [Dev console](./17-console.md) (`SusConsoleService`) | Shipped | `OverlayCategory.Console` |
| `SusToast` / toast UI kit widget | Planned | Use `OverlayCategory.Toast` / `SusToastBase` when you build one |
| World-space healthbar / nameplate | Downstream UI package | Separate world-space panel — **under** screens, not OverlayHost |

## API

```csharp
public class OverlayHost : SusLayer   // SusLayer : VisualElement
{
    public OverlayEntry AddToOverlay(VisualElement element, OverlayCategory category,
        bool dismissOnClickOutside = false, Action onDismiss = null);
    public void RemoveFromOverlay(VisualElement element);
    public void RemoveFromOverlay(OverlayEntry entry);
    public void ClearCategory(OverlayCategory category);
    public void ClearAll();
    public int Count { get; }
    public IReadOnlyList<OverlayEntry> Stack { get; }
    public void DumpStack();   // #if UNITY_EDITOR || DEVELOPMENT_BUILD - diagnostics
}

public class OverlayEntry
{
    public VisualElement Element;
    public OverlayCategory Category;
    public bool DismissOnClickOutside;
    public Action OnDismiss;
}
```
