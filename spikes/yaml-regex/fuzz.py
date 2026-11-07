# /// script
# requires-python = ">=3.12"
# dependencies = ["pyyaml"]
# ///
"""Mutate the suite's plans and look for hangs and crashes (not YamlException).

usage: uv run fuzz.py <spike-binary> <blokky-specs dir> <scratch dir> <mutants per plan>
"""

import base64
import pathlib
import random
import subprocess
import sys

import yaml

BIN, SPECS, SCRATCH, N = sys.argv[1], pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3]), int(sys.argv[4])
ALPHABET = list(":[]{},'\"-?&*!#|>%@` \n\t") + ["  ", "\n  ", "...", "---", "<<"]
rng = random.Random(7)
SCRATCH.mkdir(parents=True, exist_ok=True)
plans = []
for p in sorted((SPECS / "conformance" / "cases").rglob("*.yaml")):
    c = yaml.safe_load(p.read_text())
    if "plan" in c:
        plans.append(c["plan"])
    elif "plan_base64" in c:
        plans.append(base64.b64decode(c["plan_base64"]).decode("utf-8", "replace"))
runs = hangs = crashes = 0
f = SCRATCH / "m.plan"
for text in plans:
    for _ in range(N):
        t = text
        for _ in range(rng.randint(1, 3)):
            i = rng.randrange(len(t) + 1)
            if rng.random() < 0.5 and i < len(t):
                t = t[:i] + t[i + 1 :]
            else:
                t = t[:i] + rng.choice(ALPHABET) + t[i:]
        f.write_text(t)
        runs += 1
        try:
            r = subprocess.run([BIN, "plan", str(f)], capture_output=True, text=True, timeout=5, check=False)
        except subprocess.TimeoutExpired:
            hangs += 1
            (SCRATCH / f"hang-{hangs}.plan").write_text(t)
            continue
        if r.returncode not in (0, 1):
            crashes += 1
            (SCRATCH / f"crash-{crashes}.plan").write_text(t)
            if crashes <= 5:
                print("crash:", r.stderr.splitlines()[:2])
print(f"plans {len(plans)}, mutants {runs}, hangs {hangs}, crashes {crashes}")
