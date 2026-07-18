# 8. Events

A component can emit events up the hierarchy.

## Emit / On

```csharp
public class Counter : SusComponent
{
    public Prop<int> Count = new(0);

    private void Increment()
    {
        Count.Value++;
        Emit("count-changed", Count.Value);
    }
}

// Consumer:
var counter = new Counter();
counter.On("count-changed", (Action<int>)(newVal =>
{
    Debug.Log($"Counter is now {newVal}");
}));
root.Add(counter);
```

## Via Prop<T>

Reactive properties are the main means of transferring data between components:

```csharp
public class Parent : SusComponent
{
    public Prop<int> SharedCount = new(0);

    protected override void Build()
    {
        var child = new ChildCounter { Count = SharedCount };
        Add(child);
    }
}
```
