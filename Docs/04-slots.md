# 4. Slots

Slots allow the parent component to leave holes for content that the consumer inserts.

## Named slots

**Parent (Card.sharq):**

```xml
<template>
<ui:VisualElement $MainElement class="card">
    <slot name="header" />
    <ui:VisualElement class="card__body">
        <slot /> <!-- default — slot without name -->
    </ui:VisualElement>
    <slot name="footer" />
</ui:VisualElement>
</template>
```

**Consumer:**

```xml
<sus:Card>
    <!-- v-slot:header → falls into <slot name="header"> -->
    <template v-slot:header>
        <ui:Label text="Card Header" style="font-size: 18px;" />
    </template>

    <!-- Without v-slot → falls into <slot /> (default) -->
    <ui:Label text="Main content goes here" />

    <!-- v-slot:footer → hits <slot name="footer"> -->
    <template v-slot:footer>
        <sus:SusButton text="OK" />
    </template>
</sus:Card>
```

## SlotPropMap

```csharp
public class SlotPropMap : Dictionary<string, object>
{
    // Passes data from the parent slot to the consumer
}
```

## How it works

The compiler generates in the parent:

```csharp
var __slot_header = GetSlotContainer("header");
this.Add(__slot_header);
BuildSlot("header", null, __slot_header);

var __slot_default = GetSlotContainer("default");
this.Add(__slot_default);
BuildSlot("default", null, __slot_default);
```

In consumer:

```csharp
RegisterSlotContent("header", __el, null);
```

## Styling the slot container

`GetSlotContainer()` returns a real `VisualElement` that sits **inside** the element wrapping
`<slot>` in your template. It carries two stable USS classes:

- `sus-slot` — every slot container;
- `sus-slot--<name>` — e.g. `sus-slot--header`, `sus-slot--default`.

The container itself adds no layout of its own (UI Toolkit defaults: `flex-direction: column`).
So a row-direction wrapper lays out only that one box — the projected children still stack
vertically. Repeat the direction on the container:

```css
.my-card__actions { flex-direction: row; align-items: center; }
.my-card__actions > .sus-slot { flex-direction: row; align-items: center; }
```

Never rely on the type selector (`> VisualElement`) for this — the class is the contract.
