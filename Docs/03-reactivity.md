# 3. Reactivity

> Updated: 2026-07-01 - added prop passing between components via `SetChildProp`/`BindChildProp`.

## Prop<T> - reactive property

```csharp
public class HealthBar : SusComponent
{
    public Prop<float> Health = new(100f);
    public Prop<string> Name = new("Player");

    protected override void Created()
    {
        Watch(Health, (oldVal, newVal) =>
        {
            Debug.Log($"Health: {oldVal} → {newVal}");
        });
    }

    private void TakeDamage(float amount)
    {
        Health.Value -= amount;  // UI will update automatically
    }
}
```

**Features:**
- Implicit cast: `Prop<float>` behaves like `float` (via `implicit operator`)
- Value comparison: if new == old, the `Changed` event does not fire
- IL2CPP-safe: does not use reflection

## Computed<T> - calculated property

```csharp
public class Inventory : SusComponent
{
    public Prop<int> Gold = new(100);
    public Prop<int> Gems = new(50);

    public Computed<int> TotalValue => C(() => Gold.Value + Gems.Value * 10);

    protected override void Build()
    {
        var label = new Label();
        BindText(label, () => TotalValue.ToString());
    }
}
```

`Computed<T>` caches its value and recalculates only when a dependency changes. Auto-tracking: while `Value` runs `_fn()`, every `Prop<T>.Value` **and `Computed<T>.Value`** read inside automatically becomes a dependency.

**Since July 1, 2026:** `Computed<T>` implements `IReactiveSource` — it is itself a reactive source:
- Chains `Prop → Computed A → Computed B → BindText` work (invalidation pushes up the chain)
- `BindText(label, () => MyComputed.Value)` subscribes to the computed as a source

## Watch<T> - tracking changes

```csharp
public Prop<string> Status = new("idle");

protected override void Created()
{
    Watch(Status, (oldVal, newVal) =>
    {
        if (newVal == "error")
            PlayErrorAnimation();
    });
}
```

Returns an `IDisposable` for manual unsubscribing:

```csharp
var handle = Watch(someProp, callback);
handle.Dispose();  // Later
```

## WatchEffect(Action) - auto-tracking effect

```csharp
public Prop<float> Health = new(100f);
public Prop<float> MaxHealth = new(150f);

protected override void Created()
{
    WatchEffect(() =>
    {
        var ratio = Health.Value / MaxHealth.Value;
        bar.style.width = Length.Percent(ratio * 100f);
    });
}
```

Automatically tracks every `Prop<T>` and `Computed<T>` read inside `fn`, and re-runs `fn` whenever any of them changes. Returns a `WatchHandle` for unsubscribing.

**Internally** this uses `ReactiveEffect` - the single reactive primitive that every `Bind*` method and `WatchEffect` are built on. When a component detaches, all of its subscriptions are cleared automatically (`DisposeAllBindings`).

## ReactiveEffect - the underlying reactive primitive (internal)

Every binding (`BindText`, `BindShow`, `BindVisibility`, `BindClass`, `BindList`, `BindListFor`) is built on `ReactiveEffect`:

```csharp
// Operating principle (simplified):
private WatchHandle ReactiveEffect(Action fn)
{
    var subs = new List<IDisposable>();

    void Run()
    {
        foreach (var s in subs) s.Dispose();
        subs.Clear();

        // Auto-track: collect all Prop/Computed read in fn
        using (DependencyTracker.Track(src =>
            subs.Add(src.SubscribeInvalidate(() => ScheduleBindUpdate(Run)))))
        {
            fn();
        }
    }

    Run();
    return new WatchHandle(() => { foreach (var s in subs) s.Dispose(); });
}
```

**Key properties:**
- `fn()` runs under `DependencyTracker.Track()` - automatic dependency collection
- Subscribes via `SubscribeInvalidate` for each source
- On invalidation, restarts in a batch via `ScheduleBindUpdate` (one pass per frame)
- Deduplicates via `HashSet<Action>` - collapses repeated invalidations from frequent setters

## Helpers

```csharp
// P<T> is shorthand for new Prop<T>
public Prop<string> Title = P("Default Title");

// C<T> is shorthand for new Computed<T>
public Computed<bool> IsValid => C(() => !string.IsNullOrEmpty(Title));

// WatchEffect - auto-tracking
protected WatchHandle WatchEffect(Action fn);
```

## Cleanup on detach

All subscriptions (`Bind*`, `Watch`, `WatchEffect`) are cleared automatically when the component detaches, via `DisposeAllBindings()` in `OnDetachFromPanelHandler`. Calling `Dispose()` explicitly on watch handles is not needed unless you want manual control.

## API

### Prop<T>

```csharp
public class Prop<T> : INotifyBindablePropertyChanged
{
    public T Value { get; set; } // notifies subscribers
    public event Action<T, T> Changed; // (old, new)
    public static implicit operator T(Prop<T> p);
    public Prop(T initial = default);
}
```

### Computed<T>

```csharp
public class Computed<T> : IReactiveSource // itself is a source (push invalidation)
{
    public T Value { get; } // cached, auto-invalidated
    public static implicit operator T(Computed<T> c);
    public Computed(Func<T> fn);
    public void Invalidate();                // force mark dirty
    public void Refresh();                   // recalculate immediately
}
```

> Reading `Computed<T>.Value` calls `DependencyTracker.RegisterSource(this)` - so external tracking sees the computed as a source. `MarkDirty()` forwards invalidation to subscribers only on the `false→true` edge.

### WatchHandle

```csharp
public class WatchHandle : IDisposable
{
    public void Dispose();             // unsubscribe from Prop<T>
}
```

### IReactiveSource

```csharp
public interface IReactiveSource
{
    IDisposable SubscribeInvalidate(Action onInvalidate);
}
```

`Prop<T>` implements `IReactiveSource`. `Computed<T>` uses it for auto-tracking.

### DependencyTracker

```csharp
internal static class DependencyTracker
{
    public static IDisposable Track(Action<IReactiveSource> collector);
    public static void RegisterSource(IReactiveSource source);
}
```

`[ThreadStatic]` - thread-safe. No explicit `DependsOn()` call is needed.

---

## Props between components

Applies when using custom components (`<sus:SusButton>`) inside `.sharq`.

### Literal prop

```xml
<!-- variant="primary" - mutates the .Value of an existing Prop, does not replace it -->
<sus:SusButton variant="primary" :text="Title" />
```

The generator emits `SetChildProp(child, "variant", "primary")`, which:
1. Finds the field `Variant` (case-insensitive, `BindingFlags.IgnoreCase`)
2. If the member is a non-null `Prop<T>` → writes to `.Value` (preserves the child's internal bindings)
3. If the member is a `Prop<T>` that is `null` → creates a new instance
4. If the member is a plain type → assigns directly

### Reactive prop

```xml
<!-- :variant="item.Kind" - reactive whenever item.Kind changes -->
<sus:SusButton :variant="item.Kind" />
```

The generator emits `BindChildProp(child, "variant", () => item.Kind)`, which:
1. Finds the field `Variant` (case-insensitive)
2. Wraps it in a `ReactiveEffect` - auto-subscription plus cleanup on detach
3. Mutates `.Value` of the existing `Prop<T>`

### Scalar conversion

```csharp
// ConvertScalar(value, targetType) supports:
string → bool (via bool.TryParse)
string → int (via Convert.ChangeType)
string → float (via Convert.ChangeType)
string → enum (via Enum.Parse, ignoreCase)
string → string (direct assignment)
```

### Diagnostics

In dev builds (`#if DEVELOPMENT_BUILD || UNITY_EDITOR`):
- `LogWarning` on a conversion error or an unknown prop
- `LogError` if `BindChildProp` could not find a matching `Prop<T>` member by name
