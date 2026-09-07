# SusCore Fonts — replacing the packaged typeface

> **For whom:** integrators who want their own typeface(s) in a SUS-based project instead of
> the packaged Montserrat / IBM Plex Mono.
>
> **What:** the two USS-level override paths — a hand-written `_font.uss` override (§2), and a
> `SusFontAsset` set exported to one via an Editor menu (§3) — plus the `sus-font-*` role marker
> classes that pick a typeface per element either way, raw TTF vs. SDF FontAsset, the
> missing-glyph fallback chain, and the letter-spacing em→px recipe. Companion to `_font.uss`'s
> own header comment, which is the terse version of §1–3 below. Policy behind "USS, not C#, sets
> appearance": [Restyle without editing C#](../Docs/DESIGN_TOKENS.md#1-4-restyle-without-editing-c).

---

## 1. Two override paths — pick one or combine

SusCore ships every font reference as a CSS custom property with a Montserrat/IBM Plex Mono
fallback: `--font-family-regular: var(--sus-font-family-regular, url("Fonts/Montserrat/…"));`
(see `Runtime/Resources/SusRuntime/_font.uss`). Every override path — however you fill it in —
ends up writing to that same `--sus-font-family-*` layer; USS is the only place SUS declares a
typeface, there is no inline / code-level path any more. Two ways to fill it in, independent —
most projects only need one:

| Path | Fills in | Reaches | Needs markup changes? |
|---|---|---|---|
| **Direct USS override** (hand-written `_font.uss`, §2) | `--sus-font-family-*`, by hand | Every USS rule that reads the token, everywhere, including third-party/kit components you don't own | No |
| **`SusFontAsset` export** (`SusFontUssExporter`, §3) | the same `--sus-font-family-*`, generated from a `SusFontAsset` you fill in the Inspector | Same as above, once exported — plus the `sus-font-*` role marker classes described in §3, which pick a typeface per tagged element | Only if you want per-element roles; the export alone already reaches every consumer |

**Why the export step exists:** Unity's UI Toolkit has no public API to set a USS custom
property (`--var`) from C# at runtime, so a `SusFontAsset` filled in the Inspector cannot be
pushed onto a live tree in code. `Window > SUS > Fonts > Export Font Set to USS`
(`SusFontUssExporter`) resolves the asset **once**, in the Editor, into
`Assets/Resources/SusRuntime/_font.uss` — the exact file §2 describes hand-editing — so the rest
of the pipeline (component rules, the `sus-font-*` marker classes, skins, overlays) never has to
know the typeface came from an asset instead of a hand-written sheet.

## 2. Token-level: `Assets/Resources/SusRuntime/_font.uss`

Unity resolves `Resources.Load` by first match across all `Resources` folders, in a
project-before-package order. Placing a file at that exact path in your own project
therefore **shadows** the packaged one:

```
Assets/Resources/SusRuntime/_font.uss
```

```css
:root {
    --sus-font-family-regular:   url("Fonts/YourFont/YourFont-Regular.ttf");
    --sus-font-family-medium:    url("Fonts/YourFont/YourFont-Medium.ttf");
    --sus-font-family-bold:      url("Fonts/YourFont/YourFont-Bold.ttf");
    --sus-font-family-light:     url("Fonts/YourFont/YourFont-Light.ttf");
    --sus-font-family-heading:   url("Fonts/YourFont/YourFont-Condensed.ttf");
    --sus-font-family-mono:      url("Fonts/YourFontMono/YourFontMono-Regular.ttf");
    --sus-font-family-black:     url("Fonts/YourFont/YourFont-Black.ttf");
    --sus-font-family-italic:    url("Fonts/YourFont/YourFont-Italic.ttf");
    --sus-font-family-condensed: url("Fonts/YourFont/YourFont-Condensed.ttf");
}
```

You only need to set the tokens you're overriding — anything you omit keeps resolving to
its packaged default through the `var(--sus-font-family-x, <default>)` fallback. This is the
**only** override path for `--font-family-black` and `--font-family-italic` (no
`SusFontAsset` slot maps to them — see `SusFontAsset`'s own header comment) and for
`--font-family-condensed` when you don't already have a `SusFontAsset` wired up.

This file is not auto-discovered from a comment — before T-2216 the only place this path
was documented at all was a comment inside `_font.uss` itself, which nobody reads before
shipping. It is now also cross-linked from `SusFontAsset`'s tooltips.

## 3. `SusFontAsset` export + role marker classes

Create an asset via **Assets → Create → SUS → Font Set**, fill in the slots you have, then run
**Window → SUS → Fonts → Export Font Set to USS** (writes/overwrites
`Assets/Resources/SusRuntime/_font.uss`, prompting before it overwrites a hand-written file).
That single step is what makes the asset reach the cascade — filling the asset alone changes
nothing until you export it.

`SusApp.UseFonts(myFontSet)` / `SusFontService.ApplyFonts(root, myFontSet)` are kept for source
compatibility; they no longer write any style. If the asset you pass still has slots filled,
they log a warning (`SusLog.Warn`) naming the export step above — that is the current form of
the "don't silently do nothing" behavior T-2216 introduced.

`Regular` is the panel default (`:root` in `_font.uss`), inherited by every element; the other
five slots — `Medium`, `Bold`, `Light`, `Heading`, `Mono` — plus `Condensed` reach only elements
tagged with the matching marker class. Each marker class carries its typeface **on its own**,
as a plain USS rule keyed by the exported token — no C# call is required once the class is in
the markup:

| Slot | Resolve fallback chain | Marker class |
|---|---|---|
| `Heading` | Heading → Bold → Regular | `sus-font-heading` (`SusFontService.HeadingClassName`) |
| `Mono` | Mono → Regular | `sus-font-mono` (`SusFontService.MonoClassName`) |
| `Bold` | Bold → Medium → Regular | `sus-font-bold` (`SusFontService.BoldClassName`) |
| `Medium` | Medium → Regular | `sus-font-medium` (`SusFontService.MediumClassName`) |
| `Light` | Light → Regular | `sus-font-light` (`SusFontService.LightClassName`) |
| `Condensed` | Condensed → Heading chain | `sus-font-condensed` (`SusFontService.CondensedClassName`) |

The fallback chain is USS itself — `_font.uss` chains
`var(--sus-font-family-heading, var(--font-family-bold))` and so on — so an empty slot in the
asset simply leaves that `var()` unresolved, and it falls through to the next family in the
chain, not to a C# lookup.

Tag whatever markup should carry that role, e.g. in a `.sharq` template:

```html
<div class="hero-title sus-font-condensed">…</div>
<span class="unit-code sus-font-mono">P01</span>
```

That's enough by itself. Use `SusFontService.ApplyRoleClass(el, SusFontRole.Heading)` /
`ClearRoleClasses(el)` only when an element's role needs to change at runtime (e.g. code built
by something other than markup) — they swap the marker class and write nothing else; the
typeface still comes from `_font.uss`.

## 4. Raw TTF vs. FontAsset (SDF) — which to use

Both slot types (`FontDefinition`) accept either. They are not equivalent:

| | Raw `Font` (TTF/OTF) | `FontAsset` (SDF) |
|---|---|---|
| Rendering | Unity's legacy dynamic font rasterizer | Signed-distance-field atlas, generated once by **Window → TextMeshPro → Font Asset Creator** (or right-click → Create → Text → Font Asset) |
| Runtime cost | Rasterizes glyphs on demand into a dynamic atlas as new characters appear on screen — cheap to set up, pays a small per-new-glyph cost the first time each character is drawn | Atlas is baked ahead of time from a chosen character set; no per-glyph runtime cost, but any character missing from that set renders as a tofu/missing-glyph box unless a fallback resolves it (§5) |
| Best for | Quick integration, prototypes, fonts with a huge or unpredictable character set (e.g. full CJK) | Shipping builds — predictable atlas memory, predictable frame cost, and the only type that supports a fallback chain (§5) |

Packaged Montserrat and IBM Plex Mono in SusCore ship as FontAsset SDF for this reason.

## 5. Missing-glyph fallback chain

An SDF `FontAsset` only renders characters baked into its atlas. If your primary typeface's
atlas doesn't include a character your project needs — e.g. **IBM Plex Mono's base atlas has
no Cyrillic**, while Montserrat's does — that character renders as a missing-glyph box unless
you give the FontAsset a fallback chain:

1. Select the `FontAsset` asset in the Project window.
2. In the inspector, open **Fallback Font Assets**.
3. Add one or more `FontAsset`s that *do* cover the missing characters, in priority order —
   Unity checks each one in list order and uses the first that has the glyph
   (`FontAsset.fallbackFontAssetTable` at the API level).

A fallback FontAsset needs its own atlas covering the character set you're bridging (e.g. a
Cyrillic-covering weight of the same or a visually compatible family) — assigning a raw `Font`
here doesn't work, fallback only chains between SDF FontAssets. Decide this **before**
shipping a mixed-language project on a Latin-only mono face: the fallback face's baseline,
weight, and x-height should be close enough that the substitution isn't jarring mid-word.

## 6. Letter-spacing: em in the design file, px in USS

USS `letter-spacing` (`-unity-*` equivalents included) only accepts absolute units — Unity UI
Toolkit has no `em` unit. Design files and imported layouts, on the other hand, typically
specify tracking in `em` (a multiple of the element's own font size), because that's what
scales sanely across type sizes. Converting once per size, rather than hand-picking a px
value per component, keeps the two in sync when a size token changes:

```
tracking_px = tracking_em * font_size_px
```

Recipe as a token, mirroring the pattern already used for `--base-font-tracking-*` in
`_font.uss` (which are hand-picked absolute values, not derived — this is the derivation
your project's tokens should follow if the source design is spec'd in em):

```css
:root {
    /* design spec: 0.02em tracking at the 24px heading2 size → 0.02 * 24 = 0.48px */
    --my-tracking-heading2: 0.48px;
}

.my-heading2 {
    font-size: var(--sus-font-size-heading2); /* 24px, see _font.uss */
    letter-spacing: var(--my-tracking-heading2);
}
```

If the same em value is reused across multiple font sizes (a common design-system pattern),
compute one px value per size token rather than trying to share a single px constant across
sizes — an em-based tracking value is proportional to size by definition, and a shared px
value silently breaks that proportionality the moment the font size changes.
