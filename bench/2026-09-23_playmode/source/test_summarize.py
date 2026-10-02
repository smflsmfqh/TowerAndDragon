#!/usr/bin/env python3
"""summarize.py 회귀 테스트 (11_보완작업_마일스톤.md N2 설계 4).

실제 측정 자료는 읽지도 고치지도 않는다. 임시 디렉터리에 합성 CSV·JSON을 만들어 검사한다.
실행: python3 -m unittest bench/test_summarize.py   (저장소 루트에서)
"""

import csv
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import summarize as s  # noqa: E402

ALL_MARKERS = ["t1_hover", "t1_reclassify", "t1_hoveredborder", "t1_hoveredborderrebuild",
               "t1_modeenter", "t1_conquerui", "s0_hover", "s0_rebuild"]
HOVER_NS = 1_000
REBUILD_NS = 500
RAW_OFFSET_NS = 100
E_TRANSITION_FRAME = 30


def default_fired(variant: str, scenario: str, window: int) -> set[int]:
    if variant == "S0":
        return set(range(window))
    return {10, 20} if scenario not in s.E_UPPER_MARKER else {E_TRANSITION_FRAME}


class SyntheticRoot:
    """합성 출력 루트. 런 하나당 summary JSON · 하네스 CSV · raw CSV를 만든다."""

    def __init__(self):
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)
        for name in ("summary", "csv", "csv_from_raw"):
            (self.root / name).mkdir()
        self.summaries: dict[str, dict] = {}

    def close(self):
        self._temp.cleanup()

    def add_run(self, key, variant, scenario, run_index=1, run_id=None, valid=True,
                fired=None, calls=None, e_values=None, main_thread=None, unscaled=None,
                harness_rows=None, raw_rows=None, write_raw=True, raw_margin=2, margin_value=None,
                gc_per_frame=None, gc_markers=None, rebuild_ns=None):
        """gc_per_frame: {마커: 발화 프레임당 B}. 주면 raw에 `_gc_bytes` 열을 쓴다(gc_markers로 일부만 쓸 수 있다)."""
        window = s.WINDOW_FRAMES[scenario]
        fired = default_fired(variant, scenario, window) if fired is None else fired
        calls = calls or {}
        hover = s.HOVER_MARKER[variant]
        rebuild = s.REBUILD_MARKER[variant]
        upper_ns, inner_ns = e_values or (50_000_000, 30_000_000)

        def marker_ns(marker, index):
            if marker == hover:
                return HOVER_NS
            if marker == rebuild and index in fired:
                return (rebuild_ns or {}).get(index, REBUILD_NS)
            if scenario in s.E_UPPER_MARKER and index == E_TRANSITION_FRAME:
                if marker == s.E_UPPER_MARKER[scenario]:
                    return upper_ns
                if marker == s.E_INNER_MARKER:
                    return inner_ns
            return 0

        harness = []
        for i in range(window):
            row = {"frame_index": i,
                   "main_thread_ns": (main_thread or {}).get(i, 40_000_000),
                   "unscaled_delta_ns": (unscaled or {}).get(i, 40_000_000),
                   "gc_alloc_bytes": 100, "batches": 10, "set_pass_calls": 5,
                   "cell_match": 1, "input_checked": 1}
            for marker in ALL_MARKERS:
                row[s.column(marker, "_ns")] = marker_ns(marker, i)
            harness.append(row)

        raw = []
        for i in range(window + raw_margin):
            row = {"frame_index": i, "frame_time_ns": 40_000_000, "gc_alloc_bytes": 100}
            for marker in ALL_MARKERS:
                ns = marker_ns(marker, i) if i < window else (margin_value or 0)
                row[s.column(marker, "_ns")] = max(ns - RAW_OFFSET_NS, 1) if ns else 0
                row[s.column(marker, "_calls")] = calls.get((marker, i), 1 if ns else 0)
                if gc_per_frame is not None and (gc_markers is None or marker in gc_markers):
                    row[s.column(marker, "_gc_bytes")] = gc_per_frame.get(marker, 0) if ns else 0
            raw.append(row)

        harness = harness_rows(harness) if harness_rows else harness
        raw = raw_rows(raw) if raw_rows else raw

        self._write_csv(self.root / "csv" / f"{key}.csv", harness)
        if write_raw:
            self._write_csv(self.root / "csv_from_raw" / f"{key}.csv", raw)

        summary = {"run_id": run_id or key, "variant": variant, "scenario": scenario, "run_index": run_index,
                   "is_valid": valid, "invalid_reasons": [] if valid else ["합성 무효"], "measure_frames": window}
        (self.root / "summary" / f"{key}.json").write_text(json.dumps(summary), encoding="utf-8")
        self.summaries[key] = summary

    @staticmethod
    def _write_csv(path, rows):
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)

    def manifest(self, main, invalid=()):
        return {"schema": s.MANIFEST_SCHEMA,
                "groups": {"main": list(main), "supplemental": [], "invalid": list(invalid), "excluded": []}}

    def summarize(self, main, invalid=()):
        return s.summarize_group(self.root, "main", self.manifest(main, invalid), self.summaries, "test")


class GroupTests(unittest.TestCase):
    def setUp(self):
        self.synth = SyntheticRoot()

    def tearDown(self):
        self.synth.close()

    # 1
    def test_distinct_run_ids_each_printed(self):
        keys = ["S0_A_run1", "S0_A_run2", "T1_A_run1"]
        for index, key in enumerate(keys):
            variant = key[:2]
            self.synth.add_run(key, variant, "A", run_index=index + 1)
        lines, ok = self.synth.summarize(keys)
        self.assertTrue(ok)
        table = "\n".join(lines[lines.index("## 런별 표"):lines.index("## 교차 검증 — 하네스 ↔ raw (측정 창 합계)")])
        for key in keys:
            self.assertEqual(table.count(f"| {key} |"), 1, key)

    # 2
    def test_invalid_run_does_not_use_rerun_extract(self):
        self.synth.add_run("S0_D_run3", "S0", "D", run_index=3)
        self.synth.add_run("invalid_S0_D_run3", "S0", "D", run_index=3, run_id="S0_D_run3",
                           valid=False, write_raw=False)
        lines, ok = self.synth.summarize(["S0_D_run3"], invalid=["invalid_S0_D_run3"])
        self.assertTrue(ok)
        row = next(line for line in lines if line.startswith("| invalid_S0_D_run3 |"))
        self.assertIn("추출본 없음", row)
        self.assertNotIn("S0_D_run3.csv", row)

    # 3
    def test_margin_frames_do_not_change_results(self):
        self.synth.add_run("T1_B_run1", "T1", "B", raw_margin=2, margin_value=0)
        baseline = s.run_metrics(s.load_run(self.synth.root, "T1_B_run1", self.synth.summaries["T1_B_run1"]))
        self.synth.add_run("T1_B_run1", "T1", "B", raw_margin=2, margin_value=10 ** 12)
        noisy = s.run_metrics(s.load_run(self.synth.root, "T1_B_run1", self.synth.summaries["T1_B_run1"]))
        self.assertEqual(noisy["raw_outside_frames"], 2)
        for name in ("hover_total_ms", "hover_calls", "rebuild_calls", "rebuild_fired_frames", "cross_check",
                     "frame_gc_total_bytes", "rebuild_median_ns"):
            self.assertEqual(noisy[name], baseline[name], name)

    # 4
    def test_duplicate_and_missing_frames_rejected(self):
        def duplicate(rows):
            # 모든 인덱스가 있는 채로 한 행만 겹친다 - 누락 검사가 아니라 중복 검사에 걸려야 한다.
            return rows + [dict(rows[4])]

        def missing(rows):
            return rows[:7] + rows[8:]

        cases = {"harness_dup": {"harness_rows": duplicate}, "harness_missing": {"harness_rows": missing},
                 "raw_dup": {"raw_rows": duplicate}, "raw_missing": {"raw_rows": missing}}
        for name, kwargs in cases.items():
            with self.subTest(name):
                self.synth.add_run("T1_A_run1", "T1", "A", **kwargs)
                with self.assertRaises(s.ValidationError) as caught:
                    s.load_run(self.synth.root, "T1_A_run1", self.synth.summaries["T1_A_run1"])
                expected = "중복" if name.endswith("dup") else "누락"
                self.assertTrue(any(expected in message for message in caught.exception.messages),
                                caught.exception.messages)
                lines, ok = self.synth.summarize(["T1_A_run1"])
                self.assertFalse(ok)
                self.assertFalse(any(line.startswith("## 본편") for line in lines))

    def test_harness_frame_outside_window_rejected(self):
        def extra(rows):
            return rows + [dict(rows[-1], frame_index=len(rows))]

        self.synth.add_run("T1_A_run1", "T1", "A", harness_rows=extra)
        with self.assertRaises(s.ValidationError):
            s.load_run(self.synth.root, "T1_A_run1", self.synth.summaries["T1_A_run1"])

    # 5
    def test_frame_time_columns_are_separate(self):
        window = s.WINDOW_FRAMES["A"]
        main_thread = {i: 30_000_000 for i in range(window)}
        unscaled = {i: 50_000_000 for i in range(window)}
        unscaled[0] = 10_000_000
        self.synth.add_run("T1_A_run1", "T1", "A", main_thread=main_thread, unscaled=unscaled)
        metrics = s.run_metrics(s.load_run(self.synth.root, "T1_A_run1", self.synth.summaries["T1_A_run1"]))
        self.assertEqual(metrics["main_thread"]["median_ms"], 30.0)
        self.assertEqual(metrics["unscaled_delta"]["median_ms"], 50.0)
        self.assertEqual(metrics["main_thread"]["min_ms"], 30.0)
        self.assertEqual(metrics["unscaled_delta"]["min_ms"], 10.0)
        self.assertEqual(metrics["main_thread"]["over_budget"], window)
        self.assertEqual(metrics["unscaled_delta"]["over_budget"], window - 1)

    # 6
    def test_e_uses_median_of_session_differences(self):
        sessions = [(10, 1), (20, 15), (30, 5)]   # 차 9, 5, 25 → 중앙값 9. 중앙값의 차는 20 − 5 = 15.
        keys = []
        for index, (upper, inner) in enumerate(sessions):
            key = f"T1_EDepart_run{index + 1}"
            keys.append(key)
            self.synth.add_run(key, "T1", "EDepart", run_index=index + 1,
                               e_values=(upper * s.NS_PER_MS, inner * s.NS_PER_MS))
        lines, ok = self.synth.summarize(keys)
        self.assertTrue(ok)
        cell = s.aggregate_cells([s.run_metrics(s.load_run(self.synth.root, k, self.synth.summaries[k]))
                                  for k in keys])[("T1", "EDepart")]
        self.assertAlmostEqual(cell["e"]["session_diff_ms"]["median"], 9.0)
        self.assertAlmostEqual(cell["e"]["difference_of_medians_ms"], 15.0)
        row = next(line for line in lines if line.startswith("| EDepart |"))
        self.assertIn("| 9.00 (5.00~25.00) |", row)

    # 추가: 필수 값 누락
    def test_missing_required_value_excludes_run_and_fails_group(self):
        def blank(rows):
            rows[3][s.column("t1_hover", "_ns")] = ""
            return rows

        self.synth.add_run("T1_A_run1", "T1", "A")
        self.synth.add_run("T1_A_run2", "T1", "A", run_index=2, harness_rows=blank)
        lines, ok = self.synth.summarize(["T1_A_run1", "T1_A_run2"])
        self.assertFalse(ok)
        text = "\n".join(lines)
        self.assertIn("| T1_A_run2 |", text)
        self.assertIn("필수 값", text)
        self.assertIn("그룹 집계 실패", text)

    def test_missing_required_column_fails(self):
        def drop(rows):
            for row in rows:
                del row[s.column("t1_hoveredborderrebuild", "_calls")]
            return rows

        self.synth.add_run("T1_A_run1", "T1", "A", raw_rows=drop)
        with self.assertRaises(s.ValidationError):
            s.load_run(self.synth.root, "T1_A_run1", self.synth.summaries["T1_A_run1"])

    def test_optional_blank_counted_as_not_collected(self):
        def blank(rows):
            rows[0]["batches"] = ""
            return rows

        self.synth.add_run("T1_A_run1", "T1", "A", harness_rows=blank)
        run = s.load_run(self.synth.root, "T1_A_run1", self.synth.summaries["T1_A_run1"])
        self.assertEqual(run.optional_missing["batches"], 1)
        self.assertEqual(s.run_metrics(run)["batches_median"], 10)

    # 추가: 호출 수 ≠ 발화 프레임 수
    def test_multi_call_frame_changes_label(self):
        self.synth.add_run("T1_B_run1", "T1", "B", calls={("t1_hoveredborderrebuild", 10): 2})
        metrics = s.run_metrics(s.load_run(self.synth.root, "T1_B_run1", self.synth.summaries["T1_B_run1"]))
        self.assertEqual(metrics["rebuild_calls"], 3)
        self.assertEqual(metrics["rebuild_fired_frames"], 2)
        self.assertEqual(metrics["rebuild_multi_call_frames"], 1)
        self.assertEqual(s.aggregate_cells([metrics])[("T1", "B")]["rebuild_label"], "재구성 발화 프레임당")

    def test_single_call_frames_keep_label(self):
        self.synth.add_run("T1_B_run1", "T1", "B")
        metrics = s.run_metrics(s.load_run(self.synth.root, "T1_B_run1", self.synth.summaries["T1_B_run1"]))
        self.assertEqual(s.aggregate_cells([metrics])[("T1", "B")]["rebuild_label"], "재구성 1회")

    def test_input_mismatch_rejected(self):
        def mismatch(rows):
            rows[12]["cell_match"] = 0
            return rows

        self.synth.add_run("T1_A_run1", "T1", "A", harness_rows=mismatch)
        with self.assertRaises(s.ValidationError) as caught:
            s.load_run(self.synth.root, "T1_A_run1", self.synth.summaries["T1_A_run1"])
        self.assertTrue(any("입력 불일치" in message for message in caught.exception.messages))

    def test_unchecked_input_frames_ignored(self):
        def unchecked(rows):
            rows[12]["cell_match"] = 0
            rows[12]["input_checked"] = 0
            return rows

        self.synth.add_run("T1_A_run1", "T1", "A", harness_rows=unchecked)
        s.load_run(self.synth.root, "T1_A_run1", self.synth.summaries["T1_A_run1"])

    def test_fired_frame_mismatch_between_sources_rejected(self):
        self.synth.add_run("T1_B_run1", "T1", "B", calls={("t1_hoveredborderrebuild", 10): 0})
        with self.assertRaises(s.ValidationError):
            s.load_run(self.synth.root, "T1_B_run1", self.synth.summaries["T1_B_run1"])


def make_run_data(variant, scenario, harness_ns, raw_ns):
    """프레임 대응 검사용 RunData. harness_ns/raw_ns: {마커: {frame: ns}}. 나머지 마커는 0, raw 호출 수는 ns > 0이면 1."""
    window = s.WINDOW_FRAMES[scenario]
    harness, raw = {}, {}
    for i in range(window):
        harness[i] = {s.column(m, "_ns"): str(harness_ns.get(m, {}).get(i, 0)) for m in s.VARIANT_MARKERS[variant]}
        raw[i] = {}
        for m in s.VARIANT_MARKERS[variant]:
            ns = raw_ns.get(m, {}).get(i, 0)
            raw[i][s.column(m, "_ns")] = str(ns)
            raw[i][s.column(m, "_calls")] = "1" if ns else "0"
    return s.RunData("synthetic", variant, scenario, window, harness, raw, 0)


class N3Tests(unittest.TestCase):
    """11_보완작업_마일스톤.md N2 「설계 4 — 회귀 테스트」 7~15."""

    def setUp(self):
        self.synth = SyntheticRoot()

    def tearDown(self):
        self.synth.close()

    def load(self, key):
        return s.load_run(self.synth.root, key, self.synth.summaries[key])

    # 7
    def test_section_total_excludes_nested_markers(self):
        gc = {"s0_hover": 100, "s0_rebuild": 40, "t1_hover": 100, "t1_hoveredborder": 80,
              "t1_hoveredborderrebuild": 70, "t1_reclassify": 500, "t1_conquerui": 900, "t1_modeenter": 700}
        cases = {"S0_B_run4": ("S0", "B", ["s0_hover"]),
                 "T1_B_run4": ("T1", "B", ["t1_hover"]),
                 "T1_EDepart_run11": ("T1", "EDepart", ["t1_hover", "t1_conquerui"])}
        for key, (variant, scenario, top) in cases.items():
            with self.subTest(key):
                self.synth.add_run(key, variant, scenario, gc_per_frame=gc)
                run = self.load(key)
                result = s.section_gc(run, 0)
                self.assertEqual(result["status"], "ok")
                rows = {row["marker"]: row for row in result["rows"]}
                self.assertEqual(result["section_total"], sum(rows[m]["gc"] for m in top))
                nested_gc = sum(row["gc"] for row in result["rows"] if row["nested"])
                self.assertGreater(nested_gc, 0)
                self.assertEqual(sorted(m for m, row in rows.items() if not row["nested"]), sorted(top))

    def test_outside_rebuild_uses_hoveredborder(self):
        self.synth.add_run("T1_B_run4", "T1", "B",
                           gc_per_frame={"t1_hover": 100, "t1_hoveredborder": 80, "t1_hoveredborderrebuild": 70},
                           calls={})
        result = s.section_gc(self.load("T1_B_run4"), 0)
        d = result["t1_derived"]
        self.assertEqual(d["outside_rebuild_gc"], d["border_gc"] - d["rebuild_gc"])
        self.assertNotEqual(d["outside_rebuild_gc"], d["hover_gc"] - d["rebuild_gc"])

    # 8
    def test_floor_percentile_even_median_takes_upper(self):
        self.assertEqual(s.floor_percentile([1, 2, 3, 4], 50), 3)
        self.assertEqual(s.quantile_pair([1, 2, 3, 4], 50), (2.5, 3, 3))   # round(1.5) = 2 → 0 기반 2번 값
        self.assertEqual(s.quantile_pair([1, 2, 3, 4, 5, 6], 50), (3.5, 4, 3))   # round(2.5) = 2 (짝수 쪽 반올림)

    # 9
    def test_p95_positions_for_76_samples(self):
        values = list(range(76))
        self.assertEqual(s.floor_percentile(values, 95), 72)
        self.assertEqual(s.percentile(values, 95), 71)
        self.assertEqual(s.floor_percentile(list(range(20)), 95), 19)   # p·n이 정수로 딱 떨어지는 경계
        self.assertEqual(s.floor_percentile(list(range(1000)), 99.9), 999)

    # 10
    def test_no_rebuild_gives_blank_not_zero(self):
        self.synth.add_run("T1_A_run1", "T1", "A", fired=set())
        metrics = s.run_metrics(self.load("T1_A_run1"))
        self.assertIsNone(metrics["rebuild_floor_p50_ns"])
        self.assertIsNone(metrics["rebuild_floor_p95_ns"])
        lines, ok = self.synth.summarize(["T1_A_run1"])
        self.assertTrue(ok)
        start = next(i for i, line in enumerate(lines) if "(c) `floor(p·n)` 규칙" in line)
        row = next(line for line in lines[start:] if line.startswith("| T1 / A |"))
        self.assertIn("| — / — |", row)

    # 11
    def test_offset_search_detects_shift(self):
        # 프레임마다 불규칙한 값이어야 한다 - 선형이면 이웃 오프셋의 차도 작아져 판별이 안 된다.
        value = {i: 10_000 + (i * 7_919) % 50_000 for i in range(100)}
        harness = {"t1_hover": value}
        raw = {"t1_hover": {i + 1: value[i] - 100 for i in range(100)}}
        result = s.alignment_run(make_run_data("T1", "A", harness, raw))
        self.assertEqual(result["status"], "ok")
        self.assertEqual(result["best_offset"], 1)
        self.assertEqual(result["offset_medians"][1], 100)

    # 12
    def test_pairs_only_from_fired_frames(self):
        self.synth.add_run("T1_B_run1", "T1", "B")
        result = s.alignment_run(self.load("T1_B_run1"))
        self.assertEqual(len(result["pairs"]), 1800 + 2)   # 호버 매 프레임 + 재구성 발화 2프레임
        self.assertEqual(result["fired_markers"], 2)
        self.assertEqual(result["fired_mismatch"], 0)
        self.assertEqual(result["best_offset"], 0)

    # 13
    def test_threshold_is_relative_or_absolute(self):
        harness = {"t1_hover": {0: 1_000_000, 1: 20_000, 2: 3_000}}
        raw = {"t1_hover": {0: 990_000, 1: 10_000, 2: 1_000}}
        result = s.alignment_run(make_run_data("T1", "A", harness, raw))
        exceeds = {pair["frame"]: pair["exceeds"] for pair in result["pairs"]}
        self.assertFalse(exceeds[0])   # 절대 10,000 > 5,000이지만 상대 1% ≤ 5%
        self.assertTrue(exceeds[1])    # 절대 10,000 · 상대 50% - 둘 다 넘음
        self.assertFalse(exceeds[2])   # 상대 67%이지만 절대 2,000 ≤ 5,000

    # 14
    def test_runs_without_gc_columns_do_not_fail_group(self):
        self.synth.add_run("T1_B_run1", "T1", "B", gc_per_frame={"t1_hover": 10})
        self.synth.add_run("T1_B_run2", "T1", "B", run_index=2)
        self.synth.add_run("T1_B_run3", "T1", "B", run_index=3, gc_per_frame={"t1_hover": 10},
                           gc_markers=["t1_hover"])
        lines, ok = self.synth.summarize(["T1_B_run1", "T1_B_run2", "T1_B_run3"])
        self.assertTrue(ok)
        text = "\n".join(lines)
        self.assertIn("GC 열이 있는 런 1개 · GC 열 없음 1개 · GC 열 오류 1개", text)
        self.assertIn("GC 열 없음(0으로 채우지 않음): `T1_B_run2`", text)
        self.assertIn("GC 열 오류 `T1_B_run3`: GC 열 일부만 있음", text)

    # 15
    def test_transition_split_by_rebuild_firing(self):
        self.synth.add_run("T1_B_run1", "T1", "B")
        self.synth.add_run("S0_B_run1", "S0", "B")
        cells = s.transition_split([self.load("T1_B_run1"), self.load("S0_B_run1")])
        t1 = cells[("T1", "B")]
        self.assertEqual(len(t1["fired"]["harness"]), 2)
        self.assertEqual(len(t1["still"]["harness"]), 1798)
        self.assertEqual(len(t1["fired"]["raw"]) + len(t1["still"]["raw"]), 1800)
        self.assertEqual(t1["rebuild_calls"], 2)
        s0 = cells[("S0", "B")]
        self.assertEqual((len(s0["fired"]["harness"]), len(s0["still"]["harness"])), (1800, 0))

    def test_floor_column_values_per_run_then_median(self):
        # 런마다 발화 20프레임, 값 1000·(j+1) + 100·k ns (k = 런 번호 0~2). 규칙마다 결과가 갈리게 고른 표본이다.
        #   (c) 런별 floor: p50 = 0 기반 10번 = 11000+100k, p95 = 19번 = 20000+100k → 런 간 중앙값 11.1 / 20.1 µs
        #   (b) 런별 현재 규칙: 중앙값 (10000+11000)/2+100k, p95 round(0.95·19)=18번 → 10.6 / 19.1 µs
        fired = [100 + 80 * j for j in range(20)]
        keys = []
        for k in range(3):
            key = f"T1_B_run{k + 1}"
            keys.append(key)
            self.synth.add_run(key, "T1", "B", run_index=k + 1, fired=set(fired),
                               rebuild_ns={frame: 1000 * (j + 1) + 100 * k for j, frame in enumerate(fired)})
        self.synth.add_run("S0_B_run1", "S0", "B")
        self.synth.add_run("T1_EDepart_run1", "T1", "EDepart")
        keys += ["S0_B_run1", "T1_EDepart_run1"]
        lines, ok = self.synth.summarize(keys)
        self.assertTrue(ok)
        start = next(i for i, line in enumerate(lines) if "(c) `floor(p·n)` 규칙" in line)
        end = next(i for i in range(start + 1, len(lines)) if lines[i].startswith("#"))
        table = [line for line in lines[start:end] if line.startswith("| S0 / ") or line.startswith("| T1 / ")]
        self.assertIn("| T1 / B | 재구성 1회 | 11.1 / 20.1 | 10.6 / 19.1 |", table)
        self.assertIn("| S0 / B | 재구성 1회 | 0.5 / 0.5 | 0.5 / 0.5 |", table)   # S0에도 같은 열이 있다
        self.assertFalse(any("EDepart" in line for line in table))             # A~D 열이다


class CsvQuirkTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.path = Path(self._temp.name) / "x.csv"

    def tearDown(self):
        self._temp.cleanup()

    def test_extractor_missing_engine_gc_header_accepted(self):
        self.path.write_text(f"frame_index,{s.ENGINE_CALLS_COLUMN}\n0,1,7\n1,1,8\n", encoding="utf-8")
        header, rows = s.read_csv(self.path)
        self.assertEqual(header[-1], s.ENGINE_GC_COLUMN)
        self.assertEqual(rows[1][s.ENGINE_GC_COLUMN], "8")

    def test_other_width_mismatch_rejected(self):
        self.path.write_text(f"frame_index,{s.ENGINE_CALLS_COLUMN}\n0,1,7,9\n", encoding="utf-8")
        with self.assertRaises(s.ValidationError):
            s.read_csv(self.path)
        self.path.write_text("frame_index,other\n0,1,7\n", encoding="utf-8")
        with self.assertRaises(s.ValidationError):
            s.read_csv(self.path)


class ManifestTests(unittest.TestCase):
    EXPECTED = {"main": ["S0_A_run1", "T1_A_run1"], "supplemental": ["S0_A_run4"]}

    def summaries(self):
        runs = {}
        for key in ("S0_A_run1", "T1_A_run1", "S0_A_run4"):
            match = s.RUN_KEY_PATTERN.match(key)
            runs[key] = {"run_id": key, "variant": match.group(1), "scenario": match.group(2),
                         "run_index": int(match.group(3)), "is_valid": True}
        runs["invalid_S0_A_run1"] = {"run_id": "S0_A_run1", "is_valid": False}
        runs["pilot_T1_A_run1"] = {"run_id": "pilot_T1_A_run1", "is_valid": True}
        return runs

    def manifest(self):
        return s.build_manifest(self.summaries(), self.EXPECTED)

    def test_generated_manifest_is_valid(self):
        manifest = self.manifest()
        self.assertEqual(s.validate_manifest(manifest, self.summaries(), self.EXPECTED), [])
        self.assertEqual(s.group_members(manifest, "all"), ["S0_A_run1", "S0_A_run4", "T1_A_run1"])

    def test_real_expected_composition(self):
        expected = s.expected_composition()
        self.assertEqual(len(expected["main"]), 44)
        self.assertEqual(len(expected["supplemental"]), 10)
        self.assertEqual(set(expected["main"]) & set(expected["supplemental"]), set())

    def assert_error(self, manifest, fragment, summaries=None):
        errors = s.validate_manifest(manifest, summaries or self.summaries(), self.EXPECTED)
        self.assertTrue(any(fragment in error for error in errors), errors)

    def test_duplicate_rejected(self):
        manifest = self.manifest()
        manifest["groups"]["main"].append("S0_A_run1")
        self.assert_error(manifest, "중복")

    def test_overlap_rejected(self):
        manifest = self.manifest()
        manifest["groups"]["excluded"].append("S0_A_run4")
        self.assert_error(manifest, "겹침")

    def test_unclassified_rejected(self):
        manifest = self.manifest()
        manifest["groups"]["excluded"].remove("pilot_T1_A_run1")
        self.assert_error(manifest, "미분류")

    def test_build_refuses_unknown_key(self):
        summaries = self.summaries()
        summaries["mystery_run"] = {"run_id": "mystery_run"}
        with self.assertRaises(s.ValidationError):
            s.build_manifest(summaries, self.EXPECTED)

    def test_missing_and_extra_members_rejected(self):
        manifest = self.manifest()
        manifest["groups"]["main"].remove("T1_A_run1")
        manifest["groups"]["excluded"].append("T1_A_run1")
        self.assert_error(manifest, "빠진 런")
        manifest = self.manifest()
        manifest["groups"]["supplemental"].append("T1_A_run1")
        manifest["groups"]["main"].remove("T1_A_run1")
        self.assert_error(manifest, "예상 밖 런")

    def test_absent_member_rejected(self):
        manifest = self.manifest()
        manifest["groups"]["excluded"].append("pilot_ghost")
        self.assert_error(manifest, "summary/에 없는")

    def test_validity_mismatch_rejected(self):
        summaries = self.summaries()
        summaries["T1_A_run1"]["is_valid"] = False
        self.assert_error(self.manifest(), "is_valid", summaries)
        summaries = self.summaries()
        summaries["invalid_S0_A_run1"]["is_valid"] = True
        self.assert_error(self.manifest(), "is_valid", summaries)

    def test_expected_counts_checked(self):
        manifest = self.manifest()
        manifest["expected_counts"]["main"] = 3
        self.assert_error(manifest, "expected_counts")


if __name__ == "__main__":
    unittest.main()
