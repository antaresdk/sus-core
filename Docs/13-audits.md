# SusAudit - built-in checks (Debug / QA)

> Automatic audit modules built into`sus-core`, ` sus-router`And` downstream library`.
> Everyone turns on via`#if UNITY_EDITOR || DEVELOPMENT_BUILD`.
> In the release build, they are completely cut out by the compiler - **zero runtime overhead**.

## Pivot table

| # | Module | Where is it sewn | Trigger | Automatic |
|---|--------|----------|---------|:---:|
| 1 | **ClickAudit** | `SusComponent` + ` ClickAuditService`| Every` ClickEvent`on registered elements | ✅ |
| 2 | **BoundsAudit** | `SusComponent` constructor | 2 frames after mount | ✅ |
| 3 | **CallbackAudit** | `SusComponent.cs` methods + 11`.sharq` | ` ClickEvent`handler guard conditions / timing | ⚠️`.sharq` |
| 4 | **OverlayAudit** | `OverlayHost.AddToOverlay()`| Adding an element to the overlay | ✅ |
| 5 | **StateAudit** | `.sharq` (SusButton/Toggle/Dropdown/ListGroup/Modal) | ` WatchEffect`on controversial props | ⚠️`.sharq` |
| 6 | **LifecycleAudit** | `SusComponent.OnDetachFromPanelHandler()`| Detach from panel | ✅ |
| 7 | **NavigationAudit** | `SusRouter.Push/Replace/PushNamed/ReplaceNamed` | ` Resolve()`returned null | ✅ |
| 8 | **PerformanceAudit** | `SusComponent` constructor | 500ms after mount | ✅ |
| 9 | **DebounceAudit** | `SusComponent` constructor | Every`ClickEvent` on interactive elements | ✅ |
| 10 | **ClickTargetSizeAudit** | `SusComponent` constructor (combined with BoundsAudit) | 2 frames after mount | ✅ |
| 11 | **StackDepthAudit** | `SusRouter.PushRecord`| After`_history.Add()` | ✅ |
| 12 | **GuardAudit** | `SusRouter.Navigate()`| After` NavigateCore`- if the result` Aborted` | ✅ |
| 13 | **ModalStackAudit** | `OverlayHost.AddToOverlay()`| Adding a modal to the overlay | ✅ |
| 14 | **EmptyStateAudit** | `.sharq` (SusListGroup/SusDropdown) | ` Items.Count == 0`but the element is open/visible | ⚠️`.sharq` |
| 15 | **RemountLoopAudit** | `SusComponent.OnAttachToPanelHandler()`| >5 Attach in 1 second | ✅ |
| 16 | **OverflowAudit** | `SusComponent` constructor | Children go beyond the bounds of the parent (Unity does not clip) | ✅ |
| 17 | **DeadRouteAudit** | `SusRouter`(manual call` AuditUnusedRoutes()`) | Registered but never used routes | 🔧 manual |
| 18 | **SusTable StateAudit** | `.sharq` (SusTable) | ` WatchEffect` — ` ItemsPerPage > Items`or` Page > TotalPages` | ⚠️ `.sharq` |
| 19 | **LayoutReentryAudit** | `SusComponent.OnGeometryChangedForBreakpoint()` | >20 ` GeometryChanged`in 500ms | ✅ |
| 20 | **IdleGuardAudit** | `SusComponent` constructor | The element is visible for 30+ seconds without clicks | ✅ |
| 21 | **FocusTrapAudit** | `.sharq` (SusModal) | ` FocusEvent`— Tab takes focus away from the modal | ⚠️`.sharq` |

> ✅ = fully automatic, no need to add anything to the components.
> ⚠️ = requires one line`SetClickAuditDescription("Name")` or`WatchEffect` V`.sharq`(already built into all components).
> 🔧 = manual method call.

---

## 1. ClickAudit - whether the click reaches the element

**Files:**`sus-core/Runtime/Diagnostics/ClickAuditService.cs`, ` sus-core/Runtime/SusComponent.cs`

**Problem:** the element is registered as clickable, but when the mouse is clicked the event does not reach - it is intercepted by another element above (tooltip, overlay, modal with the wrong`pickingMode`).

**How ​​it works:**
- `ClickAuditService.Instance.Install(panel)`- Called automatically at first` SusBootstrap.Mount<T>()`.
- The service registers global`ClickEvent` on`panel.visualTree`.
- Compares with each click`evt.target` with registered elements.
- If the click does not hit any registered element, the layer of the interceptor element is logged.

**Example warning:**
```
[ClickAudit] Active: 'SusButton' blocked at center. Reason: Covered by 'SusTooltip'
```

**Integration:** element is registered via`SetClickAuditDescription("Name")` V`Created()`. Already built into 11 components (` SusButton`, ` SusLink`, ` SusToggle`, ` SusUnitCard`, ` SusDropdown`, ` SusListGroup`, ` SusModal`, ` SusTable`, ` SusForm`, ` SusDiagnosticsPanel`, ` MigratedMainMenuScreen`).

---

## 2. BoundsAudit - whether the sizes are non-zero

**File:**`sus-core/Runtime/SusComponent.cs`(constructor)

**Problem:** Unity UITK **doesn't call**`ContainsPoint` for elements with zero dimensions. The click goes through. Reasons:
- No`flexGrow`, ` width`, ` height`in USS/styles
- The parent did not set the size
- `display: none` / ` visible: false`
- The element is not added to the tree (not attached to the panel)

**How ​​it works:** delayed check via`schedule.Execute(...).StartingIn(150)`— waits 2-3 frames for the layout to be calculated, then checks` worldBound.width`And` worldBound.height`.

**Example warning:**
```
[BoundsAudit] 'SusUnitCard' has zero bounds after mount (0×0).
  display=Flex, visible=True, pickingMode=Position.
  Clicks will NOT reach this element.
```

---

## 3. CallbackAudit - whether OnClick worked

**Files:**`sus-core/Runtime/SusComponent.cs`, 8 `.sharq`

**Problem:**`ClickEvent` is registered, the element is available, the click goes through - but the handler is NOT called. Reasons:
- `Disabled.Value = true`→ handler exits early
- `Loading.Value = true`→ handler exits early
- Guard condition:`if (someCondition) return;`
- Handler executed, but took > 50ms (suspiciously long)

**Two sub-modes:**

### 3a. Guard-lock (`AuditClickBlocked`)
```csharp
// In SusButton.sharq:
if (Disabled.Value)
{
    AuditClickBlocked("Disabled");  // ← warning to the console
    return;
}
```
Example:`[CallbackAudit] 'SusButton' click blocked: Disabled`

### 3b. Timing (`AuditClickStart` / ` AuditClickEnd`)
```csharp
var t0 = AuditClickStart();
OnClick?.Invoke();
AuditClickEnd(t0);  // if > 50ms → warning
```
Example:`[CallbackAudit] 'SusButton' OnClick took 127.3ms`

**Sewn into:**`SusButton`, ` SusLink`, ` SusToggle`, ` SusUnitCard`, ` SusDropdown`, ` SusListGroup`, ` SusModal`, ` SusForm`.

---

## 4. OverlayAudit - UI overlap

**File:**`sus-core/Runtime/OverlayHost.cs`

**Problem:** When adding an element to`OverlayHost`(tooltip, dropdown, modal) a new element can intercept clicks with its pickable children.

**How ​​it works:** in`AddToOverlay()` after inserting the element -`Query<VisualElement>()` looking for everything`pickingMode == Position` inside, and if they are (and the category is not`Modal`) - varnit.

**Example warning:**
```
[OverlayAudit] 'SusTooltip' added to overlay in Tooltip category
  with 3 pickable children. It may block clicks to underlying UI.
  Consider setting pickingMode=Ignore on children.
```

---

## 5. StateAudit - consistency of props

**Files:**`SusButton.sharq`, ` SusToggle.sharq`, ` SusDropdown.sharq`, ` SusListGroup.sharq`, ` SusModal.sharq`

**Problem:** inconsistent states -`Disabled=true` And`Loading=true` simultaneously (the spinner will never appear),`IsOpen=true` at`Disabled=true`(dropdown is open but inactive),` Selected`not in` Items`(no highlighted element),` Model=false`but the modal is visible.

**How ​​it works:**`WatchEffect` inside`.sharq` components monitors combinations of props.

| Component | Rule |
|-----------|---------|
| `SusButton` | ` Disabled && Loading`→ both true at the same time |
| `SusToggle` | ` Disabled && Loading`→ both true at the same time |
| `SusDropdown` | ` IsOpen && Disabled`→ open but disabled |
| `SusListGroup` | ` Selected`not in` Items`→ no highlighted element |
| `SusModal` | ` Model=false`But` display ≠ None`→ hidden modal is visible |

**Examples of warnings:**
```
[StateAudit] SusButton: both Disabled and Loading are true.
  Loading spinner will never be visible.

[StateAudit] SusDropdown: IsOpen=true while Disabled=true.
  Dropdown should close when disabled.

[StateAudit] SusListGroup: Selected value is not in Items.
  List will have no highlighted item.

[StateAudit] SusModal: Model=false but element is still visible
  (display=Flex). Modal should be hidden.
```

---

## 6. LifecycleAudit - subscription leaks

**File:**`sus-core/Runtime/SusComponent.cs` (` OnDetachFromPanelHandler`)

**Problem:** the element has been removed from the panel, but subscriptions to`Prop<T>.Changed` remained - memory leak and potential errors when remounting.

**How ​​it works:** with detach it remembers`_bindings.Count`, after 1 second checks if the panel is still null and the number of subscriptions has not decreased → leak.

**Example warning:**
```
[LifecycleAudit] 'SusUnitCard' detached but 3 bindings were not disposed.
  Possible leak.
```

---

## 7. NavigationAudit - unresolved routes

**File:**`sus-router/Runtime/SusRouter.cs`

**Problem:**`SusRouter.Push("/settings")` called but the path was not resolved (`Resolve` returned null) - the user presses the button, nothing happens.

**How ​​it works:** in each navigation method (`Push`, ` Replace`, ` PushNamed`, ` ReplaceNamed`) - If` record == null` → warning.

**Example warning:**
```
[NavigationAudit] Push('/battle/unknown') — route not found.
```

---

## 8. PerformanceAudit - too many elements

**File:**`sus-core/Runtime/SusComponent.cs`(constructor)

**Problem:** deep tree or many children (>500 VisualElements) → slow`PickAll`, long layout, slow UI.

**How ​​it works:** 500ms after mount -`Query<VisualElement>()` counts all elements in a subtree. If > 500 → warning.

**Example warning:**
```
[PerfAudit] 'SusTable' has 1423 VisualElements.
  Consider virtualization (SusTable) or paging.
```

---

## 9. DebounceAudit - double clicks

**File:**`sus-core/Runtime/SusComponent.cs`(constructor)

**Problem:** the user presses the button twice quickly (< 300ms between clicks) - double-submit is possible (two API calls, double debiting of currency, etc.).

**How ​​it works:** in the constructor`SusComponent` registered`ClickEvent` callback (fires BEFORE handlers in`Created()`— UITK guarantees the registration procedure). For each interactive element (with` SetClickAuditDescription`) remembers the time of the last click. If <300ms has passed - warning.

**Example warning:**
```
[DebounceAudit] 'SusButton' rapid double-click (87ms).
  Possible unintended double-submit.
```

**Important:** this is an **audit**, not a block. It does not prevent a second click - it only warns the developer that a debounce is needed in the business logic.

---

## 10. ClickTargetSizeAudit - small target

**File:**`sus-core/Runtime/SusComponent.cs`(constructor, combined with BoundsAudit)

**Problem:** interactive element (button, icon, checkbox) smaller than 30x30px - difficult to hit with finger/mouse. HIG recommends a minimum of 44x44px.

**How ​​it works:** together with BoundsAudit, for elements with`SetClickAuditDescription`- If` worldBound`> 0 but < 30px on any axis → warning.

**Example warning:**
```
[ClickTargetAudit] 'SusIcon' tap target is small (16×16px).
  HIG recommends ≥44×44. Consider padding or min-size.
```

---

## 11. StackDepthAudit - navigation stack depth

**File:**`sus-router/Runtime/SusRouter.cs`

**Problem:** navigation history grows unlimitedly (> 50 entries) - possible cyclic navigation (A → B → A → B...) or forgotten`Replace()` instead of`Push()`.

**How ​​it works:** after each`_history.Add()` V`PushRecord`- examination`_history.Count > 50`.

**Example warning:**
```
[StackDepthAudit] Router history has 67 entries.
  Possible circular navigation or unbounded stack growth.
  Consider using Replace() instead of Push().
```

---

## 12. GuardAudit - rejected navigation

**File:**`sus-router/Runtime/SusRouter.cs`

**Problem:** the user presses the go button, the navigation is silently rejected by the guard or lifecycle hook - neither the screen changes nor the error messages.

**How ​​it works:** in`Navigate()`- single point of interception after` NavigateCore`. If the result` NavigationResult.Aborted`— warning with from→to paths.

**Example warning:**
```
[GuardAudit] Nav from '/lobby' → '/battle' was rejected by a guard
  or lifecycle hook (BeforeLeave/CanLeave/BeforeEach/CanEnter/BeforeResolve/BeforeEnter).
```

**Covers all abort points:**`BeforeRouteUpdate`, ` BeforeLeave`, ` CanLeave` (ISusRouteGuard), ` BeforeEach`(global),` CanEnter` (ISusRouteGuard), ` BeforeResolve`(global),` BeforeEnter`.

---

## 13. ModalStackAudit - too many modals

**File:**`sus-core/Runtime/OverlayHost.cs`

**Problem:** there are > 5 modals on the screen at the same time - there may be a bug (unclosed modals) or bad UX (the user does not understand which modal is active).

**How ​​it works:** in`AddToOverlay()` after adding - counts`_stack.Count(e => e.Category == OverlayCategory.Modal)`. If > 5 - warning.

**Example warning:**
```
[ModalStackAudit] 7 modals on screen.
  Deep modal stacking may indicate a flow bug or unclosed modals.
```

---

## Architecture

```
sus-core/Runtime/
├── SusComponent.cs            ← BoundsAudit, ClickTargetAudit, PerfAudit,
│                                 DebounceAudit, LifecycleAudit, RemountLoopAudit,
│                                 OverflowAudit, IdleGuardAudit, LayoutReentryAudit,
│                                 AuditClickBlocked/Start/End
├── Diagnostics/
│ └── ClickAuditService.cs ← ClickAudit (global click interception)
├── OverlayHost.cs             ← OverlayAudit, ModalStackAudit
│
sus-router/Runtime/
└── SusRouter.cs               ← NavigationAudit, GuardAudit, StackDepthAudit,
                                  DeadRouteAudit

downstream library/Components/
├── atoms/
│   ├── SusButton.sharq        ← CallbackAudit + StateAudit
│   ├── SusLink.sharq          ← CallbackAudit
│   ├── SusToggle.sharq        ← CallbackAudit + StateAudit
│   └── SusDropdown.sharq      ← CallbackAudit + StateAudit + EmptyStateAudit
├── molecules/
│   ├── SusUnitCard.sharq      ← CallbackAudit
│   ├── SusListGroup.sharq     ← CallbackAudit + StateAudit + EmptyStateAudit
│   └── SusModal.sharq         ← CallbackAudit + StateAudit + FocusTrapAudit
└── data/
    ├── SusTable.sharq         ← SetClickAuditDescription (ClickAudit) + StateAudit
    └── SusForm.sharq          ← CallbackAudit
```

## Enable / disable

All modules are active when:
```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
```

In the release build (`!UNITY_EDITOR && !DEVELOPMENT_BUILD`) the code is **completely cut out by the compiler** - not a single byte, not a single call.

---

## 14. EmptyStateAudit - empty list / dropdown

**Files:**`SusListGroup.sharq`, ` SusDropdown.sharq`

**Problem:**`Items.Count == 0`, but the element is open or visible - the user sees an empty container.

**How ​​it works:**`WatchEffect` inside`.sharq` keeps track of the combination.

**Examples of warnings:**
```
[EmptyStateAudit] SusListGroup: Items is empty but element is visible.
[EmptyStateAudit] SusDropdown: IsOpen=true but Items is empty.
```

---

## 15. RemountLoopAudit - cyclic remounts

**File:**`sus-core/Runtime/SusComponent.cs` (` OnAttachToPanelHandler`)

**Problem:** > 5 Attach in 1 second → reactivity bug (WatchEffect modifies visibility/layout).

**Example warning:**
```
[RemountLoopAudit] 'SusUnitCard' attached 7 times in 1s.
```

---

## 16. OverflowAudit - children going beyond bounds

**File:**`sus-core/Runtime/SusComponent.cs`(constructor)

**Problem:** children exceed the parent's boundaries. Unity UITK does not support`overflow: hidden`.

**Example warning:**
```
[OverflowAudit] 'SusCard' has 3 child(ren) exceeding parent bounds (320×180).
```

---

## 17. DeadRouteAudit - unused routes

**File:**`sus-router/Runtime/SusRouter.cs`

**Problem:** routes registered but never used are dead code.

**Call:**`router.AuditUnusedRoutes()`(manual, from the dev-panel or tests).

**Example warning:**
```
[DeadRouteAudit] 3 registered routes were never navigated to:
  - /settings/audio (settings-audio)
```

---

## 18. SusTable StateAudit - inconsistent pagination

**File:**`SusTable.sharq`

**Problem:**`ItemsPerPage > Items.Count`- one page, extra pagination.` Page > TotalPages`— the table shows empty rows.

**How ​​it works:**`WatchEffect` keeps an eye on`Controller.Value.Items`, ` ItemsPerPage`, ` Page`, ` TotalPages`.

**Examples of warnings:**
```
[StateAudit] SusTable: ItemsPerPage=25 but only 3 items. Single page.
[StateAudit] SusTable: Page=5 exceeds TotalPages=3. Table may show empty rows.
```

---

## 19. LayoutReentryAudit - recursive layout

**File:**`sus-core/Runtime/SusComponent.cs` (` OnGeometryChangedForBreakpoint`)

**Problem:** WatchEffect modifies size/position → GeometryChangedEvent → Updated() → WatchEffect → infinite loop. UI freezes with high CPU load.

**How ​​it works:** 500ms sliding window, counter`GeometryChangedEvent`. If > 20 per window - warning.

**Example warning:**
```
[LayoutReentryAudit] 'SusCard' 47 geometry changes in 500ms.
```

---

## 20. IdleGuardAudit - element was never clicked

**File:**`sus-core/Runtime/SusComponent.cs`(constructor)

**Problem:** the interactive element is visible for 30+ seconds, but not a single click - perhaps`pickingMode=Ignore`, covered with transparent overlay or forgotten` ClickEvent`.

**How ​​it works:** one-time check 30 seconds after mount. If`_lastClickTime == 0`— warning with diagnostic information (pickingMode, worldBound).

**Example warning:**
```
[IdleGuardAudit] 'SusButton' visible but never clicked.
  pickingMode=Ignore, worldBound=(320,180 80×32).
```

---

## 21. FocusTrapAudit - focus outside the modal

**File:**`SusModal.sharq`

**Problem:** when the modal is open, Tab takes focus to the elements behind it - a violation of accessibility.

**How ​​it works:**`FocusEvent` + ` this.Contains(focused)`. If` Model=true`, A` evt.target`not inside the modal - warning.

**Example warning:**
```
[FocusTrapAudit] SusModal: focus escaped outside modal to 'TextField'.
```

## Related documents

- [OverlayHost and portals](./07-overlayhost.md) - how OverlayHost works, categories, z-order
- [API Reference](./11-api-reference.md) - full SusComponent API
- [Built-in audits (Debug / QA)](./13-audits.md) - a complete catalog of audits with code examples

## 22. ScreenAudit - text dumps of screen structure

**File:**`sus-core/Runtime/Diagnostics/ScreenAudit.cs`

**Problem:** we need to understand “what does the user see on the screen?” without starting Unity.
The AI ​​agent cannot look at the screen - it needs a text report.

**How ​​it works:** three text dump modes, output to`Debug.Log`(read via` read_console`MCP).

### 22.1 LayoutDump - map of all elements with coordinates

```
══════════ LayoutDump ══════════
Root: TemplateContainer  panel=True
Screen: 1920×1080

│SusApp ⚙ 🖱 [3ch] flex (0,0 1920×1080)
│  classes: .sus-app .theme-dark
│  📍clickable area: (0,0)→(1920,1080)
││SusMainMenu ⚙ 🖱 [1ch] flex (0,0 900×1080)
││  📍clickable area: (0,0)→(900,1080)
│││SusButton ⚙ 🖱 [1ch] flex (300,200 200×40)
│││  📍clickable area: (300,200)→(500,240)
││SusSettings ⚙ ⊘ HIDDEN [0ch] none (0,0 0×0)
```

**Read by icons:**
- `⚙` = SusComponent, `🖱`= clickable,`⊘`= transparent for clicks
- `HIDDEN`= not visible (display: none / visible=false / zero-size)
- `📍clickable area`= actual clickable area

### 22.2 PickableLayerAudit - z-order of clickable elements

```
══════ PickableLayerAudit ══════
Z-order = DOM order (last sibling = topmost). No z-index in Unity.

[LAYER 000] SusMainMenu ⚙ ⚠OVERLAPPED
  area: (0,0 900×1080)
  picking=Position visible=True enabled=True
  ← blocked by [001]SusOverlayPanel
[LAYER 001] SusOverlayPanel ⚙
  area: (0,0 900×1080)
  picking=Position visible=True enabled=True

⚠ 2 elements have overlapping bounds with higher z-order elements.
```

**Read:**`[LAYER 000]`— the larger the number, the higher (closer to the eyes).
`⚠OVERLAPPED`— someone higher in the z-order overlaps this element: the click will go to the top.

### 22.3 FullPropsDump - all Prop values ​​of all SusComponents

```
══════ FullPropsDump ══════
── SusMainMenu #main-menu (900×1080)
  IsOpen = true
  Mode = "deployment"
── SusButton #play-btn (200×40)
  Text = "PLAY"
  Disabled = false
  Loading = false
  Variant = "primary"
Total: 2 SusComponents dumped.
```

### Hotkey

**Ctrl+Shift+~** - three dumps to the console at once. Installed automatically via`SusBootstrap`.

### Automatic dump

**Every time you navigate the router** (`Push`, ` Replace`, ` PushNamed`, ` ReplaceNamed`, ` Back`, ` Forward`, ` Go`) — after a successful transition, the following are automatically displayed:
- `LayoutDump`— map of elements on the new screen
- `FullPropsDump`— values ​​of all Props of all SusComponents

**Every click`SusButton`** - after execution` OnClick?.Invoke()`automatically displayed` FullPropsDump`. This gives the agent a snapshot of the UI state immediately after the button action has completed.

> Both mechanisms -`#if UNITY_EDITOR || DEVELOPMENT_BUILD`, are completely cut out in the release.

### Agent use

```
# Via MCP: read Unity console
read_console -> filter_text="[LA]" # LayoutDump (element map)
read_console -> filter_text="[PA]" # PickableLayer (z-order)
read_console -> filter_text="[FP]" # FullPropsDump (props values)
```

The agent receives a text picture of the screen and can:
- Understand the structure: what components, in what order
- Find overlaps: which elements block the click
- See the status: current prop values of all components
