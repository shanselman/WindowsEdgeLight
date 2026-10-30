# Option 3: measured native GPU prototype, not a production winner

## Decision

**Complete as a bounded local prototype; reject for production integration now.**

Actual hardware DirectComposition/D2D rendering works and greatly reduces the
controlled hover CPU cost. The API/backend is no longer an unattempted or
hypothetical blocker. However, the strict under-10% p50/p90 bar is **not robustly
met**, and exact color dragging has a severe measured regression. No production
PR should be represented as ready from this result.

This follow-up supersedes only the earlier option-3 “not attempted/rewrite”
conclusion. It does not rewrite the prior report, frozen WPF harness, control,
thin-window rejection, or threshold rejection. The actual chronology was option
2, option 1, then this explicitly requested option-3 follow-up; the originally
requested order was not fulfilled at the initial stage.

Local branch: `perf/overlay-hillclimb-v1.14`.
HEAD: `b927edd529ce97bfa729dfc0a6f185002c4878df` (v1.14.0).
Production source is unchanged. No commits, pushes, PRs, merges, releases, tags,
worktrees, nested agents, or reminder changes were made for this prototype.

## Instrument and provenance

- CPU units: process CPU milliseconds / elapsed milliseconds × 100, **one logical
  core**, not divided by this machine's 32 logical processors.
- Both arms execute the frozen production MainWindow/additional-monitor windows,
  production cursor-hole geometry and color/brightness methods. The host supplies
  identical time-based coordinates instead of moving the user's mouse.
- Control renders actual WPF windows. GPU renders real native composition HWNDs,
  reading the ellipse from the production geometry. Hidden WPF sources remain in
  memory for exact raster/color references.
- Own `Application` startup bypasses the production updater rather than disabling
  updates in shipping code. There is no app-modal update dialog in this harness.
  The earlier real-control check dismissed “Remind me later” through computer-use
  and confirmed the modal was gone; no live mouse sweep was performed.
- Initial settling, GPU upload, snapshots and collection precede measurement.
  A 15-second **moving** trace warmup is followed by timed one-second CPU samples.
- Important instrument correction: the old preserved WPF harness did not start
  its Stopwatch during warmup, so its trace warmup was stationary. The new host
  fixes that identically for both arms and receives its own A/A calibration.
  Absolute percentages across those two hosts must not be treated as directly
  interchangeable. The old harness/evidence were left untouched.
- Nominal input interval remains 33 ms and hole radius remains 70 DIPs. Actual
  dispatcher cadence was approximately 47 ms in both arms. No movement threshold,
  lower update rate, per-frame CPU image copy or readback was used to win hover.
- A later preflight-only assertion verifies real cursor polling remains stopped;
  its separate control test passed. Frozen measured-v2 code predates only this
  assertion. Its C# source snapshot matches compiler-recorded portable PDB hashes.
- All six paired batches preserved APPDATA and LOCALAPPDATA settings hashes.
  Six pairs comprise two separate rounds of AB/BA/AB, never overlapping runs.

Frozen control DLL SHA256:
`E54BD5BEEBB374E095538F6D269FED92293D26946FEB2A06B84FE1438A3961E2`

Measured-v2 GPU host DLL SHA256:
`AFDEEB00E154849C6326AFFF8718039B66B95C65464BD1EAD6B2623B459087B5`

The manifest records binaries, source, timestamps, locked dependencies and raw
evidence hashes. `pdb-source-hashes.json` and `source-measured-v2` preserve the
compiled source relationship, not merely a newer source file beside old binaries.

## Machine and real native pipeline

- .NET SDK 10.0.403; .NET 10 WPF host, Windows x64.
- NVIDIA GeForce RTX 4080 SUPER, driver 32.0.16.3020.
- Hardware D3D11 feature level 11_1; no WARP fallback.
- Vortice.Direct3D11, Vortice.Direct2D1 and Vortice.DirectComposition 3.8.3, locked
  and confined to the prototype projects. Actual API signatures were compiled and
  probed before implementation.

| Monitor | Native working area (x, y, width, height) | DPI |
|---|---|---|
| DISPLAY1, primary | 0, 0, 3840, 2488 | 144 (150%) |
| DISPLAY2 | 931, 2560, 1920, 1220 | 120 (125%) |
| DISPLAY3 | -3840, 5, 3840, 2488 | 144 (150%) |
| DISPLAY4 | 3840, 0, 3840, 2488 | 144 (150%) |

The full monitor bounds are 3840×2560 except DISPLAY2 (1920×1280); the overlay
matches production's working areas, not a hard-coded old primary resolution.

Each monitor uses a premultiplied BGRA8, double-buffered flip-sequential composition
swapchain. Production RenderTargetBitmap output is uploaded once to a D2D GPU
bitmap. D2D draws that bitmap under a rectangle-minus-ellipse layer mask;
DirectComposition presents it. Pixels are not rerasterized or uploaded during
hover. Unchanged monitor surfaces remain displayed without new draws.

Native windows are disabled popup/topmost/tool/noactivate/noredirection windows,
not WS_EX_LAYERED. They return HTTRANSPARENT, pass WindowFromPoint checks at an
opaque edge and transparent center, and successfully set/read capture affinity
17. These are **necessary guards, not functional cross-process click/capture
proof**. WS_EX_TRANSPARENT by itself is not a cross-process click-through guarantee.

## A/A and paired hover results

The preregistered contract is `contract.json`. New-host A/A used three complete
pairs, six 12-second runs. Paired run-mean differences were -1.793, +3.204 and
+5.480 percentage points; maximum absolute observed noise was **5.480 pp**.

The following is a **scoped six-pair prototype estimate**, not a ten-pair headline
claim. One-second samples within a run are correlated; p90 is exploratory.
Bootstrap confidence intervals resample paired run means, not individual seconds.

| Metric | Pinned control | GPU prototype |
|---|---:|---:|
| Timed runs | 6 | 6 |
| One-second samples | 72 | 72 |
| Pooled p50, one core | 35.391% | 1.565% |
| Pooled p90, one core | 42.924% | 9.204% |
| Pooled mean | 36.020% | 3.628% |
| Maximum GPU one-second sample | — | 21.682% |

Paired mean CPU deltas (GPU minus control):
**-30.788, -33.228, -33.764, -31.068, -34.332, -31.121 pp**.
Paired bootstrap 95% CI: **-33.513 to -31.343 pp**.
Both rounds improved beyond the observed A/A noise.

Individual GPU run p90s were **16.392, 4.614, 8.737, 10.405, 5.991, 7.544%**.
Thus even the short paired result does not mean every run or every second passed.

Control produced 256–257 timed ticks/run; GPU produced 254–258. Control tick
interval p90s were approximately 48–50 ms, GPU approximately 48–55 ms. GPU draw
and present deltas on the primary exactly equal trace ticks, with zero timed
uploads/readbacks and no redraws on the other three static surfaces. This proves
the moving hole was exercised, not frozen or removed. It does not measure
physical cursor-to-photon latency.

### Compositor/GPU cost, not “free” work

Sparse external timed counter summaries, mean of run means (19 timed observations
per arm, approximately 3–4 per run):

| Counter | Control | GPU |
|---|---:|---:|
| DWM CPU, one core | 3.154% | 3.195% |
| App summed 3D-engine utilization | 30.704% | 2.734% |
| DWM reported 3D-engine utilization | 0% | 0% |

GPU engine percentages are not total-device utilization and cannot be added to
CPU percentages. DWM's zero 3D counter is a measurement limitation, **not proof of
zero compositor/display-engine cost**. These shared-desktop readings differ from
earlier experiments; the reason was not established. DWM CPU came from performance
counters because direct process CPU access was denied. Successful raw external
counter samples and their timestamps/status are retained beside every paired run.
There were **25 counter-collection errors across 24 paired process runs**: 22 at
the final array entry, three earlier. The collector reported an invalid performance
counter sample; those error rows lack timestamps, so they cannot all be confidently
classified as outside measurement. Retained successful values have status zero.
This incomplete, sparse coverage further limits compositor-cost conclusions;
`external-counter-status.json` preserves the failures rather than hiding them.

## Sustained and behavioral checks: reasons not to ship

| Check | Control p50/p90 | GPU p50/p90 | Observed outcome |
|---|---:|---:|---|
| 120-second primary hover soak | not paired | 1.559 / **10.787%** | Fails strict p90; 120 samples, 2,548 ticks |
| 32-second all-monitor top/left sweep | 34.050 / 48.985% | 1.566 / **12.161%** | Fails strict p90; 684 vs 681 ticks |
| 12-second brightness drag | 84.541 / 96.500% | 3.873 / 22.660% | Cheaper, but 242 vs 257 ticks and slower cadence |
| Requested 8-second color drag | 73.413 / 81.530% | one 98.246% CPU sample | **One update took 10,398 ms**, versus 171 control updates |
| 12-second light-off | 0.000 / 1.549% | 1.549 / 1.570% | Zero GPU draws/uploads/readbacks/ticks; nonzero CPU remains |

The mixed-monitor run touched all four surfaces (267, 168, 254, 172 GPU draws)
with zero timed upload/readback. It exercised mixed DPI and negative coordinates
but did not simulate hotplug or a live DPI transition.

Brightness uses GPU bitmap opacity, not CPU image upload. Its tick-interval p90
was about 61.9 ms versus 50.4 ms control; maximum update callback was 10.15 ms
versus 0.17 ms control. That is a responsiveness caveat, not a no-regression pass.

Color deliberately invokes the exact production color method and rerasterizes all
four real WPF glows before replacement uploads. One change uploads **124,016,640
bytes** and blocks the UI for about **10.4 seconds**. The requested 8-second sample
window therefore overran, yielding one actual long CPU sample/update—not eight
successful seconds of responsive work. The final image changed correctly, but
this path is unacceptable for dragging. There is also one redundant primary GPU
draw in this experimental color path; that does not explain the rerasterization
stall. No frozen-source optimization was silently substituted after measurement.

GPU-off CPU is not zero and its median is above the small control sample. There
is no proven near-zero/no-regression idle conclusion. The earlier 35-second
GPU-idle check similarly had approximately 1.53/1.57% p50/p90 with no timed draws.
No instrumentation subtraction is applied to improve these numbers.

## Visual and lifecycle evidence

- All four initial native-DPI GPU upload/readbacks are byte-identical to their
  production **RenderTargetBitmap** references, including alpha. This is not a
  comparison against a captured desktop composite.
- Actual GPU texture readback at two distinct hole positions showed center
  alpha 0 and the vacated solid point alpha 255. Readback occurs only outside
  timed phases. Generated transparent PNGs contain only the renderer, not the
  private desktop.
- D2D and WPF hole-edge antialiasing differ. One final hover image has about
  0.000762/255 mean alpha difference over the full image and maximum alpha
  difference 48 near the hole boundary. Full-image averages hide localized
  differences; this is **not exact visual equivalence**.
- Brightness introduces further quantization differences (example maximum alpha
  difference 12). Straight RGB differences at near-transparent pixels can be
  large; they must not be misrepresented as fully opaque color errors or ignored
  as proof of fidelity.
- Computer-use observed visible, disabled native experiment windows and correct
  primary dimensions. It could not capture their image with capture exclusion/no
  redirection. No successful real-screen visual check is claimed.
- During the 120-second hover soak, GDI stayed 127; handles fell 1008→993 and USER
  objects 44→41. Private bytes fell 1,764,179,968→1,416,032,256; working set fell
  815,558,656→471,416,832. No growing GDI/handle trend was observed in this run.
- Ten independent create/render/readback/dispose cycles held handles/GDI/USER at
  **405/7/7 after the first cycle**. Private bytes were 317.6 MB after cycle one,
  peaked around 477.9 MB and ended at 449.5 MB. This is not complete return to the
  51.6 MB process baseline or proof of leak freedom. Driver/framework caches,
  managed readback buffers and longer-duration behavior require further work.
- The main host retains hidden production windows and substantial raster/GPU
  resources (roughly 1.4–1.9 GB private bytes in these checks). It demonstrates
  no production memory improvement.

The GPU-specific verifier passed **27 frozen runs, 44 native-window checks, six
settings batches and all nine prior manifest files**. It also checks measured
C# source against the original portable PDB. Existing production unit tests
passed **13/13**; these do not cover the missing production integration.

## Implementation gaps and a bounded next investment

No existing-backend or unsupported-API blocker remains on this tested device.
The concrete barriers are the measured tails/color latency and missing native
integration safeguards:

1. Make exact color changes responsive without synchronous full-monitor WPF
   rerasterization. GPU-side color representation or a different exact update
   strategy needs its own fidelity and latency tests; no projected win is claimed.
2. Resolve and remeasure sustained/mixed-monitor p90 and brightness cadence.
   The current implementation allocates/rebuilds a D2D mask each update; caching
   or translation is an untested follow-up hypothesis, not a result.
3. Validate real input click-through, z-order/activation and capture exclusion
   in actual applications; API flags/readback do not establish these behaviors.
4. Integrate and test controls, hotkeys, single/all monitor selection, toggle/
   close lifecycle, live resizing/DPI changes, hotplug and device-loss recovery.
5. Remove experimental retained raster sources where appropriate and measure
   resource stability, different GPUs, display arrangements and ARM64. None is
   validated by this x64 RTX test.

Stop here as requested. The evidence supports further targeted investigation,
not a claim that the user's complete no-regression success bar has been reached.

## Files and reproduction

See `tools\GpuOverlayBench\README.md` for build/serial-run commands.
Important local artifacts relative to this directory:

- `contract.json`: registered bar and stop rule.
- `aa`, `aa-summary.json`: three new-host A/A pairs.
- `hover-round1`, `hover-round2`, `hover-six-pairs-summary.json`: six full pairs.
- `brightness-guard`, `color-guard`, `monitors-guard`: behavioral pairs and summaries.
- `hover-soak`, `off-guard`, `control-off-guard`: sustained/idle evidence.
- `lifetime-ten-cycles.json`: repeated native object lifetime results.
- `guard-results.json`: integrity/necessary-guard results, not production acceptance.
- `visual-differences.json`: final GPU/production PNG difference measurements.
- `external-counter-status.json`: successful-counter limitations and read failures.
- `source-measured-v2`, `pdb-source-hashes.json`: compiled-source provenance.
- `build-manifest.json`, `evidence-manifest.json`, `final-safety.json`: final provenance/safety.
- `tests\production-tests.trx`: preserved existing unit-test outcome.

The executable is `tools\GpuOverlayBench\bin\measured-v2\GpuOverlayBench.exe`.
Keep that folder and `tools\OverlayBench\bin\control-b927edd` intact alongside the
raw evidence. `bin` is ignored; no claim that these artifacts were committed or
published is made.
