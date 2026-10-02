#!/usr/bin/env python3
"""점령 하이라이트 실측 집계기 (M2 골격 → M6 사용 → 2026-09-29 N2 개정).

M8 시점 원본은 bench/2026-09-23_playmode/source/summarize.py 에 그대로 보존한다(N5에서 history/로 옮긴다).
개정 내용은 11_보완작업_마일스톤.md 「N2 — 설계 2」의 1~13번이다.

사용
  python3 bench/summarize.py <출력루트> --write-manifest   런 그룹 manifest 생성 (이미 있으면 비교만 한다)
  python3 bench/summarize.py <출력루트> --group main        본 측정 44런
  python3 bench/summarize.py <출력루트> --group supplemental 보완 10런
  python3 bench/summarize.py <출력루트> --group all         위 둘의 합집합 54런

입력
  <출력루트>/runs_manifest.json  런 그룹 목록. 파일 stem(_key)으로 적는다 - run_id는 무효본과 재실행본이 겹친다.
  <출력루트>/summary/<key>.json  런별 메타·유효성
  <출력루트>/csv/<key>.csv       하네스 프레임별 기록 (시간값의 출처)
  <출력루트>/csv_from_raw/<key>.csv  raw 추출본 (호출 수의 출처, 시간 교차 검증)

원칙
  * 런을 고르지 않는다. 그룹의 모든 런이 필수다. 하나라도 검증에 실패하면 그 런은 공식 집계에서 빠지고
    그룹 집계 전체를 실패로 처리한다(표를 내지 않는다).
  * 필수 값의 누락·빈 값을 0으로 바꾸지 않는다.
  * 측정 창은 A~D frame_index 0~1799, E 0~179. raw의 창 밖 여유 프레임은 잘라낸다.
  * 두 출처는 행 순서가 아니라 frame_index로 대응시킨다.
  * 0이 분모인 배율을 내지 않는다.
"""

import argparse
import csv
import json
import re
import statistics
import sys
from dataclasses import dataclass, field
from pathlib import Path

NS_PER_MS = 1_000_000
NS_PER_US = 1_000
FRAME_BUDGET_NS = 16_700_000

# 교차 검증 상대 오차 기준(M3 이전부터의 목표). 절대 오차 기준은 사전에 정하지 못했다 -
# frame_alignment.md의 5,000 ns는 같은 자료를 보고 정한 탐색적 기준이라 여기 넣지 않는다.
CROSS_CHECK_RELATIVE_TOLERANCE = 0.05

# 같은 칸의 반복 런 사이 호버 누적 시간이 이 배수를 넘게 벌어지면 산포 큼으로 표시한다(§2-4).
SPREAD_WARN_RATIO = 1.25

UNITY_TEXT_ENCODING = "utf-8-sig"

MANIFEST_FILE_NAME = "runs_manifest.json"
MANIFEST_SCHEMA = 1

WINDOW_FRAMES = {"A": 1800, "B": 1800, "C": 1800, "D": 1800, "EDepart": 180, "EReenter": 180}
AD_SCENARIOS = ["A", "B", "C", "D"]
E_SCENARIOS = ["EDepart", "EReenter"]

MARKER_PREFIX = "tnd_bench_"
HOVER_MARKER = {"S0": "s0_hover", "T1": "t1_hover"}
REBUILD_MARKER = {"S0": "s0_rebuild", "T1": "t1_hoveredborderrebuild"}
E_UPPER_MARKER = {"EDepart": "t1_conquerui", "EReenter": "t1_modeenter"}
E_INNER_MARKER = "t1_reclassify"

# ProfilerRawSummary.cs(보완 10런 추출 버전)는 헤더에 엔진 마커의 _gc_bytes 열 이름을 빠뜨리고
# 값은 3개씩 썼다. 그래서 데이터 행이 헤더보다 정확히 1열 많다. 앞 열의 위치는 맞으므로
# 이 형태일 때만 마지막 열에 이름을 붙여 받아들인다. 다른 길이 불일치는 오류다.
ENGINE_CALLS_COLUMN = "conquestmodecontroller_update_calls"
ENGINE_GC_COLUMN = "conquestmodecontroller_update_gc_bytes"

HARNESS_BASE_COLUMNS = ["frame_index", "main_thread_ns", "unscaled_delta_ns", "gc_alloc_bytes",
                        "cell_match", "input_checked"]
HARNESS_OPTIONAL_COLUMNS = ["batches", "set_pass_calls"]
RAW_BASE_COLUMNS = ["frame_index"]

EXCLUDED_PREFIXES = ["pilot_", "smoke_", "check_", "diag_", "visual_"]
INVALID_PREFIX = "invalid_"

RUN_KEY_PATTERN = re.compile(r"^(S0|T1)_(A|B|C|D|EDepart|EReenter)_run(\d+)$")


class ValidationError(Exception):
    """런 하나의 검증 실패. 메시지를 여러 개 담는다."""

    def __init__(self, messages: list[str]):
        super().__init__("; ".join(messages))
        self.messages = messages


# ---------------------------------------------------------------------------
# 런 그룹 manifest
# ---------------------------------------------------------------------------

def expected_composition() -> dict[str, list[str]]:
    """main·supplemental의 정확한 구성. 개수가 아니라 이 목록과 집합이 같아야 한다."""
    main = [f"{v}_{s}_run{i}" for v in ("S0", "T1") for s in AD_SCENARIOS for i in (1, 2, 3)]
    main += [f"T1_{s}_run{i}" for s in E_SCENARIOS for i in range(1, 11)]
    supplemental = [f"{v}_{s}_run4" for v in ("S0", "T1") for s in AD_SCENARIOS]
    supplemental += [f"T1_{s}_run11" for s in E_SCENARIOS]
    return {"main": sorted(main), "supplemental": sorted(supplemental)}


def load_summaries(summary_dir: Path) -> dict[str, dict]:
    """run_id 키가 있는 JSON만 런으로 본다. 키는 파일 stem이다."""
    runs = {}
    for path in sorted(summary_dir.glob("*.json")):
        with path.open(encoding=UNITY_TEXT_ENCODING) as handle:
            data = json.load(handle)
        if isinstance(data, dict) and "run_id" in data:
            runs[path.stem] = data
    return runs


def classify_key(key: str, expected: dict[str, list[str]]) -> str | None:
    if key in expected["main"]:
        return "main"
    if key in expected["supplemental"]:
        return "supplemental"
    if key.startswith(INVALID_PREFIX):
        return "invalid"
    if any(key.startswith(prefix) for prefix in EXCLUDED_PREFIXES):
        return "excluded"
    return None


def build_manifest(summaries: dict[str, dict], expected: dict[str, list[str]]) -> dict:
    groups = {"main": [], "supplemental": [], "invalid": [], "excluded": []}
    unclassified = []
    for key in sorted(summaries):
        group = classify_key(key, expected)
        if group is None:
            unclassified.append(key)
        else:
            groups[group].append(key)
    if unclassified:
        raise ValidationError([f"분류되지 않은 런: {', '.join(unclassified)}"])
    return {
        "schema": MANIFEST_SCHEMA,
        "note": "파일 stem(_key) 기준. main=본 측정 44, supplemental=보완 10, all=둘의 합집합. "
                "invalid는 표에 넣지 않고 목록으로만 출력한다. excluded는 파일럿·스모크·검증용 런이다.",
        "excluded_prefixes": EXCLUDED_PREFIXES,
        "groups": groups,
        "expected_counts": {"main": len(expected["main"]), "supplemental": len(expected["supplemental"])},
    }


def validate_manifest(manifest: dict, summaries: dict[str, dict],
                      expected: dict[str, list[str]]) -> list[str]:
    """구성·중복·그룹 간 겹침·미분류·존재·메타 일치를 전부 검사한다. 오류 목록을 돌려준다."""
    errors = []
    if manifest.get("schema") != MANIFEST_SCHEMA:
        errors.append(f"manifest schema가 {MANIFEST_SCHEMA}이 아니다: {manifest.get('schema')}")

    groups = manifest.get("groups", {})
    for name in ("main", "supplemental", "invalid", "excluded"):
        if name not in groups or not isinstance(groups[name], list):
            errors.append(f"그룹 '{name}' 목록이 없다")
    if errors:
        return errors

    for name, members in groups.items():
        duplicates = sorted({key for key in members if members.count(key) > 1})
        if duplicates:
            errors.append(f"그룹 '{name}' 안에 중복: {', '.join(duplicates)}")

    names = list(groups)
    for i, first in enumerate(names):
        for second in names[i + 1:]:
            overlap = sorted(set(groups[first]) & set(groups[second]))
            if overlap:
                errors.append(f"그룹 '{first}'와 '{second}'가 겹침: {', '.join(overlap)}")

    for name in ("main", "supplemental"):
        missing = sorted(set(expected[name]) - set(groups[name]))
        extra = sorted(set(groups[name]) - set(expected[name]))
        if missing:
            errors.append(f"그룹 '{name}'에 빠진 런: {', '.join(missing)}")
        if extra:
            errors.append(f"그룹 '{name}'에 예상 밖 런: {', '.join(extra)}")

    counts = manifest.get("expected_counts", {})
    for name in ("main", "supplemental"):
        if counts.get(name) != len(expected[name]):
            errors.append(f"expected_counts.{name}={counts.get(name)}, 예상 {len(expected[name])}")

    listed = set().union(*[set(members) for members in groups.values()])
    unclassified = sorted(set(summaries) - listed)
    if unclassified:
        errors.append(f"manifest에 없는(미분류) 런: {', '.join(unclassified)}")
    absent = sorted(listed - set(summaries))
    if absent:
        errors.append(f"summary/에 없는 manifest 항목: {', '.join(absent)}")

    for name in ("main", "supplemental"):
        for key in groups[name]:
            run = summaries.get(key)
            if run is None:
                continue
            match = RUN_KEY_PATTERN.match(key)
            if run.get("run_id") != key:
                errors.append(f"{key}: run_id가 파일 이름과 다르다({run.get('run_id')})")
            if run.get("is_valid") is not True:
                errors.append(f"{key}: {name} 그룹인데 is_valid가 참이 아니다")
            if match and (run.get("variant"), run.get("scenario"), run.get("run_index")) != (
                    match.group(1), match.group(2), int(match.group(3))):
                errors.append(f"{key}: variant/scenario/run_index가 파일 이름과 다르다")

    for key in groups["invalid"]:
        run = summaries.get(key)
        if run is None:
            continue
        if not key.startswith(INVALID_PREFIX):
            errors.append(f"{key}: invalid 그룹인데 접두어가 '{INVALID_PREFIX}'가 아니다")
        if run.get("is_valid") is not False:
            errors.append(f"{key}: invalid 그룹인데 is_valid가 거짓이 아니다")

    for key in groups["excluded"]:
        if not any(key.startswith(prefix) for prefix in EXCLUDED_PREFIXES):
            errors.append(f"{key}: excluded 그룹인데 제외 접두어가 없다")

    return errors


def group_members(manifest: dict, group: str) -> list[str]:
    if group == "all":
        return sorted(manifest["groups"]["main"] + manifest["groups"]["supplemental"])
    return sorted(manifest["groups"][group])


# ---------------------------------------------------------------------------
# 런 로드 · 검증
# ---------------------------------------------------------------------------

def column(marker: str, suffix: str) -> str:
    return MARKER_PREFIX + marker + suffix


def read_csv(path: Path) -> tuple[list[str], list[dict]]:
    with path.open(encoding=UNITY_TEXT_ENCODING, newline="") as handle:
        rows = list(csv.reader(handle))
    if not rows:
        raise ValidationError([f"{path.name}: 빈 파일"])

    header = rows[0]
    width = len(header)
    widths = {len(row) for row in rows[1:]}

    if widths and widths != {width}:
        if widths == {width + 1} and header[-1] == ENGINE_CALLS_COLUMN:
            header = header + [ENGINE_GC_COLUMN]
        else:
            raise ValidationError([f"{path.name}: 헤더 {width}열과 행 길이 {sorted(widths)}가 맞지 않는다"])

    return header, [dict(zip(header, row)) for row in rows[1:]]


def required_int(row: dict, name: str, where: str) -> int:
    value = row.get(name)
    if value is None or value == "":
        raise ValidationError([f"{where}: 필수 값 '{name}' 누락"])
    try:
        return int(value)
    except ValueError as error:
        raise ValidationError([f"{where}: '{name}' 값 '{value}'가 정수가 아니다"]) from error


def index_frames(rows: list[dict], window: int, source: str, exact: bool) -> tuple[dict[int, dict], int]:
    """frame_index로 행을 묶는다. 중복·누락을 거부한다. 창 밖 행 수를 함께 돌려준다."""
    indexed: dict[int, dict] = {}
    duplicates = []
    for position, row in enumerate(rows):
        index = required_int(row, "frame_index", f"{source} 행 {position}")
        if index in indexed:
            duplicates.append(index)
        indexed[index] = row

    errors = []
    if duplicates:
        errors.append(f"{source}: frame_index 중복 {sorted(set(duplicates))[:5]}")
    expected = set(range(window))
    missing = sorted(expected - set(indexed))
    if missing:
        errors.append(f"{source}: 측정 창 frame_index 누락 {missing[:5]} (총 {len(missing)})")
    outside = sorted(set(indexed) - expected)
    if exact and outside:
        errors.append(f"{source}: 측정 창 밖 frame_index {outside[:5]} (하네스는 창과 정확히 같아야 한다)")
    if errors:
        raise ValidationError(errors)

    return {index: indexed[index] for index in range(window)}, len(outside)


@dataclass
class RunData:
    key: str
    variant: str
    scenario: str
    window: int
    harness: dict[int, dict]
    raw: dict[int, dict]
    raw_outside_frames: int
    optional_missing: dict[str, int] = field(default_factory=dict)


def run_markers(variant: str, scenario: str) -> list[str]:
    markers = [HOVER_MARKER[variant], REBUILD_MARKER[variant]]
    if scenario in E_UPPER_MARKER:
        markers += [E_UPPER_MARKER[scenario], E_INNER_MARKER]
    return markers


def load_run(root: Path, key: str, summary: dict) -> RunData:
    """런 하나를 읽고 검증한다. 실패하면 ValidationError. 필수 값은 0으로 채우지 않는다."""
    errors = []
    variant = summary.get("variant")
    scenario = summary.get("scenario")
    if variant not in HOVER_MARKER or scenario not in WINDOW_FRAMES:
        raise ValidationError([f"{key}: 알 수 없는 variant/scenario ({variant}, {scenario})"])
    window = WINDOW_FRAMES[scenario]

    if summary.get("is_valid") is not True:
        errors.append(f"{key}: is_valid가 참이 아니다")
    if summary.get("measure_frames") != window:
        errors.append(f"{key}: measure_frames={summary.get('measure_frames')}, 예상 {window}")

    harness_path = root / "csv" / f"{key}.csv"
    raw_path = root / "csv_from_raw" / f"{key}.csv"
    for path in (harness_path, raw_path):
        if not path.exists():
            errors.append(f"{key}: {path.parent.name}/{path.name} 없음")
    if errors:
        raise ValidationError(errors)

    markers = run_markers(variant, scenario)
    harness_header, harness_rows = read_csv(harness_path)
    raw_header, raw_rows = read_csv(raw_path)

    harness_required = HARNESS_BASE_COLUMNS + [column(m, "_ns") for m in markers]
    raw_required = RAW_BASE_COLUMNS + [column(m, s) for m in markers for s in ("_ns", "_calls")]
    for name in harness_required:
        if name not in harness_header:
            errors.append(f"{key}: 하네스 필수 열 '{name}' 없음")
    for name in raw_required:
        if name not in raw_header:
            errors.append(f"{key}: raw 필수 열 '{name}' 없음")
    if errors:
        raise ValidationError(errors)

    harness, _ = index_frames(harness_rows, window, f"{key} 하네스", exact=True)
    raw, outside = index_frames(raw_rows, window, f"{key} raw", exact=False)

    for index in range(window):
        for name in harness_required:
            required_int(harness[index], name, f"{key} 하네스 frame {index}")
        for name in raw_required:
            required_int(raw[index], name, f"{key} raw frame {index}")

    # 입력 대응: 대조 대상 프레임(input_checked=1)이 전부 일치해야 한다.
    checked = [i for i in range(window) if harness[i]["input_checked"] == "1"]
    mismatched = [i for i in checked if harness[i]["cell_match"] != "1"]
    if not checked:
        errors.append(f"{key}: 입력 대조 대상 프레임이 0개")
    if mismatched:
        errors.append(f"{key}: 입력 불일치 {len(mismatched)}프레임 (첫 frame {mismatched[0]})")

    # 두 출처가 같은 프레임을 보는지: 마커 발화 프레임 집합이 같아야 한다(frame_alignment.md 1차 근거).
    for marker in markers:
        harness_fired = {i for i in range(window) if int(harness[i][column(marker, "_ns")]) > 0}
        raw_fired = {i for i in range(window) if int(raw[i][column(marker, "_calls")]) > 0}
        if harness_fired != raw_fired:
            diff = sorted(harness_fired ^ raw_fired)
            errors.append(f"{key}: {marker} 발화 프레임 불일치 {len(diff)}개 (첫 frame {diff[0]})")

    if errors:
        raise ValidationError(errors)

    optional_missing = {}
    for name in HARNESS_OPTIONAL_COLUMNS:
        if name not in harness_header:
            optional_missing[name] = window
        else:
            optional_missing[name] = sum(1 for i in range(window) if harness[i].get(name, "") == "")

    return RunData(key, variant, scenario, window, harness, raw, outside, optional_missing)


# ---------------------------------------------------------------------------
# 런별 지표
# ---------------------------------------------------------------------------

def percentile(values: list[int], percent: float):
    """M8 집계기와 같은 방식(정렬 후 round((n-1)·p) 위치). 보간하지 않는다."""
    ordered = sorted(values)
    if not ordered:
        return None
    index = min(len(ordered) - 1, int(round(percent / 100 * (len(ordered) - 1))))
    return ordered[index]


def frame_stats(values: list[int]) -> dict:
    return {
        "median_ms": statistics.median(values) / NS_PER_MS,
        "min_ms": min(values) / NS_PER_MS,
        "max_ms": max(values) / NS_PER_MS,
        "over_budget": sum(1 for value in values if value > FRAME_BUDGET_NS),
    }


def run_metrics(run: RunData) -> dict:
    frames = range(run.window)
    h = run.harness
    r = run.raw

    def harness_values(marker):
        return [int(h[i][column(marker, "_ns")]) for i in frames]

    def raw_values(marker, suffix):
        return [int(r[i][column(marker, suffix)]) for i in frames]

    hover = HOVER_MARKER[run.variant]
    rebuild = REBUILD_MARKER[run.variant]
    rebuild_calls = raw_values(rebuild, "_calls")
    rebuild_ns = harness_values(rebuild)
    fired = [i for i in frames if rebuild_calls[i] > 0]
    fired_ns = [rebuild_ns[i] for i in fired]

    metrics = {
        "key": run.key,
        "variant": run.variant,
        "scenario": run.scenario,
        "window": run.window,
        "raw_outside_frames": run.raw_outside_frames,
        "hover_total_ms": sum(harness_values(hover)) / NS_PER_MS,
        "hover_calls": sum(raw_values(hover, "_calls")),
        "rebuild_calls": sum(rebuild_calls),
        "rebuild_fired_frames": len(fired),
        "rebuild_multi_call_frames": sum(1 for i in fired if rebuild_calls[i] > 1),
        "rebuild_fired_ns": fired_ns,
        "rebuild_median_ns": statistics.median(fired_ns) if fired_ns else None,
        "rebuild_p95_ns": percentile(fired_ns, 95) if fired_ns else None,
        "main_thread": frame_stats([int(h[i]["main_thread_ns"]) for i in frames]),
        "unscaled_delta": frame_stats([int(h[i]["unscaled_delta_ns"]) for i in frames]),
        "frame_gc_total_bytes": sum(int(h[i]["gc_alloc_bytes"]) for i in frames),
        "optional_missing": run.optional_missing,
        "input_checked": sum(1 for i in frames if h[i]["input_checked"] == "1"),
    }

    for name in HARNESS_OPTIONAL_COLUMNS:
        values = [int(h[i][name]) for i in frames if h[i].get(name, "") != ""]
        metrics[name + "_median"] = statistics.median(values) if values else None

    cross = {}
    for marker in run_markers(run.variant, run.scenario):
        harness_ms = sum(harness_values(marker)) / NS_PER_MS
        raw_ms = sum(raw_values(marker, "_ns")) / NS_PER_MS
        base = max(harness_ms, raw_ms)
        cross[marker] = {"harness_ms": harness_ms, "raw_ms": raw_ms,
                         "relative_diff": abs(harness_ms - raw_ms) / base if base else None}
    metrics["cross_check"] = cross

    if run.scenario in E_UPPER_MARKER:
        upper = E_UPPER_MARKER[run.scenario]
        upper_ms = sum(harness_values(upper)) / NS_PER_MS
        inner_ms = sum(harness_values(E_INNER_MARKER)) / NS_PER_MS
        metrics["e"] = {
            "upper_marker": upper,
            "upper_ms": upper_ms,
            "inner_ms": inner_ms,
            "diff_ms": upper_ms - inner_ms,
            "upper_calls": sum(raw_values(upper, "_calls")),
            "inner_calls": sum(raw_values(E_INNER_MARKER, "_calls")),
        }

    return metrics


# ---------------------------------------------------------------------------
# 칸(변형 × 시나리오) 집계
# ---------------------------------------------------------------------------

def spread(values: list[float]) -> dict:
    ordered = sorted(values)
    return {"median": statistics.median(ordered), "min": ordered[0], "max": ordered[-1]}


def aggregate_cell(runs: list[dict]) -> dict:
    """런별 값을 먼저 구하고 런 간에 집계한다. 풀링 통계는 이름에 '풀링'을 붙여 구분한다."""
    pooled_ns = [value for run in runs for value in run["rebuild_fired_ns"]]
    run_medians = [run["rebuild_median_ns"] for run in runs if run["rebuild_median_ns"] is not None]
    run_p95s = [run["rebuild_p95_ns"] for run in runs if run["rebuild_p95_ns"] is not None]
    multi = sum(run["rebuild_multi_call_frames"] for run in runs)

    cell = {
        "runs": [run["key"] for run in runs],
        "hover_total_ms": spread([run["hover_total_ms"] for run in runs]),
        "rebuild_calls": spread([run["rebuild_calls"] for run in runs]),
        "rebuild_fired_frames": spread([run["rebuild_fired_frames"] for run in runs]),
        "rebuild_multi_call_frames": multi,
        "rebuild_label": "재구성 1회" if multi == 0 else "재구성 발화 프레임당",
        "rebuild_pooled_median_ns": statistics.median(pooled_ns) if pooled_ns else None,
        "rebuild_pooled_p95_ns": percentile(pooled_ns, 95) if pooled_ns else None,
        "rebuild_run_median_of_medians_ns": statistics.median(run_medians) if run_medians else None,
        "rebuild_run_median_of_p95_ns": statistics.median(run_p95s) if run_p95s else None,
        "frame_gc_total_bytes": spread([run["frame_gc_total_bytes"] for run in runs]),
    }
    for name in ("main_thread", "unscaled_delta"):
        cell[name] = {
            "median_of_run_medians_ms": statistics.median(run[name]["median_ms"] for run in runs),
            "run_median_range_ms": (min(run[name]["median_ms"] for run in runs),
                                    max(run[name]["median_ms"] for run in runs)),
            "min_ms": min(run[name]["min_ms"] for run in runs),
            "over_budget_total": sum(run[name]["over_budget"] for run in runs),
            "frames_total": sum(run["window"] for run in runs),
        }

    e_runs = [run["e"] for run in runs if "e" in run]
    if e_runs:
        cell["e"] = {
            "upper_marker": e_runs[0]["upper_marker"],
            "upper_ms": spread([e["upper_ms"] for e in e_runs]),
            "inner_ms": spread([e["inner_ms"] for e in e_runs]),
            "session_diff_ms": spread([e["diff_ms"] for e in e_runs]),
            "upper_calls": sorted({e["upper_calls"] for e in e_runs}),
            "inner_calls": sorted({e["inner_calls"] for e in e_runs}),
        }
        cell["e"]["difference_of_medians_ms"] = (cell["e"]["upper_ms"]["median"]
                                                 - cell["e"]["inner_ms"]["median"])
    return cell


def aggregate_cells(metrics: list[dict]) -> dict:
    grouped: dict = {}
    for run in metrics:
        grouped.setdefault((run["variant"], run["scenario"]), []).append(run)
    return {key: aggregate_cell(runs) for key, runs in sorted(grouped.items())}


# ---------------------------------------------------------------------------
# 출력
# ---------------------------------------------------------------------------

def fmt_ns_as_us(value) -> str:
    return "—" if value is None else f"{value / NS_PER_US:.1f}"


def fmt_spread(values: dict, digits: int) -> str:
    return f"{values['median']:.{digits}f} ({values['min']:.{digits}f}~{values['max']:.{digits}f})"


def source_line(group: str, calculation: str, window: str) -> str:
    return f"> 입력: `{group}` 그룹 · 계산: {calculation} · 측정 창: {window}"


def render_invalid_list(root: Path, manifest: dict, summaries: dict[str, dict]) -> list[str]:
    """무효 런은 표에 넣지 않고 목록으로만 낸다. raw 추출본은 자기 _key로만 찾는다."""
    lines = ["## 무효 런 (집계 제외, 항상 출력)", "",
             "| 파일 | run_id | 무효 사유 | raw 추출본 |", "|---|---|---|---|"]
    for key in manifest["groups"]["invalid"]:
        run = summaries[key]
        reasons = "; ".join(run.get("invalid_reasons", [])) or "-"
        extract = root / "csv_from_raw" / f"{key}.csv"
        status = extract.name if extract.exists() else "추출본 없음 (재실행 런의 추출본으로 대체하지 않음)"
        lines.append(f"| {key} | {run.get('run_id')} | {reasons} | {status} |")
    lines.append("")
    return lines


def render_excluded(manifest: dict) -> list[str]:
    excluded = manifest["groups"]["excluded"]
    return ["## 성능 표에서 뺀 런 (파일럿·스모크·검증용)", "",
            f"{len(excluded)}개: " + ", ".join(f"`{key}`" for key in excluded), ""]


def render_run_table(group: str, metrics: list[dict]) -> list[str]:
    lines = ["## 런별 표", "",
             source_line(group, "런마다 측정 창 합계. 시간은 하네스 CSV, 호출 수는 raw `*_calls`",
                         "A~D 0~1799 · E 0~179"), "",
             "| 런 | 호버 누적 ms | 호버 호출 | 재구성 호출 / 발화 프레임 / 2회+ 프레임 | 재구성 중앙값 µs / p95 µs "
             "| main_thread 중앙값·최소 ms | unscaled_delta 중앙값·최소 ms | 16.7ms 초과 (main / unscaled) "
             "| 전체 프레임 GC 합계 B | raw 창 밖 행 |",
             "|---|---:|---:|---|---|---|---|---|---:|---:|"]
    for run in metrics:
        mt, ud = run["main_thread"], run["unscaled_delta"]
        lines.append(
            f"| {run['key']} | {run['hover_total_ms']:.3f} | {run['hover_calls']} | "
            f"{run['rebuild_calls']} / {run['rebuild_fired_frames']} / {run['rebuild_multi_call_frames']} | "
            f"{fmt_ns_as_us(run['rebuild_median_ns'])} / {fmt_ns_as_us(run['rebuild_p95_ns'])} | "
            f"{mt['median_ms']:.2f} · {mt['min_ms']:.2f} | {ud['median_ms']:.2f} · {ud['min_ms']:.2f} | "
            f"{mt['over_budget']} / {ud['over_budget']} | {run['frame_gc_total_bytes']} | {run['raw_outside_frames']} |")
    lines.append("")
    return lines


def render_main_table(group: str, cells: dict) -> list[str]:
    lines = ["## 본편 — S0 ↔ T1 (A~D)", "",
             source_line(group, "런별 합계 → 런 간 중앙값 (최소~최대)", "0~1799"), "",
             "| 변형 / 시나리오 | 런 | 재구성 호출 중앙값 (최소~최대) | 발화 프레임 중앙값 | 2회+ 프레임 | "
             "호버 누적 ms 중앙값 (최소~최대) |",
             "|---|---:|---|---:|---:|---|"]
    for scenario in AD_SCENARIOS:
        for variant in ("S0", "T1"):
            cell = cells.get((variant, scenario))
            if cell is None:
                continue
            calls = cell["rebuild_calls"]
            lines.append(
                f"| {variant} / {scenario} | {len(cell['runs'])} | "
                f"{calls['median']:.0f} ({calls['min']:.0f}~{calls['max']:.0f}) | "
                f"{cell['rebuild_fired_frames']['median']:.0f} | {cell['rebuild_multi_call_frames']} | "
                f"{fmt_spread(cell['hover_total_ms'], 3)} |")
    lines.append("")

    lines += ["### 호버 누적 시간 배율 (S0 중앙값 / T1 중앙값)", "",
              source_line(group, "각 변형의 런 간 중앙값끼리의 비. 분모가 0이면 내지 않음", "0~1799"), "",
              "| 시나리오 | S0 ms | T1 ms | 배율 |", "|---|---:|---:|---:|"]
    for scenario in AD_SCENARIOS:
        s0, t1 = cells.get(("S0", scenario)), cells.get(("T1", scenario))
        if s0 is None or t1 is None:
            continue
        s0_ms, t1_ms = s0["hover_total_ms"]["median"], t1["hover_total_ms"]["median"]
        ratio = f"{s0_ms / t1_ms:.1f}" if t1_ms > 0 else "분모 0 - 내지 않음"
        lines.append(f"| {scenario} | {s0_ms:.3f} | {t1_ms:.3f} | {ratio} |")
    lines.append("")

    lines += ["### 재구성 마커 시간 — 두 계산법", "",
              source_line(group, "(a) 칸의 모든 발화 프레임을 풀링한 중앙값/p95, "
                                 "(b) 런별 중앙값·p95의 런 간 중앙값. 발화 = raw `*_calls` ≥ 1", "0~1799"), "",
              "> 칸에 한 프레임 2회 이상 호출이 있으면 '1회'라는 이름을 쓰지 않는다.", "",
              "| 변형 / 시나리오 | 이름 | (a) 풀링 중앙값 µs / p95 µs | (b) 런별 중앙값의 중앙값 µs / 런별 p95의 중앙값 µs |",
              "|---|---|---|---|"]
    for scenario in AD_SCENARIOS:
        for variant in ("S0", "T1"):
            cell = cells.get((variant, scenario))
            if cell is None:
                continue
            lines.append(
                f"| {variant} / {scenario} | {cell['rebuild_label']} | "
                f"{fmt_ns_as_us(cell['rebuild_pooled_median_ns'])} / {fmt_ns_as_us(cell['rebuild_pooled_p95_ns'])} | "
                f"{fmt_ns_as_us(cell['rebuild_run_median_of_medians_ns'])} / "
                f"{fmt_ns_as_us(cell['rebuild_run_median_of_p95_ns'])} |")
    lines.append("")

    lines += ["### 프레임 시간 — 두 열을 따로", "",
              source_line(group, "런별 중앙값의 런 간 중앙값 (런별 중앙값 범위) · 전 프레임 최소 · 16.7 ms 초과 프레임 수",
                          "0~1799"), "",
              "| 변형 / 시나리오 | main_thread 중앙값 ms (범위) | main_thread 최소 ms | main_thread 초과/전체 | "
              "unscaled_delta 중앙값 ms (범위) | unscaled_delta 최소 ms | unscaled_delta 초과/전체 |",
              "|---|---|---:|---:|---|---:|---:|"]
    for scenario in AD_SCENARIOS:
        for variant in ("S0", "T1"):
            cell = cells.get((variant, scenario))
            if cell is None:
                continue
            parts = []
            for name in ("main_thread", "unscaled_delta"):
                stat = cell[name]
                low, high = stat["run_median_range_ms"]
                parts.append(f"{stat['median_of_run_medians_ms']:.2f} ({low:.2f}~{high:.2f}) | {stat['min_ms']:.2f} | "
                             f"{stat['over_budget_total']}/{stat['frames_total']}")
            lines.append(f"| {variant} / {scenario} | " + " | ".join(parts) + " |")
    lines.append("")
    return lines


def render_e_table(group: str, cells: dict) -> list[str]:
    lines = ["## E 표본 — 상태 변경 시 갱신 (T1만)", "",
             source_line(group, "세션마다 상위·내부 마커 창 합계와 그 차 → 세션 간 중앙값 (최소~최대). "
                                "상위−내부는 세션별 차의 중앙값이며 두 중앙값의 차가 아니다", "0~179"), "",
             "| 유형 | 세션 | 상위 마커 | 상위 ms | 재분류 ms | 상위−내부 ms (세션별 차) | 참고: 중앙값의 차 ms | "
             "상위/재분류 호출 수 |",
             "|---|---:|---|---|---|---|---:|---|"]
    for scenario in E_SCENARIOS:
        cell = cells.get(("T1", scenario))
        if cell is None or "e" not in cell:
            continue
        e = cell["e"]
        lines.append(
            f"| {scenario} | {len(cell['runs'])} | `{e['upper_marker']}` | {fmt_spread(e['upper_ms'], 2)} | "
            f"{fmt_spread(e['inner_ms'], 2)} | {fmt_spread(e['session_diff_ms'], 2)} | "
            f"{e['difference_of_medians_ms']:.2f} | {e['upper_calls']} / {e['inner_calls']} |")
    lines.append("")
    return lines


def render_repetition(group: str, cells: dict) -> list[str]:
    lines = ["## 반복 집계 (전 칸)", "",
             source_line(group, "런별 값 → 런 간 중앙값 (최소~최대). GC는 하네스의 프레임 전체 `gc_alloc_bytes` 합이며 "
                                "구간 GC가 아니다(구간 GC는 N3)", "A~D 0~1799 · E 0~179"), "",
             "| 변형 | 시나리오 | 런 | 호버 누적 ms | 재구성 호출 | 전체 프레임 GC 합계 중앙값 B | 호버 최대/최소 비 | 산포 |",
             "|---|---|---:|---|---|---:|---:|---|"]
    for (variant, scenario), cell in cells.items():
        hover = cell["hover_total_ms"]
        calls = cell["rebuild_calls"]
        ratio = hover["max"] / hover["min"] if hover["min"] > 0 else None
        if len(cell["runs"]) < 2 or ratio is None:
            verdict, ratio_text = "런 1개 또는 0 - 판정 보류", "-"
        else:
            verdict = "안정" if ratio <= SPREAD_WARN_RATIO else "산포 큼"
            ratio_text = f"{ratio:.2f}"
        lines.append(
            f"| {variant} | {scenario} | {len(cell['runs'])} | {fmt_spread(hover, 3)} | "
            f"{calls['median']:.0f} ({calls['min']:.0f}~{calls['max']:.0f}) | "
            f"{cell['frame_gc_total_bytes']['median']:.0f} | {ratio_text} | {verdict} |")
    lines.append("")
    return lines


def render_cross_check(group: str, metrics: list[dict]) -> list[str]:
    lines = ["## 교차 검증 — 하네스 ↔ raw (측정 창 합계)", "",
             source_line(group, f"마커별 창 합계의 상대차 |h−r|/max(h,r). 목표 ≤{CROSS_CHECK_RELATIVE_TOLERANCE:.0%}. "
                                "절대 오차 기준은 사전에 정하지 못해 두지 않음", "A~D 0~1799 · E 0~179"), "",
             "| 런 | 마커 | 하네스 ms | raw ms | 상대차 | 판정 |", "|---|---|---:|---:|---:|---|"]
    worst = None
    for run in metrics:
        for marker, check in run["cross_check"].items():
            relative = check["relative_diff"]
            if relative is None:
                verdict, text = "양쪽 0", "-"
            else:
                verdict = "이내" if relative <= CROSS_CHECK_RELATIVE_TOLERANCE else "초과"
                text = f"{relative:.4%}"
                if worst is None or relative > worst[0]:
                    worst = (relative, run["key"], marker)
            lines.append(f"| {run['key']} | {marker} | {check['harness_ms']:.3f} | {check['raw_ms']:.3f} | "
                         f"{text} | {verdict} |")
    lines.append("")
    if worst:
        lines += [f"최대 상대차: {worst[0]:.4%} (`{worst[1]}`, {worst[2]})", ""]
    return lines


def render_not_produced() -> list[str]:
    return ["## 이 집계기가 내지 않는 것", "",
            "- 구간별 GC(raw `*_gc_bytes`, 중첩 제외 합계): N3에서 추가한다. 위 GC 열은 전체 프레임 GC다.",
            "- 재구성 발생/미발생 프레임 분할 통계(`transition_vs_still.md`): N3에서 추가한다.",
            "- 프레임 단위 오차 분포(`frame_alignment.md`): N3 설계 3, N2 완료 뒤 결정.",
            ""]


def summarize_group(root: Path, group: str, manifest: dict, summaries: dict[str, dict],
                    command: str) -> tuple[list[str], bool]:
    members = group_members(manifest, group)
    lines = [f"# 집계 — `{group}` 그룹", "",
             f"실행 명령: `{command}`", "",
             "기존 `summary/summarize_output.md`는 M8 집계기(보완 포함 풀링, 결함 포함)의 당시 출력이며 덮어쓰지 않았다.", ""]

    loaded: list[RunData] = []
    failures: dict[str, list[str]] = {}
    for key in members:
        try:
            loaded.append(load_run(root, key, summaries[key]))
        except ValidationError as error:
            failures[key] = error.messages

    lines += ["## 런 검증", "",
              f"필수 런 {len(members)}개 · 통과 {len(loaded)} · 실패 {len(failures)}", ""]
    optional_missing = {name: sum(run.optional_missing.get(name, 0) for run in loaded)
                        for name in HARNESS_OPTIONAL_COLUMNS}
    lines.append("선택 열 미수집 프레임 수(집계에서 제외): "
                 + ", ".join(f"`{name}` {count}" for name, count in optional_missing.items()))
    lines.append("")

    if failures:
        lines += ["| 실패 런 | 사유 |", "|---|---|"]
        for key, messages in failures.items():
            lines.append(f"| {key} | {'; '.join(messages)} |")
        lines += ["", f"**그룹 집계 실패** — 필수 런 {len(failures)}개가 검증에 실패해 표를 내지 않는다.", ""]
        lines += render_invalid_list(root, manifest, summaries)
        return lines, False

    metrics = [run_metrics(run) for run in loaded]
    cells = aggregate_cells(metrics)

    lines += render_main_table(group, cells)
    lines += render_e_table(group, cells)
    lines += render_repetition(group, cells)
    lines += render_run_table(group, metrics)
    lines += render_cross_check(group, metrics)
    lines += render_invalid_list(root, manifest, summaries)
    lines += render_excluded(manifest)
    lines += render_not_produced()
    return lines, True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="점령 하이라이트 실측 집계기")
    parser.add_argument("output_root", type=Path, help="bench/<날짜>_playmode 경로")
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--group", choices=["main", "supplemental", "all"])
    action.add_argument("--write-manifest", action="store_true")
    args = parser.parse_args(argv)

    root: Path = args.output_root
    summaries = load_summaries(root / "summary")
    expected = expected_composition()
    manifest_path = root / MANIFEST_FILE_NAME

    if args.write_manifest:
        manifest = build_manifest(summaries, expected)
        errors = validate_manifest(manifest, summaries, expected)
        if errors:
            print("manifest 생성 실패:\n- " + "\n- ".join(errors), file=sys.stderr)
            return 2
        text = json.dumps(manifest, ensure_ascii=False, indent=2) + "\n"
        if manifest_path.exists():
            if manifest_path.read_text(encoding="utf-8") != text:
                print(f"{manifest_path}가 이미 있고 새로 만든 내용과 다르다. 덮어쓰지 않는다.", file=sys.stderr)
                return 2
            print(f"{manifest_path}: 기존 파일과 같다.")
            return 0
        manifest_path.write_text(text, encoding="utf-8")
        print(f"{manifest_path} 작성: " + ", ".join(f"{k} {len(v)}" for k, v in manifest["groups"].items()))
        return 0

    if not manifest_path.exists():
        print(f"{manifest_path}가 없다. --write-manifest로 먼저 만든다.", file=sys.stderr)
        return 2
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    errors = validate_manifest(manifest, summaries, expected)
    if errors:
        print("manifest 검증 실패:\n- " + "\n- ".join(errors), file=sys.stderr)
        return 2

    command = "python3 " + " ".join([sys.argv[0]] + (argv if argv is not None else sys.argv[1:]))
    lines, ok = summarize_group(root, args.group, manifest, summaries, command)
    print("\n".join(lines))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
