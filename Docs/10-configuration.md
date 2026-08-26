# 10. Configuration

File `Assets/sus.config.json`:

```json
{
  "SharqDirectory": "Assets/MyUI",
  "GeneratedDirectory": "Assets/MyUI/Generated",
  "ResourcesDirectory": "Assets/MyUI/Generated/Resources/SusRuntime",
  "EnableValidation": true,
  "StrictVForKey": true,
  "LogGeneratedFiles": true,
  "HotReloadStatePreserve": true,
  "logLevel": "Warn"
}
```

> Keep `ResourcesDirectory` **inside** `GeneratedDirectory` (the Resources mirror is written under
> the generated output). Mismatched roots (e.g. `.../gen` + `.../Generated/Resources`) split the
> output into two trees.

| Field | Default | Meaning |
|---|---|---|
| `SharqDirectory` | `Assets/SusUI` | Where `.sharq` sources live |
| `GeneratedDirectory` | `Assets/SusUI/Generated` | Output for `.g.cs` / `.g.uss` |
| `ResourcesDirectory` | `…/Generated/Resources/SusRuntime` | Compiler-synced runtime Resources folder (under `GeneratedDirectory`) |
| `EnableValidation` | `true` | Run SharqValidator |
| `StrictVForKey` | `true` | Warn if `v-for` lacks `:key` |
| `LogGeneratedFiles` | `true` | Log generation to the console |
| `HotReloadStatePreserve` | `true` | Snapshot `Prop<T>` across domain reload while Playing |
| `logLevel` | `Warn` | Runtime minimum for `SusLog` (`Error` / `Warn` / `Info` / `Verbose`, case-insensitive). Audits and other diagnostics stay silent until `Verbose`. See [11-api-reference](./11-api-reference.md#suslog). |

`logLevel` is read at runtime from `Assets/sus.config.json` on first `SusLog` use (player-safe; not the Editor-only config UI). Code can override it with `SusApp.UseLogLevel(...)` / `SusLog.Level`. Scripting define `SUS_VERBOSE_LOGS` floors the level at `Verbose` and cannot be lowered by config or `UseLogLevel`.

There is no `EnableHashCaching` field — incremental compilation uses the compiler’s own cache.
