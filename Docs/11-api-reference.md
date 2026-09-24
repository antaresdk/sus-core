# 11. API Reference

## Public type names

`Sus*` is the product API (Runtime and buyer-facing Editor types you call after `using Sharq.Core`).
`Sharq*` is the `.sharq` compiler and pipeline (parser, interpreter, importers). Product interfaces
use `ISus*`. Unprefixed public types are a closed grandfathered set (reactive primitives, host and
layer nouns, companions of those APIs). Do not add new unprefixed public types; new product types
are `Sus*` / `ISus*`, new compiler types are `Sharq*`.

## SusApp — fluent bootstrap

Documented application entry point. Thin fluent builder over `SusBootstrap` that guarantees
initialization order: panel TSS → EventSystem → token cascade → OverlayHost → world panel →
fonts → custom styles → configure → mount → theme (last).

```csharp
public sealed class SusApp
{
    public VisualElement Root { get; }
    public UIDocument Document { get; }
    public SusWorldSpacePanel WorldPanel { get; }  // after Run/Mount; null if UseWorldSpace(false)

    // Create
    public static SusApp Create(UIDocument document);   // ApplyDefaultTSS + root
    public static SusApp Create(VisualElement root);    // advanced — no TSS

    // Fluent config (all return this)
    public SusApp UseTheme(SusTheme theme);             // default Dark; applied last
    public SusApp UseTokenCascade(bool enabled = true); // L1–L5 + OverlayHost (default on)
    public SusApp UseWorldSpace(bool enabled = true);   // SusWorldSpacePanel + Default (default on)
    public SusApp UseCustomStyles(params string[] resourcePaths);  // after cascade
    public SusApp UseFonts(SusFontAsset fontAsset);     // writes no style; warns unless exported to USS
    public SusApp UseIcons(params ISusIconProvider[] providers);
    public SusApp UseIcons(SusIconSetAsset iconSet);
    public SusApp UseLogLevel(SusLogLevel level);       // process gate; call before Run/Mount
    public SusApp Configure(Action<VisualElement> configure);  // before theme; router/manual UI

    // Finalize
    public VisualElement Run();                         // no root component
    public T Mount<T>() where T : SusComponent, new();  // Mount + Finalize
}
```

### Finalize order (`Run` / `Mount`)

1. Icons (`UseIcons` → `SusIconRegistry.RegisterProvider`)
2. Token cascade (`LoadTokenCascade`: `_palette` → `_font` → `_theme` → `design-tokens` → `_icon` → extras + OverlayHost)
3. World-space panel (`EnsureWorldSpacePanel`, if playing and `UseWorldSpace`)
4. Fonts (`UseFonts` — no-op on style; typeface comes from `_font.uss`, see [Design tokens §2](./DESIGN_TOKENS.md#2-fonts))
5. Custom styles (`UseCustomStyles` on root + OverlayHost)
6. Configure callbacks
7. Mount root component (if `Mount<T>`)
8. Theme last (`SusThemeService.Instance.SetTheme(root, theme)`)

`UseLogLevel` is not part of finalize — it sets `SusLog.Level` immediately (safe before `Run` / `Mount`).

```csharp
SusApp.Create(uiDocument)
    .UseTheme(SusTheme.Dark)
    .UseLogLevel(SusLogLevel.Verbose)   // optional — diagnostics / audits
    .UseCustomStyles("SusRuntime/demo-tokens")
    .UseWorldSpace(true)
    .Configure(root => BuildManualUi(root))
    .Mount<HomeScreen>();

// Router apps (sus-router extension):
SusApp.Create(uiDocument)
    .UseTheme(SusTheme.Dark)
    .UseRouter(router, r => r.Register("/", typeof(HomeScreen)), initialPath: "/")
    .Run();
```

## SusLog

Process-wide gated logger in `Sharq.Core`. Proxies to `UnityEngine.Debug` so the Editor Console and
in-game [dev console](./17-console.md) keep receiving messages. **Not** the ring-buffer
`SusLogEntry` type used by `SusConsoleService` — that struct only stores intercepted Unity logs
for the overlay UI.

```csharp
public enum SusLogLevel
{
    Error = 0,
    Warn = 1,      // default buyer level
    Info = 2,
    Verbose = 3,   // audits, probes, bootstrap traces
}

public static class SusLog
{
    public static SusLogLevel Level { get; set; }   // minimum emitted; default Warn
    public static bool IsEnabled(SusLogLevel level);
    public static bool IsVerbose { get; }           // IsEnabled(Verbose)

    public static void Error(string message);
    public static void Error(string message, Object context);
    public static void Warn(string message);
    public static void Warn(string message, Object context);
    public static void Info(string message);
    public static void Verbose(string message);
    public static void Diagnostic(string message);  // same gate as Verbose
}
```

**Default:** `Warn` (Error + critical Warn). Diagnostics stay quiet until you raise the level.

**How to enable Verbose**

| Source | Effect |
|---|---|
| `SusApp.UseLogLevel(SusLogLevel.Verbose)` / `SusLog.Level = …` | Code override; call before `Run` / `Mount` |
| `Assets/sus.config.json` → `"logLevel": "Verbose"` | Read on first `SusLog` access (see [10-configuration](./10-configuration.md)) |
| Scripting define `SUS_VERBOSE_LOGS` | Floors level at `Verbose` on init; cannot be lowered by config or `UseLogLevel` |

Priority: define floor → last `UseLogLevel` / `SusLog.Level` → `logLevel` in config → default `Warn`.

For expensive dumps, gate first: `if (SusLog.IsVerbose) SusLog.Verbose(...)`.

## SusComponent - base class

```csharp
public abstract partial class SusComponent : VisualElement
{
    // ─── Creating reactive properties ───
    protected Prop<T> P<T>(T initial = default);
    protected Computed<T> C<T>(Func<T> fn);
    protected WatchHandle Watch<T>(Prop<T> source, Action<T, T> callback);
    protected WatchHandle WatchEffect(Action fn);

    // ─── Life cycle ───
    protected virtual void Created();          // constructor
    protected virtual void BeforeMounted();    // between Created() and Build()
    protected virtual void Mounted();          // after Build() - deferred
    protected virtual void Updated();          // every frame
    protected virtual void BeforeUnmounted();  // BEFORE removing from the panel
    protected virtual void Unmounted();        // AFTER deletion

    // ─── Generation ───
    protected abstract void Build();           // generated by the compiler

    // ─── Provide / Inject ───
    // overwrite:false (default) fires OnDuplicateProvide if the key already exists, then still writes
    protected void Provide<T>(string key, T value, bool overwrite = false);
    protected T Inject<T>(string key);
    protected bool TryInject<T>(string key, out T value);
    protected bool HasInjection(string key);

    // ─── Events ───
    protected void Emit<T>(string eventName, T data);
    public void On(string eventName, Delegate handler);
    public void Off(string eventName, Delegate handler);

    // ─── Slots ───
    protected void RegisterSlotContent(string name, VisualElement content,
        Func<Dictionary<string, object>, VisualElement> builder);   // called from the generated Build()
    protected void BuildSlot(string name, Func<VisualElement, VisualElement> wrapper,
        VisualElement container);
    protected VisualElement GetSlotContainer(string name);
    public VisualElement Slot(string name);                          // runtime access after Build()
}
```

### Life cycle (order)

```
Constructor:
  Created() → BeforeMounted() → Build() → LoadCompanionStyleSheets()
Deferred(schedule.Execute, next frame):
  Mounted()
OnAttachToPanel:
  ScheduleReactiveUpdates() → Updated() ~60 FPS
OnDetachFromPanel:
  BeforeUnmounted() → _updateItem.Pause() → DisposeAllBindings() → Unmounted()
```

> `Updated()` runs **only** when attached to the panel (`OnAttachToPanelHandler`). From constructor/deferred call `ScheduleReactiveUpdates` removed — before attachment `schedule` may not tick.

### Introspection (`DescribeProps` / `DescribeAllowed` / `DescribeEvents`)

Three read-only answers a component gives about itself **on the instance, without the caller
knowing its concrete type**. This is what the [storybook](./storybook.md) control panel is built
from — nothing about it is specific to that panel, so anything (a save-preset UI, a debug
inspector) can call the same API:

```csharp
public partial class SusComponent
{
    public IReadOnlyList<SusPropInfo>                  DescribeProps();
    public IReadOnlyDictionary<string, SusAllowedInfo> DescribeAllowed();
    public IReadOnlyList<SusEventInfo>                 DescribeEvents();
    public IDisposable SubscribeEvent(string eventName, Action<object> handler); // null if unknown
    public bool        IsDependencySatisfied(SusPropInfo prop);
    public string      DescribeDependency(SusPropInfo prop);   // "ContentMode = icon" | null
}
```

- `DescribeProps()` walks every public `Prop<T>` / `ReadonlyProp<T>` field: name, value type,
  current value (read without registering a reactive dependency), group, declared range/
  dependencies, and whether anything has ever read or observed it.
- `DescribeAllowed()` returns the legal-value set for a prop registered through `UseAllowed` —
  values, fallback, aliases — so a caller can build a picker without knowing the concrete
  `SusOptionSets` the component used.
- `DescribeEvents()` lists public `On*` delegate fields (name, bus name, payload type);
  `SubscribeEvent` attaches to one generically, boxing the argument.

Two attributes make numeric ranges and prop dependencies explicit instead of guessed:

```csharp
[SusRange(0, 5, 0.5)]                       public Prop<float>  Rating   = new(0);
[SusRange(0, 100, "%")]                     public Prop<int>    Progress = new(0);
[SusDependsOn(nameof(ContentMode), "icon")] public Prop<string> Icon     = new("");
```

`[SusRange(min, max, step)]` (or `[SusRange(min, max, unit)]`) bounds a `Prop<int>`/`Prop<float>`;
without it a consumer falls back to 0…100. `[SusDependsOn(prop, value = null)]` — repeatable, ANDed
— declares that a prop only matters while another prop of the same component holds a given value;
`IsDependencySatisfied` / `DescribeDependency` let a caller disable that control and show why,
instead of rendering one that silently does nothing.

## Bind helpers (reactive)

All bindings work through `ReactiveEffect` — auto-subscription to `Prop<T>` / `Computed<T>` and updating when any source changes. Each helper returns a `WatchHandle` (tracked for dispose on detach).

```csharp
// v-if: add/remove from DOM (reactive)
protected WatchHandle BindVisibility(VisualElement el, Func<bool> getter);

// v-show: toggle display (reactive)
protected WatchHandle BindShow(VisualElement el, Func<bool> getter);

// :text: bind string to Label (reactive)
protected WatchHandle BindText(Label label, Func<string> getter);

// :class: switch CSS class by condition (reactive)
protected WatchHandle BindClass(VisualElement el, string className, Func<bool> getter);

// v-for (generic, key-based diff, reactive)
protected WatchHandle BindList<T>(VisualElement container,
    Func<IEnumerable<T>> source,
    Func<T, int, VisualElement> itemBuilder,
    Func<T, object> keySelector = null);

// v-for (generic, IEnumerable - typed item access, reactive)
protected WatchHandle BindListFor<T>(VisualElement container,
    IEnumerable<T> source,
    Func<T, int, VisualElement> itemBuilder,
    Func<T, object> keySelector = null);
```

### v-model (two-way binding)

```csharp
// Real BindModel overloads (SusComponent.Bind.cs):
BindModel(myTextField, NameProp);     // TextField      ↔ Prop<string>
BindModel(mySlider, VolumeProp);      // Slider         ↔ Prop<float>
BindModel(myToggle, MuteProp);        // Toggle         ↔ Prop<bool>
BindModel(myDropdown, ModeProp);      // DropdownField  ↔ Prop<string>
```

### Props between components

```csharp
// Reactive bind on child Prop<T> (:prop="expr" in .sharq)
// case-insensitive, ReactiveEffect, auto-cleanup. Non-generic: getter returns object.
protected void BindChildProp(VisualElement child, string propName, Func<object> getter);

// Direct bind to a Prop<T> instance (old way, PascalCase-sensitive)
protected void BindProperty<T>(Prop<T> target, Func<T> getter);

// Literal prop (prop="value" in .sharq)
// case-insensitive, mutates .Value (does not replace Prop<T>)
internal static void SetChildProp(VisualElement el, string propName, object value);

// Scalar conversion: string→bool/int/float/enum/string
private static object ConvertScalar(object value, Type targetType);
```

### BindList (stateful reorderer)

```csharp
// Insert instead of Remove+Add - focus/scroll/input is not lost
// All three options (BindList<T>, BindListFor, BindListFor<T>) have been fixed.
```

## SusBootstrap

```csharp
public static class SusBootstrap
{
    // Mounts component T into the container.
    // Loads the design-token cascade in order:
    //   _palette → _font → _theme → design-tokens → _icon → extras + OverlayHost
    // (_global comes from SusDefault.tss / ApplyDefaultTSS — not this cascade.)
    // When called for the first time, automatically creates an EventSystem (no InputModule).
    public static T Mount<T>(VisualElement container) where T : SusComponent, new();
    public static T Mount<T>(UIDocument uiDocument) where T : SusComponent, new();

    // Cascade only (no component) — used by SusApp and manual UI.
    public static void LoadTokenCascade(VisualElement container);

    // Returns or creates the OverlayHost as the last child of the container.
    public static OverlayHost GetOrCreateOverlay(VisualElement container);

    // Finds or creates SusWorldSpacePanel + wires WorldSpaceService.Default
    // (SusApp calls this automatically unless UseWorldSpace(false)).
    public static SusWorldSpacePanel EnsureWorldSpacePanel(
        Camera camera = null, OverlayHost overlayHost = null);

    // Panel TSS (_palette + _font + _global via SusDefault.tss).
    public static void ApplyDefaultTSS(UIDocument document);
}
```

## SusMotion.Reduce — reduce motion

`SusMotion.Reduce` is an application-wide `Prop<bool>` for the "reduce motion" accessibility
setting. It is off by default, and with `false` every `SusMotion.Play` behaves exactly as before.

```csharp
// Settings screen: the player asked for less motion.
SusMotion.Reduce.Value = true;
```

While the flag is on:

- a **finite** `SusMotion` play writes its end values at once and calls `onComplete`
  synchronously, with no ticks and no delay;
- a play with a **forever** group (`Repeat <= 0`) does not start and leaves the target untouched;
- switching the flag from `false` to `true` stops every forever play that is already running,
  applying its restore mode. Finite plays that are already running are left to finish.

Components with their own loops (scheduled tickers outside `SusMotion`) should watch the flag and
pause, the same way they watch any other `Prop<T>`:

```csharp
SusMotion.Reduce.Changed += (_, reduce) => SetPaused(reduce);
SetPaused(SusMotion.Reduce.Peek());
```

Subscribe while the element is attached and unsubscribe on detach: the flag outlives every panel.
USS transitions are not affected; the flag covers continuous and scripted motion.

## What replaces (v1 → v2)

| Old (v1) | New (v2) |
|---|---|
| `sus` (UPM `com.sus.sfc`) | `com.sharq-it.sus.core` |
| `sharq-ui-system` (SusCompiler.exe, LibSassHost) | `SharqFileImporter` (AssetPostprocessor) |
| `ElementBase` — reflective | `SusComponent : VisualElement` |
| `compiled ui/` — a mixture of manual and auto | `generated/` — auto only, `.gitignored` |

## Place in the ecosystem

```
sus-core (this package)
    ├── sus-router — navigation (Push/Replace/Back, screens)
    └── your Unity project — consumer app
```
