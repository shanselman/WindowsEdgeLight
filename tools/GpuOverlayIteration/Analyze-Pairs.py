import json
import math
import random
import statistics
import sys
from datetime import datetime, timedelta
from pathlib import Path


def percentile(values, p):
    a = sorted(values)
    position = (len(a) - 1) * p
    low, high = math.floor(position), math.ceil(position)
    return a[low] + (a[high] - a[low]) * (position - low)


def summary(values):
    return ({"n": len(values), "p50": percentile(values, .5), "p90": percentile(values, .9),
             "mean": statistics.mean(values), "min": min(values), "max": max(values)}
            if values else None)


runs, failed = [], []
for directory in sys.argv[1:]:
    for path in sorted(Path(directory).glob("pair-*")):
        if not path.is_dir():
            continue
        if not (path / "result.json").exists() or (path / "error.txt").exists():
            failed.append(str(path))
            continue
        result = json.loads((path / "result.json").read_text(encoding="utf-8-sig"))
        samples = result["samples"]
        row = {
            "path": str(path), "round": directory, "pair": path.name.split("-")[1],
            "arm": path.name.split("-")[-1], "harnessHash": result["metadata"]["harnessHash"],
            "cpu": summary([s["appCorePercent"] for s in samples]),
            "weightedCpu": sum(s["appCpuMs"] for s in samples) / sum(s["wallMs"] for s in samples) * 100,
            "cpuSamples": [s["appCorePercent"] for s in samples], "ticks": result["ticks"],
            "tickMs": summary(result["tickIntervalsMs"]),
            "updateMs": summary(result.get("updateDurationsMs", [])),
            "resourcesStart": result["startResources"], "resourcesEnd": result["endResources"],
        }
        for key in ("Gpu", "Color"):
            start, end = result.get("start" + key, []), result.get("end" + key, [])
            row[key.lower() + "Delta"] = [
                {k: b[k] - a[k] for k in a if isinstance(a[k], (int, float))}
                for a, b in zip(start, end)
            ]
        external_path = path.parent / (path.name + ".external.json")
        counters = {"dwmCorePercent": [], "app3dEnginePercent": [], "dwm3dEnginePercent": []}
        errors, timed_errors, timed_successes = 0, 0, 0
        if external_path.exists():
            start = datetime.fromisoformat(result["measuredStartedUtc"]) + timedelta(seconds=1)
            end = datetime.fromisoformat(result["measuredEndedUtc"])
            prefix = f"pid_{result['metadata']['pid']}_"
            for sample in json.loads(external_path.read_text(encoding="utf-8-sig")):
                timed = "utc" in sample and start <= datetime.fromisoformat(sample["utc"]) <= end
                if "error" in sample:
                    errors += 1
                    timed_errors += int(timed)
                    continue
                if not timed:
                    continue
                timed_successes += 1
                valid = [v for v in sample["values"] if v["Status"] == 0 and math.isfinite(v["CookedValue"])]
                cpu = [v["CookedValue"] for v in valid if v["InstanceName"] == "dwm"]
                if cpu:
                    counters["dwmCorePercent"].append(sum(cpu))
                for key, app in (("app3dEnginePercent", True), ("dwm3dEnginePercent", False)):
                    values = [v["CookedValue"] for v in valid if "engtype_3d" in v["InstanceName"]
                              and v["InstanceName"].startswith(prefix) == app]
                    if values:
                        counters[key].append(sum(values))
        row["external"] = {k: summary(v) for k, v in counters.items()}
        row["externalObservations"] = {"timedSuccesses": timed_successes, "errors": errors, "timedErrors": timed_errors}
        runs.append(row)

deltas = []
for control in (r for r in runs if r["arm"] == "control"):
    treatment = next((r for r in runs if r["arm"] == "treatment"
                      and r["pair"] == control["pair"] and r["round"] == control["round"]), None)
    if treatment:
        deltas.append(treatment["weightedCpu"] - control["weightedCpu"])
rng = random.Random(927)
boot = [statistics.mean(rng.choices(deltas, k=len(deltas))) for _ in range(20000)] if deltas else []
print(json.dumps({
    "runs": runs, "failed": failed, "completePairs": len(deltas),
    "pairedTreatmentMinusControl": deltas,
    "pairedDelta95BootstrapCI": [percentile(boot, .025), percentile(boot, .975)] if boot else [],
    "pooledOneSecondCpu": {arm: summary([v for r in runs if r["arm"] == arm for v in r["cpuSamples"]])
                          for arm in ("control", "treatment")},
    "externalRunMeans": {
        arm: {key: summary([r["external"][key]["mean"] for r in runs if r["arm"] == arm and r["external"][key]])
              for key in ("dwmCorePercent", "app3dEnginePercent", "dwm3dEnginePercent")}
        for arm in ("control", "treatment")
    },
    "note": "Small-n p90 is exploratory. Bootstrap uses paired weighted run means, not correlated one-second samples. Missing/failed runs are explicit; do not treat them as successful pairs."
}, indent=2))
