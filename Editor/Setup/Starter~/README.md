# Setup Project starter (committed, pre-generated)

Unity ignores this folder (`~` suffix). Setup Wizard **copies** these files into the
consumer project so `Mount<HomeScreen>()` compiles without a chicken-and-egg first generation.

| File | Destination |
|---|---|
| `HomeScreen.sharq` | `{UI-root}/HomeScreen.sharq` |
| `Generated/HomeScreen.g.cs` | `{UI-root}/Generated/HomeScreen.g.cs` |
| `MyApp.Mount.cs.txt` / `MyApp.Customization.cs.txt` / `MyApp.Run.cs.txt` | `{UI-root}/{AppName}.cs` (`{{CLASS_NAME}}` replaced) |

## Refresh Generated after editing HomeScreen.sharq

```powershell
# from sus-core repo root
powershell -NoProfile -File Tools~/refresh-starter-generated.ps1
```

Or in Unity (with sus-core as a package / file link):

`Window → SUS → Setup → Refresh Starter Generated`

Commit the updated `Generated/HomeScreen.g.cs` to **sus-core** git.
