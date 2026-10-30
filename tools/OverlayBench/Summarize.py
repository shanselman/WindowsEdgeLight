import json
import math
import pathlib
import random
import statistics
import sys
from datetime import datetime, timedelta


def percentile(values, p):
    a = sorted(values)
    n = (len(a) - 1) * p
    lo, hi = math.floor(n), math.ceil(n)
    return a[lo] + (a[hi] - a[lo]) * (n - lo)


def summary(values):
    return {"n": len(values), "p50": percentile(values, .5), "p90": percentile(values, .9),
            "mean": statistics.mean(values), "min": min(values), "max": max(values)}


files = [p for directory in sys.argv[1:] for p in pathlib.Path(directory).glob("pair-*/result.json")]
runs = []
for p in sorted(files):
    r = json.loads(p.read_text(encoding="utf-8-sig"))
    s = r["samples"]
    cpu = [v["appCorePercent"] for v in s]
    means = sum(v["appCpuMs"] for v in s) / sum(v["wallMs"] for v in s) * 100
    runs.append({
        "path": str(p.parent), "arm": p.parent.name.split("-")[-1], "pair": p.parent.name.split("-")[1],
        "round": str(p.parent.parent), "cpu": summary(cpu), "weightedCpu": means,
        "ticks": r["ticks"], "tickMs": summary(r["tickIntervalsMs"]) if r["tickIntervalsMs"] else None,
        "resourcesStart": r["startResources"], "resourcesEnd": r["endResources"],
        "maxUpdateMs": r["maxUpdateMs"], "samples": cpu
    })
    external_path = p.parent.parent / (p.parent.name + ".external.json")
    if external_path.exists() and "measuredStartedUtc" in r:
        start = datetime.fromisoformat(r["measuredStartedUtc"]) + timedelta(seconds=1)
        end = datetime.fromisoformat(r["measuredEndedUtc"])
        app_pid = f"pid_{r['metadata']['pid']}_"
        external = {"dwmCorePercent": [], "app3dEnginePercent": [], "dwm3dEnginePercent": []}
        for sample in json.loads(external_path.read_text(encoding="utf-8-sig")):
            if "utc" not in sample or not start <= datetime.fromisoformat(sample["utc"]) <= end:
                continue
            valid = [v for v in sample["values"] if v["Status"] == 0 and math.isfinite(v["CookedValue"])]
            dwm_cpu = [v["CookedValue"] for v in valid if v["InstanceName"] == "dwm"]
            if dwm_cpu:
                external["dwmCorePercent"].append(sum(dwm_cpu))
            for label, belongs in [
                ("app3dEnginePercent", lambda v: v["InstanceName"].startswith(app_pid)),
                ("dwm3dEnginePercent", lambda v: not v["InstanceName"].startswith(app_pid))
            ]:
                gpu = [v["CookedValue"] for v in valid if belongs(v) and "engtype_3d" in v["InstanceName"]]
                if gpu:
                    external[label].append(sum(gpu))
        runs[-1]["externalCounters"] = {k: summary(v) for k, v in external.items() if v}
pairs = []
for c in [r for r in runs if r["arm"] == "control"]:
    t = next((r for r in runs if r["arm"] == "treatment" and r["pair"] == c["pair"] and r["round"] == c["round"]), None)
    if t:
        pairs.append(t["weightedCpu"] - c["weightedCpu"])
rng = random.Random(927)
ci = []
if pairs:
    boot = [statistics.mean(rng.choices(pairs, k=len(pairs))) for _ in range(20000)]
    ci = [percentile(boot, .025), percentile(boot, .975)]
report = {
    "runs": runs, "completePairs": len(pairs), "pairedTreatmentMinusControl": pairs,
    "pairedDelta95BootstrapCI": ci,
    "pooledOneSecondCpu": {
        arm: summary([v for r in runs if r["arm"] == arm for v in r["samples"]])
        for arm in ("control", "treatment") if any(r["arm"] == arm for r in runs)
    },
    "externalRunMeans": {
        arm: {
            metric: summary([r["externalCounters"][metric]["mean"] for r in runs
                             if r["arm"] == arm and metric in r.get("externalCounters", {})])
            for metric in ("dwmCorePercent", "app3dEnginePercent", "dwm3dEnginePercent")
            if any(r["arm"] == arm and metric in r.get("externalCounters", {}) for r in runs)
        } for arm in ("control", "treatment")
    },
    "note": "p90 exploratory for small n; bootstrap CI is over paired run means, not correlated one-second samples"
}
print(json.dumps(report, indent=2))
