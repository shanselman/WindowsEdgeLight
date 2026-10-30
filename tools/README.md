# Edge-light CPU hill-climb (experiment, do not merge)

This branch is kept for posterity. It is **not** a PR candidate and nothing here ships. Production code in `WindowsEdgeLight/` is identical to v1.14.0 (`b927edd`); only `tools/` is added.

## Question

After v1.14.0 ([#220](https://github.com/shanselman/WindowsEdgeLight/pull/220)), hovering with the light on still costs roughly 30-40% of one core, most likely because the full-monitor layered window is recomposed whenever the hover hole moves. Can that get under 10% (p50 and p90) with no regressions?

## Result: no shippable win

CPU is percent of one logical core (process CPU ms / wall ms x 100), measured with a synthetic cursor trace against the frozen v1.14.0 build.

| Approach | Control p50 / p90 | Candidate p50 / p90 | Verdict |
|---|---|---|---|
| Thin edge windows | 28.0 / 39.3 | 9.3 / 17.9 | Rejected: p90 over 10%, not pixel-identical, no app lifecycle integration |
| Skip hole moves under 2 DIP | 29.6 / 44.1 | 31.8 / 42.2 | Rejected: no measurable gain |
| Native GPU (DirectComposition + Direct2D + D3D11) | 34.1-35.4 / 40.6-42.9 | 0-1.6 / 8.6-9.2 | Under 10% in warm-state synthetic runs, but not shippable |

Why the GPU prototype is overkill for this app:

- Exact color needs a precomputed lookup cache that takes about 310 s to build.
- About 1.9 GB of private memory while running.
- Hole-edge antialiasing and opacity differ from WPF, and brightness/color drag cadence is slower (tick p90 about 62 ms vs 49-56 ms).
- One short run still hit 12.1% p90. Click-through, capture exclusion, hotkeys, hotplug/DPI changes, device loss, HDR and ARM64 were never validated.

If this is revisited, look for a simpler way to avoid recompositing the whole monitor surface rather than a native GPU renderer.

## What is here

| Path | What it is |
|---|---|
| `OverlayBench/` | Synthetic WPF harness with the frozen v1.14.0 control, plus the thin-window and threshold experiments |
| `GpuOverlayBench/` | First native GPU prototype |
| `GpuOverlayIteration/` | Follow-up: exact-color lookup shader and diagnostics |
| `GpuOverlayLifetime/` | Resource-lifetime test for the GPU prototype |
| `reports/` | The three written reports, plus the rejected arm sources in `reports/arms/` |

The reports are verbatim snapshots. Statements such as "no commits, pushes" describe when they were written, not this branch. Raw run data (`bin/evidence`, several hundred MB) is git-ignored and not included. These projects are not part of `WindowsEdgeLight.sln` or CI.

Test machine: four monitors at mixed DPI (three 3840x2488 at 150%, one 1920x1220 at 125%), RTX 4080 SUPER, 32 logical cores, .NET SDK 10.0.403, Vortice 3.8.3.
