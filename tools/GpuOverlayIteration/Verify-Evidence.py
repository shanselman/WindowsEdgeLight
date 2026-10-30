"""Verify integrity/workload guards; this does not assert production readiness."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path

ROOT = Path(r"tools\GpuOverlayIteration\bin\evidence")
CONTROL = "E54BD5BEEBB374E095538F6D269FED92293D26946FEB2A06B84FE1438A3961E2"
HOST = "1D87E81700790AF7CA9CDD9DB26BC22F225F51D18D03D4342924BE5755C13AAE"
BATCHES = {"fresh-aa": 6, "hover-round1": 6, "hover-round2": 6, "color-pair": 2,
           "brightness-pair": 2, "mixed-pair": 2, "off-pair": 2, "soak-pair": 2}


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


intervals, reports, displays = [], [], None
for batch, count in BATCHES.items():
    folder = ROOT / batch
    check(load(folder / "settings-before.json") == load(folder / "settings-after.json"), f"Settings changed: {batch}")
    paths = sorted(folder.glob("pair-*/result.json"))
    check(len(paths) == count, f"Missing runs: {batch}")
    for path in paths:
        r = load(path)
        m = r["metadata"]
        label = str(path.parent.relative_to(ROOT))
        check(not path.with_name("error.txt").exists(), f"Runtime error: {label}")
        check(load(path.with_name("metadata.json")) == m, f"Metadata disagrees: {label}")
        check(m["rendererHash"] == CONTROL and m["harnessHash"] == HOST, f"Wrong frozen binaries: {label}")
        check(not m["profile"] and m["lightweightSampler"] and not m["cachedMask"], f"Wrong treatment/instrument: {label}")
        displays = displays or m["monitorInfo"]
        check(m["monitorInfo"] == displays and len(displays) == 4, f"Display configuration changed: {label}")
        check(all(value == 0 for value in m["noHolePixelComparison"].values()), f"Initial glow changed: {label}")
        check(m["holeChecks"] == {"firstCenterAlpha": 0, "firstSolidAlpha": 255,
                                 "secondCenterAlpha": 0, "secondSolidAlpha": 255}, f"Hole failed: {label}")
        check(r["ticks"] == r["updates"] == len(r["updateDurationsMs"]), f"Lost callback: {label}")
        check(r["samples"] and r["samples"][-1]["endMs"] >= m["duration"] * 1000, f"Short run: {label}")
        for sample in r["samples"]:
            check(sample["wallMs"] > 0 and
                  abs(sample["appCpuMs"] / sample["wallMs"] * 100 - sample["appCorePercent"]) < 1e-8,
                  f"CPU normalization: {label}")
        intervals.append((datetime.fromisoformat(m["startedUtc"]), datetime.fromisoformat(r["measuredEndedUtc"]), label))
        if m["arm"] == "gpu":
            check(m["gpuAdapter"] == "NVIDIA GeForce RTX 4080 SUPER" and m["gpuFeatureLevel"] == "Level_11_1",
                  f"Device changed: {label}")
            check(m["detachVisuals"] and m["colorCacheDirectory"], f"Candidate feature missing: {label}")
            check(len(m["windowInfo"]) == len(r["startGpu"]) == len(r["endGpu"]) == 4, f"Missing surface: {label}")
            for monitor, window, comparison in zip(displays, m["windowInfo"], m["uploadComparisons"]):
                check(window["styles"] & 0x082000A8 == 0x082000A8 and not window["styles"] & 0x80000,
                      f"Wrong native extended styles: {label}")
                check(window["windowStyles"] & 0x98000000 == 0x98000000 and window["visible"] and
                      not window["enabled"] and window["affinity"] == 17 and
                      window["edgeHitThrough"] and window["centerHitThrough"], f"Native window guard: {label}")
                x, y, width, height = map(int, re.findall(r"-?\d+", monitor["workArea"]))
                check(window["rect"] == {"Left": x, "Top": y, "Right": x + width, "Bottom": y + height} and
                      window["dpi"] == monitor["DpiX"] * 96, f"Coordinates/DPI: {label}")
                check(all(v == 0 for v in comparison["comparison"].values()), f"Color upload changed pixels: {label}")
            delta = [{k: b[k] - a[k] for k in a} for a, b in zip(r["startGpu"], r["endGpu"])]
            colors = [{k: b[k] - a[k] for k in a if isinstance(a[k], (int, float))}
                      for a, b in zip(r["startColor"], r["endColor"])]
            check(all(d["Readbacks"] == d["Uploads"] == d["UploadBytes"] == 0 and d["Draws"] == d["Presents"]
                      for d in delta), f"Timed copy/readback or lost present: {label}")
            if m["lane"] == "hover":
                check(delta[0]["Draws"] == r["ticks"] and all(d["Draws"] == 0 for d in delta[1:]),
                      f"Hover work changed: {label}")
            elif m["lane"] == "monitors":
                check(all(d["Draws"] > 0 for d in delta), f"Skipped monitor: {label}")
            elif m["lane"] == "brightness":
                check(all(d["Draws"] == r["ticks"] for d in delta), f"Skipped brightness frame: {label}")
            elif m["lane"] == "off":
                check(r["ticks"] == 0 and all(d["Draws"] == 0 for d in delta), f"Off work continued: {label}")
            elif m["lane"] == "color":
                check(all(c["requests"] == r["ticks"] and .8 * r["ticks"] <= c["updates"] <= r["ticks"]
                          for c in colors), f"Dropped/throttled color requests: {label}")
                check(delta[0]["Draws"] == r["ticks"] and
                      all(d["Draws"] == c["updates"] for d, c in zip(delta[1:], colors[1:])),
                      f"Color not drawn: {label}")
                final = load(path.with_name("final-color-proof.json"))
                check(final["countersBeforeVerification"] == r["endColor"] and len(final["checks"]) == 4 and
                      all(c["difference"]["MaxChannelDifference"] == 0 for c in final["checks"]), f"Final color wrong: {label}")
        reports.append({"path": label, "samples": len(r["samples"]), "ticks": r["ticks"]})

intervals.sort()
for previous, current in zip(intervals, intervals[1:]):
    check(current[0] > previous[1], f"Overlapping runs: {previous[2]}, {current[2]}")
check(sha(Path(r"tools\OverlayBench\bin\control-b927edd\WindowsEdgeLight.dll")) == CONTROL, "Control changed")
check(sha(Path(r"tools\GpuOverlayIteration\bin\shader-v3\GpuOverlayIteration.dll")) == HOST, "Host changed")
old_files = load(ROOT / "first-pass-manifest.json")["files"]
for item in old_files:
    check(sha(Path(item["path"])) == item["sha256"], f"First-pass evidence changed: {item['path']}")
old_build = load(Path(r"tools\GpuOverlayBench\bin\evidence\build-manifest.json"))
old_build_files = old_build["sources"] + old_build["binaries"]
for item in old_build_files:
    check(sha(Path(item["path"])) == item["sha256"], f"First-pass source/build changed: {item['path']}")
for source in Path(r"tools\GpuOverlayIteration\bin\shader-v3\source").glob("*.cs"):
    check(sha(source) == sha(Path(r"tools\GpuOverlayIteration") / source.name), f"Measured source changed: {source.name}")
cpu_colors = load(ROOT / "color-proof" / "color-proof.json")
gpu_colors = load(ROOT / "gpu-color-proof" / "gpu-color-proof.json")
check(len(cpu_colors) == 4 and all(len(m["checks"]) == 10 and all(c["difference"]["max"] == 0 for c in m["checks"])
                                 for m in cpu_colors), "CPU color response proof failed")
check(len(gpu_colors) == 40 and all(c["difference"]["MaxChannelDifference"] == 0 for c in gpu_colors), "GPU color proof failed")
brightness = load(ROOT / "gpu-color-proof" / "gpu-brightness-proof.json")
check(len(brightness) == 4 and all(c["colorDifference"]["MaxChannelDifference"] == 0 and c["centerAlpha"] == 0
                                for c in brightness), "New shader changed brightness/hole pixels")
lifetime = load(ROOT / "color-lifetime" / "lifetime.json")
check(len(lifetime["rows"]) == 10 and all(len(r["checks"]) == 4 and
      all(c["difference"]["MaxChannelDifference"] == 0 for c in r["checks"]) for r in lifetime["rows"]), "Lifetime recreation failed")
check(load(ROOT / "iteration-settings-checkpoint.json") == load(ROOT / "iteration-settings-after.json"), "Settings changed")
state = load(ROOT / "final-state.json")
check(state["head"] == "b927edd529ce97bfa729dfc0a6f185002c4878df" and not state["productionDiff"]
      and not state["rendererProcesses"], "Production or renderer-process cleanup guard failed")
test = ET.parse(ROOT / "tests" / "iteration-production-tests.trx")
counters = next(element for element in test.iter() if element.tag.endswith("}Counters"))
check(counters.get("total") == counters.get("passed") == "13" and counters.get("failed") == "0", "Existing tests failed")
print(json.dumps({"integrityAndWorkloadGuards": "passed", "runs": reports, "preservedFirstPassFiles": len(old_files),
                  "preservedFirstPassSourceAndBuildFiles": len(old_build_files), "existingTestsPassed": 13,
                  "cpuColorChecks": 40, "gpuColorChecks": 40, "nativeBrightnessChecks": 4,
                  "lifetimeCycles": 10,
                  "notAnAcceptanceVerdict": "CPU/resource results and pre-existing WPF/native AA, opacity, startup and desktop E2E gaps require separate judgement."}, indent=2))
