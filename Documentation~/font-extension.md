# SusCore Fonts — replacing the packaged typeface

> **For whom:** integrators who want their own typeface(s) in a SUS-based project instead of
> the packaged Montserrat / IBM Plex Mono.
>
> **What:** the two independent override mechanisms (token-level USS, and per-role code-level
> via `SusFontAsset` / `SusFontService`), raw TTF vs. SDF FontAsset, the missing-glyph fallback
> chain, and the letter-spacing em→px recipe. Companion to `_font.uss`'s own header comment,
> which is the terse version of §1–2 below.

---

## 1. Two override paths — pick one or combine

SusCore ships every font reference as a CSS custom property with a Montserrat/IBM Plex Mono
fallback: `--font-family-regular: var(--sus-font-family-regular, url("Fonts/Montserrat/…"));`
(see `Runtime/Resources/SusRuntime/_font.uss`). There are two ways to replace that default,
and they are independent — most projects only need one:

| Path | Sets | Reaches | Needs markup changes? |
|---|---|---|---|
| **Token-level** (USS override file, §2) | `--sus-font-family-*` | Every USS rule that reads the token, everywhere, including third-party/kit components you don't own | No |
| **Code-level** (`SusFontAsset` + `SusFontService`, §3) | inline `-unity-font-definition` on tagged elements | Only elements tagged with a `SusFontService.<Role>ClassName` marker class | Yes — one class per element |

**Why both exist:** Unity's UI Toolkit has no public API to set a USS custom property
(`--var`) from C#. The token-level path is USS-only by construction. The code-level path
works around that by writing an *inline* style directly onto tagged elements — inline
styles outrank USS rules regardless of selector specificity, so a marker class lets
`SusFontService.ApplyFonts` win against a component's own more-specific `-unity-font-definition`
rule. Body text is the one role that gets a free ride: `ApplyFonts` also sets the root's
inline font, which cascades down by inheritance to any element that doesn't have its own
explicit rule.

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

## 3. Code-level: `SusFontAsset` + `SusFontService`

Create an asset via **Assets → Create → SUS → Font Set**, fill in the slots you have, then:

```csharp
SusApp.UseFonts(myFontSet); // or: SusFontService.ApplyFonts(root, myFontSet);
```

`Regular` is always applied (inherited from the root). The other five slots — `Medium`,
`Bold`, `Light`, `Heading`, `Mono` — plus `Condensed` are applied **only** to elements
tagged with the matching marker class:

| Slot | Resolve fallback chain | Marker class |
|---|---|---|
| `Heading` | Heading → Bold → Regular | `SusFontService.HeadingClassName` (`sus-font-heading`) |
| `Mono` | Mono → Regular | `SusFontService.MonoClassName` (`sus-font-mono`) |
| `Bold` | Bold → Medium → Regular | `SusFontService.BoldClassName` (`sus-font-bold`) |
| `Medium` | Medium → Regular | `SusFontService.MediumClassName` (`sus-font-medium`) |
| `Light` | Light → Regular | `SusFontService.LightClassName` (`sus-font-light`) |
| `Condensed` | Condensed → Heading chain | `SusFontService.CondensedClassName` (`sus-font-condensed`) |

Tag whatever markup should carry that role, e.g. in a `.sharq` template:

```html
<div class="hero-title sus-font-condensed">…</div>
<span class="unit-code sus-font-mono">P01</span>
```

If a slot is filled on the asset but nothing under the root you called `ApplyFonts(root, …)`
on carries its marker class, `SusFontService` logs a warning (`SusLog.Warn`, level `Warn`
by default) instead of silently doing nothing — that silent half-application (only Regular
ever changing) is exactly the defect T-2216 closed.

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
