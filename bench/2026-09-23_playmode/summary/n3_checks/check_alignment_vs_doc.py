"""N3 재현 대조: frame_alignment.md 런별 54행 ↔ 집계기 출력(all).

실행(저장소 루트): python3 bench/2026-09-23_playmode/summary/n3_checks/check_alignment_vs_doc.py bench/2026-09-23_playmode/summary/summarize_output_all.md
구조 열(오프셋·창·마커·쌍·최대·초과·불일치) 일치 여부와, 중앙값·p99가 현재 규칙/floor 규칙 중 어느 쪽과 맞는지 센다.
"""
import re, sys
doc = open("bench/2026-09-23_playmode/summary/frame_alignment.md").read()
out = open(sys.argv[1]).read()
num = lambda x: float(x.replace(",", "").replace(" ns", "").replace("**", "").replace("+", "").strip())
d = {}
for line in doc.splitlines():
    m = re.match(r"\| `(\w+)` \| (\+\d) \| (\d+) \| (\d) \| ([\d,]+) \| ([\d,]+) ns \| ([\d,]+) ns \| ([\d,]+) ns \| \*\*(\d+)\*\* \| (\d+) \|", line)
    if m: d[m.group(1)] = m.groups()[1:]
o = {}
sec = out[out.index("## 프레임 대응 요약"):]
for line in sec.splitlines():
    m = re.match(r"\| `(\w+)` \| ([+\-]\d) \| (\d+) \| (\d) \| ([\d,]+) \| ([\d,.]+) \[([\d,.]+)\](?: ⟨[\d,.]+⟩)? \| ([\d,.]+) \[([\d,.]+)\] \| ([\d,]+) \| (\d+) \| (\d+) \|", line)
    if m: o[m.group(1)] = m.groups()[1:]
print("doc rows", len(d), "out rows", len(o), "missing", set(d) ^ set(o))
stat = {"median_cur": 0, "median_floor": 0, "p99_cur": 0, "p99_floor": 0}
bad = []
for k, (off, win, mk, pairs, med, p99, mx, exc, mis) in d.items():
    (o_off, o_win, o_mk, o_pairs, o_med, o_medf, o_p99, o_p99f, o_mx, o_exc, o_mis) = o[k]
    for name, a, b in (("offset", off, o_off), ("win", win, o_win), ("markers", mk, o_mk), ("pairs", pairs, o_pairs),
                       ("max", mx, o_mx), ("exceed", exc, o_exc), ("mismatch", mis, o_mis)):
        if num(a) != num(b): bad.append((k, name, a, b))
    stat["median_cur"] += num(med) == num(o_med); stat["median_floor"] += num(med) == num(o_medf)
    stat["p99_cur"] += num(p99) == num(o_p99); stat["p99_floor"] += num(p99) == num(o_p99f)
    if num(med) != num(o_medf) or num(p99) != num(o_p99f):
        print("floor mismatch", k, med, o_med, o_medf, p99, o_p99, o_p99f)
    if num(med) != num(o_med) or num(p99) != num(o_p99):
        print("  cur mismatch", k, "med", med, o_med, "p99", p99, o_p99)
print("structural mismatches:", bad)
print("matches out of", len(d), stat)
