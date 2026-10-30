"""Verify saved window/trace guards without driving the user's desktop."""
import json
import pathlib
import sys

checks = []
for directory in sys.argv[1:]:
    for path in sorted(pathlib.Path(directory).glob("pair-*/result.json")):
        result = json.loads(path.read_text(encoding="utf-8-sig"))
        metadata = result["metadata"]
        windows = metadata["windowInfo"]
        assert len(windows) in (4, 20), (path, "unexpected window count")
        for w in windows:
            assert w["IsVisible"]
            assert w["styles"] & 0x80028 == 0x80028, (path, "not layered/click-through/topmost")
            assert w["affinity"] == 17, (path, "capture exclusion missing")
        hole = metadata["holeChecks"]
        assert hole["firstCenterAlpha"] <= 2 and hole["secondCenterAlpha"] <= 2
        assert hole["firstSolidAlpha"] >= 180 and hole["secondSolidAlpha"] >= 180
        assert result["ticks"] >= metadata["duration"] * 19, (path, "trace starved")
        if len(windows) == 20:
            for index, owner in enumerate(windows[:4]):
                o = owner["rect"]
                width, height = o["Right"] - o["Left"], o["Bottom"] - o["Top"]
                top, bottom, left, right = [w["rect"] for w in windows[4 + 4 * index:8 + 4 * index]]
                assert top["Top"] == o["Top"] and bottom["Bottom"] == o["Bottom"]
                assert top["Left"] == bottom["Left"] == left["Left"] == o["Left"]
                assert top["Right"] == bottom["Right"] == right["Right"] == o["Right"]
                assert top["Bottom"] == left["Top"] == right["Top"]
                assert bottom["Top"] == left["Bottom"] == right["Bottom"]
                assert left["Right"] < right["Left"]
                assert 0 < top["Bottom"] - top["Top"] < height / 2
                assert 0 < left["Right"] - left["Left"] < width / 2
        checks.append({
            "path": str(path), "windowFlagsCaptureAndCoverage": "pass",
            "movingHolePixels": "pass", "traceTicks": result["ticks"],
            "gdiDelta": result["endResources"]["gdi"] - result["startResources"]["gdi"],
            "userDelta": result["endResources"]["user"] - result["startResources"]["user"],
            "handleDelta": result["endResources"]["handles"] - result["startResources"]["handles"],
            "privateBytesDelta": result["endResources"]["privateBytes"] - result["startResources"]["privateBytes"]
        })
print(json.dumps({
    "runs": checks,
    "limitations": "Flags/geometry are necessary but not a functional click-through, z-order, capture, or hotplug test. Twelve-second resource deltas are not a leak soak."
}, indent=2))
