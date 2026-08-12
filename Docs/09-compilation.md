# 9. Compilation and generation

## Pipeline

```
Saving .sharq to Assets/
    │
    ▼ AssetPostprocessor.OnPostprocessAllAssets
    ├── SharqFileImporter.ProcessSharq()
    │   ├── SharqFileParser → SharqFileModel (template, script, style)
    │ ├── SharqSectionCache → incremental regeneration
    │ ├── SharqValidator → diagnostics (warnings)
    │   ├── BuildMethodGenerator → .g.cs
    │   ├── ScopedCssGenerator → _scoped.g.uss
    │   └── StyleParser → .g.uss (global)
    │
    ▼ Result:
        Assets/SusUI/Generated/ComponentName.g.cs
        Assets/SusUI/Generated/ComponentName_scoped.g.uss
        Assets/SusUI/Generated/ComponentName.g.uss (global)
```

## Incremental compilation

- **Content-hash**: if`.sharq` not changed - skipped
- **Section-level**: if only the`<style>`- only regenerated`.uss`(hot reload without C# domain reload)
- **SharqAutoRegenerate**: when Domain Reload checks freshness

## SharqValidator - diagnostics

| Check | Level |
|----------|---------|
| `v-for` without`:key`(at` StrictVForKey: true`) | Warning |
| Unused fields in`<script>` | Info |

## Generated files

```
Assets/SusUI/
├── Component.sharq ← source (you can edit here, in any subfolder)
├── Resources/SusRuntime/ ← AUTHOR'S runtime resources (in git): Icons/, Fonts/, theme-overrides
└── Generated/ ← auto-generation (.gitignored)
    ├── Component.g.cs
    ├── Component_scoped.g.uss
    ├── Component.g.uss
    ├── Component_static.g.uss
    ├── Component.sections.json
    ├── Component.sharq.hash
    ├── .gitignore
    └── Resources/SusRuntime/ ← sync USS components for runtime (auto)
```

> Basic theme/icons/fonts (`_theme`, `_icon`, `_font`, `design-tokens`) are supplied in the package itself:
> `sus-core/Runtime/Resources/SusRuntime/`. `SusRuntime` is a virtual Resources namespace
> (`Resources.Load("SusRuntime/..")`), not "compiler output"; Unity merges all
> `Resources/SusRuntime/` folders (package + project) into one path.

**Important:** Always edit`.sharq`, not` generated/*`— generated files are overwritten.

> **Cleanup:** on deletion`.sharq` all 6 generated files +`.meta`+ Resources copies are deleted automatically. When renaming, old artifacts are deleted and new ones are created.

## Loading USS in runtime

Components load styles via`Resources/SusRuntime/`(in the build). Editor has a fallback - if the file is not found in Resources, it is loaded directly from` generated/`through` AssetDatabase`.

## Batch outline (for library packages)

In addition to the project-scoped importer (`Assets/SusUI`), there is a **declarative batch outline** -
for libraries that supply already generated`.cs`/`.uss` inside the package itself
(optional downstream UI packages).

The package declares WHAT to generate in the descriptor**`sharq.gen.json`** (next to` package.json`):

```json
{
  "displayName": "My UI Package",
  "sources":   ["Components"],
  "generated": "Runtime/Generated",
  "resources": "Runtime/Resources/SusRuntime",
  "watch": true,
  "namespace": "MyCompany.UI"
}
```

Optional **`namespace`**: dotted C# identifier wrapped around every generated `.g.cs`
type for that package. Empty / omitted → global namespace (legacy). Downstream UI
packages should set this to their package root namespace so short type names do not
collide across packages.

Optional **`usings`**: string array of extra C# namespaces emitted as `using` directives in every generated `.g.cs` (when composing types from another UI package).

Infrastructure in`sus-core/Editor/Packaging/`:

- **`SusPackageRegistry`** - finds descriptors for all resolved packages; taken into account
  only mutable packages (`file:`-links / embedded) - registry/git packages are already delivered ready-made
  artifacts.
- **`SusPackageGenerator`** — menu **` Sharq/Generate All Packages`** (all packages at once) and
  window**`Sharq/Generate Package…`** (one at a time). Core -` SharqBatchCompiler.CompileDirectory`
  (`sus-core/Editor/AssetPipeline/`), **same**` BuildMethodGenerator`, as the importer.
- **`SusPackageAutoCompile`** — FileSystemWatcher for each source directory of the package with
  `watch: true`(packages out` Assets/`not visible to AssetPostprocessor): saving`.sharq` →
auto-regeneration. **In Play:** style-only / template-only →`SharqBatchMode.HotReloadSafe`
(USS + interpreter, without`.g.cs` / domain reload); `<script>`→ defer before exiting Play,
  then full Generate. In Edit mode - full package Generate as before.
- The result is **committed to the package** (as opposed to project-scoped`Generated/`, which in
  `.gitignore`).

### Three generation circuits

| Contour | Trigger | Scope | Who |
|---|---|---|---|
| Project | save `.sharq` in `Assets/` (AssetPostprocessor) + `Window → SUS → Sharq → Regenerate All Prototype Components` | `sus.config.json` → `SharqDirectory` | `SharqFileImporter` |
| Packages | conservation`.sharq` in the package (watcher) + freshness on domain reload + menu | all packages with`sharq.gen.json` | ` SusPackage*`(see above) |
| CLI | manually / CI |`Assets/` without Unity (only`.g.cs`) | ` SharqBootstrap` |

> The consumer of the package cannot rely on the runtime-Roslyn source generator - so`.cs`
> generated in advance and sent in a package like regular scripts.

## How the generator broadcasts the author's`.sharq` → C#

`BuildMethodGenerator` converts DSL syntax to valid C#:

| IN`.sharq`| IN`.g.cs` |
|---|---|
| `[CreateProperty(default:0)] public Prop<int> Damage = new(0);` | `[CreateProperty]`(DSL parameters` default:`/` validate:`discarded) + backing` public Prop<int> Damage = new(0);`+ companion property` public int damage { get => Damage.Value; set => Damage.Value = value; }` |
| availability`[CreateProperty]`| auto` using Unity.Properties;` |
| Vue quotes in expressions:`v-if="Mode.Value != 'delete'"`, `!= ''`| C# quotes:` Mode.Value != "delete"`, `!= ""` |
| `$using System.Linq;` | ` using System.Linq;` |

Always added`using System;` And`using System.Collections.Generic;`.

### Agreements for`<script>` component

- `SusComponent : VisualElement`- **root is the component itself**: use` this.Q<…>()`,
  `this.style`, ` this.schedule`(not` Element.…`).
- Lifecycle base hooks:`Created()`, ` BeforeMounted()`, ` Mounted()`, ` Updated()`,
  `BeforeUnmounted()`, ` Unmounted()`(All` protected override void`). Method` Init()`**no** —
  initialization of elements (`this.Q<…>()`, ` Watch(...)`) do in` Created()`.

## Hot reload (Editor + DEVELOPMENT_BUILD)

### USS (E1 / E4)

- Editing only`<style>`→ regeneration`.g.uss` without domain reload →`UssHotReloadService`
reloads companion sheets on live components (<1s in Editor Play).
- Remote (E4): `SusRuntimeHotReload.ApplyUss` + Runtime MCP ` ui.hotreload.uss` /
  Session MCP `client.hotreload_uss`. StyleSheet-from-text - via Editor
  `StyleSheetImporterImpl` (` SusUssFromString`). On standalone player without factory
  USS-parse is not available; template hot reload works.
- Editor push: `RemoteHotReloadPushService`(disable: EditorPrefs
  `Sharq.RemoteHotReload.Enabled` = 0).

### Templates (E3)

- Editing only`<template>` → ` SharqCompileEvents.OnTemplateChanged` →
  `TemplateHotReloadService` / ` SharqTemplateInterpreter.TryApply`.
- Remote: `ui.hotreload.template` / ` client.hotreload_template`.

#### Template interpreter support matrix

| Feature | Status | Behavior |
|---|---|---|
| `$MainElement` / root attrs → ` this`| ✅ | How` BuildMethodGenerator`: class / `:class`/name to host component, children inside |
| `class`, `:class` (object), ` name`, `:text` | ✅ | |
| `v-if` / ` v-show` (literal, `!`, `==`/`!=`, `\|\|`/`&&`, ` Prop.Value`) | ✅ | Complex C# (` string.IsNullOrEmpty`, calls) → fallback |
| `Prop="lit"` / `:Prop="expr"`| ✅ | Simple strings/numbers/bool/enum |
| `<slot>`/ named slot | ✅ | Through` GetSlotContainer` / ` BuildSlot` |
| `@click` / `@*` events | ⚠️ skip | Log`[SharqInterp] Skipping unsupported event…`; tree is applied without handlers |
| `transition=` on v-if | ⚠️ignore | The attribute is skipped; animation - after full recompilation |
| `v-for` | ❌ fallback | ` TryApply`→ false, domain reload / full Build needed |
| Nested arbitrary C#-expr | ❌fallback | |

Criteria: simple downstream UI templates (Divider / Badge / simplified Alert) -`TryApply == true`;
notoriously complex (`v-for`, heavy expr) - predictable fallback + warning.

### State-preserving domain reload (E2b)

When editing`<script>` Unity does domain reload. To prevent the UI from resetting:

1. **Edit → Preferences → General → Script Changes While Playing =
Recompile And Continue Playing** (otherwise Play stops). Without this pref
   Unity stops Play whenever the script changes - snapshot is useless.
2. `HotReloadStatePreserveService` removes`SusComponentSnapshot` with **UIDocument**
   and **EditorWindow** Sus-trees before reload and restores after`delayCall`.
3. Primitives + **enum** are serialized`Prop<T>`; other types are ignored.
4. Option in`Assets/sus.config.json`: `"HotReloadStatePreserve": true`(default).
   Turn off:`false`.

> The “do not compile in battle” rule remains a priority for a manual PvP session -
> E2b is an Editor-convenience, not a replacement for this rule.
