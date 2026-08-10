#17 – Dev Console: SusConsoleService, SusConsoleDriver

> Service:`SusConsoleService` (sus-core/Runtime/Services/)
> Driver:`SusConsoleDriver` (sus-core/Runtime/Services/)

---

## 1. Purpose

In-game dev console: intercepts Unity logs and displays them on top of the entire UI
(category`Console = 50`- the very top), allows you to filter/search and execute
teams. The main case is debugging on a device/build without Editor Console.

---

## 2. Connection

Wrap in`#if`to exclude from release builds:

```csharp
#if DEVELOPMENT_BUILD || UNITY_EDITOR
var overlay = SusBootstrap.GetOrCreateOverlay(uiDocument.rootVisualElement);
var console = new SusConsoleService
{
    OverlayHost = overlay,
    ToggleKey = KeyCode.BackQuote, // ~
    MaxEntries = 500,
};
console.Attach(); // subscription to log + hotkey driver
SusConsoleService.Instance = console; // for access from any code
#endif
```

`Attach()`does three things:
1. Subscribes to`Application.logMessageReceivedThreaded`.
2. Creates (or finds)`SusConsoleDriver`for survey`ToggleKey`V`Update`.
3. Registers built-in commands (`clear`, `help`, `filter`).

`Detach()`unsubscribes from the log and hides the UI.

---

## 3. Use

### User Interface

Click`~`— the console opens from the bottom (40% of the screen height), dark panel:

| Element | Destination |
|---|---|
| Buttons **All / Log / Warn / Err** | Filter by type |
| Search field | Filter by substring (case-insensitive) |
| Command input field | Enter command → Enter → execute |
| **Tab** in command field | Command name completion |
| ✕ | Close console |

Colored logs: gray (Log), yellow (Warning), red (Error/Exception/Assert).
Autoscroll down for new messages; When manually scrolling up, it stops.

### Registering commands

```csharp
#if DEVELOPMENT_BUILD || UNITY_EDITOR
SusConsoleService.Instance.RegisterCommand("spawn", args =>
{
    if (args.Length > 0)
        SpawnUnit(args[0]);
    else
        Debug.Log("Usage: spawn <unitId>");
}, help: "Spawn a unit by id");

SusConsoleService.Instance.RegisterCommand("gold", args =>
{
    int amount = args.Length > 0 ? int.Parse(args[0]) : 1000;
    player.Gold += amount;
    Debug.Log($"Added {amount} gold. Total: {player.Gold}");
}, help: "Add gold (default 1000)");
#endif
```

Built-in commands (registered automatically in`Attach`):

| Team | Destination |
|---|---|
| `clear`| Clear log buffer |
| `help`| List of all commands |
| `filter <all\|log\|warn\|error>`| Toggle filter |

---

## 4. Architecture

### Thread safety

`Application.logMessageReceivedThreaded`can be called from a background thread.
The records are added up to`Queue<SusLogEntry>`under`lock`, A`SusConsoleDriver.Update()`
causes`DrainPendingEntries()`in the main thread - transfers records to the loop
buffer and updates the UI.

###Ring buffer

When overflowing (`MaxEntries`, default 500) old entries are evicted:

```csharp
if (_buffer.Count >= MaxEntries)
    _buffer.RemoveAt(0);
_buffer.Add(entry);
```

### Lazy UI design

UI (`_root`, `_scrollView`, toolbar, command field) is built at the first`Show()`.
When closed, the console does not create`VisualElement`-ov - zero overhead
to render.

### Z-order

`OverlayCategory.Console = 50` — the top floor of **screen-space** OverlayHost.
The console is always above modals, tooltips, dropdowns, and toasts.

World-space UI (healthbars) is **not** in this stack: it uses a separate panel
**under** the screens. See [07-overlayhost.md](./07-overlayhost.md).

```
World-space panel                         ← UNDER screens (not OverlayHost)
└── healthbar / nameplate / …

Screen UIDocument
├── SusRouteView                          ← screens
└── OverlayHost
     ├── [Transition = 10]
     ├── [Modal      = 20]
     ├── [Tooltip    = 30]  ← above modals
     ├── [Dropdown   = 40]
     ├── [Toast      = 45]
     └── [Console    = 50]  ← console here (topmost)
```

---

## 5. API

### SusConsoleService

| Method/Property | Type | Destination |
|---|---|---|
| `Attach()`| void | Log subscription + hotkey driver |
| `Detach()`| void | Unsubscribe + close |
| `Show()`| void | Show console |
| `Hide()`| void | Hide |
| `Toggle()`| void | Toggle |
| `Clear()`| void | Clear buffer |
| `DrainPendingEntries()`| void | Transfer records from queue to buffer (main thread) |
| `SetFilter(filter)` | void | `ConsoleFilter.All/Log/Warning/Error` |
| `SetSearch(text)`| void | Filter by substring |
| `RegisterCommand(name, handler, help)`| void | Register a team |
| `ExecuteCommand(input)`| bool | Execute command line |
| `IsOpen`| bool | Is the console open?
| `OverlayHost`| OverlayHost | Portal container |
| `ToggleKey`| KeyCode | Hotkey (default`~`) |
| `MaxEntries`| int | Ring Buffer Size (500) |
| `Instance`| static SusConsoleService | Singleton |

### SusConsoleDriver

```csharp
public class SusConsoleDriver : MonoBehaviour
{
    public SusConsoleService Service;

    private void Update()
    {
        if (Input.GetKeyDown(Service.ToggleKey))
            Service.Toggle();
        Service.DrainPendingEntries();
    }
}
```

### SusLogEntry

```csharp
public struct SusLogEntry
{
    public LogType Type;        // Log, Warning, Error, Exception, Assert
    public string Message;      // Text
    public string StackTrace;   // Stack (for errors)
    public float Time;          // Time.unscaledTime
}
```

### ConsoleFilter

```csharp
public enum ConsoleFilter { All, Log, Warning, Error }
```

---

## 6. Tests

| File | Tests | What covers |
|---|---|---|
| `sus-core/Runtime/Tests/SusConsoleServiceTests.cs`| 10 playmode | Show/Hide/Toggle, log interception, buffer overflow, filter, search, RegisterCommand/ExecuteCommand, Clear |
