#!/usr/bin/env python3
"""Plot per-kernel measured changes and separately measured whole-process peak RSS."""
import csv
import json
import os
from pathlib import Path
from statistics import median
import argparse

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('input', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
args.output.parent.mkdir(parents=True, exist_ok=True)
os.environ.setdefault('MPLCONFIGDIR', str(args.output.parent / '.matplotlib'))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

names = [('depth', 'Depth\n8 photos · 1024 px'), ('atlas', '8K atlas + GLB\n2,400 triangles'),
         ('bundle', 'Bundle adjustment\n12 cameras · 120 tracks'), ('features', 'Points + lines\n3 photos · 2000 px')]
fig, axes = plt.subplots(1, 3, figsize=(14, 5.5), layout='constrained', gridspec_kw={'width_ratios':[1.5,1.5,1]})
fig.suptitle('Measured core processing changes', fontsize=18, fontweight='bold')
for axis, key, title in zip(axes[:2], ['seconds','managed_allocated_bytes'], ['Elapsed time', 'Managed allocation traffic']):
    for index, (name, label) in enumerate(names):
        rows = list(csv.DictReader((args.input / f'{name}-improvement.csv').open()))
        medians = [median(float(r[key]) for r in rows if r['revision']==group and int(r['iteration'])>0) for group in ['before','after']]
        ratio = medians[1] / medians[0] * 100
        axis.barh(index, ratio, height=.55, color='#168a86' if ratio<=100 else '#c47736')
        axis.text(ratio+2,index,f'{ratio-100:+.1f}%',va='center',fontsize=10)
    axis.axvline(100,color='#5f6874',linestyle='--',linewidth=1)
    axis.set_xlim(0,132)
    axis.set_yticks(range(len(names)), [label for name,label in names] if key=='seconds' else [])
    axis.invert_yaxis()
    axis.set_xlabel('After / before (%) · lower is better')
    axis.set_title(title)
    axis.spines[['top','right']].set_visible(False)
values=[]
for group in ('before','after'):
    record=json.loads((args.input/f'{group}-rss.json').read_text())
    values.append(record['peakResidentBytes']/1e6)
axes[2].bar([0,1],values,color=['#a5afbc','#168a86'],width=.55)
for i,value in enumerate(values): axes[2].text(i,value+20,f'{value:,.0f} MB',ha='center')
axes[2].set_ylim(0,max(values)*1.2)
axes[2].set_xticks([0,1],['Before','After'])
axes[2].set_title('Whole-process peak RAM\n8K core fixture')
axes[2].set_ylabel('Peak resident set (MB)')
axes[2].spines[['top','right']].set_visible(False)
fig.supxlabel('Kernel bars: medians of five warm passes; identical output hashes. Small time changes are inconclusive.\nPeak RAM: one separate process per version, including native buffers. CPU only; no end-to-end reconstruction speedup claimed.',fontsize=10)
fig.savefig(args.output.with_suffix('.png'),dpi=170)
fig.savefig(args.output.with_suffix('.svg'))
svg = args.output.with_suffix('.svg')
svg.write_text('\n'.join(line.rstrip() for line in svg.read_text().splitlines()) + '\n')
print(args.output.with_suffix('.png'))
