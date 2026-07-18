# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
