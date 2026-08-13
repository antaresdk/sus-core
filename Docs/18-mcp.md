# 18 — MCP / Agent probe (Phase 0)

> Status: Phase 0 (Foundation API). Gives an AI agent structured access to the live UI as
> JSON — without parsing the Console. The MCP wrapper (tool registration) is a separate,
> development-only phase and is not part of this package.

## What it is

`SusUiProbe` is a thin C# façade over existing diagnostics. It returns JSON strings
and **does not write to the Console by default** (the agent parses the return value, not logs).
It compiles only under `UNITY_EDITOR || DEVELOPMENT_BUILD` — it does not ship in a release player.

## API

Runtime — `Sharq.Core.Diagnostics.SusUiProbe`:

| Method | Returns |
|---|---|
| `GetTreeJson(VisualElement root, int maxDepth = 10, bool emitToConsole = false)` | flat JSON array of nodes: `depth,type,name,classes,sus,children,w,h,x,y,text?,hidden?,invisible?,pickable?` |
| `GetPropsJson(SusComponent component, bool emitToConsole = false)` | `{ type, name?, visualState?, <propName>: value, … }` for all public `Prop<T>` |
| `GetPropsJson(VisualElement root, string nameOrType, bool emitToConsole = false)` | same, after finding the component by `#name` or type name; `{ "error":"not found" }` if missing |
| `GetHealthJson(VisualElement root, bool emitToConsole = false)` | `{ totalElements, susComponents, totalChildren, maxDepth, anomalies:[] }` |

Editor — `Sharq.Core.Editor.Diagnostics.SusUiProbeEditor`:

| Method | Returns |
|---|---|
| `ValidateSetupJson()` | `{ ok:bool, issues:[ { severity, category, message, fix? } ] }` (wrapper over `SusSetupValidator`, no Console/dialog) |

## Example

```csharp
using Sharq.Core.Diagnostics;

var root = susApp.ScreenHost;               // or UIDocument.rootVisualElement
string tree   = SusUiProbe.GetTreeJson(root);
string health = SusUiProbe.GetHealthJson(root);
string props  = SusUiProbe.GetPropsJson(root, "HomeScreen");
// in the editor:
string setup  = Sharq.Core.Editor.Diagnostics.SusUiProbeEditor.ValidateSetupJson();
```

Via Unity MCP (`execute_code`) the agent gets the UI tree in one call, without `read_console`
and without filtering on `[LA]`/`[FP]`.

## Relation to existing diagnostics

- `ScreenAudit` (Runtime/Diagnostics) — human-facing Console dump (kept for manual debugging).
- `SusDiagnostics` (in downstream UI packages) — JSON dumps for the diagnostics panel; `SusUiProbe`
  repeats that logic in core (core does not depend on downstream UI packages) so the façade lives in the free package.
- `SusSetupValidator` (Editor) — source for `ValidateSetupJson`.

## Next (later phases)

- Phase 1: ✅ DONE (development-only, outside this package). Tools `sus_ui_tree` /
  `sus_ui_props` / `sus_ui_health` / `sus_setup_validate` auto-register with CoplayDev
  MCPForUnity via `[McpForUnityTool(..., Group="ui")]` + `HandleCommand(JObject)`, wrapping
  `SusUiProbe`. The Coplay reference lives only in that development-only editor assembly;
  core stays dependency-free.
- Phase 2: act tools (`router.push`, `ui.set_prop`, `ui.click`, `sharq.regen`).
- Phase 3: Docs MCP + Storybook.

## DoD Phase 0

- [x] `SusUiProbe` façade with Console off by default (`emitToConsole=false`).
- [x] EditMode smoke: `SusUiProbeTests` — probe returns parseable JSON.
- [x] Phase 1: SUS MCP tools in a development-only editor assembly (outside this package).
- [ ] Run in Unity: confirm probe+tools compile and tests are green (needs Editor).
