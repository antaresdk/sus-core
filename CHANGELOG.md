# Changelog

## [Unreleased]

## [1.0.25] - 2026-08-21

### Changed
- Public-scope hygiene: Set Doctor tests / doc-comments and the input-device R25 guard test use
  neutral package ids instead of downstream package names (no behaviour change).

## [1.0.24] - 2026-08-21

### Added
- `SusInputDevice` + `SusInputGlyph` (with `ISusInputGlyphProvider`, `SusInputActionId`,
  `SusInputDeviceKind`): last-active input device policy (pointer / keyboard / gamepad / touch)
  and glyph resolution for prompts; idempotent `EnsureInstalled` from `SusBootstrap` after
  `EnsureEventSystem`; Edit/Play tests (T-1398).
- `SusTouchMin` + shared `--sus-touch-min` design token — one source for the touch-target
  minimum consumed by kit and game (T-1267).

### Changed
- `SusComponent` ctor/dev-audit hooks extracted into the `SusComponent.Audits.cs` partial;
  unused `BuildStarter`/`BuildHomeScreen` SetupWizard templates (`Starter~` path) dropped (T-1111).
- Sharq codegen: AOT-safe `v-for` key emit through a `GetItemMember` reflection helper (no
  `dynamic`); an unknown UITK `@event` now emits `#error` instead of `EventBase<EventBase>` (T-1106).
- `SusSampleSync.SyncTree` force-syncs `.unity` scenes (T-948).
- Shipped-code comments swept of internal ticket references and rephrased as behavior
  comments (T-1113).
- Docs: README + DESIGN_TOKENS §1.4 "restyle without C#" (T-1235), responsive lead with the
  desktop+mobile payoff (T-1230), DESIGN_TOKENS load wording / VectorImage capitalisation (T-1378).

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.23] - 2026-08-20

### Fixed
- `Prop<T>` reentrant self-trigger: subscriber notification (`Changed` / `propertyChanged` / `invalidated`)
  now runs with dependency tracking suspended, so a watch handler that reads `Value` synchronously during a
  prop write is no longer misattributed to whichever `WatchEffect` happened to be tracking on the caller's
  stack — the caller's next write no longer re-fires its own still-executing flush (previously bounded only
  by `MaxSteadyStateFlushIterations = 100`). New `Peek()` escape hatch reads `Value` without registering a
  dependency, for read-modify-write accumulator helpers (T-1302, T-1206).
- Companion-USS lookup falls back up the base-type chain: a Tier-B C# subclass of a Sharq visual-root
  component with no `.sharq` of its own (e.g. `MySkinScoreboard : SusTable`) inherits its nearest styled
  ancestor's companion `.g.uss` sheet(s) instead of silently losing them; the resolved owner type is cached
  per most-derived type, and hot-reload removal keys off the same resolved owner (T-1273).

## [1.0.22] - 2026-08-20

### Fixed
- Steady-state bind-cascade desync: when a bind action run from `ApplyAllBindUpdates` synchronously wrote a
  derived `Prop<T>` that another `Bind*`/`WatchEffect` on the same component reads, the re-entrant scheduler
  re-arm was silently dropped by UITK and the bound text/class stayed one generation behind permanently.
  `ApplyAllBindUpdates` now drains re-queued bind actions within the same dispatch (bounded by
  `MaxSteadyStateFlushIterations = 100`, warns instead of hanging on a real Prop cycle) (T-1204).
- `SusMotion`: a forever-`Repeat` motion now stops and unregisters when its target leaves the panel
  (`DetachFromPanelEvent`) instead of ticking a dead `VisualElement`; `ActiveByTarget` also gets a
  play-mode-restart reset like the other statics (T-1103).

### Changed
- Perf: `(Type, propName)` reflection accessors for `SetChildProp`/`BindChildProp` are cached instead of
  `GetField`/`GetProperty` on every prop-bind change (T-1101).
- Perf: `Updated()` is scheduled (`Every(16)`) only for component types that actually override it — removes a
  60 Hz no-op tick on every `SusComponent` (T-1102).
- Docs: 04-slots.md — expanded "Styling the slot container" section (T-906).

## [1.0.21] - 2026-08-19

### Added
- `SusComponent.GetSlotContainer()` tags slot containers with stable classes `sus-slot sus-slot--<name>`
  (`SlotContainerClass` / `SlotContainerClassFor`, `_global.uss` hook rule, Docs/04-slots.md "Styling the slot
  container") (T-530).
- `SusSampleSync` (Editor-only): whole-tree sample sync + Verify + GuardCopyFresh for Refresh menus and live tests
  (T-507).

### Changed
- `Samples~` Comp / SusBootstrap / SusKeepAlive examples wrapped in `Sharq.Core.Examples` namespace
  (Asset Store Validator "Type Namespaces") (T-659).
- README hero banner re-rendered from the hub (T-801).

## [1.0.20] - 2026-08-19

### Changed
- Docs: router install URL pinned to v1.0.12.

## [1.0.19] - 2026-08-19

### Added
- SusUiProbe tree JSON emits image src/width/height/scaleMode for the frame-geometry aspect gate (T-654).

### Fixed
- Sharq USS AtomicWrite no longer fails on locked Resources files (T-886).

### Changed
- README: hero banner, support e-mail, GitHub Releases link (T-703, T-747, T-749, T-761).

## [1.0.18] - 2026-08-18

### Added
- **`SusMotion`** — code-driven tween builder for UITK inline styles (opacity / scale / translate /
  rotate / background-color) with a fluent API, `SusEase` curves, `SusRestoreMode`, presets
  (`SusMotionPresets`: FadeIn/Out, SlideIn/Out, Bounce, PunchScale, Shake) and
  `SusMotionStagger.Children`. One active play per target; distinct from `SusTransition`
  (USS enter/leave phases). Tests added.

### Fixed
- WebGL `RangeError: Maximum call stack size exceeded` on deep synchronous mounts: a bind /
  `WatchEffect` action that adds a freshly built child during the parent's own attach-flush
  re-entered `FlushPendingBindUpdatesOnAttach` one stack level deeper per nested reveal. The
  synchronous flush depth is now capped at 1; nested flushes are queued and drained iteratively
  by the outermost caller within the same tick (nothing is deferred to the scheduler, nothing
  is dropped). Regression tests added.

### Changed
- README: first-touch section (requirements, `.sharq` quickstart, exit cost); legacy product
  comparison dropped.
- Versioned hooks: `scripts~/pre-push` / `scripts~/prepare-commit-msg` carry the attribution
  guard (no AI trailers in the pushed range).

## [1.0.17] - 2026-08-17

### Added
- **SUS Set Doctor**: `RootFileProvenance` — reads the packer's `Generated for: <set> v<version>`
  marker in generated root files (README / LICENSE / Third-Party Notices) and warns (never
  "delete") when a smaller set's re-import overwrote the root files of a larger installed set.
- `ResourcesFolderIconProvider`: a requested icon weight (e.g. `Fill`) that the curated set does
  not ship degrades to `Regular` instead of returning no image.

### Fixed
- Reactive sibling attach-flush race: several freshly built reactive components attached in the
  same synchronous cascade could lose their post-attach bind catch-up (blank / uncoloured
  `BindClass` / `BindVisibility` targets); the flush is now applied without a per-component
  one-shot scheduler item. Regression tests added.
- `SusWorldSpacePanel`: the auto-created world-space `PanelSettings` stays in Screen Space until
  the first `AttachElement` call — apps that never mount world-space UI no longer keep an idle
  WorldSpace panel alive.
- Public doc-comments in `SusSetDoctor` no longer mention internal tool names.

### Changed
- Test infrastructure: `SusLogLevelGuardAttribute` resets `SusLog.Level` around every test in
  the shared PlayMode domain; `scripts~/prepare-commit-msg` is the versioned hook (PowerShell
  copy removed).
- README: community links (Discord / Telegram); `Docs/13-audits.md` callback-audit count fixed.

## [1.0.16] - 2026-08-16

### Added
- **SUS Set Doctor v2**: reads every per-module `sus-module.json` and per-set `sus-set.<set>.json`
  under `Assets/` and attributes each path to its owner. "Delete" is advised only for a path that
  belongs to a module which is present but no longer lists it (`Residual`); everything else is
  reported without a destructive hint (`Unattributed`, `ModuleManifestMissing`, `IncompleteSet`,
  `Relocated`). Installing the Kit set on top of the Complete set no longer suggests removing the
  Game module.
- `SusClassicSampleLocator` (Editor): resolves a module's sample folder under a classic
  (`.unitypackage`) install from its `sus-module.json`, so the Kit/Game *Setup* menus work for a
  classic purchaser instead of asking to install a package they never had.

### Fixed
- `BindTransitionVisibility`: the "no animation on initial mount" latch is now gated on the first
  real layout pass instead of the first reactive run, so a component whose prop is set right after
  construction (before `Add()`) no longer plays a real Enter animation on its first paint
  (expansion panels captured at `opacity: 0`).
- Sample asmdefs are `autoReferenced: false` — a purchaser's `Assembly-CSharp` no longer
  implicitly references sample code.

### Changed
- Package push gate (`scripts~/pre-push`): docs check runs always and first; version bump is
  hard-required on source changes.

## [1.0.15] - 2026-08-13

### Added
- **SUS Set Doctor** (Editor, `Window/SUS/Set Doctor` + auto-run on load/import): detects the three
  states that silently break a classic (`.unitypackage`) install — a UPM package and the same
  module under `Assets/` at once, residual files left over from an older set version, and a
  version mismatch between modules — and prints what to delete/re-import.
- Classic-layout starter assets and `sharq.gen.json` discovery for modules installed under
  `Assets/` (not only under `Packages/`).
- Project-local Phosphor icon subset provider + sample README (icon-subset sample).
- `SusUiProbe` marks truncated single-line text nodes; dev console filter chips have stable names
  for UX targeting.

### Fixed
- `OverlayHost.ClearAll` / `ClearCategory` no longer corrupt the UIR render tree when a close
  re-enters the host.
- Bindings and schedules survive a relocation-only detach (portal / self-teleporting elements),
  so overlay components no longer close themselves while opening.
- `v-if` / `transition` elements: a hidden-at-mount element re-appears at its authored sibling
  index instead of jumping to last child; the first evaluation snaps to the start state instead
  of playing a leave animation.
- Portal popups resync width/position on `GeometryChangedEvent`, not only at `Show()`.
- Fast Enter Play Mode: dev/diagnostic statics are reset on Enter Play Mode.
- Samples: dangling `PanelSettings` reference in Comp/ThemeShowcase/SusKeepAlive.
- Sharq source generator: a `[CreateProperty(default: "…")]` string default lost its quotes and
  overrode the author's field initializer, and a field with a trailing `// comment` after `;`
  skipped its `[UxmlAttribute]` companion. String literals are re-emitted verbatim, the author's
  initializer wins, trailing comments are preserved (regenerate `.g.cs` in dependent packages).

### Changed
- Docs: internal workspace paths removed from buyer-facing docs; translation artifacts fixed;
  screenshots added.

## [1.0.14] - 2026-08-12

### Changed
- The dev console (`SusConsoleService`) is now styled by a stylesheet instead of C# inline styles:
  `Runtime/Resources/SusRuntime/sus-console.uss`, loaded onto the console root on first `Show()`.
  Every colour is a design token, so the console follows the active theme, and a project can
  restyle it by overriding the `.sus-console*` classes — previously the look was compiled into the
  service and could not be changed at all. Documented in `Docs/17-console.md`.

### Fixed
- The open console looked broken: filter buttons, text fields and the scrollbar rendered with the
  UI Toolkit defaults (light grey controls, white input boxes, editor-chrome scroller) on top of a
  dark game UI. Filters are now chips that show which one is active, inputs are dark with
  placeholders, and the scroller is a thin dragger.
- The status line under the log said either the active filter or how many entries were shown,
  depending on which code path updated it last. It now always shows both.
- `SusConsoleService.Instance` and `OnDuplicateCommand` survived leaving Play Mode with Domain
  Reload disabled (Fast Enter Playmode), so the next session could start with a console pointing
  at a destroyed panel. Both are reset on `SubsystemRegistration`, matching the other services.

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
