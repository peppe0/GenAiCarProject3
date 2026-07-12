import csv, sys, re, statistics
from collections import defaultdict

# Uso:
#   python3 analyze_test.py file1.csv file2.csv ...
#   python3 analyze_test.py *.csv        (tutti i CSV della cartella)
# Raggruppa per 'circuit' (= circuitLabel, es. ppo_normal_s1) e, se le etichette
# finiscono con _s<numero>, calcola anche la media +/- std sui seed per condizione.

files = sys.argv[1:]
if not files:
    print("Passa uno o piu' file CSV: python3 analyze_test.py *.csv")
    sys.exit(1)

rows = []
for path in files:
    try:
        rows.extend(csv.DictReader(open(path)))
    except Exception as e:
        print(f"! salto {path}: {e}")

# --- 1) per etichetta completa (modello+seed) ---
g = defaultdict(list)
for r in rows:
    g[r["circuit"]].append(r)

def succ_rate(rs):
    return sum(int(r["success"]) for r in rs) / len(rs)
def lap_mean(rs):
    laps = [float(r["lap_time_s"]) for r in rs if r["success"] == "1" and r["lap_time_s"]]
    return sum(laps) / len(laps) if laps else float("nan")

print(f'{"label":<20}{"episodi":>8}{"success":>10}{"lap_s":>10}')
print("-" * 48)
for k in sorted(g):
    rs = g[k]
    print(f'{k:<20}{len(rs):>8}{succ_rate(rs):>10.3f}{lap_mean(rs):>10.2f}')

# --- 2) media sui seed per condizione (togliendo il suffisso _s<numero>) ---
cond = defaultdict(list)   # condizione -> lista di success_rate per seed
for k, rs in g.items():
    base = re.sub(r'_s\d+$', '', k)   # ppo_normal_s1 -> ppo_normal
    cond[base].append(succ_rate(rs))

print("\n=== MEDIA SUI SEED (per la tabella del report) ===")
print(f'{"condizione":<20}{"n_seed":>7}{"success_media":>15}{"std":>8}')
print("-" * 50)
for k in sorted(cond):
    vals = cond[k]
    m = statistics.mean(vals)
    s = statistics.pstdev(vals) if len(vals) > 1 else 0.0
    print(f'{k:<20}{len(vals):>7}{m:>15.3f}{s:>8.3f}')
    print(f'   LaTeX -> ... & ${m:.2f} \\pm {s:.2f}$ \\\\')
