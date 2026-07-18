# 3. Reactivity

> Updated: 2026-07-01 - added props between components via`SetChildProp`/` BindChildProp`.

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
- Implicit cast:`Prop<float>` How`float`(through` implicit operator`)
- Comparison by value: if new == old, event`Changed` not called
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

`Computed<T>` caches the value and recalculates it only when dependencies change. Auto tracking: when`Value` calculates`_fn()`, All` Prop<T>.Value`**And` Computed<T>.Value`** read inside automatically become dependencies.

**From July 1, 2026:**`Computed<T>` implements`IReactiveSource`— is itself a reactive source:
- Chains`Prop → Computed A → Computed B → BindText` work (push invalidation up the chain)
- `BindText(label, () => MyComputed.Value)`— subscribes to computed as a source

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

Returns`IDisposable`— for manual unsubscribing:

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

Automatically tracks everything`Prop<T>` And`Computed<T>`, read inside` fn`, and restarts` fn`when any of them changes. Returns` WatchHandle`to unsubscribe.

**Internally** uses`ReactiveEffect`- a single reactive primitive on which all` Bind*`methods and` WatchEffect`. When a component detaches, all subscriptions are automatically cleared (` DisposeAllBindings`).

## ReactiveEffect - a single reactive primitive (internal)

All bindings (`BindText`, ` BindShow`, ` BindVisibility`, ` BindClass`, ` BindList`, ` BindListFor`) work through` ReactiveEffect`:

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
- `fn()` executed under`DependencyTracker.Track()`- auto-collection of dependencies
- Subscribe via`SubscribeInvalidate` for each source
- In case of invalidation - batch restart via`ScheduleBindUpdate`(one pass per frame)
- Deduplication via`HashSet<Action>`— eliminates repetitions with frequent setters

## Helpers

```csharp
// P<T> is shorthand for new Prop<T>
public Prop<string> Title = P("Default Title");

// C<T> is short for new Computed<T>
public Computed<bool> IsValid => C(() => !string.IsNullOrEmpty(Title));

// WatchEffect - auto-tracking
protected WatchHandle WatchEffect(Action fn);
```

## Cleaning when disconnected from panel

All subscriptions (`Bind*`, ` Watch`, ` WatchEffect`) are automatically cleared when the component is detached via` DisposeAllBindings()`V` OnDetachFromPanelHandler`. Explicitly call` Dispose()`not needed on watch handles unless manual control is required.

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
public class Computed<T> : IReactiveSource // itself is the source (push validation)
{
    public T Value { get; } // cached, auto-invalidated
    public static implicit operator T(Computed<T> c);
    public Computed(Func<T> fn);
    public void Invalidate();                // force mark dirty
    public void Refresh();                   // recalculate immediately
}
```

> `Computed<T>.Value` causes`DependencyTracker.RegisterSource(this)`— external tracking sees computed as a source.` MarkDirty()`forwards invalidation to subscribers only at the front` false→true`.

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

`Prop<T>` implements`IReactiveSource`. ` Computed<T>`uses it for auto-tracking.

### DependencyTracker

```csharp
internal static class DependencyTracker
{
    public static IDisposable Track(Action<IReactiveSource> collector);
    public static void RegisterSource(IReactiveSource source);
}
```

`[ThreadStatic]`- thread safe. No obvious` DependsOn()`no need.

---

## Props between components

When using custom components (`<sus:SusButton>`) V`.sharq`.

### Literal prop

```xml
<!-- variant="primary" - mutates the .Value of an existing Prop, does not replace -->
<sus:SusButton variant="primary" :text="Title" />
```

Generator issues`SetChildProp(child, "variant", "primary")` which:
1. Finds the field`Variant`(case insensitive,` BindingFlags.IgnoreCase`)
2. If the member is`Prop<T>` and not`null`→ writes in`.Value`(preserves the child's internal bindings)
3. If the member is`Prop<T>` And`null`→ creates a new instance
4. If the member is a regular type → direct assignment

### Jet prop

```xml
<!-- :variant="item.Kind" - reactive whenever item.Kind changes -->
<sus:SusButton :variant="item.Kind" />
```

Generator issues`BindChildProp(child, "variant", () => item.Kind)` which:
1. Finds the field`Variant`(case insensitive)
2. Wraps in`ReactiveEffect`— auto-subscription + cleaning when detaching
3. Mutates`.Value` existing`Prop<T>`

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

In the dev build (`#if DEVELOPMENT_BUILD || UNITY_EDITOR`):
- `LogWarning` in case of conversion error or unknown prop
- `LogError` If`BindChildProp` didn't find it`Prop<T>`-member by name
