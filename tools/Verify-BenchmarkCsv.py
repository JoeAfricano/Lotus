"""Independent arithmetic check of Lotus against the preserved PresentMon CSV (not an independent ETW collector)."""
import csv
import json
import math
import statistics
import sys
from pathlib import Path

session = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
streams = {}
with open(session["RawPath"], newline="", encoding="utf-8-sig") as source:
    for row in csv.DictReader(source):
        if int(row["ProcessID"]) != session["Target"]["Pid"] or row.get("FrameType", "Application") != "Application":
            continue
        key = row["SwapChainAddress"]
        if key not in streams:
            streams[key] = []
            continue
        value = float(row["MsBetweenPresents"])
        if math.isfinite(value) and value > 0:
            streams[key].append(value)
values = sorted(max(streams.values(), key=len))
tail = math.ceil(len(values) / 100)
reference = {
    "Count": len(values),
    "AverageFps": 1000 / statistics.mean(values),
    "Low1Fps": 1000 / statistics.mean(values[-tail:]),
    "P95Ms": values[math.ceil(len(values) * .95) - 1],
    "P99Ms": values[math.ceil(len(values) * .99) - 1],
}
lotus = session["Data"]["Statistics"]
checks = {key: math.isclose(value, lotus[key], rel_tol=1e-10, abs_tol=1e-10) for key, value in reference.items()}
print(json.dumps({"reference": reference, "matches_lotus": checks, "all_matched": all(checks.values())}, indent=2))
if not all(checks.values()):
    raise SystemExit(1)
