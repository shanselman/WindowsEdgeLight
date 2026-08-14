# Changelog

All notable changes to Windows Edge Light are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and the project uses [Semantic Versioning](https://semver.org/).

---

## [v1.13.0] – 2026-08-08

### Added
- Drag support for the control window ([#144](https://github.com/shanselman/WindowsEdgeLight/pull/144) by @garrettcampbell3)

### Changed
- Batch engineering improvements merged via [#170](https://github.com/shanselman/WindowsEdgeLight/pull/170):
  - Cached `JsonSerializerOptions` as a static field to avoid repeated allocations
  - Atomic JSON write in `AppSettings` (write to temp file, then replace)
  - NuGet restore caching in CI for faster builds
  - Upgraded `softprops/action-gh-release` action v1 → v2
  - Added xUnit test project `WindowsEdgeLight.Tests` with 19 tests for `AppSettings`
  - Fixed sync dropdown shadow glow colour to match colour temperature
  - Fixed `AllMonitors` button and tray menu to reflect active "all monitors" state
  - Fixed `SettingsWindow` opening on the correct screen in multi-monitor setups
  - Fixed `IndexOutOfRangeException` when a monitor is unplugged

---

## [v1.12.0] – 2026-07-08

### Added
- Modal Settings page with sliders for brightness, visibility, and control-window button customisation ([#68](https://github.com/shanselman/WindowsEdgeLight/pull/68) by @garrettcampbell3)

### Fixed
- `SetupWindowForScreen` used the wrong DPI source ([#30](https://github.com/shanselman/WindowsEdgeLight/pull/30))
- Secondary-monitor windows did not handle DPI changes correctly ([#29](https://github.com/shanselman/WindowsEdgeLight/pull/29))

---

## [v1.11.0] – 2025-12-08

### Added
- **Exclude from Screen Capture**: edge light is now hidden from screen sharing by default (Teams, Zoom, OBS, screenshots)
  - Toggle via system tray → *Exclude from Screen Capture*
  - Uses Windows 10 2004+ `WDA_EXCLUDEFROMCAPTURE` API
  - Setting persists between sessions

---

## [v1.10.2] – 2025-12-03

### Fixed
- Multi-monitor DPI scaling bug: edge light appeared 1 000+ pixels offset when mixing DPI settings (e.g. 4K at 150 % + 1080p at 100 %)
  - Added `GetDpiForMonitor` Windows API for accurate per-monitor DPI detection
  - Window positioning now uses the correct DPI for each monitor

---

## [v1.10.1] – 2025-11-28

### Changed
- Executables are now code-signed with **Azure Trusted Signing** (publisher: Scott Hanselman) — no more SmartScreen warnings

---

## [v1.10.0] – 2025-11-24

### Added
- **Cursor Ring & Hole-Punch Effect**: a glowing ring follows the cursor near the edge; creates a magnifying-glass "hole" in the frame
  - Uses an efficient low-level mouse hook instead of polling
  - Hole punch works independently on each monitor in all-monitors mode
- **Color Temperature Control** ([#10](https://github.com/shanselman/WindowsEdgeLight/pull/10) by @cocallaw): 🔥 warmer / ❄️ cooler buttons shift the edge light between amber and blue-white tones
- **All Monitors Mode** ([#14](https://github.com/shanselman/WindowsEdgeLight/pull/14) by @MatthewSteeples): display edge light on all monitors simultaneously with synchronised brightness and colour temperature
- **Hide/Show Controls** ([#15](https://github.com/shanselman/WindowsEdgeLight/pull/15)): toggle control toolbar visibility from the tray menu; setting persists between sessions
- Single-instance enforcement — prevents accidentally launching multiple copies

### Fixed
- Hole-punch offset with per-monitor DPI scaling
- Geometry scaling on mixed-DPI monitors
- Crash and flashing when switching monitors
- Refactored hole-punch logic to reduce code duplication

---

## [v1.9] – 2025-11-16

### Added
- GitVersion integration ([#8](https://github.com/shanselman/WindowsEdgeLight/pull/8) by @phenixita): semantic versioning from git tags; version numbers derived automatically during build

### Changed
- Fixed build script to read version from `.csproj`

---

## [v1.8] – 2025-11-16

### Added
- Monitor switching: click the 🖥️ button to cycle the edge light through connected displays ([#6](https://github.com/shanselman/WindowsEdgeLight/pull/6)); properly handles DPI differences between monitors

---

## [v1.7] – 2025-11-15

### Added
- Assembly version displayed in the UI (replaces hard-coded string) ([#4](https://github.com/shanselman/WindowsEdgeLight/pull/4))
- Fluent Design improvements ([#2](https://github.com/shanselman/WindowsEdgeLight/pull/2))

---

## [v1.6] – 2025-11-14

### Fixed
- Auto-update (Updatum): removed `InstallUpdateWindowsInstallerArguments` so the ZIP updater works correctly
- ZIP packages now include `README.md` for proper portable-app detection by Updatum

---

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
