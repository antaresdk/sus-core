# 5. CSS Scoping

A `.sharq` `<style>` block is part of the SFC (same file as `<template>` and `<script>`).
There are two modes: **global** (default) and **scoped**.

## Global `<style>` (default, recommended)

Without the `scoped` attribute, the CSS is emitted **verbatim** (raw) into the component's
generated USS. Selectors match by class name anywhere in the component's subtree, so **BEM naming
already isolates** your styles:

```xml
<style>
.card { flex-grow: 1; background-color: #1f1f1f; }
.card__title { font-size: 18px; color: white; }
.card__title:hover { opacity: 0.8; }
</style>
```

```css
/* Compiled Card.g.uss — identical, no rewriting: */
.card { flex-grow: 1; background-color: #1f1f1f; }
.card__title { font-size: 18px; color: white; }
.card__title:hover { opacity: 0.8; }
```

Both the root rule (`.card`) and child rules (`.card__title`) match, because the elements carry
those classes (from `class="…"` in the template). This is the reliable default — prefer it, using
unique BEM class names (`component__part`) for isolation.

## Scoped `<style scoped>`

With `scoped`, the compiler appends a per-component scope class `.s-{hash}` to **every** selector
and adds that scope class to the component **root only** (`ApplyScopedAttribute` →
`AddToClassList("s-xxxxxx")` in the generated `Build()`):

```xml
<style scoped>
.card { flex-grow: 1; }
.card:hover { opacity: 0.9; }
</style>
```

```css
/* Compiled Card_scoped.g.uss: */
.card.s-a1b2c3 { flex-grow: 1; }
.card.s-a1b2c3:hover { opacity: 0.9; }
```

Because the scope class lands on the **root**, `.card.s-a1b2c3` matches the root correctly.

> **Important limitation.** Only the **root** element gets `.s-{hash}` — child elements do **not**.
> So a scoped **child** selector like `.card__title` becomes `.card__title.s-a1b2c3`, which requires
> both classes on the *same* element and therefore **will not match child elements**. Use scoped
> styles for **root/host-level** rules only. For child styling, use a **global `<style>`** with BEM
> class names (which match children reliably).

## Companion unscoped USS (escape hatch)

For rules you cannot express in either block — e.g. targeting the component from *outside*, or
theme-level `:root` variables — put an unscoped USS next to the component
(`Resources/SusRuntime/Card.uss`); it is auto-loaded at runtime via `LoadCompanionStyleSheets()`.

## Generated file names

| Source | Generated USS |
|---|---|
| Global `<style>` | `Component.g.uss` |
| `<style scoped>` | `Component_scoped.g.uss` |
| inline `style="…"` on template elements | `Component_static.g.uss` |

See also [`.sharq` format](./02-sharq-format.md) — Style section.
