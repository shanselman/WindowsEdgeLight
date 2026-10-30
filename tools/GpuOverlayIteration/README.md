# Bounded GPU follow-up (experimental, not production)

This separate executable tests two follow-up hypotheses against the **unchanged**
v1.14.0 assembly at `..\OverlayBench\bin\control-b927edd\WindowsEdgeLight.dll`.
It does not replace the application, run its updater, move the system cursor, or
save settings. It creates real native overlay windows and invokes the production
input, geometry, brightness, and temperature methods. The first-pass
`GpuOverlayBench` source, measured-v2 build, and evidence are separate and frozen.

See `bin\evidence\contract.json` for the preregistered scope and acceptance bar.
Generated captures contain only this renderer's pixels, not desktop screenshots.
Run only one rendering/measurement process at a time.

## Build and modes

From the repository root, after preparing the pinned control as documented in
`..\OverlayBench`:

```powershell
dotnet restore tools\GpuOverlayIteration\GpuOverlayIteration.csproj --locked-mode
dotnet build tools\GpuOverlayIteration\GpuOverlayIteration.csproj --no-restore -c Release -o tools\GpuOverlayIteration\bin\your-new-build
```

Invoke `GpuOverlayIteration.exe <new-output-directory> <control|gpu> <lane> <seconds>`.
Existing result directories are rejected. Lanes are `hover`, `monitors`,
`brightness`, `color`, `idle`, `off`, `color-proof`, `gpu-color-proof`, and
`color-lifetime`. The proof lanes are untimed fidelity/cache-preparation checks,
not CPU benchmarks. `color-lifetime` interprets the last argument as 1–10 native
resource recreation cycles and checks every monitor; it is not production hotplug.
All timed lanes have the same nominal 33 ms dispatcher cadence and 15-second
moving warmup; actual intervals and callback durations are retained.

Environment switches:

| Variable | Meaning |
|---|---|
| `GPU_PROFILE=1` | Phase wall times, allocations, cycles, per-thread CPU and GC diagnostics. Disable for acceptance runs. |
| `GPU_LIGHT_SAMPLER=1` | Native process CPU/memory/handle sampling instead of expensive `Process.Refresh` enumeration. Use identically in both arms. |
| `GPU_DETACH_WPF=1` | Detach unused hidden source roots in the GPU arm, while retaining and calling production input/geometry methods. Verification temporarily reattaches roots outside timing. |
| `GPU_CACHE_MASK=1` | Diagnostic translated native mask. **Rejected:** measured antialiasing differences versus the original native mask. Do not silently use as a fidelity-preserving optimization. |
| `GPU_COLOR_CACHE=<directory>` | Use validated production channel-response caches and a D3D11 lookup shader. No timed full-image color uploads. |

`Run-Pairs.ps1` runs serial interleaved pairs, collects external DWM/GPU counters,
preserves counter errors with timestamps, and checks settings hashes without
restoring anything. `-TreatmentMode control` selects A/A calibration. Do not mix
results from different host builds, sampler settings, or display configurations.
`Analyze-Pairs.py` retains failed runs and timestamped counter errors in its report.
`Verify-Evidence.py` checks the frozen follow-up dataset, workload/colour guards,
and preservation of first-pass evidence; passing it is not a performance or
production-readiness verdict.

## What the diagnostics mean

`Analyze-Diagnostics.py <result-directory> ...` reports one-core CPU and phase/thread
data. Phase durations are wall time, not exclusive process CPU. Thread CPU uses
`GetThreadTimes`; threads created after initial registration are absent.
`dotnet-trace`'s Windows `dotnet-sampled-thread-time` stacks are **sampled wall time**,
not CPU attribution. The local tool manifest pins the profiler version.

The cached-mask ablation is separate from hidden-WPF detachment and sampler
correction. Neither a single diagnostic p90 nor subtraction of measured overhead
constitutes an acceptance claim.

## Exact-color hypothesis

A black/white two-basis interpolation is explicitly compared against production
pixels first. It is not assumed to be a faithful tint: the gradient's endpoints
stay white and the shadow/color pipeline is not necessarily affine.

`color-proof` renders grayscale channel responses 180 through 255 with the real
production geometry, gradient, and shadow. It verifies channel symmetry and
color-independent alpha, compresses identical response signatures, and tests
reconstruction at endpoints, intermediate temperatures, and byte boundaries on
every monitor. The cache uses a 32-bit class/alpha map and 80-byte response rows,
bounded to 262,144 classes. Cache creation duration and storage are reported,
not hidden as startup-free work.

`gpu-color-proof` tests actual GPU backbuffer bytes against production at the same
temperatures. It also compares lookup-based brightness/hole rendering with the
original native rendering of the exact production raster. Timed color runs keep
request/update counters and verify the trailing requested color on every GPU
surface after timing. Reuse of an identical production byte RGB is not a dropped
input. Other brightness/mask differences against WPF remain visible in reports.

## Limits

This is not an integrated renderer or a shipping candidate. The original native
hole antialiasing differences and real desktop E2E gaps are not waived.
Window styles, capture affinity and hit-test checks do not prove physical
cross-process click-through, actual capture exclusion, hotkeys, hotplug,
multi-adapter behavior, ARM64 execution, or visual equivalence on other hardware.
No result from this tool authorizes a production PR or merge.
