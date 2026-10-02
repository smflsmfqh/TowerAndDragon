"""N3 결함 주입: bench/summarize.py 사본을 일부러 망가뜨려 회귀 테스트가 실패하는지 본다.

실행(저장소 루트): python3 bench/2026-09-23_playmode/summary/n3_checks/inject_faults.py
원본 파일은 건드리지 않는다. 임시 디렉터리에 사본을 만든다.
floor_float는 결함이 아니라 같은 동작의 변형이다 - p ∈ {50, 95, 99, 99.9}, n < 6,000에서 두 계산 결과가 같다.
"""
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

BENCH = Path("bench")
FAULTS = {
    "nested_added": ('section_total = sum(row["gc"] for row in rows if not row["nested"])',
                     'section_total = sum(row["gc"] for row in rows)'),
    "floor_as_round": ("index = min(len(ordered) - 1, Fraction(str(percent)) * len(ordered) // 100)",
                       "index = min(len(ordered) - 1, int(round(percent / 100 * (len(ordered) - 1))))"),
    "floor_float": ("Fraction(str(percent)) * len(ordered) // 100", "int(percent / 100 * len(ordered))"),
    "threshold_abs_only": ('pair["exceeds"] = (absolute > ALIGNMENT_ABSOLUTE_TOLERANCE_NS\n'
                           '                           and pair["relative"] > ALIGNMENT_RELATIVE_TOLERANCE)',
                           'pair["exceeds"] = absolute > ALIGNMENT_ABSOLUTE_TOLERANCE_NS'),
    "pairs_all_frames": ("fired = [i for i in range(window) if h[i] > 0]", "fired = list(range(window))"),
    "offset_ignored": ("abs(h[i] - r[i + offset])", "abs(h[i] - r[i])"),
    "gc_missing_fails": ('    if not present:\n        return {"status": "no_gc", "key": run.key}',
                         '    if not present:\n        raise ValidationError(["GC 열 없음"])'),
    "split_by_hover": ('calls = int(run.raw[i][column(rebuild, "_calls")])',
                       'calls = int(run.raw[i][column(hover, "_calls")])'),
    "outside_from_hover": ("outside = border - rebuild", "outside = hover - rebuild"),
    "floor_cell_pooled": ('"rebuild_floor_p50_ns": median_or_none(\n            [run["rebuild_floor_p50_ns"] for run in runs if run["rebuild_floor_p50_ns"] is not None]),',
                          '"rebuild_floor_p50_ns": floor_percentile(pooled_ns, 50),'),
    "m8_median_dropped": ("statistics.median(values), floor_percentile(values, percent), percentile(values, percent)",
                          "statistics.median(values), floor_percentile(values, percent), statistics.median(values)"),
}

source = (BENCH / "summarize.py").read_text(encoding="utf-8")
with tempfile.TemporaryDirectory() as temp:
    temp = Path(temp)
    shutil.copy(BENCH / "test_summarize.py", temp)
    for name, (before, after) in FAULTS.items():
        if source.count(before) != 1:
            print(f"{name:22s} -> 주입 지점을 찾지 못함")
            continue
        (temp / "summarize.py").write_text(source.replace(before, after), encoding="utf-8")
        result = subprocess.run([sys.executable, "-m", "unittest", "test_summarize.py"],
                                cwd=temp, capture_output=True, text=True)
        verdict = [line for line in result.stderr.splitlines() if line.startswith(("OK", "FAILED"))]
        print(f"{name:22s} -> {verdict[-1] if verdict else result.stderr[-200:]}")
