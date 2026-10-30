"""Verify the frozen local GPU experiment, without treating its measured failures as a win."""
import hashlib
import json
import re
import statistics
import sys
from datetime import datetime
from pathlib import Path

CONTROL = "E54BD5BEEBB374E095538F6D269FED92293D26946FEB2A06B84FE1438A3961E2"
HOST = "AFDEEB00E154849C6326AFFF8718039B66B95C65464BD1EAD6B2623B459087B5"
BATCHES = ("aa", "hover-round1", "hover-round2", "brightness-guard", "color-guard", "monitors-guard")


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def percentile(values, fraction):
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    low = int(position)
    high = min(low + 1, len(ordered) - 1)
    return ordered[low] + (ordered[high] - ordered[low]) * (position - low)


def stats(values):
    return {"n": len(values), "p50": statistics.median(values), "p90": percentile(values, .9),
            "mean": statistics.mean(values), "max": max(values)}


def check(condition, message):
    if not condition:
        raise ValueError(message)


def verify(root):
    paths = []
    for batch in BATCHES:
        paths.extend(sorted(path / "result.json" for path in (root / batch).glob("pair-*") if path.is_dir()))
        before = load(root / batch / "settings-before.json")
        after = load(root / batch / "settings-after.json")
        check(before == after, f"Settings changed: {batch}")
    paths.extend(root / name / "result.json" for name in ("hover-soak", "off-guard", "control-off-guard"))
    check(len(paths) == 27, "Expected 24 paired and three sustained/off result files.")

    reference_monitors = None
    intervals = []
    runs = []
    native_windows = 0
    for path in paths:
        result = load(path)
        metadata = result["metadata"]
        label = str(path.parent.relative_to(root))
        check(load(path.with_name("metadata.json")) == metadata, f"Metadata disagrees: {label}")
        check(metadata["rendererHash"] == CONTROL, f"Wrong production renderer: {label}")
        check(metadata["harnessHash"] == HOST, f"Wrong measurement host: {label}")
        check(not path.with_name("error.txt").exists(), f"Runtime error: {label}")
        monitors = metadata["monitorInfo"]
        reference_monitors = reference_monitors or monitors
        check(monitors == reference_monitors and len(monitors) == 4, f"Display configuration changed: {label}")
        check(metadata["noHolePixelComparison"] == {
            "MaxChannelDifference": 0, "AlphaDifferent": 0, "PixelsDifferent": 0
        }, f"Initial glow pixels differ: {label}")
        check(metadata["holeChecks"] == {
            "firstCenterAlpha": 0, "firstSolidAlpha": 255,
            "secondCenterAlpha": 0, "secondSolidAlpha": 255
        }, f"Moving hole failed: {label}")
        check(result["samples"], f"No CPU samples: {label}")
        check(result["ticks"] == result["updates"], f"Skipped update: {label}")
        intervals.append((datetime.fromisoformat(metadata["startedUtc"]),
                          datetime.fromisoformat(result["measuredEndedUtc"]), label))
        for sample in result["samples"]:
            expected_cpu = sample["appCpuMs"] / sample["wallMs"] * 100
            check(abs(sample["appCorePercent"] - expected_cpu) < 1e-8, f"Incorrect CPU normalization: {label}")

        gpu_delta = []
        if metadata["arm"] == "gpu":
            check(metadata["gpuAdapter"] == "NVIDIA GeForce RTX 4080 SUPER", f"Adapter changed: {label}")
            check(metadata["gpuFeatureLevel"] == "Level_11_1", f"Unexpected feature level: {label}")
            check(len(metadata["windowInfo"]) == len(metadata["uploadComparisons"]) == 4,
                  f"Missing native monitor: {label}")
            for monitor, window, comparison in zip(monitors, metadata["windowInfo"], metadata["uploadComparisons"]):
                native_windows += 1
                check(window["styles"] & 0x082000A8 == 0x082000A8, f"Missing native window flag: {label}")
                check(window["styles"] & 0x80000 == 0, f"Unexpected layered window: {label}")
                check(window["windowStyles"] & 0x98000000 == 0x98000000, f"Wrong popup/disabled/visible style: {label}")
                check(window["visible"] and not window["enabled"] and window["affinity"] == 17,
                      f"Window visibility/input/capture state failed: {label}")
                check(window["edgeHitThrough"] and window["centerHitThrough"], f"Necessary hit-test guard failed: {label}")
                x, y, width, height = map(int, re.findall(r"-?\d+", monitor["workArea"]))
                check(window["rect"] == {"Left": x, "Top": y, "Right": x + width, "Bottom": y + height},
                      f"Native working-area coordinates differ: {label}")
                check(window["dpi"] == 96 * monitor["DpiX"], f"Native DPI differs: {label}")
                check(comparison["DeviceName"] == monitor["DeviceName"] and
                      all(value == 0 for value in comparison["comparison"].values()),
                      f"Upload changed native-DPI pixels: {label}")
            check(len(result["startGpu"]) == len(result["endGpu"]) == 4, f"Missing GPU counters: {label}")
            gpu_delta = [{key: end[key] - start[key] for key in start}
                         for start, end in zip(result["startGpu"], result["endGpu"])]
            check(all(delta["Readbacks"] == 0 and delta["Draws"] == delta["Presents"] for delta in gpu_delta),
                  f"Readback or missing present during timing: {label}")
            if metadata["lane"] != "color":
                check(all(delta["Uploads"] == delta["UploadBytes"] == 0 for delta in gpu_delta),
                      f"Unexpected timed upload: {label}")
            if metadata["lane"] == "hover":
                check(gpu_delta[0]["Draws"] == result["ticks"] and
                      all(delta["Draws"] == 0 for delta in gpu_delta[1:]), f"Hover work changed: {label}")
            elif metadata["lane"] == "monitors":
                check(all(delta["Draws"] > 0 for delta in gpu_delta), f"Monitor trace skipped a surface: {label}")
            elif metadata["lane"] == "brightness":
                check(all(delta["Draws"] == result["ticks"] for delta in gpu_delta),
                      f"Brightness did not reach all monitors: {label}")
            elif metadata["lane"] == "off":
                check(result["ticks"] == 0 and all(delta["Draws"] == 0 for delta in gpu_delta),
                      f"Light-off kept rendering: {label}")

        runs.append({"path": label, "arm": metadata["arm"], "lane": metadata["lane"],
                     "cpu": stats([sample["appCorePercent"] for sample in result["samples"]]),
                     "ticks": result["ticks"], "maxUpdateMs": result["maxUpdateMs"],
                     "tickMs": stats(result["tickIntervalsMs"]) if result["tickIntervalsMs"] else None,
                     "gpuDelta": gpu_delta, "resourcesStart": result["startResources"],
                     "resourcesEnd": result["endResources"]})

    intervals.sort()
    for previous, current in zip(intervals, intervals[1:]):
        check(current[0] > previous[1], f"Overlapping measurement hosts: {previous[2]}, {current[2]}")
    for relative, expected in (
        (r"tools\OverlayBench\bin\control-b927edd\WindowsEdgeLight.dll", CONTROL),
        (r"tools\GpuOverlayBench\bin\measured-v2\WindowsEdgeLight.dll", CONTROL),
        (r"tools\GpuOverlayBench\bin\measured-v2\GpuOverlayBench.dll", HOST),
    ):
        check(hashlib.sha256(Path(relative).read_bytes()).hexdigest().upper() == expected,
              f"Frozen binary changed: {relative}")
    old_manifest = load(Path(r"tools\OverlayBench\bin\evidence\build-manifest.json"))
    for item in old_manifest:
        check(hashlib.sha256(Path(item["path"]).read_bytes()).hexdigest().upper() == item["sha256"],
              f"Previous frozen evidence changed: {item['path']}")
    pdb_documents = load(root / "pdb-source-hashes.json")
    for name in ("ApiProbe.cs", "GpuRenderer.cs", "Program.cs"):
        document = next(item for item in pdb_documents if Path(item["path"]).name == name)
        snapshot = root / "source-measured-v2" / name
        check(hashlib.sha256(snapshot.read_bytes()).hexdigest().upper() == document["hash"],
              f"Measured source differs from compiler-recorded PDB checksum: {name}")
    lifetime = load(root / "lifetime-ten-cycles.json")
    check(lifetime["cycles"] == 10 and len(lifetime["rows"]) == 11, "Lifetime cycles incomplete.")
    for metric in ("handles", "gdi", "user"):
        check(len({row["resources"][metric] for row in lifetime["rows"][1:]}) == 1,
              f"Lifetime {metric} grew after first initialization.")

    return {
        "status": "Evidence integrity and necessary rendering guards passed; NOT a production acceptance.",
        "verifiedRuns": len(runs), "nativeWindowChecks": native_windows, "settingsBatches": len(BATCHES),
        "priorManifestFilesVerified": len(old_manifest), "runs": runs, "lifetime": lifetime,
        "acceptance": {
            "productionReady": False,
            "reason": "Sustained/mixed-monitor p90 exceeds 10%; exact color updates stall; integration/E2E gaps remain.",
            "limitations": "Window flags and GPU readback do not prove physical cross-process clicks, actual capture exclusion, desktop compositing fidelity, device-loss recovery or leak freedom."
        }
    }


if __name__ == "__main__":
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r"tools\GpuOverlayBench\bin\evidence")
    print(json.dumps(verify(root), indent=2))
