# 18 — MCP / Agent probe (Phase 0)

> Статус: Phase 0 (Foundation API). Даёт AI-агенту структурный доступ к живому UI как
> JSON — без парсинга Console. MCP-обёртка (регистрация tools) — отдельная фаза,
> см. `planning/SUS_MCP_PLAN.md`.

## Что это

`SusUiProbe` — тонкий C#-фасад поверх существующей диагностики. Возвращает JSON-строки
и **по умолчанию не пишет в Console** (агент парсит возвращаемое значение, а не логи).
Компилируется только под `UNITY_EDITOR || DEVELOPMENT_BUILD` — в релизный плеер не попадает.

## API

Runtime — `Sharq.Core.Diagnostics.SusUiProbe`:

| Метод | Возвращает |
|---|---|
| `GetTreeJson(VisualElement root, int maxDepth = 10, bool emitToConsole = false)` | плоский JSON-массив узлов: `depth,type,name,classes,sus,children,w,h,x,y,text?,hidden?,invisible?,pickable?` |
| `GetPropsJson(SusComponent component, bool emitToConsole = false)` | `{ type, name?, visualState?, <propName>: value, … }` для всех публичных `Prop<T>` |
| `GetPropsJson(VisualElement root, string nameOrType, bool emitToConsole = false)` | то же, найдя компонент по `#name` или имени типа; `{ "error":"not found" }`, если не найден |
| `GetHealthJson(VisualElement root, bool emitToConsole = false)` | `{ totalElements, susComponents, totalChildren, maxDepth, anomalies:[] }` |

Editor — `Sharq.Core.Editor.Diagnostics.SusUiProbeEditor`:

| Метод | Возвращает |
|---|---|
| `ValidateSetupJson()` | `{ ok:bool, issues:[ { severity, category, message, fix? } ] }` (обёртка над `SusSetupValidator`, без Console/диалога) |

## Пример

```csharp
using Sharq.Core.Diagnostics;

var root = susApp.ScreenHost;               // или UIDocument.rootVisualElement
string tree   = SusUiProbe.GetTreeJson(root);
string health = SusUiProbe.GetHealthJson(root);
string props  = SusUiProbe.GetPropsJson(root, "HomeScreen");
// в редакторе:
string setup  = Sharq.Core.Editor.Diagnostics.SusUiProbeEditor.ValidateSetupJson();
```

Через Unity MCP (`execute_code`) агент получает дерево UI одним вызовом, без `read_console`
и без фильтра по `[LA]`/`[FP]`.

## Связь с существующим

- `ScreenAudit` (Runtime/Diagnostics) — human-facing dump в Console (остаётся для ручной отладки).
- `SusDiagnostics` (в downstream-библиотеках) — JSON-дампы для панели диагностики; `SusUiProbe` повторяет
  их логику в core (core не зависит от kit), чтобы фасад жил в бесплатном пакете.
- `SusSetupValidator` (Editor) — источник для `ValidateSetupJson`.

## Дальше (следующие фазы, из SUS_MCP_PLAN)

- Phase 1: ✅ РЕАЛИЗОВАНО (dev-only, `sus-dev/Assets/SusMcp/`). Инструменты `sus_ui_tree` /
  `sus_ui_props` / `sus_ui_health` / `sus_setup_validate` авто-регистрируются в CoplayDev
  MCPForUnity через `[McpForUnityTool(..., Group="ui")]` + `HandleCommand(JObject)`, оборачивают
  `SusUiProbe`. Ссылка на Coplay — только в sus-dev asmdef, core остаётся без зависимости.
- Phase 2: act-инструменты (`router.push`, `ui.set_prop`, `ui.click`, `sharq.regen`).
- Phase 3: Docs MCP + Storybook.

## DoD Phase 0

- [x] Фасад `SusUiProbe` без Console по умолчанию (`emitToConsole=false`).
- [x] EditMode-smoke: `SusUiProbeTests` — probe возвращает парсируемый JSON.
- [x] Phase 1: SUS MCP-инструменты в sus-dev (`Sus.Mcp.Editor` asmdef).
- [ ] Прогнать в Unity: подтвердить компиляцию probe+tools и зелёные тесты (нужен редактор).
