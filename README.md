# Autonomous Racing with Deep Reinforcement Learning
### A Comparative Study of PPO and SAC with Curriculum Learning

A Unity ML-Agents project that trains a self-driving racing car with deep reinforcement
learning, comparing **PPO** and **SAC** — with and without **curriculum learning** — on
tracks of increasing difficulty culminating in a jump.

**Authors:** Giuseppe Abbatiello (s346795) · Daniele Cecconata (s346832) · Marianna Mercurio (s349500)

---

## Overview

An agent must drive a car around procedurally distinct race tracks, with two objectives:
1. complete a lap without leaving the track boundaries;
2. minimize lap time.

We train and compare two continuous-control RL algorithms — Proximal Policy Optimization
(**PPO**) and Soft Actor-Critic (**SAC**) — and study the effect of **curriculum learning**
(pretraining on simple tracks, then transferring to harder ones) against training directly
on the target track. Models are evaluated on a **held-out test track** and on **hand-designed
scenarios** (hairpin, hill, jump).

- **Engine / framework:** Unity + [ML-Agents](https://github.com/Unity-Technologies/ml-agents) (release 22, package 3.0.0)
- **Vehicle physics:** MSVehicleSystem (free version)
- **Assets:** Kenney Racing Kit

### Agent design
- **Observation (148 features):** three `RayPerceptionSensor3D` (one each for `wall`, `road`,
  `checkpoint`) + a 7-value kinematic/goal vector (3D direction to next checkpoint, 3D local
  velocity, normalized distance).
- **Actions:** 2 continuous — `[0]` acceleration/braking, `[1]` steering.
- **Reward:** dense progress toward the next checkpoint + per-checkpoint bonus, lap-completion
  bonus scaled by lap time, wall-crash penalty, out-of-sequence penalty, and a training-only
  "launch-zone" speed bonus before the jump. See `CarAgent.cs`.

---

## Repository structure

```
Project/
├── Assets/ML-Agents/Examples/CarProject/
│   ├── Scripts/CarAgent.cs           # Agent logic, observations, reward, CSV logging
│   ├── Scripts/Editor/WallTagChecker.cs
│   ├── Scenes/                       # Training & test tracks (the "dataset")
│   ├── Prefabs/ , Models/ , Materials/
│   └── run/                          # TensorBoard logs + exported .onnx (per condition/seed)
├── config/                           # ML-Agents trainer configs (see below)
└── TrainingLogs/                     # CSV evaluation logs + analysis scripts
```

### Training configs (`config/`)
| File | Purpose |
|---|---|
| `ppo_normal.yaml` / `sac_normal.yaml` | PPO / SAC trained directly on the target track (5M steps) |
| `ppo_curriculum.yaml` | (automatic-curriculum template) |
| `ppo_cur_p1/p2/p3.yaml` | PPO curriculum phases: closed → complex → jump |
| `sac_cur_p1/p2/p3.yaml` | SAC curriculum phases: closed → complex → jump |

Curriculum step split (total 5M): **closed 0.9M → complex 1.5M → jump 2.6M** (adjust as needed).

---

## Requirements

- Unity (with the tracks/scenes opened from `Assets/ML-Agents/Examples/CarProject/Scenes`)
- Python 3.10 + `mlagents==1.1.0` (`pip install mlagents`)
- PyTorch 2.2.x (CPU is sufficient)

---

## Training

Run from the folder that contains your build(s) and the `results/` directory.

**Direct (normal) training:**
```bash
mlagents-learn config/ppo_normal.yaml --run-id=ppo_jump_s1 \
  --env=./jumpBuild.app --no-graphics --num-envs=2 --seed=1
```

**Curriculum (sequential transfer):** train each phase and seed the next with `--initialize-from`.
```bash
# phase 1 – closed (from scratch)
mlagents-learn config/ppo_cur_p1.yaml --run-id=ppo_cur_closed_s1 --env=./closedBuild.app ...
# phase 2 – complex (from closed)
mlagents-learn config/ppo_cur_p2.yaml --run-id=ppo_cur_complex_s1 \
  --initialize-from=ppo_cur_closed_s1 --env=./complexBuild.app ...
# phase 3 – jump (from complex)
mlagents-learn config/ppo_cur_p3.yaml --run-id=ppo_cur_jump_s1 \
  --initialize-from=ppo_cur_complex_s1 --env=./jumpBuild.app ...
```
> SAC curriculum is identical with the `sac_cur_p*.yaml` configs.
> `--initialize-from` must point to a run of the **same algorithm** (never mix PPO and SAC weights).

**Monitor:**
```bash
tensorboard --logdir results        # or the run/ folder with the archived logs
```

---

## Testing (inference)

In Unity, set the agent's **Behavior Type = Inference Only** and drop the final
`results/<run-id>/CarAgent.onnx` into the **Model** field, then open a **test scene**
(different from the training tracks) and press Play. Each episode is logged to a CSV.

- Set `Circuit Label` on the `CarAgent` component to `<algo>_<method>_s<seed>` (e.g. `ppo_curriculum_s2`).
- Use a fresh `Csv File Name` (e.g. `testing_log.csv`).
- For **scenarios**, enable `Use Fixed Spawn` and pick the `Fixed Spawn Index` of the critical segment.

Analyze the logs:
```bash
python3 TrainingLogs/analyze_test.py TrainingLogs/*.csv   # success rate + lap time, seed-averaged
python3 TrainingLogs/plot_curves.py "Car/Success"         # training curves (seed-averaged, ±std)
```

---

## Results (summary)

| Metric (jump track) | PPO normal | PPO curriculum | SAC normal | SAC curriculum |
|---|---|---|---|---|
| Training success (final) | 0.65 | **0.75** | 0.33 | 0.18 |
| Held-out test success | 0.08 | **0.18** | 0.00 | 0.00 |

**Key findings:** PPO learns a *stable* policy on the hard jump track and generalizes better;
SAC is more sample-efficient on easy tracks but *unstable* on the jump (its policy entropy
collapses and it crashes most of the time). Curriculum learning provides a clear warm-start and
markedly improves PPO's generalization, but does not fix SAC's instability.

---

## Deliverables

- **Code:** this repository
- **Trained weights:** `Assets/ML-Agents/Examples/CarProject/run/**/*.onnx`
- **Dataset / tracks:** `Assets/ML-Agents/Examples/CarProject/Scenes`
- **Paper:** 6–8 page report (PPO vs SAC, curriculum, hyper-parameters, metrics, results)
