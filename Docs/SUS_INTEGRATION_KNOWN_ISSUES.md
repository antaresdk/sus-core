# SUS Core - known problems with integration into a consumer project

Revealed during creation of a separate Unity consumer demo (sus-core + sus-router as packages).

---

## 1. Namespace: `Sharq.Router` vs `Sharq.Core`

> ✅ **CORRECTED (refactor P1.4, 07/09/2026):** router classes moved to
> `namespace Sharq.Router`. The previous instruction “everything is in `Sharq.Core`" is no longer true.

**Current status:**

- **`Sharq.Router`** — `SusRouter`, `SusScreen`, `SusRouterModal`/`SusModal`, `SusRouteRecord`,
  `SusRouteConfig`, `SusRouteBuilder`, `SusRouteView`, router services, `SusAppRouterExtensions`.
- **`Sharq.Core`** — `SusBootstrap`, `SusBreakpointService`, `SusApp`, `SusComponent`, `Prop`,
  `OverlayHost`, topic/density/scale-services, tokens.

**Solution for a consumer project:** connect **both** namespace where both core and
router: `using Sharq.Core;` + `using Sharq.Router;`. Screens (`SusScreen`) and router-API - from
`Sharq.Router`; bootstrap/`SusApp`/components - from `Sharq.Core`.

---

## 2. `SusBreakpointService` - no property `Instance`

**Symptom:** `error CS0117: 'SusBreakpointService' does not contain a definition for 'Instance'`

**Reason:** Architecture - static factory methods returning per-root instance:

```csharp
public static SusBreakpointService For(VisualElement root)
public static SusBreakpointService Attach(VisualElement root)
public static void Detach(VisualElement root)
```

Singleton properties `Instance` No.

**Solution:** `SusBreakpointService.Attach(_root)` instead of `.Instance.Attach()`.

---

## 3. `SusBootstrap.ApplyDefaultTSS` accepts `UIDocument`, Not `VisualElement`

**Symptom:** `error CS1503: Argument 1: cannot convert from 'VisualElement' to 'UIDocument'`

**Reason:** Signature:

```csharp
public static void ApplyDefaultTSS(UIDocument uiDocument)
```

Accepts `UIDocument`, loads `SusDefault.tss` from Resources and assigns as `panelSettings.themeStyleSheet`.

**Solution:** transfer `UIDocument`, not `rootVisualElement`:

```csharp
// ❌ SusBootstrap.ApplyDefaultTSS(rootVisualElement);
// ✅ SusBootstrap.ApplyDefaultTSS(uiDocument);
```

---

## 4. `SusRouteRecord.Config` - getter-only property

**Symptom:** `error CS0200: Property or indexer 'SusRouteRecord.Config' cannot be assigned to`

**Cause:** `Config` can only be specified in the constructor:

```csharp
public SusRouteRecord(string path, Type screenType, SusRouteConfig config = null)
{
    Config = config ?? new SusRouteConfig();
}
```

After creation `record.Config = ...` doesn't work.

**Solution:** transfer `SusRouteConfig` to the constructor:

```csharp
// ❌ new("create", typeof(T)) { Config = new SusRouteConfig {...} }
// ✅ new("create", typeof(T), new SusRouteConfig { ... })
```

---

## 5. `SusRouterModal.Dismiss()` - protected method

**Symptom:** `error CS0122: 'SusRouterModal.Dismiss()' is inaccessible due to its protection level`

**Cause:** `Dismiss()` declared as `protected`, and the generated `*Content` inherited from `SusComponent`, not from `SusRouterModal`.

**Solution:** use the router's public API to close the modal:

```csharp
// ❌ (parent as SusRouterModal)?.Dismiss();
// ✅ DemoBootstrapper.Instance.Router.ModalService.Close();
```

---

## 6. Sharq Generator: `__wrap` not declared in v-for with 1 child

**Symptom:** `error CS0103: The name '__wrap' does not exist in the current context`

**Cause:** `BuildMethodGenerator.cs` created `var __wrap = new VisualElement();` only when `node.Children.Count > 1`. But `GenerateForTemplate` always writes with sub-children `__wrap.Add()` - even when there is only one child element, but there are nested elements inside it.

**Where:** `BuildMethodGenerator.cs`, line ~579 (before fix).

**Fixed:** `__wrap` is always created before the body of the lambda, regardless of the number of descendants.

```diff
- if (node.Children.Count == 1)
-   GenerateForTemplate(sb, node.Children[0], subIndent, itemVar, isTyped, true, ...);
+ sb.AppendLine($"{subIndent}var __wrap = new VisualElement();");
+ if (node.Children.Count == 1)
+   GenerateForTemplate(sb, node.Children[0], subIndent, itemVar, isTyped, false, ...);
```

---

> ℹ️ **Historical (§7–§8):** `BindListFor` described according to the integration status sus-demo. After
> refactor P0.1 codogen v-for emit reactive**`BindList`** - these two points refer to
> already eliminated code and left as history.

## 7. Sharq Generator: `BindListFor` no generic for `Prop<List<T>>`

**Symptom:** `error CS0411: The type arguments for method 'SusComponent.BindListFor<T>' cannot be inferred`

**Reason:** Generator `InferItemType` couldn't extract element type from `Prop<List<SquadRow>> Squads` — matched only `List<T> Name`, but not `Prop<List<T>> Name`.

**Where:** `BuildMethodGenerator.cs`, method `InferItemType`.

**Fixed:** added regex match for `Prop<(List|IList|IEnumerable|ObservableList)<T>>`:

```csharp
// Match: Prop<List<T>> varName  /  Prop<IList<T>> varName
pattern = $@"Prop<(?:List|IList|IEnumerable|ObservableList)<([^>]+)>>\s+{Regex.Escape(collectionVar)}\b";
```

---

## 8. Runtime: method `SusComponent.BindListFor` was absent

**Symptom:** `error CS0117: 'SusComponent' does not contain a definition for 'BindListFor'`

**Reason:** The generator generated a call `BindListFor<T>(...)`, but the method was not implemented at runtime `SusComponent.cs`.

**Where:** `SusComponent.cs`.

**Fixed:** Added two methods:

```csharp
// Generic: unwraps Prop<List<T>>, iterates IEnumerable<T>
protected void BindListFor<T>(VisualElement container, object source,
    Func<T, int, VisualElement> factory, Func<T, object> keySelector = null)

// Non-generic fallback (object-based)
protected void BindListFor(VisualElement container, object source,
    Func<object, int, VisualElement> factory, Func<object, object> keySelector = null)
```

---

## 9. Sharq Generator: `text="..."` on custom components

**Symptom:** `error CS1061: 'SusButton' does not contain a definition for 'text'`

**Reason:** The generator wrote `__el.text = "..."` for any element with attribute `text`, including custom components (SusButton). But SusButton doesn't have a property `.text` - the text is transmitted via `SetChildProp`.

**Where:** `BuildMethodGenerator.cs`, line ~808.

**Fixed:** split into built-in (`Label`, `Button`) → `.text =` and custom → `SetChildProp`:

```csharp
if (!IsCustomComponent(typeName) && ...)
    sb.AppendLine($"{varName}.text = \"...\";");
if (IsCustomComponent(typeName) && ...)
    sb.AppendLine($"SusComponent.SetChildProp({varName}, \"text\", \"...\");");
```

---

## 10. `#nullable enable` - absent in manual and generated ones `.cs`

**Symptom:** `warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context`  
And also `warning CS8669` For `.g.cs`.

**Reason:** The project uses `T?` (nullable reference types) but the directive is not included in any manual `.cs`, nor in the generated `.g.cs`.

**Fixed:**
- Generator: added line `#nullable enable` after the auto-generated comment in each `.g.cs`
- Manual `.cs`: `#nullable enable` in 16 files of the consumer project

---

## 11. `$using System.Linq` - not picked up in generated `.g.cs`

**Symptom:** `error CS1061: 'List<UnitData>' does not contain a definition for 'Select'/'Sum'/'Max'`

**Reason:** The `.sharq` files used LINQ methods (`.Select`, `.Sum`, `.Max`, `.ToList`), but `System.Linq` was not explicitly imported via `$using`. The generator does not add it automatically.

**Working solution:** added `$using System.Linq;` inside the `<script>` block of every `.sharq` file that uses LINQ.

---

## 12. `@click` - problems with inline lambdas in `.sharq`

**Symptom:**
- `error CS1012: Too many characters in character literal`
- Double lambdas `() => () => ...` in the generated code

**Cause:** The Sharq generator did not process Vue-style strings in attributes correctly (single quotes became C# character literals).

**Working solution:** replacing inline lambdas with reference methods:

```xml
<!-- ❌ @click='() => Navigate("squad-manager")' -->
<!-- ✅ @click='GoToSquadManager' -->
```

With the appropriate method in `<script>`:

```csharp
private void GoToSquadManager() => DemoBootstrapper.Instance.Router.Push("/squad-manager");
```

---

## 13. `$using Namespace` - incorrect syntax

**Symptom:** `CS0246: The type or namespace name 'X' could not be found`

**Reason:** Inside `<script>` sections of `.sharq` files, `using Namespace;` cannot be used — only `$using Namespace;` is supported. The generator processes `$using` and adds it to the generated `.g.cs`.

**Solution:** replace every `using X;` with `$using X;` inside `<script>`.

---

## 14. `Router.Push()` / `Router.Back()` from the Content component

**Problem:** `SusComponent` (base class of content components) does not have a property `Router`. The router is only accessible through `SusScreen`/`SusRouterModal` (wrappers).

**Solution:** access via bootstrapper singleton:

```csharp
// ❌ Router.Push("/path");  - no such property
// ✅ DemoBootstrapper.Instance.Router.Push("/path");
```

---

## 15. Property `text` at `SusButton` - does not work directly

**Symptom:** `error CS1061: 'SusButton' does not contain a definition for 'text'`

**Cause:** `SusButton` does not inherit `text` from `VisualElement`/`Label` - it has its own internal structure with `_label`.

**Transmission channels:**
- Via generator: `SetChildProp(__el, "text", "...")` - works
- Through `.sharq` `<sus:SusButton text="Back" />` - works through a generator
- Via C# directly: `button.text = "..."` - **doesn't work**, only `SetChildProp`

---

## Summary of fixed files

### sus-core (4 files)

| File | What's fixed |
|---|---|
| `Runtime/SusComponent.cs` | Added `BindListFor<T>` + non-generic `BindListFor` |
| `Runtime/OverlayHost.cs` | Full implementation: `AddToOverlay`, `RemoveFromOverlay`, `Stack`, `Count`, `InstallFocusTrap`, `ValidateIsLastChild`, `DumpStack` |
| `Editor/SourceGenerator/Generator/BuildMethodGenerator.cs` | `__wrap` always being created; `InferItemType` For `Prop<List<T>>`; `text` on custom components → `SetChildProp`; `#nullable enable` in the generated files; `ResolvePropExpr` For `.Value` unwrapping |

### sus-router (8 files)

| File | What's fixed |
|---|---|
| `Runtime/SusScreen.cs` | Created by: lifecycle `BeforeEnter`/`Entered`/`BeforeRouteUpdate`/`BeforeLeave`/`Left`, `GetProp<T>`/`GetProp`, `RegisterChildView`, `ChildView`/`ChildViews` |
| `Runtime/SusModal.cs` | Created by: lifecycle `Shown`/`BeforeDismiss`/`Dismissed`, `Dismiss()` |
| `Runtime/SusModalService.cs` | Created by: modal stack, `Show`/`Close`/`CloseAll` |
| `Runtime/SusTransitionService.cs` | Created by: Transition Animation Stub |
| `Runtime/SusOverlayServices.cs` | Created by: aggregation `Host`/`ModalService`/`TransitionService` |
| `Runtime/SusRouter.cs` | `FindCommonPrefixDepth` internal→public; `SetRouteView` internal→public; `Init()` fix `OverlayServices.Host`; `BeforeEnter`/`BeforeLeave` return `bool` instead of `void` |
| `Runtime/StandardScreens/*.cs` (5 files) | Access modifiers override → `public`; `HostScreen*` — `new` workaround for bug CS0507 |
| `Runtime/Tests/*.cs` + `Editor/Tests/*.cs` (3 files) | Access modifiers override → `public`

### sus-core/Tools~ (no changes required)

| File | Note |
|---|---|
| `Tools~/SharqBootstrap/` | CLI bootstrap for the first generation `.g.cs` outside of Unity (chicken-egg problem). Folder `Tools~` with suffix `~` — Unity does not import it (neither source nor build output). |

---

## Recommendations for new consumer projects

1. **Always** enable `#nullable enable` in `.cs` files and the project's `.asmdef`
2. **Always** add `$using System.Linq;` in `.sharq` files where `.Select/.Sum/.Max/.ToList` is used
3. Router is accessible through its bootstrapper (`YourBootstrapper.Instance.Router`)
4. `SusRouterModal.Dismiss()` do not call - use `Router.ModalService.Close()`
5. `SusRouteRecord.Config` - only through the constructor
6. `SusBreakpointService.Attach(root)` - static method, not singleton
7. `SusBootstrap.ApplyDefaultTSS(UIDocument)` - Not `VisualElement`
8. `@click` in `.sharq` — prefer reference methods, avoid inline lambdas
9. Warnings from generated UI package folders - expected, not design errors
10. **Don't** create scenes manually - use Editor script `Tools → SusDemo → Create DemoScene`
11. **After changing the signatures of base classes** (access modifiers, new virtual methods) - delete `Library/Bee/` And `Library/ScriptAssemblies/` to force a complete recompilation
12. **SusScreen Lifecycle methods** - all `public virtual`; override → `public override`; if CS0507 does not go away - bypass via `public new` (with loss of polymorphism)

---

## 16. `SusComponent` / `SusScreen` have `protected` constructor

**Symptom:** `TargetInvocationException` — `Activator.CreateInstance` crashes when the router creates a screen.

**Cause:** `SusComponent` (base class) has `protected SusComponent()`. The router creates screens through `Activator.CreateInstance(type)`, which is required by the public constructor.

**Where:** `SusRouter.cs` — `NavigateCore` (line 864, 898).

**Fixed:**

1. **sus-router:** `Activator.CreateInstance(type)` → `Activator.CreateInstance(type, nonPublic: true)` (2 places)
2. **consumer-project:** all `*Screen` classes must have a public constructor:

```csharp
public class SplashScreen : SusScreen
{
    public SplashScreen() { } // required!
    protected override void Build() { ... }
}
```

---

## 17. Sharq Generator: `:value="PropField"` conveys `Prop<T>` instead of `T`

**Symptom:** `ArgumentException: Object of type 'Sharq.Core.Prop`1[System.Int32]' cannot be converted to type 'System.Single'.`

**Reason:** Generator for `:value="Progress"` (Where `Progress` — `Prop<int>`) generates:

```csharp
BindChildProp(__el, "value", () => Progress);  // ← returns Prop<int>, not int
```

A `SusProgressLinear.value` awaits `float`.

**Corrected:** in `SusComponent.SetChildProp` added auto-unwrapping `Prop<T>` → `T.Value` at the entrance:

```csharp
if (value != null && IsPropType(value.GetType()))
{
    var vp = value.GetType().GetProperty("Value");
    if (vp != null) value = vp.GetValue(value);
}
```

**Remaining problem:** reactivity DOESN'T WORK - `WatchEffect` does not trigger updates because `getter()` doesn't read `.Value`. The generator must generate `() => Progress.Value`.

**Full correction (07/08/2026):**
1. `BuildMethodGenerator.ResolvePropExpr()` — checks `<script>`: if expression is field identifier `Prop<T>`, generates `.Value`
2. `SetChildProp` saves auto-unwrapping as fallback for complex expressions (`unit.CurrentHp`)
3. `GenerateCommonAttributes` got the parameter `SharqFileModel` to access component fields

---

## 18. `SusRadioGroup` - slot component, does not accept `:items`

**Symptom:** The “Graphics” block in the settings is empty - there are no radio buttons.

**Cause:** `SettingsContent.sharq` used `<sus:SusRadioGroup :model="..." :items="..." />`, But `SusRadioGroup` - slot component (`<slot>`), expecting children `SusRadio`.

**Correction:**
```xml
<sus:SusRadioGroup :model="GraphicsQuality">
    <sus:SusRadio value="low" label="Low" />
    <sus:SusRadio value="medium" label="Average" />
    <sus:SusRadio value="high" label="High" />
</sus:SusRadioGroup>
```

---

## 19. `SusModalService` / `SusScreen` / `OverlayHost` - classes did not exist

**Symptom:** modals do not close (`Close() → ModalService.Close()` - NPE or no-op).

**Reason:** Classes `SusModalService`, `SusScreen`, `OverlayHost`, `SusRouterModal`, `SusTransitionService`, `SusOverlayServices` are not defined anywhere in the codebase. `SusRouter.cs` references them, but they must be created as separate files.

**Correction:** 6 files created:
- `sus-core/Runtime/OverlayHost.cs` — container for overlays (last sibling → on top)
- `sus-router/Runtime/SusRouterModal.cs` — lifecycle: Shown/BeforeDismiss/Dismissed
- `sus-router/Runtime/SusScreen.cs` — lifecycle: BeforeEnter/Entered/BeforeRouteUpdate/Left
- `sus-router/Runtime/SusModalService.cs` — modal stack with backdrop, Show/Close/CloseAll
- `sus-router/Runtime/SusTransitionService.cs` — a placeholder for transition animations
- `sus-router/Runtime/SusOverlayServices.cs` — aggregation of overlay services

---

## 20. `SusScreen` lifecycle methods - access modifiers conflict

**Symptom:**
```
error CS0507: 'X.Entered()': cannot change access modifiers when overriding 'public' inherited member 'SusScreen.Entered()'
error CS0507: 'X.Left()': cannot change access modifiers when overriding 'public' inherited member 'SusScreen.Left()'
error CS0507: 'X.BeforeLeave(SusRoute)': cannot change access modifiers when overriding 'protected internal' inherited member 'SusScreen.BeforeLeave(SusRoute)'
```

**Cause:** `SusScreen.cs` created in issue #19 had inconsistent access modifiers for lifecycle methods - some were `public`, Part `protected internal`. Standard screens (`HostScreen`, `HostScreen`, `HostScreen`, `HostScreen`, `HostScreen`) and tests used `protected internal override`/`public override` in different combinations, which caused CS0507 when there was a mismatch with the base class.

**Correction (07/08/2026):**
1. Base class `SusScreen` — all lifecycle methods are reduced to `public virtual`:
   - `BeforeEnter(SusRoute)` → `public virtual bool`
   - `Entered()` → `public virtual void`
   - `BeforeRouteUpdate(SusRoute)` → `public virtual bool`
   - `BeforeLeave(SusRoute)` → `public virtual bool`
   - `Left()` → `public virtual void`

2. All override in heirs - `public override`.

3. **Bug workaround in 3 files:** `HostScreen`, `HostScreen`, `HostScreen` - methods `Entered()` And `Left()` use `public new void` instead of `public override void`. The Unity compiler issues CS0507 despite a complete match of signatures - a probable Roslyn/caching bug in this version of Unity (appears selectively, not in all files).

**Side effect of bypassing `new`:** router calls `SusScreen.Entered()`/`SusScreen.Left()` by reference of the base type, so with `new` an empty base implementation will be called. The life cycle of these three screens is incomplete. The correct solution is the Template Method pattern (`public void Left() { OnLeft(); }` + `protected virtual void OnLeft()`).

---

## 21. `SusRouter.FindCommonPrefixDepth` - was `internal`, tests required `public`

**Symptom:**
```
error CS0117: 'SusRouter' does not contain a definition for 'FindCommonPrefixDepth'
```
Tests `SusRouterPipelineTests.cs` (Editor/Tests) call `SusRouter.FindCommonPrefixDepth()`, but the method was declared as `internal`.

**Fixed:** `internal static int FindCommonPrefixDepth(...)` → `public static int FindCommonPrefixDepth(...)`.

**Where:** `sus-router/Runtime/SusRouter.cs`, line 299.

---

## 22. `SusRouter.SetRouteView` - was `internal`, tests required `public`

**Symptom:**
```
error CS1061: 'SusRouter' does not contain a definition for 'SetRouteView'
```
Tests `SusRouterKeepAliveTests.cs` cause `_router.SetRouteView(_view)`, but the method was declared as `internal`.

**Fixed:** `internal void SetRouteView(...)` → `public void SetRouteView(...)`.

**Where:** `sus-router/Runtime/SusRouter.cs`, line 1008.

---

## 23. `SusScreen.ChildView` - property was missing

**Symptom:**
```
error CS1061: 'ParentScreen' does not contain a definition for 'ChildView'
```
Tests `SusRouterPipelineTests.cs` (NestedRoute) read `rootScreen.ChildView`, but in `SusScreen.cs` there was only `internal IReadOnlyList<SusRouteView> ChildViews`.

**Fixed:** Added public property:
```csharp
public SusRouteView ChildView => _childViews.Count > 0 ? _childViews[0] : null;
```

**Where:** `sus-router/Runtime/SusScreen.cs`.

---

## 24. Caching DLLs in `Library/Bee/` And `Library/ScriptAssemblies/`

**Symptom:** edits `.cs` files are applied, but Unity continues to throw errors for the old code (for example, CS0507 for lines that are no longer in the file).

**Reason:** Unity caches compiled assemblies in:
- `Library/Bee/artifacts/<dag>/` — intermediate artifacts Tundra build
- `Library/ScriptAssemblies/` - final DLLs

When changing base class signatures (`SusScreen`) assemblies that depend on it (`sus.router` + test asmdef), are not recompiled automatically - Unity uses cached `.ref.dll` with the old signature.

**Solution:** delete cache:
```powershell
Remove-Item "Library/Bee" -Recurse -Force
Remove-Item "Library/ScriptAssemblies/*" -Force
```
After this, Unity will recompile everything from scratch and pick up the latest signatures.

**Important:** Perform after any change `public`/`protected`/`internal` signatures in base classes on which other asmdef assemblies depend.
