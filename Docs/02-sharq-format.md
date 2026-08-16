# 2. `.sharq` format and template directives

## `.sharq` — Single File Component

Every `.sharq` file is a **Vue-like SFC** with three sections:

| Section | Role |
|---------|------|
| `<template>` | UXML markup with directives (`v-if`, `:text`, `@click`, …) |
| `<script>` | C# fields, methods, `Prop<T>`, lifecycle |
| `<style>` | USS styles for this component (optionally `scoped`) |

```
<template> … </template>
<script>   … </script>
<style>    … </style>          <!-- global USS for this file -->
<style scoped> … </style>      <!-- isolated USS (this component only) -->
```

> **Styles live in `.sharq`.** Prefer USS classes in `<style>` over `element.style.*` in C#.  
> See also [CSS Scoping](./05-css-scoping.md) — important for root selectors.

### Full example

```xml
<!-- Card.sharq -->
<template>
  <ui:VisualElement $MainElement class="card">
    <ui:Label v-if="ShowTitle" :text="Title" class="card__title" />
    <ui:Label v-show="ShowSubtitle" :text="Subtitle" class="card__subtitle" />
    <ui:VisualElement v-for="item in Items" :key="item.Id" class="card__row">
      <ui:Label :text="item.Name" />
      <ui:Label :text="item.Description" />
    </ui:VisualElement>
    <ui:Button @click="OnCardClicked" class="card__button" />
    <slot name="footer" />
  </ui:VisualElement>
</template>

<script>
$using UnityEngine;
$using System.Collections.Generic;

public class CardItem
{
    public int Id;
    public string Name;
    public string Description;
}

public string Title = "Card Title";
public string Subtitle = "This is a subtitle";
public bool ShowTitle = true;
public bool ShowSubtitle = true;
public List<CardItem> Items = new()
{
    new CardItem { Id = 1, Name = "Feature A", Description = "First feature" },
    new CardItem { Id = 2, Name = "Feature B", Description = "Second feature" },
};

private void OnCardClicked()
{
    ShowSubtitle = !ShowSubtitle;
    Items.Add(new CardItem { Id = Items.Count + 1, Name = "New", Description = "Added" });
}
</script>

<style>
/* Child selectors — scoping works correctly after compile */
.card__title {
    font-size: 18px;
    color: white;
    -unity-font-style: bold;
}
.card__subtitle {
    font-size: 14px;
    color: rgba(255, 255, 255, 0.7);
    margin-top: 4px;
}
.card__row {
    flex-direction: row;
    margin-top: 8px;
}
.card__button {
    margin-top: 12px;
    height: 36px;
}
</style>
```

The compiler emits:

- `Card.g.cs` — C# component
- `Card.g.uss` (and/or scoped/static USS) — styles from `<style>`

**Edit the `.sharq` source**, not the generated files.

---

## Style section (`<style>`)

This is a first-class part of the format — same weight as `<template>` and `<script>`.

### Global `<style>`

USS rules compiled for this component. Use **BEM-style child classes** (`.card__title`), not the root class itself when scoping is applied:

```xml
<style>
.card__title { font-size: 18px; }
</style>
```

### `<style scoped>`

Appends a unique `.s-{hash}` class to every selector and adds that class to the component **root
only** (see [CSS Scoping](./05-css-scoping.md)):

```xml
<style scoped>
.card { flex-grow: 1; }
.card:hover { opacity: 0.9; }
</style>
```

> Scoped works for **root/host-level** rules. The scope class is on the root only, so scoped
> **child** selectors (`.card__title`) won't match child elements. For child styling use a global
> `<style>` with BEM classes.

### Root vs child styles

- **Root/host** rules → `<style scoped>` (`.card` → `.card.s-hash`, matches the root) or a global `<style>`.
- **Child** rules → **global `<style>`** with BEM class names (`.card__title`) — emitted verbatim, match reliably.
- Targeting the component from **outside**, or `:root` variables → **companion unscoped USS**
  (`Resources/SusRuntime/Card.uss`), auto-loaded at runtime.

Details: [CSS Scoping](./05-css-scoping.md).

### Prefer classes over C# `style.*`

| Do | Don’t |
|----|--------|
| USS in `<style>` + `AddToClassList` / `:class` | `element.style.width = 12f` for static layout |
| Modifier classes (`.card--open`) | Copying the same tokens into C# |

Use C# `style.*` only for runtime geometry (`resolvedStyle`, `GeometryChangedEvent`) or one-off hosts outside the component.

### Inline `style="…"` attribute

Different from the `<style>` **section**. Attribute styles are compiled into static USS classes (deduplicated):

```xml
<ui:VisualElement style="flex-grow: 1; font-size: 24px; color: white;" />
```

Prefer the `<style>` section for anything reusable.

---

### `$using` — namespaces

Like C# `using`. Placed at the top of `<script>`; the compiler emits them on the generated `.g.cs`:

```csharp
$using UnityEngine;
$using System.Collections.Generic;
```

### `$MainElement` — root element

Marks which element **is** the component. Without it, Sharq wraps content in an empty `VisualElement`.

```xml
<!-- Root = Label -->
<ui:Label $MainElement :text="Greeting" />

<!-- Root = VisualElement -->
<ui:VisualElement $MainElement class="wrapper" />
```

## Template directives

### `v-if` — conditional render

Element is **added/removed** from the hierarchy (`BindVisibility`).

```xml
<ui:Label v-if="IsVisible" :text="Message" />
```

### `v-show` — hide via display

Element stays in the DOM; toggles `DisplayStyle.Flex` / `None`.

```xml
<ui:Label v-show="IsActive" :text="Status" />
```

### `v-for` — lists

```xml
<ui:VisualElement v-for="person in People" :key="person.Id" class="row">
    <ui:Label :text="person.Name" />
    <ui:Label :text="person.Role" />
</ui:VisualElement>
```

`:key` is required for key-based diff (`StrictVForKey`).

### `:text` — text binding

```xml
<ui:Label :text="Greeting" />
```

### `text="literal"` — static text

```xml
<ui:Button text="Click Me" @click="OnClick" />
```

### `@click` / `@event` — handlers

```xml
<ui:Button @click="OnButtonClick" text="Click me" />
```

Supported: `click`, `mouseenter`, `mouseleave`, `change`.

### `:class` — reactive classes

```xml
<ui:VisualElement :class='{ "card--open": IsOpen }' />
```
