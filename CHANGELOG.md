# Changelog

## [Unreleased]

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.13] - 2026-08-12

### Fixed
- `Assets → Create → SUS → Sharq Screen` produced a file that did not compile in a project with
  only this package installed: the template's script section referenced `Sharq.Router`, which
  lives in a separate package. The navigation call is now a commented example, so the created
  component compiles as-is and still shows how to wire a router when one is installed.
- The Screen and Modal templates styled themselves with token names this package does not define,
  so every colour silently fell back to a hardcoded literal. They now use the `--sus-*` tokens
  from `design-tokens.uss`.
- Generated code lost its artifacts permanently when `Generated/` was missing — a fresh clone of a
  project that does not commit that folder, or a "delete it and let it regenerate" cleanup. Both
  the content hash and the section cache reported "nothing changed", so nothing was written back
  and the project stayed uncompilable until a `.sharq` file was edited by hand. A missing artifact
  now counts as a change.
- The header of a generated file recorded the absolute path of the machine that ran the generator,
  so teams committing `Generated/` got a one-line diff per teammate. The path is now project
  relative.

### Removed
- Three unused template files (`HomeScreen.sharq.txt`, `SettingsScreen.sharq.txt`, `MyApp.cs.txt`)
  that shipped in `Editor/Templates/` but were referenced by nothing — leftovers of the setup flow
  that now lives in `Editor/Setup/Starter~`, and stale copies of it.

## [1.0.12] - 2026-08-12

### Fixed
- Icon SVGs were imported with the vector cropped to its geometry bounds instead of the source
  `viewBox`. Since a `VectorImage` is painted as a stretched background, sparse glyphs came out
  distorted: `minus` (an 18×1 bar inside a 24×24 box) filled its element as a solid block, and
  `check` / the carets rendered visibly larger than the rest of the set. All shipped icons — the
  built-in subset and the optional Phosphor sample — now preserve the viewBox, so one set of
  icons is optically consistent at any size.
- New icon SVGs dropped into a `Resources/SusRuntime/Icons/**` folder get that import setting by
  default (`SusIconImportDefaults`); existing `.meta` files are never overwritten.

## [1.0.11] - 2026-08-12

### Changed
- The full Phosphor set (1,512 icons x 6 weights, 31 MB) moved out of `Runtime/Resources` into the
  optional `Phosphor Icon Set` sample. The package now ships a 127-icon subset — everything the
  built-in components use — so `Resources`, which lands in **every** player build, is 1.6 MB
  instead of 32.5 MB. Import the sample if you need the long tail; it keeps the same
  `SusRuntime/Icons/phosphor/{weight}/{name}` resource path, so no code changes are required.
  The manifest already declared this sample, but the assets had never been moved, which also
  broke store packaging.

## [1.0.10] - 2026-08-12

### Added
- `Prop<T>.ClearSubscribers()` for Domain Reload–off / static Prop hygiene.
- `SusLog` / `SusLogLevel` gated logger + `SusApp.UseLogLevel` (default Warn; `SUS_VERBOSE_LOGS` / `sus.config.json` `logLevel`).
- Spacing and radius design tokens (`--sus-space-0…64`, `--sus-radius-sm…full`) in `design-tokens.uss`.
  They are documented in the theming guide but were never defined, so `padding: var(--sus-space-16)`
  silently resolved to `0` and `border-radius: var(--sus-radius-md)` to sharp corners.

### Changed
- Breaking: rename core icon primitive `SusIcon` → `SusIconElement` (frees short name for downstream UI packages). `SusIconRegistry` / `SusIconWeight` / `ISusIconProvider` unchanged.
- Optional package `namespace` in `sharq.gen.json` for generated `.g.cs` (downstream packages opt in).
- Runtime diagnostics and *Audit call-sites emit via `SusLog.Verbose` (buyer default stays quiet).

### Fixed
- Editor Domain Reload off: reset statics on SusBootstrap / SusBreakpointService / SusThemeService so Play Mode does not leak handlers.
- SusBootstrap sample (theme showcase): every `font-size` referenced a token that does not exist
  (`--sus-font-hero`) or is a font asset rather than a size (`--sus-font-heading`), so the whole
  type scale rendered at the UI Toolkit default. Now uses `--sus-font-size-*` and shows the real
  eight-step scale with honest labels (hero is 48px, not 32px).
- SusBootstrap sample: icon tint never followed the theme — the rule targeted a `sus-icon__image`
  child that `SusIconElement` does not create. The tint now sits on the icon element itself, which also
  removes the hard-coded near-white/near-black C# fallback from a tokens-only sample.

## [1.0.9] - 2026-08-12

### Fixed
- Overlay: remember restore parent on remount (T-251).

### Changed
- Docs: English-only package documentation (T-246).

## [1.0.8] - 2026-08-11

### Changed
- Docs: public-repo reference hygiene — external product references and bundled gallery images removed; neutral wording in integration notes.
- CI: tests workflow is manual-only (`workflow_dispatch`) until license secrets are configured.

## [1.0.7] - 2026-08-11

### Added
- Release-build no-op stubs for `AuditClickBlocked` / `StateAudit` helpers — fixes CS0103 in WebGL/Release player builds.
- `SusUiProbe.ScrollJson` + `ResolveScrollView` — synthetic scroll probe for AI/MCP tooling.

### Fixed
- Prop binds applied before panel attach are flushed on attach (UITK `schedule` is a no-op while detached); covered by `PreAttachBindFlushTests`.

## [1.0.6] - 2026-08-02

### Added
- `SusUiProbe` — machine-readable (JSON) snapshot of the live UI (tree / props / health) for AI agents / MCP, plus an Editor `SusUiProbeEditor.ValidateSetupJson`. No Console output by default; Editor / Development builds only.

### Fixed
- `SusBreakpointService`: gate breakpoint-change logs behind `VerboseLogging` (were unconditional).

## [1.0.5] - 2026-07-22

### Fixed
- `SusBootstrap.EnsureEventSystem`: recreate after destroy; always attach `BaseInputModule` (Input System UI module or `StandaloneInputModule`) so UITK clicks work
- Quiet OverlayAudit / BoundsAudit false positives (prior commit)

## [1.0.4] - 2026-07-19

### Changed
- Setup wizard PanelSettings default: `ConstantPixelSize` (no Unity auto-scale)
- Docs: responsive = `SusBreakpointService` only; width from cascade root `resolvedStyle` (same path as removed resolution service)
- `SusBreakpointService`: geometry push from every `SusComponent`, panel poll hook, optional `SetOverride` for Storybook/QA

### Removed
- `SusResolutionService` and High/Low resolution classes — screen adaptation is breakpoints only

## [1.0.3] - 2026-07-19

### Fixed
- Move git hook tooling to `scripts~` so Unity AssetDatabase no longer imports them (GUID conflicts across packages)

## [1.0.2] - 2026-07-19

### Fixed
- OverlayHost stack order test matches Modal under Tooltip (enum Modal=20, Tooltip=30)

## [1.0.1] - 2026-07-18

### Changed
- Public surface documents only core + router; downstream UI packages register via generic hooks
- Setup wizard and Inspector no longer reference optional sample scenes from other products

## [1.0.0] - 2026-07-18

### Added
- Initial public release (MIT)
- `.sharq` SFC compiler (template / script / style → C# + USS)
- Reactive props, directives, slots, themes
- SusApp layer scaffold: `WorldMarkerLayer` → `ScreenHost` → `OverlayHost`
- Overlay host, world-marker mounting, documentation and tests
