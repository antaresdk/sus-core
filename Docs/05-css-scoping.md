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

## Variant recipes (`@variants`)

A prop that drives a family of classes — size, color, rounded corners — used to mean writing every
combination by hand: one `:class` condition per class, one CSS rule per value, kept in sync by hand
whenever a step is added or renamed. The `@variants` at-rule inside `<style>` (scoped or global)
compiles that mapping from a single declaration instead:

```
@variants <axis> [from <Prop>] [as "<prefix>"] [ambient] {
    <value> ["<alias>" …] {
        <declarations, optionally with nested `&` rules>
    }
    …
}
```

- `<axis>` — a kebab-case name (`size`, `density`, `rounded`, …).
- `from <Prop>` — the backing prop (a `Prop<string>` or `Prop<bool>` field/property on the
  component); defaults to the PascalCase form of the axis name when omitted.
- `as "<prefix>"` — overrides the emitted class prefix, for a legacy class shape that doesn't
  follow `<block>--<axis>-<value>`: `as ""` on axis `variant` emits `sus-button--elevated` instead
  of `sus-button--variant-elevated`.
- `<value>` — a step name; any quoted strings after it are **aliases** — extra prop values that
  also select this step. The step named `default` additionally matches an empty prop value.
- `ambient` — marks an axis that emits no class or rule of its own (see below); it takes no block.

For each value, the compiler emits a CSS rule under the value's class and a matching condition in
the component's generated `Build()`; for an axis without `ambient` it also emits a validator, so a
prop value outside the recipe is caught while editing instead of silently matching nothing.

### Example — a `size` axis

Real recipe, abbreviated (two of five steps), from a button-style component's `<style>` block:

```
@variants size from Size {
    xs "x-small" {
        min-height: rung(control-height, xs);
        padding-left: rung(space, 10); padding-right: rung(space, 10);
        font-size: rung(font-size, xsmall);
        &.icon-only    { width: rung(control-height, xs); max-width: rung(control-height, xs); }
        &.rounded-pill { border-radius: rung(pill, xs); }
        .icon-wrap     { min-width: rung(control-box, xs); min-height: rung(control-box, xs); }
    }
    default {
        min-height: rung(control-height, lg);
        &.icon-only    { width: rung(control-height, lg); max-width: rung(control-height, lg); }
    }
}
```

`&` inside a value's block follows the CSS Nesting standard: it stands for the value's own emitted
selector, so `&.icon-only` becomes `<block>--size-xs.icon-only`, not a descendant rule. A line
with no `&` (`.icon-wrap` above) nests as a **descendant** of the value's selector instead.

`rung(<family>, <rung>)` is the one piece of arithmetic a recipe is allowed: it resolves at compile
time against the shared dimension ladder and becomes `var(--sk-<family token>-<rung>, <fallback>)` —
the token name comes from the ladder, never typed by hand, and the fallback is the ladder's own
value for that family at its base step. A family or rung the ladder doesn't have is a compile error
naming the `.sharq` file and line, not a silently-wrong number.

### Ambient axes — token override, not a class

Some props (density is the common one) don't add a class or write a rule themselves — they work by
having a **different** value of the same `--sk-*` tokens already in play (via a cascade class set
higher up, e.g. on the app root) resolve differently underneath. Declaring the axis `ambient` says
this out loud instead of leaving it invisible:

```
@variants density from Density ambient;
```

This is a statement, not a shortcut: it tells the compiler (and the component's own recipe
description used by tooling) "this prop drives an effect that happens elsewhere," which is exactly
what separates an intentionally ambient prop from one whose axis was simply never written.

### Boundary

A recipe compiles a fixed cross product of axis × value; it is not a small stylesheet language.
There is no `extend`, no user-defined functions, and no import between a `.sharq` style body and a
utility-class sheet — none of the three is planned. A component that needs more than a cross product
of named steps keeps writing plain rules in `<style>` (see above), which remains a fully supported,
unmigrated form.

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
