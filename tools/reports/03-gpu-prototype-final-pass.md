# Bounded GPU follow-up: measured result and implementation gaps

## Verdict

**No production integration or winning PR.** Both requested hypotheses were tested.

* **Reject the translated-mask optimization:** it changes native antialiasing and
  did not consistently improve the diagnostic CPU tails.
* **Keep the corrected instrument and hidden-WPF isolation for research.**
  Six fresh control/GPU pairs, sustained hover and mixed-monitor runs show low
  typical CPU, but one short GPU run still exceeded the strict p90 bar.
* **The cached exact-color representation works in this prototype.** Actual GPU
  bytes match production in 40 temperature/monitor comparisons. The old
  10.398-second color callback is replaced by 243 synchronous requests in a
  12-second timed run, maximum callback 9.937 ms, with the final requested color
  exact on all four GPU surfaces. This is **warm-cache steady state**, not a
  startup-free production fix: generating two caches took 309.932 seconds.
* Color/brightness callback cadence, existing WPF/native opacity and hole-edge
  differences, startup cost, resource retention, and desktop E2E gaps still block
  a “below 10%, without regressions” claim. No further tuning was started.

All implementation is in `tools\GpuOverlayIteration`. Production remains at
`b927edd529ce97bfa729dfc0a6f185002c4878df` on local branch
`perf/overlay-hillclimb-v1.14`. There are no commits, pushes, PRs or merges.
The existing production tests pass **13/13**.

## Frozen inputs and instrument

| Item | SHA256 |
|---|---|
| Pinned v1.14.0 `WindowsEdgeLight.dll` | `E54BD5BEEBB374E095538F6D269FED92293D26946FEB2A06B84FE1438A3961E2` |
| Follow-up `bin\shader-v3\GpuOverlayIteration.dll` | `1D87E81700790AF7CA9CDD9DB26BC22F225F51D18D03D4342924BE5755C13AAE` |
| Preserved first GPU `measured-v2\GpuOverlayBench.dll` | `AFDEEB00E154849C6326AFFF8718039B66B95C65464BD1EAD6B2623B459087B5` |

`bin\shader-v3\source` contains the exact C# source snapshot copied before the
successful build. All current C# sources for that frozen formal host still match
it. Builds use .NET SDK
10.0.403 and Vortice 3.8.3, including D3DCompiler only in this experimental project.
The frozen host built with zero warnings/errors. The source/build and evidence
manifests retain hashes, sizes and timestamps.

Hardware is the same NVIDIA RTX 4080 SUPER, D3D feature level 11_1, 32 logical
processors, four monitors: three 3840×2488 working areas at 150% and one 1920×1220
at 125%, including negative desktop coordinates. No WARP fallback was used.

CPU is **percent of one logical core**, not Task Manager's whole-machine
normalization: process CPU milliseconds / elapsed milliseconds × 100.
Nominal input cadence remains 33 ms; observed intervals are retained. The source
cursor poller is explicitly stopped and verified so it cannot fight the synthetic
trace. Both arms use a moving 15-second warmup. No OS mouse movement, settings
save, updater modification, or direct external-app UI automation is used.

The final host disables profiling, uses the native lightweight sampler in **both**
arms, and uses original native masks, detached unused WPF source roots, and the
color lookup in the GPU arm. The actual production input/geometry/setter methods
still run. Final/reference rasterization and GPU readback occur outside timing.

This is a new matched-host comparison against pinned WPF, **not** a paired
estimate of incremental improvement over the first GPU prototype. Correcting
sampler overhead is not credited as a production renderer optimization.

## 1. Profiling and the hover-tail hypothesis

The 60-second `diagnostic-original` trial used per-phase measurements, native
per-thread CPU, allocation/GC counters, and a 45-second process-scoped
`dotnet-trace` capture. Its supported Windows profile samples **wall-time stacks**,
not CPU stacks. An earlier unsupported `cpu-sampling` attempt is preserved under
`v2-process-profile`; its owned process was stopped and it is not a valid trial.

| Original diagnostic phase | Total wall ms | Maximum call ms |
|---|---:|---:|
| Production input | 52.205 | 0.439 |
| Native geometry | 64.901 | 0.258 |
| D2D draw/EndDraw | 239.584 | 1.441 |
| Present | 83.078 | 0.300 |
| Old resource sampler, 61 calls | 467.489 | 8.563 |

Geometry allocated 112,112 managed bytes; production input allocated 805,168
bytes. Total measured allocation was about 16.6 MB, with one Gen0 collection and
no Gen1/Gen2 collections. High-CPU windows occurred without GC. For example, the
29th sample was 15.406% CPU with 93.750 ms on the UI thread and 46.875 ms across
two NVIDIA driver threads, while the measured geometry/draw/present wall totals
for that window were only 1.139/3.794/1.377 ms.

The registered WPF render thread accumulated zero CPU in this run; the UI thread
and largest NVIDIA worker accumulated 1,093.750 and 421.875 ms respectively.
Threads created after initial registration are absent from this breakdown.
The evidence does **not** identify every out-of-scope UI/driver activity or prove
one exclusive cause. It does rule against large present waits or managed native
mask allocation being the dominant measured bottleneck.

Six serial 30-second ablations used the `profile-v2` diagnostic build:

| Diagnostic variant | CPU p50 / p90 | Geometry total ms | Draw total ms |
|---|---:|---:|---:|
| Lightweight sampler only | 1.555 / 7.963 | 32.06 | 118.46 |
| Lightweight + cached mask | 1.551 / 12.663 | 0.13 | 212.42 |
| Lightweight + detached WPF | 0 / 6.319 | 33.86 | 120.93 |
| Lightweight + both | 0 / 9.463 | 0.12 | 214.41 |

These are single diagnostic observations, not acceptance comparisons or additive
effect estimates. The corrected resource sampler took 1.27 ms total in the
first row, versus roughly 7.7 ms **per call** in the old sampler. Detachment
reduced production input phase work and UI CPU, but does not eliminate driver
bursts.

Both translated-mask variants differed from the original native mask by up to
**60/255**, with 601 differing alpha pixels in each tested edge-hole snapshot.
Reusing a whole-circle complement and translating it is not equivalent to the
original finite-window geometry combination/antialiasing. It was rejected;
all formal trials use the original native mask.

Old-sampler and lightweight-sampler off diagnostics had mean CPU 1.245% and
0.626%, with zero trace ticks. This helps expose the measurement floor; it is
not a randomized paired idle-performance claim.

## 2. Faithful color representation

The production middle gradient stops and shadow use temperature RGB; gradient
endpoints remain white. Interpolating black/white basis rasters was tested, not
assumed equivalent. On the large raster it differed by up to **37/255** in
1,345,265–1,392,073 pixels across the tested temperatures. The actual color/effect
response is not represented faithfully by that affine tint; that approach was
rejected.

The accepted **experimental representation** records the production grayscale
channel response for every byte 180–255. The builder checks channel symmetry and
color-independent alpha, groups identical response signatures, and stores a
class/alpha map plus a small lookup table. A D3D11 pixel shader selects the three
responses using the **production method's byte RGB**, including its truncation
semantics, rather than approximating temperature in the shader. White endpoints,
blur, quantization and premultiplication come from the production rasters.

| Raster | Classes | Map bytes | Table bytes | Cache construction |
|---|---:|---:|---:|---:|
| 3840×2488 / 150% | 1,971 | 38,215,680 | 157,680 | 227.535 s |
| 1920×1220 / 125% | 3,763 | 9,369,600 | 301,040 | 82.397 s |

Two cache files total 48,044,228 bytes including headers. Three large monitors
share the same disk cache identity, but this prototype uploads separate GPU
resources per window: 124,790,720 cache bytes total. Cache construction times
exclude the surrounding equivalence tests; they must not be presented as complete
startup times or hidden off-budget work.

CPU reconstruction and actual GPU backbuffer checks both passed at temperatures
0, .125, .25, .37, .5, .75, .875, 1, and .4±1e-8 on **all four monitors**:
40 comparisons each, maximum byte error zero. These are tested values, not an
exhaustive proof over every possible temperature/driver. The separate pixel
proof's four-monitor synchronous submission times ranged 2.713–7.514 ms.
Submission/callback timings are **not measured display latency**.

In the timed color guard:

* 243 requests reached each surface; 228 changed production byte RGB.
* The other 15 requests had identical byte RGB; no input was throttled, queued
  for later replacement, or asynchronously dropped.
* All changed colors were drawn, and the primary moving hole was drawn on every
  tick. There were **zero timed image uploads or readbacks**.
* Callback p50/p90/max was **3.326 / 6.978 / 9.937 ms**.
* Final requested temperature `0.02083393939393924`, RGB `DCEAFD`, matched actual
  GPU pixels exactly on every monitor after timing.

Brightness/hole output at .2, .5, .82 and 1 was byte-identical between the new
lookup and the original native rendering of the exact production raster.
That is not full WPF equivalence: at brightness .5, 2,091,922 alpha pixels differ
from WPF (including rounding differences), and the hole boundary still reaches
48/255 error at full brightness. These native-renderer gaps were not waived.

## Fresh calibration and formal measurements

Fresh A/A: six runs / three pairs, paired weighted-mean differences
−3.513, −2.897, +0.467 percentage points; maximum absolute difference **3.513 pp**.
Both arms used the same frozen host and corrected sampler.

The hover pilot was three pairs; the second round added three complete pairs.
There were no failed/missing formal runs. Actual durations and samples are
retained: nominal 12-second runs sometimes require the next dispatcher sample
boundary, giving 12–13 seconds. CPU means are elapsed-time weighted per run.
The GPU does not get a cheaper or reduced-rate trace.

| Lane | Control CPU p50 / p90 | GPU CPU p50 / p90 | Samples C / G |
|---|---:|---:|---:|
| Six hover pairs, two rounds | 34.104 / 40.627 | 0 / 8.575 | 74 / 76 |
| Sustained hover, requested 120 s | 34.662 / 42.070 | 1.569 / 7.887 | 120 / 121 |
| Mixed-monitor trace, 32 s | 31.209 / 55.804 | 0 / 6.255 | 32 / 32 |
| Color dragging | 75.347 / 83.016 | 1.561 / 25.528 | 12 / 12 |
| Brightness dragging | 81.441 / 90.529 | 3.102 / 16.343 | 13 / 13 |
| Light off | 0 / 1.246 | 0 / 1.566 | 13 / 12 |

All percentages are one-core units. Zero medians reflect quantized short-window
CPU accounting, **not zero work**. GPU mean CPU is 2.261% for the six short hover
runs, 3.333% for the soak, and 2.541% for mixed monitors.

The six paired weighted-mean hover deltas have bootstrap 95% CI
**−33.123 to −29.805 pp**. Resampling is over six paired run means, not correlated
one-second samples. There are fewer than ten pairs: no headline/generalized
performance claim is made. Small-n p90s and the one-pair guard lanes are exploratory.

Individual short GPU hover p90s were **7.525, 7.524, 5.962, 7.532, 8.835,
12.055%**. Thus, although pooled, sustained and mixed-monitor p90s pass 10%, the
strict tail goal is not consistently met. The mixed trace also contains a
29.754% one-second maximum; “p90 below 10” does not mean every interval is below 10.

Responsiveness remains an acceptance gap:

| Lane | Control tick p90 | GPU tick p90 | Control / GPU ticks |
|---|---:|---:|---:|
| Color | 55.900 ms | 61.616 ms | 252 / 243 |
| Brightness | 49.025 ms | 61.985 ms | 277 / 260 |

The nominal rate was not lowered, but native callback work stretches observed
cadence. Actual presented-frame/input-to-display latency was not measured, so
these data do not establish preserved responsiveness.

## Compositor and resource costs

External hover means of run means:

| Counter | Control | GPU |
|---|---:|---:|
| DWM CPU, one core | 2.944% | 3.069% |
| App summed 3D-engine utilization | 27.756% | 2.918% |

The hover comparison has only 22 control and 21 GPU timed counter observations.
Its 11 collection errors are timestamped and outside the selected timed windows.
All formal batches retain raw errors; the analyzer excludes error rows, not their
existence. A separate timestamp audit finds **27 errors across all 28 processes,
zero inside any complete measured window** (`counter-errors.json`).
Reported DWM 3D values were zero: that is **not proof of zero compositor
GPU cost**. These are sparse per-engine counters, not total physical-GPU or
per-frame energy measurements. Only DWM PID 2692/session 1 was observed at cleanup.

During GPU soak, GDI stayed 127, handles fell 1010→989, and private bytes were
1,872,850,944→1,872,515,072 (sample range 1,869,807,616–1,873,313,792).
Mixed-monitor private bytes were 1,960,845,312→1,961,267,200 with GDI 127.
Off had zero ticks/draws/uploads/readbacks, but mean CPU was 0.522% versus control
0.240% in one short pair. Normal light-on idle polling is not proven here.

Ten native color-resource recreation cycles completed, checking all four
monitors' actual pixels and necessary window guards each cycle:

* Handles: 407 after cycle 1, 413 through cycle 7, 414 for cycles 8–10.
* GDI/USER: 7/15 throughout those cycles.
* Private bytes: baseline 64,733,184; cycle 1 985,157,632; peak 1,048,334,336;
  final 1,035,440,128.

This is functional recreation evidence with a large retained footprint, **not**
proof of leak freedom, return to baseline, or production hotplug/disposal safety.
Dedicated/shared GPU-memory usage was not independently profiled.

## Remaining production investment / limits

1. The finite-response method demonstrates faithful steady-state color on this
   configuration, but cold preparation takes over five minutes, caches are tied
   to geometry/DPI/content, and resources are duplicated across same-size
   monitors. Startup, invalidation and memory ownership need a deliberate design.
2. The original native hole AA and opacity rounding differ from WPF. The tested
   translated-mask shortcut makes AA worse. Neither is production-equivalent.
3. Callback cadence and occasional CPU tails still need work; attributing all
   remaining UI/driver CPU would require deeper native CPU-stack investigation.
   No further prototype hypothesis or broad rewrite was started.
4. Physical cross-process click-through, actual capture exclusion, visual
   desktop comparison, real cursor interaction, hotkeys, toggle/close wiring,
   single/all-monitor selection, hotplug/DPI changes, device loss, HDR/color
   management, other adapters and ARM64 execution are not validated/integrated.
   Necessary native style, visibility, hit-test, DPI/rect and affinity-17 checks
   do pass, but cannot substitute for these E2E tests.
5. This remains a controlled synthetic workload on a shared live Windows desktop,
   not a dedicated performance lab or production-traffic result.

## Evidence, verification and reproduction

Relevant files under `tools\GpuOverlayIteration`:

* `Program.cs`, `GpuRenderer.cs`: frozen-host source, original/cached mask
  ablations, retained-root isolation, actual rendering/pixel/counter guards.
* `Diagnostics.cs`, `Analyze-Diagnostics.py`: phase, allocation, native-thread CPU
  and resource-sampler attribution.
* `ColorResponse.cs`, `ColorShader.cs`: tested basis rejection, finite response
  construction, validated cache loading and real D3D11 shader.
* `Run-Pairs.ps1`, `Analyze-Pairs.py`, `Verify-Evidence.py`: serial paired driver,
  timestamped external data, analysis and integrity/workload checks.
* `GpuOverlayIteration.csproj`, `packages.lock.json`, `dotnet-tools.json`, `README.md`.

All relative evidence paths below are under `bin\evidence`:

* `contract.json`, `ledger.json`: registered bar, scope, decisions.
* `diagnostic-original`, `causal-hover-summary.json`, `causal-off-summary.json`,
  `spike-attribution.json`: attribution and rejected-mask evidence.
* `color-proof`, `gpu-color-proof`, `color-pair\pair-01-treatment\final-color-proof.json`:
  actual CPU/GPU equivalence and exact trailing color.
* `fresh-aa-summary.json`, `hover-six-pair-summary.json`, `*-pair-summary.json`:
  summaries; corresponding batch directories retain raw results, intervals,
  captures, logs, settings hashes and external counter rows.
* `color-lifetime\lifetime.json`, `verification.json`,
  `tests\iteration-production-tests.trx`, `final-state.json`.
* `build-manifest.json`, `evidence-manifest.json`: this iteration's artifacts.

The verifier passes **28 frozen timed runs**, 40 CPU color comparisons, 40 GPU
temperature comparisons, four native brightness equivalence checks, ten
recreation cycles and 13 existing tests. It also checks unchanged settings,
empty production diff, no remaining renderer processes, **343 preserved
first-pass evidence files and 92 preserved first-pass source/build files**.
Passing integrity/workload checks is deliberately not a keep verdict.

Reproduce on this display configuration using a new output directory each time:

```powershell
dotnet restore tools\GpuOverlayIteration\GpuOverlayIteration.csproj --locked-mode
dotnet build tools\GpuOverlayIteration\GpuOverlayIteration.csproj --no-restore -c Release -o tools\GpuOverlayIteration\bin\repeat-host
$env:GPU_PROFILE='0'
$env:GPU_LIGHT_SAMPLER='1'
$env:GPU_CACHE_MASK='0'
$env:GPU_DETACH_WPF='1'
$env:GPU_COLOR_CACHE=(Resolve-Path tools\GpuOverlayIteration\bin\evidence\color-proof).Path
& tools\GpuOverlayIteration\bin\repeat-host\GpuOverlayIteration.exe tools\GpuOverlayIteration\bin\repeat-pixels gpu gpu-color-proof 1
& tools\GpuOverlayIteration\Run-Pairs.ps1 -Executable tools\GpuOverlayIteration\bin\repeat-host\GpuOverlayIteration.exe -Output tools\GpuOverlayIteration\bin\repeat-aa -TreatmentMode control -Pairs 3 -Seconds 12
# Only then run separate pilot/confirmation batches, never concurrently.
python tools\GpuOverlayIteration\Verify-Evidence.py
dotnet test WindowsEdgeLight.Tests\WindowsEdgeLight.Tests.csproj --no-restore -c Release
```

The verifier intentionally targets the archived shader-v3 dataset/hash, not an
arbitrary rebuilt executable. To regenerate color caches, unset
`GPU_COLOR_CACHE` and run the `color-proof` lane into a fresh directory first.
No new captures of the user's desktop are included.

The requested Reachy completion notification was attempted but failed; the helper
also hit a console-encoding error while reporting its failure. Its log is retained.
No robot services were restarted or changed.
