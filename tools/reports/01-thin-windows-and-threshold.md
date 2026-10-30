# WindowsEdgeLight CPU hill-climb — 2026-10-29

## Verdict

**No candidate qualifies for a winning PR.** Do not publish a performance fix
from this experiment. Local branch: `perf/overlay-hillclimb-v1.14`, still based on
`b927edd529ce97bfa729dfc0a6f185002c4878df`. No commits, pushes, PRs, merges, or
tags were made. PR214 and its reminder were not touched.

Production files are byte-for-byte restored. A fresh restored build has exactly
the original control DLL SHA256. The only remaining source additions are
`tools\OverlayBench\{OverlayBench.csproj,Program.cs,Run-Pairs.ps1,Summarize.py,Verify-Evidence.py,README.md}`.
The benchmark and evidence are useful deliverables, not a shippable renderer.

## Registered contract and instrument

`contract.json` was written before treatment changes. It registers p50 **and**
p90 below 10% of one logical core, unchanged appearance/responsiveness and
behavior, serial arms 2→3→1, A/A noise first, three-pair pilots, six pairs/two
rounds for keep, ten pairs for headline claims. No keep/headline claim is made.

Control is freshly rebuilt v1.14.0, **not** the older hook implementation, old
session baseline folder, or provisional ~42% hover number.

The harness instantiates the actual production MainWindow XAML and additional
monitor windows. It stops only real cursor polling, injects a synthetic trace
through production `HandleMouseMove`, and renders actual layered HWNDs. It
does not launch `App.OnStartup`, move the OS cursor, call settings Save, or
automate app UI through shell/UIA. It renders all monitors and continuously
sweeps the primary top edge, at the same timer interval and wall-clock trace
for both arms. Every run verifies that the hole is transparent at two distinct
expected centers and opaque where the previous hole used to be.

Hardware discovered: Windows, .NET SDK 10.0.403, 32 logical processors, NVIDIA
GeForce RTX 4080 SUPER driver 32.0.16.3020, WPF render tier 2.

| Display | Working area, physical pixels | Scale |
|---|---|---:|
| Primary DISPLAY1 | (0,0), 3840×2488 | 150% |
| DISPLAY2 | (931,2560), 1920×1220 | 125% |
| DISPLAY3 | (-3840,5), 3840×2488 | 150% |
| DISPLAY4 | (3840,0), 3840×2488 | 150% |

Real original-control startup was checked through computer-use. Its
**Update Available** dialog was observed, **Remind me later** clicked, and a
new window listing confirmed that the dialog was gone. Its actual overlay
geometry and Controls accessibility tree were observed. It was exited using
the Controls Exit button. No live cursor sweep was performed; this is only a
partial live-control cross-check, not real-input performance validation.

### Instrument revisions, retained honestly

Early pilots exposed two measurement defects: a `CompositionTarget.Rendering`
subscription kept rendering unnecessarily; later, dynamic binding in READY
logging and live snapshot objects contaminated first samples. Those probes and
the first A/A set are retained but **excluded** from conclusions.

The final instrument removed the Rendering subscription, warmed and released
snapshots/GC before timing, removed timed dynamic binding, recorded explicit
timed UTC bounds, and reran all six A/A runs before final treatment pilots.
`Program.frozen.cs` and `OverlayBench.frozen.csproj` preserve that source.
Each arm's binary was recompiled against its own referenced renderer, so its
binary harness hash differs; the harness source is identical.

The final valid A/A pair deltas (second control minus first) were
**-0.0274, +0.8169, -0.0395 percentage points**. Maximum absolute paired
difference: **0.817pp**. Run means: **32.44/32.41, 35.15/35.97, 32.47/32.43%**.

## Results (pilot rejection evidence, not headline claims)

All CPU percentages are **process CPU milliseconds / elapsed milliseconds ×
100**, i.e. one logical core. They are not divided by 32.

Each treatment pilot: three paired/interleaved AB/BA/AB pairs, 12 seconds per
run, 36 one-second CPU windows per arm. p90s and n=3 pair-bootstrap intervals
are exploratory. CI resamples paired run means, not correlated CPU windows.

| Arm | Control p50/p90 | Treatment p50/p90 | Paired mean delta 95% bootstrap CI | Verdict |
|---|---:|---:|---:|---|
| 2, shared-source thin windows v6 | 29.71 / 41.08% | 7.81 / 18.62% | -24.57 to -19.71pp | Reject p90 |
| 2, suppress original empty hosts v7 | 28.03 / 39.33% | 9.26 / 17.87% | -20.07 to -18.79pp | Reject p90 |
| 3, GPU composition | — | Not measured | — | Blocked by redesign scope |
| 1, sub-2-DIP movement threshold | 29.57 / 44.13% | 31.77 / 42.18% | -2.34 to +8.54pp | Reject |

Final arm2 paired weighted run means, control→candidate:
**28.56→8.49%, 29.09→9.56%, 29.28→10.49%**.
Arm1: **30.45→28.12%, 28.95→37.49%, 29.42→32.24%**.

No arm passed its pilot, so no additional keep/confirmation rounds were run.
This stops failed candidates; it does not lower the six-pair keep bar.

### Arm 2 implementation and visual findings

The prototype moves each existing cached production glow into a shared,
full-monitor, explicitly bounded VisualBrush source. Four nonoverlapping
176-DIP edge surfaces own top/bottom corners and left/right interiors. Native
pixel boundaries are integer-aligned. Only intersecting strip clips move.
Brightness still binds to the original GlowHost; the original geometry,
gradient, DropShadowEffect and 70-DIP hole are reused. Sixteen strip HWNDs plus
four original owners are created for this machine.

Prototype iterations exposed and fixed substantial brush-origin, scaling and
halo-clipping errors before the paired pilots. Final generated images preserve
the halo, placement and seams, but are **not pixel-identical**: the v6 comparison
had mean absolute alpha difference 0.011/255, with 4,269 pixels differing by
more than 2 alpha levels, mainly corner/border antialiasing (max61). V7 changes
only suppression of empty source hosts. These differences are not waved away
as a validated zero-regression result.

Even after suppressing invalidation of the empty original host, p90 remains
17.87%. The prototype is therefore archived in
`arm2-rejected\EdgeStripPresenter.cs`, not integrated into production.
Monitor switch/hotplug/resize/device lifecycle integration was intentionally
not undertaken after it failed the performance bar.

### Arm 3 scope barrier

WPF is already hardware tier2, with measured app GPU activity around12% of the
active3D engine in the control. Enabling existing WPF GPU defaults is not an
independent treatment. A real option3 requires replacing layered-window
presentation with a native composition target/shared GPU surfaces and
reimplementing exact glow/hole drawing, DPI/resize/device-loss behavior,
click-through/topmost/capture handling and COM/resource lifetime.

No existing DirectComposition/Direct2D backend or graphics dependency exists
in this repo. The impact was reported before undertaking an unbounded rewrite.
No native arm was built or measured; **BLOCKED_DESIGN_SCOPE is not a measured
performance rejection or a successful attempt**. No PR214 native code was
imported. Proceeded to option1 in the requested order.

### Arm 1

Kept the33ms polling interval and skipped only hole-center changes shorter
than2 DIPs. Fast continuous motion still traverses the actual renderer every
tick. It showed no reliable reduction and remains far over10%.
`arm1-threshold.patch` preserves the exact change. The experimental hunk was
then reversed with apply_patch. A lower redraw rate was not substituted to
manufacture a win by worsening responsiveness.

## Compositor and GPU cost

Direct DWM Process.TotalProcessorTime access was denied; that field is null,
not zero. The external runner successfully sampled Windows performance
counters and restricted summaries to the timed interval. Means below average
the three run means. DWM covers the shared desktop, not just this application.
GPU numbers sum this process's active3D engine counters and are not total-device
utilization or directly additive to CPU.

| Final pilot | DWM CPU control→candidate, one-core% | App3D engine% | DWM3D engine% |
|---|---:|---:|---:|
| Arm2 v7 | 26.63→21.59 | 12.16→2.06 | 11.11→9.55 |
| Arm1 | 29.49→28.57 | 12.29→11.91 | 10.93→10.95 |

The strip result was not presented as zero compositor cost.

## Guards, tests and limits

- Fresh production build and restored production build: **0 warnings/errors**.
- Existing unit tests: **13/13** before and after the hill-climb.
- `Verify-Evidence.py`: **18/18 saved runs pass** necessary window visibility,
  topmost/layered/click-through style flags, capture affinity17, hole-center
  pixels, and trace-count checks. All16 strip rectangles pass nonoverlap and
  pixel-boundary coverage assertions at125/150% DPI, including negative coords.
- Same actual cadence: approximately255–258 trace ticks per12seconds; timer
  scheduling on this machine is roughly47ms despite the nominal33ms setting.
  Final strip run p90 intervals48.26–51.69ms; corresponding controls49.71–55.82ms.
  No frozen or removed hole was accepted.
- GDI delta0 in all18 checked runs; USER count usually-1 as tooltip/icon
  initialization settles. Handles/private memory vary; the raw start/end
  values are retained. **Twelve seconds is not a leak soak.**
- Both user's settings files have unchanged before/after hashes for every
  completed paired batch. No stale backup was restored.
- No benchmark or test app processes remained after cleanup.

Not validated: functional click-through under another app, sustained z-order
stress, live input/hover latency, brightness/color dragging, keyboard hotkeys,
toggle/off long-term CPU, single/all monitor selection UI, real capture apps,
physical hotplug/device loss, extended leak soak, ARM64 execution, other GPUs.
The harness supports several additional lanes, but failed candidates did not
earn this full regression campaign. Window flags and generated snapshots are
not substitutes for those E2E tests. No zero-regression claim is made.

## Reproducibility / preserved artifacts

See `..\..\README.md` for exact build/run commands. Summaries can be regenerated:

```powershell
python tools\OverlayBench\Summarize.py tools\OverlayBench\bin\evidence\aa-final
python tools\OverlayBench\Summarize.py tools\OverlayBench\bin\evidence\arm2-suppressed-original-pilot
python tools\OverlayBench\Summarize.py tools\OverlayBench\bin\evidence\arm1-pilot
python tools\OverlayBench\Verify-Evidence.py tools\OverlayBench\bin\evidence\aa-final tools\OverlayBench\bin\evidence\arm2-suppressed-original-pilot tools\OverlayBench\bin\evidence\arm1-pilot
dotnet test WindowsEdgeLight.Tests\WindowsEdgeLight.Tests.csproj -c Release --no-restore -v:q
```

`build-manifest.json` records exact paths, timestamps, lengths and SHA256 hashes.
Key renderer DLL hashes:

- Control/restored: `E54BD5BEEBB374E095538F6D269FED92293D26946FEB2A06B84FE1438A3961E2`
- Arm2 v7: `6CD3985175FFB07877F5CE5DA736E0505AC8287222973CBF84C7200FB6D9E330`
- Arm1: `E19A637BBA0182800B84A5688E965F89014CD16C44FD4E3B45096812F0EBCA04`

Retained ready-to-run harnesses:
`bin\harness-control-final`, `bin\harness-arm2-v7`, `bin\harness-arm1`.
Each directory contains its specific renderer DLL. The raw per-run metadata
also records renderer/harness hashes and the exact process ID, preventing stale
or wrong-process comparisons. Raw outputs include untouched samples, timing
intervals, resource snapshots, external counters, generated PNGs and logs.

**Preserve `bin\evidence` before build-output cleanup.** It is intentionally
ignored by Git. All screenshots there are synthetic app visuals; no old private
desktop screenshots were used or copied. The parent can move the evidence
bundle into its session artifact storage. Nothing here authorizes publication.
