# Overlay rendering experiments

**The 2026-10-29 hill-climb found no qualifying winner.** Production source was
restored to `b927edd529ce97bfa729dfc0a6f185002c4878df` (v1.14.0).
Only this measurement tool is left as a source change. No commits, pushes, PRs,
tags, or merges were made.

## What this measures

This Windows/.NET 10 WPF console harness references a **fresh, separately built**
production assembly. It creates the actual `MainWindow` XAML and production
additional-monitor windows, and invokes their `HandleMouseMove` method with a
deterministic, wall-clock-based trace. It does not generate OS mouse input.
Reflection is deliberate: the pinned control needs no measurement-only source
changes. Changing production internals may require updating this harness.

All connected monitors are rendered, at their discovered coordinates and DPI.
The heavy lane sweeps the primary monitor's top edge continuously. It does not
replace a fast trace with tiny motions to favor a threshold optimization.
The synthetic dispatcher timer uses the production polling interval.

The harness:

- refuses to coexist with the app or another harness via the existing mutex;
- shows its windows without activation; **the edge light is visible**;
- does not run `App.OnStartup` (no updater/network request or update modal);
- does not write user settings, use global cursor input, or capture the desktop;
- validates transparent pixels at two distinct hole centers before timing;
- records actual visible HWND geometry, DPI, styles, and capture affinity;
- settles startup, snapshots, GC, and trace warmup before measuring;
- samples process CPU as **CPU milliseconds / wall milliseconds × 100**:
  percent of **one logical core**, not of the whole computer;
- records tick cadence, handles, USER/GDI objects, private bytes, and working set;
- closes its windows and disposes resources on success and failure.

`RenderTargetBitmap` screenshots contain only the generated app visual and
transparency, never the user's desktop. They prove the visual-tree output, not
physical display presentation. Real desktop input, hotkeys, dragging, hotplug,
capture applications, and device-loss behavior remain separate E2E tests.

The optional archived `EdgeStripPresenter` prototype is instantiated by the
harness if present in the referenced app assembly. It is **not part of the final
production source**, nor a finished production integration.

## Reproduce a control run

From the repository root in PowerShell, with .NET 10 installed:

```powershell
dotnet build WindowsEdgeLight\WindowsEdgeLight.csproj -c Release -t:Rebuild `
  -o tools\OverlayBench\bin\my-control -v:q
if ($LASTEXITCODE -ne 0) { throw "Renderer build failed" }

$renderer = (Resolve-Path tools\OverlayBench\bin\my-control).Path
dotnet build tools\OverlayBench\OverlayBench.csproj -c Release -t:Rebuild `
  "-p:RendererDirectory=$renderer" -o tools\OverlayBench\bin\my-harness -v:q
if ($LASTEXITCODE -ne 0) { throw "Harness build failed" }

tools\OverlayBench\bin\my-harness\OverlayBench.exe `
  tools\OverlayBench\bin\my-single-run hover 12
```

Outputs must be new directories; the tools refuse overwrites. Other lanes:
`monitors` (use at least 32 seconds to cover four monitors), `brightness`,
`color`, `idle`, and `off`. These exist for validation but **were not all run in
this rejected experiment**. The production updater is not disabled or changed.

### Serial paired runs

```powershell
tools\OverlayBench\Run-Pairs.ps1 `
  -Control tools\OverlayBench\bin\my-harness `
  -Treatment tools\OverlayBench\bin\candidate-harness `
  -Output tools\OverlayBench\bin\new-pairs -Pairs 3 -Seconds 12

python tools\OverlayBench\Summarize.py tools\OverlayBench\bin\new-pairs
python tools\OverlayBench\Verify-Evidence.py tools\OverlayBench\bin\new-pairs
```

For A/A, pass the same harness directory to both parameters. The runner
alternates AB/BA ordering, runs only one benchmark process at a time, captures
DWM CPU and per-process GPU engine counters externally, bounds each run, and
uses only PID-specific cleanup. It hashes both settings files before/after;
unexpected changes fail without overwriting a possible concurrent user edit.
External counters are restricted to the timed interval when summarized.
DWM includes unrelated desktop activity. GPU engine percentages must not be
added to CPU percentages, nor interpreted as total-device GPU utilization.
Unavailable counters are reported as missing/errors, not zero.

`Summarize.py` requires only Python's standard library. Its 95% bootstrap
interval uses paired **run means**, not correlated one-second samples. With
only three pairs, intervals and p90s are exploratory rejection evidence, not
headline claims or a keep decision.

## Recorded outcome

Local raw evidence, registered contract, ledger, source snapshots, build
manifest, and full report are under `bin\evidence`. This directory is ignored
by Git; preserve it before cleaning build output.

| Arm, in requested order | Control p50 / p90 | Candidate p50 / p90 | Decision |
|---|---:|---:|---|
| 2: thin nonoverlapping edge windows, final pilot | 28.03 / 39.33% | 9.26 / 17.87% | Reject: p90 fails <10% |
| 3: GPU composition | — | Not measured | Blocked: separate native backend redesign |
| 1: 2-DIP movement threshold | 29.57 / 44.13% | 31.77 / 42.18% | Reject: no reliable improvement |

Each timed treatment pilot had three complete pairs, 12 seconds per run,
36 one-second samples per arm. The final A/A instrument had three pairs/six
runs; maximum absolute paired difference was 0.817 percentage points.
No arm passed the pilot, so no six-pair keep or ten-pair headline claim was made.
The strip prototype still had small corner-antialiasing differences and lacked
full production lifecycle integration. Reduced median CPU alone is not a win.

Production already reports WPF hardware tier 2 and measurable GPU activity.
Merely enabling WPF GPU rendering again is not a genuine option 3. Replacing
layered-window presentation would require a native composition target and
new rendering/device-loss/resource-lifetime integration. That work was not
silently treated as implemented or successful.
