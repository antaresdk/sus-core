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
  "HotReloadStatePreserve": true
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

There is no `EnableHashCaching` field — incremental compilation uses the compiler’s own cache.
