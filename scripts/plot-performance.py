#!/usr/bin/env python3
"""Graph benchmark JSON streams; iteration zero is the excluded warm-up."""
import argparse
import csv
import json
import os
from pathlib import Path
from statistics import median

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("before", type=Path)
parser.add_argument("after", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--title", default="Depth search · 8 photos at 1024 px")
parser.add_argument("--stage")
parser.add_argument("--after-label", default="Span + cached geometry")
parser.add_argument("--context", default="CPU · 2 images × 8 workers · same saved inputs")
args = parser.parse_args()
args.output.parent.mkdir(parents=True, exist_ok=True)
os.environ.setdefault("MPLCONFIGDIR", str(args.output.parent / ".matplotlib"))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

def records(path):
    text = path.read_text().strip()
    decoder = json.JSONDecoder()
    result = []
    while text:
        value, end = decoder.raw_decode(text)
        result.append(value)
        text = text[end:].strip()
    return [row for row in result if args.stage is None or row.get("stage") == args.stage]

groups = [records(args.before), records(args.after)]
hashes = {row["hash"] for rows in groups for row in rows}
if len(hashes) != 1:
    raise SystemExit("Output hashes differ; do not present this as a parity-preserving improvement.")
with args.output.with_suffix(".csv").open("w") as stream:
    writer = csv.writer(stream, lineterminator="\n")
    writer.writerow(["revision", "iteration", "seconds", "managed_allocated_bytes", "hash"])
    for name, rows in zip(["before", "after"], groups):
        for row in rows:
            writer.writerow([name, row["iteration"], row["seconds"], row["allocatedBytes"], row["hash"]])

fig, axes = plt.subplots(1, 2, figsize=(10, 4.5), layout="constrained")
fig.suptitle(args.title, fontsize=16, fontweight="bold")
for axis, key, scale, label in zip(axes, ["seconds", "allocatedBytes"], [1, 1e6],
                                    ["Compute time (seconds)", "Managed allocations (MB / pass)"]):
    samples = [[row[key] / scale for row in rows if row["iteration"] > 0] for rows in groups]
    values = [median(sample) for sample in samples]
    axis.bar([0, 1], values, color=["#a5afbc", "#148a86"], width=.58)
    for index, sample in enumerate(samples):
        axis.plot([index] * len(sample), sample, "o", color="#263b4e", markersize=3)
        axis.text(index, max(sample) + max(values) * .035, f"{values[index]:,.3f}" if scale == 1 else f"{values[index]:,.0f}", ha="center")
    reduction = (1 - values[1] / values[0]) * 100
    axis.set_title(f"{reduction:.1f}% reduction", fontsize=13)
    axis.set_ylabel(label)
    axis.set_xticks([0, 1], ["Before", args.after_label])
    axis.set_ylim(0, max(max(s) for s in samples) * 1.22)
    axis.spines[["top", "right"]].set_visible(False)
fig.supxlabel("Medians of 5 warm passes; dots = individual passes. Identical output hashes.\nAllocation traffic is not peak RAM. " + args.context, fontsize=9)
fig.savefig(args.output.with_suffix(".png"), dpi=170)
fig.savefig(args.output.with_suffix(".svg"))
svg = args.output.with_suffix(".svg")
svg.write_text("\n".join(line.rstrip() for line in svg.read_text().splitlines()) + "\n")
print(args.output.with_suffix(".png"))
