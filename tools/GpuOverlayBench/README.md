# Local GPU overlay experiment

This is a **prototype, not a production renderer or a qualifying performance win**.
It leaves `WindowsEdgeLight` unchanged and references the frozen v1.14.0 assembly
from `..\OverlayBench\bin\control-b927edd`.

The measured implementation rasterizes the actual production glow once at native
DPI, uploads premultiplied BGRA pixels to a hardware D3D11/D2D bitmap, and presents
through DirectComposition swapchains. Hover moves the unchanged 70-DIP hole with
a GPU geometry mask. Hover and brightness do not upload or read back image pixels
inside the timed interval. Exact color updates currently rerasterize the WPF
source synchronously; this is prohibitively slow and must not be shipped.

## Result and scope

The preserved report is `bin\evidence\report.md`, with raw JSON/PNG evidence,
compiler-recorded source hashes, dependency locks and build manifests beside it.
These ignored local artifacts must be preserved separately before cleaning `bin`.

- Six 12-second paired runs: pooled app CPU p50/p90 **1.57/9.20%**, versus the
  pinned control's **35.39/42.92%** of one logical core.
- This is **not robustly under 10%**: two individual short runs exceeded the
  p90 bar, a 120-second soak reached **10.79% p90**, and a mixed-monitor run
  reached **12.16% p90**.
- An exact four-monitor color update took **10.4 seconds**. Brightness changes
  were cheaper but also had slower callback cadence than control.
- Initial unmasked GPU pixels matched production raster output exactly on all
  four displays. Hole antialiasing and brightness rounding differ.
- This is a shared-desktop, synthetic-input integration benchmark, not an
  isolated machine or a physical cursor/click/capture E2E test.

Dependencies are pinned Vortice.Direct3D11, Vortice.Direct2D1 and
Vortice.DirectComposition **3.8.3**, confined to the tools projects. Hardware
device creation is required; there is no silent WARP fallback.

## Build and run

Use .NET 10 on Windows and the frozen control documented in
`..\OverlayBench\README.md`. Do not overwrite any existing control or measured
output. Production source must still match pinned commit
`b927edd529ce97bfa729dfc0a6f185002c4878df`.

From the repository root, with the normal app closed:

```powershell
git diff --exit-code b927edd529ce97bfa729dfc0a6f185002c4878df -- WindowsEdgeLight
if ($LASTEXITCODE -ne 0) { throw 'Production source differs from the control' }
if (Test-Path tools\GpuOverlayBench\bin\local-build) { throw 'Choose a new build path' }
dotnet build tools\GpuOverlayBench\GpuOverlayBench.csproj -c Release `
  -p:RestoreLockedMode=true -o tools\GpuOverlayBench\bin\local-build
if ($LASTEXITCODE -ne 0) { throw 'Build failed; do not run stale binaries' }

# Separate sequential A/A validation for a new host/environment:
tools\GpuOverlayBench\Run-Pairs.ps1 `
  -Executable tools\GpuOverlayBench\bin\local-build\GpuOverlayBench.exe `
  -Output tools\GpuOverlayBench\bin\local-aa -TreatmentMode control -Pairs 3

# Three serial AB/BA/AB pairs. Use a distinct output directory for round two.
tools\GpuOverlayBench\Run-Pairs.ps1 `
  -Executable tools\GpuOverlayBench\bin\local-build\GpuOverlayBench.exe `
  -Output tools\GpuOverlayBench\bin\local-pairs -Pairs 3 -Seconds 12 -Lane hover

python tools\OverlayBench\Summarize.py tools\GpuOverlayBench\bin\local-pairs
```

The direct executable syntax is:

```text
GpuOverlayBench <new-output> <control|gpu> <hover|monitors|brightness|color|idle|off> <seconds>
```

Use positive durations, valid listed lanes, and serial runs only. Both arms use
the same host, production methods and time-based trace, with a moving 15-second
warmup after initial settling and image checks. The host claims the normal app's
mutex, does not move the OS cursor, does not run the updater, and never saves
settings. It creates real topmost overlay windows: do not run it during a meeting
or visual task. The driver bounds each process, checks settings hashes, captures
external DWM/GPU counters and performs only PID-specific cleanup. Do not remove
capture exclusion or change app settings to obtain screenshots.

Recorded measurements used frozen `bin\measured-v2`. The current `Program.cs`
adds only a fail-closed, premeasurement assertion that real cursor polling remains
stopped. Its separate `bin\evidence\poll-assertion` control run passed. The measured
source is preserved in `bin\evidence\source-measured-v2` and its C# hashes are
checked against the original portable PDB.

## Verification

```powershell
# Dataset-specific: checks the preserved 27 runs, 44 native windows,
# upload/readback counts, settings, prior artifacts and measured source hashes.
python tools\GpuOverlayBench\Verify-Evidence.py

dotnet test WindowsEdgeLight.Tests\WindowsEdgeLight.Tests.csproj `
  -c Release --no-restore

dotnet build tools\GpuOverlayLifetime\GpuOverlayLifetime.csproj -c Release `
  -p:RestoreLockedMode=true
```

`Verify-Evidence.py` deliberately checks the frozen local dataset and exact build
hashes; it is not a generic assertion that new GPU results pass the acceptance
bar. It reports measured acceptance failures separately from evidence corruption.

`GpuOverlayLifetime` links the same GPU implementation and accepts:

```text
GpuOverlayLifetime <generated-production-png> <new-result-json> <1..20 cycles>
```

Use a generated `production-no-hole.png` matching the current primary display,
not a desktop screenshot. This test repeatedly creates, renders, reads back and
disposes the native device, window and surfaces. It is not a hotplug/device-loss
or complete production toggle test.

## Before any production integration

Resolve responsive exact color rendering and sustained/mixed-monitor p90 first.
Then validate brightness cadence/rounding, hole-edge antialiasing, real input
click-through, z-order, capture applications, hotkeys, controls, monitor selection,
toggle/close behavior, resize/DPI changes, hotplug and device recovery. The
prototype retains hidden WPF sources and substantial memory; it does not
demonstrate production memory savings. ARM64 and other GPUs are unvalidated.

API references used for the implementation:

- [CreateSwapChainForComposition](https://learn.microsoft.com/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgifactory2-createswapchainforcomposition)
- [Extended window styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles)
- [SetWindowDisplayAffinity](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)
