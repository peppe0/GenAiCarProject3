import csv, sys
from collections import defaultdict
rows=list(csv.DictReader(open(sys.argv[1])))
g=defaultdict(list)
for r in rows: g[r["circuit"]].append(r)   # raggruppa per modello (circuitLabel)
print(f'{"model":<14}{"episodi":>8}{"success_rate":>14}{"laptime_medio":>15}')
print("-"*51)
for k in sorted(g):
    e=g[k]; n=len(e)
    succ=sum(int(r["success"]) for r in e)/n
    laps=[float(r["lap_time_s"]) for r in e if r["success"]=="1" and r["lap_time_s"]]
    lap=sum(laps)/len(laps) if laps else float("nan")
    print(f'{k:<14}{n:>8}{succ:>14.3f}{lap:>15.2f}')
    # riga pronta per LaTeX:
    print(f'   LaTeX -> ... & {succ:.2f} & {lap:.1f} \\\\')
