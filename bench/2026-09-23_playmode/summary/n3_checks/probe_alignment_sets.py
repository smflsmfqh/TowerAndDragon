"""N3 진단: frame_alignment.md의 "중앙 126", "+0 166 → +1 63,792"가 어느 비교 집합의 값인지 후보를 찾는다.

실행(저장소 루트): python3 bench/2026-09-23_playmode/summary/n3_checks/probe_alignment_sets.py
후보 탐색이며 재현 판정이 아니다. 사전 고정 정의(11 N3 설계 3) 밖의 정의를 채택하는 데 쓰지 않는다.
"""
import sys, json, statistics
sys.path.insert(0, "bench")
import summarize as s
from pathlib import Path
root = Path("bench/2026-09-23_playmode")
man = json.loads((root/"runs_manifest.json").read_text()); sums = s.load_summaries(root/"summary")
res = {}
for g in ("main", "supplemental"):
    for k in man["groups"][g]:
        res[k] = (g, s.alignment_run(s.load_run(root, k, sums[k])))
fl = lambda v: s.floor_percentile(v, 50)
def show(label, vals): print(f"{label:55s} n={len(vals):6d} median={statistics.median(vals)} floor={fl(vals)}")
for g in ("main", "supplemental"):
    show(f"group {g} offset0", [abs(p["diff"]) for gg, r in res.values() if gg == g for p in r["pairs"]])
    show(f"group {g} offset+1", [v for gg, r in res.values() if gg == g for v in r["offset_abs"][1]])
allp = [p for _, r in res.values() for p in r["pairs"]]
show("all excluding exceed", [abs(p["diff"]) for p in allp if not p["exceeds"]])
show("all signed diff", [p["diff"] for p in allp])
for m in sorted({p["marker"] for p in allp}):
    show(f"marker {m}", [abs(p["diff"]) for p in allp if p["marker"] == m])
show("T1_D_run1 all", [abs(p["diff"]) for p in res["T1_D_run1"][1]["pairs"]])
show("T1_D_run1 hover", [abs(p["diff"]) for p in res["T1_D_run1"][1]["pairs"] if p["marker"] == "t1_hover"])
print("--- runs/markers with +1 median near 63,792, or 166 at +0")
for k, (g, r) in res.items():
    for m in s.VARIANT_MARKERS[k[:2]]:
        p0 = [abs(p["diff"]) for p in r["pairs"] if p["marker"] == m]
        if not p0: continue
        # recompute +1 per marker
        run = s.load_run(root, k, sums[k]); w = run.window
        h = [int(run.harness[i][s.column(m, "_ns")]) for i in range(w)]
        rr = [int(run.raw[i][s.column(m, "_ns")]) for i in range(w)]
        p1 = [abs(h[i] - rr[i+1]) for i in range(w) if h[i] > 0 and i + 1 < w]
        for name, f in (("stat", statistics.median), ("floor", fl)):
            a, b = f(p0), f(p1) if p1 else None
            if b is not None and abs(b - 63792) <= 100: print("  +1 match", k, m, name, a, b)
    v1 = r["offset_abs"][1]
    for name, f in (("stat", statistics.median), ("floor", fl)):
        if abs(f(v1) - 63792) <= 100: print("  run-level +1 match", k, name, f([abs(p["diff"]) for p in r["pairs"]]), f(v1))
