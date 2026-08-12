# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
