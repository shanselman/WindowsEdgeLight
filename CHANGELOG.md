# Changelog

All notable changes to Windows Edge Light are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [v1.13.0] - 2026-08-08

### Added

- Drag support for the control window ([#144](https://github.com/shanselman/WindowsEdgeLight/pull/144) by @garrettcampbell3).

### Changed

- Batch engineering improvements merged via [#170](https://github.com/shanselman/WindowsEdgeLight/pull/170):
  - Cached `JsonSerializerOptions` to avoid repeated allocations.
  - Added atomic JSON writes for application settings.
  - Added NuGet restore caching in CI.
  - Upgraded `softprops/action-gh-release` from v1 to v2.
  - Added the xUnit test project.
  - Fixed synchronized dropdown shadow color.
  - Fixed all-monitors button and tray-menu state.
  - Opened Settings on the correct monitor.
  - Fixed monitor-removal index handling.

## [v1.12.0] - 2026-07-08

### Added

- Settings page for brightness, visibility, and control-window customization ([#68](https://github.com/shanselman/WindowsEdgeLight/pull/68)).

### Fixed

- Corrected the DPI source used by `SetupWindowForScreen` ([#30](https://github.com/shanselman/WindowsEdgeLight/pull/30)).
- Added DPI-change handling for secondary-monitor windows ([#29](https://github.com/shanselman/WindowsEdgeLight/pull/29)).

## [v1.11.0] - 2025-12-08

### Added

- Exclude-from-capture support for screen sharing and screenshots.

## [v1.10.2] - 2025-12-03

### Fixed

- Corrected multi-monitor positioning for mixed-DPI configurations.

## [v1.10.1] - 2025-11-28

### Changed

- Release executables are signed with Azure Trusted Signing.

## [v1.10.0] - 2025-11-24

### Added

- Cursor ring and hole-punch effects.
- Color-temperature controls ([#10](https://github.com/shanselman/WindowsEdgeLight/pull/10)).
- All-monitors mode ([#14](https://github.com/shanselman/WindowsEdgeLight/pull/14)).
- Hide/show controls through the tray menu ([#15](https://github.com/shanselman/WindowsEdgeLight/pull/15)).
- Single-instance enforcement.

### Fixed

- Hole-punch offsets and geometry scaling on mixed-DPI monitors.
- Crashes and flashing while switching monitors.

## [v1.9] - 2025-11-16

### Added

- GitVersion-based semantic versioning ([#8](https://github.com/shanselman/WindowsEdgeLight/pull/8)).

## [v1.8] - 2025-11-16

### Added

- Monitor switching with per-monitor DPI handling ([#6](https://github.com/shanselman/WindowsEdgeLight/pull/6)).

## [v1.7] - 2025-11-15

### Added

- Assembly version display ([#4](https://github.com/shanselman/WindowsEdgeLight/pull/4)).
- Fluent Design improvements ([#2](https://github.com/shanselman/WindowsEdgeLight/pull/2)).

## [v1.6] - 2025-11-14

### Fixed

- Corrected portable ZIP update handling.
- Included `README.md` in release archives for Updatum portable-app detection.

[v1.13.0]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.13.0
[v1.12.0]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.12.0
[v1.11.0]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.11.0
[v1.10.2]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.10.2
[v1.10.1]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.10.1
[v1.10.0]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.10.0
[v1.9]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.9
[v1.8]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.8
[v1.7]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.7
[v1.6]: https://github.com/shanselman/WindowsEdgeLight/releases/tag/v1.6
