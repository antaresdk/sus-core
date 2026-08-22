<p align="center">
  <img src="Documentation~/images/sharq-mark.png" width="96" height="96" alt="Sharq mark">
</p>

<p align="center">
  <img src="Documentation~/images/readme-banner.png" width="1280" height="640" alt="Sharq UI System Core — Unity UI Toolkit 单文件组件">
</p>

# Sharq UI System Core

[English](./README.md) · 简体中文

**SusCore** —— SUS UI 系统的基础层，是 Unity UI Toolkit 上的 Vue 风格单文件组件方案：响应式、
SFC 编译器、指令、插槽、作用域 CSS、主题、断点、world-space、控制台。

**适用引擎：Unity 6000.3 及以上（全球版 Unity 6，`unity.com`）。** 本包依赖 Unity 6 的
UI Toolkit 特性（原生 SVG → `VectorImage` 导入见 6.3），**不针对**团结引擎（Tuanjie／统一引擎）
或其他 Unity China 分支；在这些引擎上尚未验证，也没有移植计划。

**协议：** [MIT](./LICENSE.md)

**社区与支持：** [support@sus-ui.dev](mailto:support@sus-ui.dev) · GitHub Issues ·
（Discord / Telegram 在中国大陆网络环境下不可用，优先使用邮件或 Issues）

**测试与发布：** 378 个自动化测试 · [CHANGELOG](./CHANGELOG.md) ·
[GitHub Releases](https://github.com/antaresdk/sus-core/releases)

**包名：** `com.sharq-it.sus.core`（发布本文件时的版本 —— `1.0.26`；权威版本号见英文
[README.md](./README.md) 顶部的自动生成区块，本文件的版本号由 release 角色手动同步，不参与
`docs:loop` 自动打标）

---

## 环境要求

<!-- sus:gen unity kind=min -->
- **Unity 6000.3** 或更新版本
<!-- /sus:gen -->
- **仅支持 UI Toolkit** —— 本包不面向 uGUI / Canvas
- **编辑器期编译** —— `.sharq` 文件在编辑器内编译（`AssetPostprocessor`），不在运行时编译；
  构建产物是普通的生成 C# 代码

---

## 快速开始

写一个 `.sharq` 文件——模板、脚本、样式三合一。编辑器把它编译成普通的
`[UxmlElement] partial class` 加上作用域 USS：

```xml
<!-- Counter.sharq -->
<template>
  <ui:VisualElement $MainElement class="counter">
    <ui:Label :text="Count" class="counter__value" />
    <ui:Button text="+1" @click="OnInc" />
  </ui:VisualElement>
</template>

<script>
public Prop<int> Count = new(0);
private void OnInc() => Count.Value++;
</script>

<style>
.counter { flex-direction: row; align-items: center; }
.counter__value { font-size: 24px; margin-right: 12px; }
</style>
```

从 `MonoBehaviour` 挂载（`.sharq` 已生成之后）：

```csharp
SusApp.Create(uiDocument)
    .UseTheme(SusTheme.Dark)
    .Mount<Counter>();
```

更完整的从零开始教程见 [`Docs/GETTING_STARTED.zh-CN.md`](./Docs/GETTING_STARTED.zh-CN.md)。

---

## 画廊

来自本包示例（ThemeShowcase + Comp）——设计令牌与组合，运行在原生 UITK 之上：

<table>
<tr>
<td><img src="Documentation~/images/theme-tokens-dark.png" width="280" alt="Design tokens dark"><br><sub>ThemeShowcase —— 颜色 / 排版 / 图标（暗色）</sub></td>
<td><img src="Documentation~/images/theme-tokens-light.png" width="280" alt="Design tokens light"><br><sub>ThemeShowcase —— 同一套令牌（亮色）</sub></td>
<td><img src="Documentation~/images/composition.png" width="280" alt="Component composition"><br><sub>Comp —— 父子 props 组合，原生 UITK</sub></td>
</tr>
</table>

---

## 不改 C# 也能换皮肤

外观是 USS 的职责，不是 C# 的职责。语义化令牌（`--sus-*`）一次性给整个级联重新上色；
项目级 class 只覆盖单个控件；视觉状态通过 class 切换（`AddToClassList` / `RemoveFromClassList`）
实现，所以它们复用同一套样式表。遵循这套规则的控件，理论上不需要改生成代码或手写 C#
就能让界面贴合你的项目风格。

C# 里直接写外观属性（颜色、字体、圆角等）的旧调用仍然会覆盖任何选择器 —— 这部分调用点正在
逐步迁移到 USS；把 `.style.<appearance> = …` 当作最后手段，而不是主题 API。详见
[Design tokens](./Docs/DESIGN_TOKENS.md)。

---

## 安装

```
https://github.com/antaresdk/sus-core.git#v1.0.26
```

> 权威安装地址与版本号见英文 [README.md](./README.md)（该区块由 `docs:loop` 自动打标，
> 本文件为手动同步的快照）。本仓库在 Gitee 上有只读镜像，方便中国大陆访问源码；镜像地址见
> 仓库描述。UPM 安装请使用上面的 GitHub git-URL —— Gitee 镜像目前仅用于浏览代码，不是独立的
> 安装渠道。

配置文件（`Assets/sus.config.json`）：

```json
{
  "SharqDirectory": "Assets/SusUI",
  "GeneratedDirectory": "Assets/SusUI/Generated",
  "EnableValidation": true,
  "StrictVForKey": true,
  "LogGeneratedFiles": true,
  "HotReloadStatePreserve": true
}
```

**公开 demo**（可克隆的运行时示例）：[sus-demo-public](https://github.com/antaresdk/sus-demo-public)

---

## 包内不含的内容

- **导航**（路由、守卫、嵌套屏幕、模态栈）在**独立的**兄弟包 `sus-router` 中。
- **现成的组件库**（按钮、表格、对话框、HUD 元素）**不在**本包中。这是框架层，你在它之上
  搭建自己的组件，或叠加下游 UI 包。
- 生成文件位于你配置的目录（默认 `Assets/SusUI/Generated`），设计为可重新生成，不建议手改。

---

## 退出成本

编译器输出的是普通 C# 和 USS —— 一个普通的 `[UxmlElement] partial class : SusComponent`，
你可以阅读、单步调试、在 UI Builder 里打开。即使日后移除本包，这些生成文件仍会留在你的项目里。

---

## 包含内容

| 子系统 | 文件 | 状态 |
|---|---|---|
| **响应式** | `Prop<T>`、`Computed<T>`、`ReactiveEffect`、`DependencyTracker` | ✅ |
| **组件模型** | `SusComponent`、`Watch()`、`WatchEffect()`、生命周期钩子 | ✅ |
| **绑定** | `BindText`、`BindShow`、`BindVisibility`、`BindClass`、`BindList`、`BindModel` | ✅ |
| **主题** | `SusThemeService` + `SusTheme`（`readonly struct`）+ `.theme-*` class | ✅ |
| **配色（三层）** | `_palette.uss`（L1 `--base-*`）、`_theme.uss`（L2 `--thm-*`）、`design-tokens.uss`（L3 `--sus-*`） | ✅ |
| **字体** | `_font.uss`（Montserrat + 覆盖）、`--sus-font-*` 令牌 | ✅ |
| **图标** | 包内精选子集；可选的 Phosphor 长尾示例。`SusIconRegistry` / providers、`SusIconElement`、主题着色 | ✅ |
| **断点** | `SusBreakpointService`、`Prop<Breakpoint>`、根节点 `.breakpoint-*` class | ✅ |
| **OverlayHost** | Portal 容器，按 `OverlayCategory` 分层，DOM 顺序即 z-order | ✅ |
| **World-space** | `WorldSpaceService`（优先独立 world panel；`OverlayCategory.World` 作为回退） | ✅ |
| **控制台** | `SusConsoleService` + `SusConsoleDriver`（热键 `~`、过滤、搜索、Tab 补全） | ✅ |
| **编译器** | Sharq SFC → C#，作用域 CSS，校验器，增量编译 | ✅ |
| **审计（Debug/QA）** | 21 个模块 + ScreenAudit（文本屏幕转储）：ClickAudit、BoundsAudit、CallbackAudit、OverlayAudit、StateAudit、LifecycleAudit、NavigationAudit、PerformanceAudit、DebounceAudit、ClickTargetSizeAudit、StackDepthAudit、GuardAudit、ModalStackAudit、EmptyStateAudit、RemountLoopAudit、OverflowAudit、DeadRouteAudit、SusTable StateAudit、LayoutReentryAudit、IdleGuardAudit、FocusTrapAudit | ✅ |

## 在生态中的位置

`sus-router` 是依赖本包的**兄弟包**，不是本包内的一个文件夹。

```
你的 Unity 项目
├── sus-core（本包）—— 响应式、编译器、主题、overlay
└── sus-router —— 导航（屏幕、模态、KeepAlive）；依赖本包
```

## 文档

- 包内指南：[`Docs/README.md`](./Docs/README.md)（英文）· [`Docs/GETTING_STARTED.zh-CN.md`](./Docs/GETTING_STARTED.zh-CN.md)（中文入门）
- 产品网站：[sus-ui.dev](https://sus-ui.dev)
