"""N3 재현 대조: transition_vs_still.md 22개 값 ↔ main 그룹 하네스 호버 시간, 분위수 규칙 3종.

실행(저장소 루트): python3 bench/2026-09-23_playmode/summary/n3_checks/check_transition_rules.py
"""
import sys, json, statistics
sys.path.insert(0, "bench")
import summarize as s
from pathlib import Path
root = Path("bench/2026-09-23_playmode")
man = json.loads((root/"runs_manifest.json").read_text()); sums = s.load_summaries(root/"summary")
runs = [s.load_run(root, k, sums[k]) for k in man["groups"]["main"]]
cells = s.transition_split(runs)
doc = {("A","S0"):(686.5,1136.2,None,None),("A","T1"):(None,None,19.1,29.5),("B","S0"):(752.8,1198.8,None,None),
       ("B","T1"):(371.9,632.4,21.7,47.4),("C","S0"):(746.2,1212.7,None,None),("C","T1"):(401.5,649.0,21.5,46.2),
       ("D","S0"):(705.2,1175.1,None,None),("D","T1"):(386.5,626.8,21.6,45.9)}
rules = {"stat+round": lambda v,p: statistics.median(v) if p==50 else s.percentile(v,p),
         "round(n-1)p both": lambda v,p: s.percentile(v,p),
         "floor both": lambda v,p: s.floor_percentile(v,p)}
for rname, f in rules.items():
    hits = tot = 0; miss = []
    for (sc, var), exp in doc.items():
        c = cells[(var, sc)]
        for (bucket, p), e in zip((("fired",50),("fired",95),("still",50),("still",95)), exp):
            if e is None: continue
            vals = c[bucket]["harness"]; got = round(f(vals,p)/1000, 1); tot += 1
            if abs(got-e) < 1e-9: hits += 1
            else: miss.append((sc,var,bucket,p,e,got))
    print(f"{rname:18s} {hits}/{tot}", miss)
# raw 2 values in the midpoint: show exact middle values for the two disputed cells
for key in (("S0","B"),("T1","D")):
    v = sorted(cells[key]["fired"]["harness"]); n=len(v); print(key, n, v[n//2-1], v[n//2])
