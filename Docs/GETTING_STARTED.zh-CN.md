# SUS Core —— 中文快速入门

> 本文件是 [README.zh-CN.md](../README.zh-CN.md) 的延伸，内容改编自 sus-ui.dev 官网中文指南
> （`00-integration` / `01-quickstart`）。英文权威版见 [`Docs/00-integration.md`](./00-integration.md)
> 与 [`Docs/01-quickstart.md`](./01-quickstart.md)——版本号、install URL 等以那两份英文文档为准，
> 本文件由 release 角色随发行手动同步，不参与 `docs:loop` 自动打标。

**目标：** 不查阅其他文档，把一个空的 Unity 项目搭建到第一个 `.sharq` 屏幕。

**适用引擎：** Unity 6000.3 及以上（全球版 Unity 6）。不支持团结引擎（Tuanjie）或其他 Unity <!-- sus:ok -->
China 分支。

---

## 1. 安装

通过 Unity Package Manager 的 Git URL 安装（权威版本号见英文 README 顶部的自动生成区块）：

```
https://github.com/antaresdk/sus-core.git#v1.0.26 <!-- sus:ok -->
```

可选：安装完 `sus-core` 后再装 `sus-router`（导航，独立包）：

```
https://github.com/antaresdk/sus-router.git#v1.0.15 <!-- sus:ok -->
```

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

## 2. 写第一个组件

一个 `.sharq` 文件 = 模板 + 脚本 + 样式三合一。编辑器在保存时把它编译成普通的
`[UxmlElement] partial class` 加上作用域 USS——不是运行时反射，是**编辑期编译**
（`AssetPostprocessor`），构建产物是普通生成 C# 代码：

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

编译结果大致是：

```csharp
[UxmlElement]
public partial class Counter : SusComponent
{
    public Prop<int> Count = new(0);
    private void OnInc() => Count.Value++;

    protected override void Build()
    {
        AddToClassList("counter");
        // ...绑定 Count → Label、@click → OnInc
    }
}
```

## 3. 接入场景（bootstrap）

类似 Vue 的 `createApp(App).mount('#app')`。**顺序很重要**：`Mount<App>()` 只有在
`App.sharq` 已经生成为 `App.g.cs` **之后**才能编译通过——先创建组件（保存 `.sharq` /
运行 Setup Project），再写挂载代码，否则会遇到 `CS0246`。

**推荐入口 —— `SusApp`**（应用 TSS、令牌级联、主题，创建 `OverlayHost` 和
`SusWorldSpacePanel`）：

```csharp
using Sharq.Core;
using UnityEngine;
using UnityEngine.UIElements;

public class AppEntry : MonoBehaviour
{
    public UIDocument uiDocument;

    void Start()
    {
        SusApp.Create(uiDocument)
            .UseTheme(SusTheme.Dark)
            .Mount<App>();
    }
}
```

更底层的替代方案 `SusBootstrap.Mount<T>`——会加载令牌级联，但**不会**应用
`SusDefault.tss` 或设置主题，需要的话自行调用 `SusBootstrap.ApplyDefaultTSS(uiDocument)`
和 `SusThemeService.Instance.SetTheme(root, SusTheme.Dark)`：

```csharp
SusBootstrap.Mount<App>(uiDocument);
```

也可以不用 `UIDocument`，直接挂到任意 `VisualElement`：`SusBootstrap.Mount<App>(someVisualElement)`。

> **EventSystem：** `SusApp` / `SusBootstrap.Mount<T>()` 首次运行时会自动创建
> `EventSystem`（不含 legacy `StandaloneInputModule`）——UI Toolkit 的输入不需要它。

## 4. 组件组合（父 → 子）与响应式

```xml
<!-- ParentScreen.sharq -->
<template>
  <ui:VisualElement $MainElement class="parent">
    <!-- 字面量 prop -->
    <sus:SusButton variant="primary" :text="BtnText" />

    <!-- 响应式 prop —— Status.Value 变化时按钮会自动更新 -->
    <sus:SusButton :variant="Status.Value" text="Dynamic" />

    <!-- 插槽：标签之间的内容进入 <slot> 子节点 -->
    <sus:SusCard>
      <ui:Label text="I'm in the #default slot!" />
    </sus:SusCard>
  </ui:VisualElement>
</template>

<script>
public Prop<string> BtnText = new("Click Me");
public Prop<string> Status = new("primary");
</script>
```

`Prop<T>` 是响应式容器（对标 Vue 的 `ref`）；对 `Prop.Value` 赋值会触发依赖它的绑定
（`:text`、`:variant`、`v-if`……）自动更新，不需要手动调用刷新。

## 5. 关键：bootstrap 之后各部分的位置

`SusApp` 是 fluent bootstrap，不是带三个子槽位的 VisualElement。实际树结构：

```
Camera
└── __SusWorldSpacePanel__ (SusWorldSpacePanel + UIDocument)   ← 在所有屏幕 UI 之下
    └── healthbars / nameplates / floating damage
        (WorldSpaceService.Default → BindToWorld)

Screen UIDocument  ← SusApp.Create(uiDocument) 的目标根节点
└── rootVisualElement
    ├── screen content (Mount<T> or SusRouteView)           ← 屏幕
    └── OverlayHost                                         ← 最后一个 child：模态、
                                                               提示、下拉、toast、控制台
```

World-space **不**接入 OverlayHost。挂载 / 运行之后使用：

```csharp
WorldSpaceService.BindToWorld(healthBar, unit.transform, offset: new Vector3(0, 2f, 0));
```

## 6. 下一步

- 导航（路由、守卫、模态、KeepAlive）——安装 `sus-router`，见站点指南
  [sus-ui.dev](https://sus-ui.dev)（中文）或英文包内 `docs/GETTING_STARTED`。
- `.sharq` 全部指令（`v-if` / `v-show` / `v-for` / `:prop` / `@event` / `$using` /
  `$MainElement`）——英文 [`02-sharq-format.md`](./02-sharq-format.md)。
- 响应式 API（`Prop<T>` / `Computed<T>` / `Watch<T>`）——英文 [`03-reactivity.md`](./03-reactivity.md)。
- 设计令牌与换肤——[`DESIGN_TOKENS.md`](./DESIGN_TOKENS.md)。
- 产品网站（含完整中文站点指南）：[sus-ui.dev](https://sus-ui.dev)
