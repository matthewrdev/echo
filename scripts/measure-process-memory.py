#!/usr/bin/env python3
"""Record whole-process high-water RSS, including native allocations, on macOS/Linux."""
import json
import platform
import resource
import subprocess
import sys
import time
from pathlib import Path

output = Path(sys.argv[1])
started = time.perf_counter()
with output.with_suffix(".log").open("w") as stream:
    result = subprocess.run(sys.argv[2:], stdout=stream, check=False)
usage = resource.getrusage(resource.RUSAGE_CHILDREN)
record = {
    "command": sys.argv[2:],
    "exitCode": result.returncode,
    "wallSecondsIncludingStartup": time.perf_counter() - started,
    "peakResidentBytes": usage.ru_maxrss * (1 if platform.system() == "Darwin" else 1024),
    "measurement": "Whole-process high-water RSS; includes native memory, warm-up and all benchmark iterations."
}
output.write_text(json.dumps(record, indent=2) + "\n")
print(json.dumps(record))
raise SystemExit(result.returncode)
