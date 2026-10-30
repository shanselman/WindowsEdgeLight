import json
import statistics
import sys
from pathlib import Path


def percentile(values, fraction):
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    low = int(position)
    high = min(low + 1, len(ordered) - 1)
    return ordered[low] + (ordered[high] - ordered[low]) * (position - low)


results = []
for argument in sys.argv[1:]:
    path = Path(argument)
    data = json.loads((path / "result.json").read_text())
    samples = [s["appCorePercent"] for s in data["samples"]]
    profile = data["diagnostics"]
    first, last = profile[0], profile[-1]
    threads = {t["Id"]: t for t in first["threads"]}
    totals = [
        {**thread, "cpuDeltaMs": thread["cpuMs"] - threads[thread["Id"]]["cpuMs"]}
        for thread in last["threads"]
    ]
    results.append({
        "path": str(path),
        "diagnosticOnly": True,
        "flags": {key: data["metadata"][key] for key in ("profile", "lightweightSampler", "cachedMask", "detachVisuals")},
        "cpu": {"n": len(samples), "p50": statistics.median(samples), "p90": percentile(samples, .9),
                "mean": statistics.mean(samples), "max": max(samples)},
        "ticks": data["ticks"],
        "tickP90Ms": percentile(data["tickIntervalsMs"], .9) if data["tickIntervalsMs"] else None,
        "phases": last["phases"],
        "threadCpu": sorted(totals, key=lambda value: value["cpuDeltaMs"], reverse=True),
        "gcDelta": [end - start for start, end in zip(first["gc"], last["gc"])],
        "allocatedBytes": last["totalAllocatedBytes"] - first["totalAllocatedBytes"],
        "maskComparisons": data["metadata"]["maskComparisons"],
        "note": "Stages are wall-time durations, thread deltas are CPU. Sampled-stack profile is wall time, not CPU. Threads created after profile startup are not included in the thread breakdown."
    })
print(json.dumps(results, indent=2))
