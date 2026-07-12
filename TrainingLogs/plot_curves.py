import glob, sys, numpy as np, matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

# Genera un grafico con 4 linee (media sui seed) + banda +/-std.
# Uso:  python3 plot_curves.py [tag] [out.png]
#   tag  = metrica da plottare (default 'Car/Success'; es. 'Environment/Cumulative Reward')
#   out  = file di uscita (default jump_success_curves.png)

TAG = sys.argv[1] if len(sys.argv) > 1 else "Car/Success"
OUT = sys.argv[2] if len(sys.argv) > 2 else "/Users/giuseppeabbatiello/Downloads/jump_success_curves.png"

BASE = "/Users/giuseppeabbatiello/Downloads/ml-agents-release_22 2022/Project/Assets/ML-Agents/Examples/CarProject/run/jumpCircuit"
conds = {
    "PPO normal":     ("PPO/normal",     "#1f77b4"),
    "PPO curriculum": ("PPO/curriculum", "#2ca02c"),
    "SAC normal":     ("SAC/normal",     "#ff7f0e"),
    "SAC curriculum": ("SAC/curriculum", "#d62728"),
}

def load(path):
    ea = EventAccumulator(path, size_guidance={'scalars': 0}); ea.Reload()
    if TAG not in ea.Tags().get('scalars', []): return None
    ev = ea.Scalars(TAG)
    return np.array([e.step for e in ev]), np.array([e.value for e in ev])

def ema(y, w=0.6):
    out = np.copy(y).astype(float)
    for i in range(1, len(y)): out[i] = w*out[i-1] + (1-w)*y[i]
    return out

plt.figure(figsize=(7, 4.3))
for name, (sub, col) in conds.items():
    seeds = []
    for d in sorted(glob.glob(f"{BASE}/{sub}/*/")):
        fs = glob.glob(d + "events.out.tfevents.*")
        if fs:
            r = load(sorted(fs)[0])
            if r: seeds.append(r)
    if not seeds: continue
    max_step = min(s[0][-1] for s in seeds)
    grid = np.linspace(0, max_step, 200)
    curves = np.array([np.interp(grid, s[0], s[1]) for s in seeds])
    mean = ema(curves.mean(axis=0)); std = curves.std(axis=0)
    plt.plot(grid/1e6, mean, color=col, label=f"{name} (n={len(seeds)})", lw=2)
    plt.fill_between(grid/1e6, mean-std, mean+std, color=col, alpha=0.15)

plt.xlabel("Training steps (millions)")
plt.ylabel(TAG)
plt.title(f"Jump track: {TAG} vs. steps (seed-averaged, $\\pm$std)")
plt.legend(loc="best", fontsize=9)
plt.grid(alpha=0.3)
plt.tight_layout()
plt.savefig(OUT, dpi=150)
print("SALVATO:", OUT)
