#!/usr/bin/env python3
"""점령 하이라이트 실측 집계기 (M2에서 골격 작성, M6에서 사용).

입력
  <출력루트>/csv/           하네스가 프레임마다 숫자 버퍼에 담아 두었다가 종료 후 쓴 CSV
  <출력루트>/csv_from_raw/  ProfilerRawSummary가 .raw에서 뽑은 CSV (독립 출처)
  <출력루트>/summary/       런별 메타·유효성 JSON

출력 (stdout, 마크다운)
  1. 런 목록 - 유효/무효와 사유를 전부 적는다. 무효 런도 지우지 않고 표시만 한다.
  2. 본편 표 - 계획 §3-4 형식. 절대값(호출 수·누적 ms·할당)을 먼저, 배율을 뒤에.
  3. 교차 검증 표 - 하네스 CSV ↔ raw 추출값. 목표 ±5%, 작은 값은 절대 오차.

원칙 (계획 §2-4)
  * 런을 고르지 않는다. 모든 유효 런의 중앙값과 최소/최대를 함께 낸다.
  * 0 또는 계측 한계 이하 값을 분모로 쓴 배율을 내지 않는다 - "계측 한계 이하"로 표기한다.
  * 지표마다 유리한 런을 골라 조합하지 않는다.
"""

import argparse
import csv
import json
import re
import statistics
from pathlib import Path

# 이 값들은 M3 파일럿에서 실측으로 확정한다. 확정 전에는 None으로 두고 판정을 보류한다.
CROSS_CHECK_RELATIVE_TOLERANCE = 0.05
CROSS_CHECK_ABSOLUTE_TOLERANCE_NS = None  # M3에서 정한다

# 같은 칸의 반복 런 사이에서 이 배수를 넘게 벌어지면 원인을 확인하고 반복을 늘릴지 판단한다(§2-4).
# 파일럿에서 프레임 시간 자체가 런 간 42~54 ms로 흔들렸으므로 넉넉히 잡는다.
SPREAD_WARN_RATIO = 1.25

# 본 측정 런의 ID 형태. 파일럿·스모크·시각 검증 런은 접두어가 붙어 여기 걸리지 않는다 -
# 파일럿 결과를 본 측정 표에 섞지 않기 위해서다(§2-4).
MAIN_RUN_ID_PATTERN = re.compile(r"^(S0|T1|S1p)_(A|B|C|D|EDepart|EReenter)_run\d+$")

NS_PER_MS = 1_000_000

SECTION_MARKERS = [
    "tnd_bench_t1_hover",
    "tnd_bench_t1_reclassify",
    "tnd_bench_t1_hoveredborder",
    "tnd_bench_t1_hoveredborderrebuild",
    "tnd_bench_t1_modeenter",
    "tnd_bench_t1_conquerui",
    "tnd_bench_s0_hover",
    "tnd_bench_s0_rebuild",
]


# Unity가 쓴 CSV/JSON에는 UTF-8 BOM이 붙는다. utf-8로 읽으면 json이 바로 터진다.
UNITY_TEXT_ENCODING = "utf-8-sig"


def load_summaries(summary_dir: Path) -> list[dict]:
    """런 요약 JSON만 모은다.

    summary/에는 런이 아닌 파일도 있다(M1의 origin_manifest.json, snapshots.md 등).
    파일 이름으로 거르지 않고 하네스가 넣은 run_id 키가 있는 것만 런으로 본다.
    """
    runs = []
    for path in sorted(summary_dir.glob("*.json")):
        with path.open(encoding=UNITY_TEXT_ENCODING) as handle:
            data = json.load(handle)

        if not isinstance(data, dict) or "run_id" not in data:
            continue

        data["_summary_path"] = str(path)
        data["_is_main_run"] = bool(MAIN_RUN_ID_PATTERN.match(data["run_id"]))
        # 재실행한 칸은 invalid_<id>.json과 <id>.json이 함께 남는다 - run_id로 키를 잡으면
        # 둘 중 하나가 덮여 사라진다. 파일 이름을 키로 쓴다(§2-4: 모든 런을 보존하고 전부 표에 넣는다).
        data["_key"] = path.stem
        runs.append(data)
    return runs


def load_frames(csv_path: Path) -> list[dict]:
    if not csv_path.exists():
        return []
    with csv_path.open(encoding=UNITY_TEXT_ENCODING) as handle:
        return list(csv.DictReader(handle))


def to_int(row: dict, column: str) -> int:
    value = row.get(column)
    if value is None or value == "":
        return 0
    return int(value)


def aggregate_run(frames: list[dict]) -> dict:
    """프레임 행에서 런 하나의 지표를 뽑는다. 값이 없는 열은 0이 아니라 None으로 남긴다."""
    if not frames:
        return {}

    result = {"frame_count": len(frames)}

    for marker in SECTION_MARKERS:
        column = marker + "_ns"
        if column not in frames[0]:
            result[marker] = None
            continue

        values = [to_int(row, column) for row in frames]
        nonzero = [value for value in values if value > 0]

        result[marker] = {
            "total_ms": sum(values) / NS_PER_MS,
            # 마커가 발화하지 않은 프레임 = 그 구간이 돌지 않은 프레임.
            # 리코더 미수집과 구분하려면 summary/의 recorder_valid를 함께 봐야 한다.
            "fired_frames": len(nonzero),
            "median_ns": statistics.median(nonzero) if nonzero else None,
            "p95_ns": percentile(nonzero, 95) if nonzero else None,
            "max_ns": max(nonzero) if nonzero else None,
        }

    gc_values = [to_int(row, "gc_alloc_bytes") for row in frames]
    result["gc_alloc_total_bytes"] = sum(gc_values)
    result["gc_alloc_max_bytes"] = max(gc_values) if gc_values else 0

    frame_times = [to_int(row, "unscaled_delta_ns") for row in frames]
    result["elapsed_ms"] = sum(frame_times) / NS_PER_MS
    main_thread = [to_int(row, "main_thread_ns") for row in frames]
    result["frame_median_ms"] = statistics.median(main_thread) / NS_PER_MS if main_thread else None
    result["frames_over_16_7ms"] = sum(1 for value in frame_times if value > 16_700_000)

    batches = [to_int(row, "batches") for row in frames]
    set_pass = [to_int(row, "set_pass_calls") for row in frames]
    result["batches_median"] = statistics.median(batches) if batches else None
    result["set_pass_median"] = statistics.median(set_pass) if set_pass else None

    # E-출발의 버튼 클릭 구간처럼 대조 대상이 아닌 프레임은 분모에서 뺀다(하네스가 input_checked로 표시).
    # 열이 없는 CSV는 모든 프레임을 대조 대상으로 본다 - 다만 "열이 없어서 전부 센 것"과
    # "열이 있고 전부 대조 대상인 것"은 다르므로 어느 쪽인지 표에 드러낸다
    # (§2-4의 미수집/미발생 구분과 같은 이유).
    result["has_input_checked_column"] = "input_checked" in frames[0]
    checked = [row for row in frames if row.get("input_checked", "1") == "1"]
    matched = sum(1 for row in checked if row.get("cell_match") == "1")
    result["input_frames_checked"] = len(checked)
    result["input_frames_total"] = len(frames)
    result["input_match_ratio"] = matched / len(checked) if checked else None
    result["virtual_mouse_ratio"] = sum(
        1 for row in frames if row.get("mouse_is_virtual") == "1") / len(frames)

    return result


def percentile(values: list[int], percent: float) -> float:
    ordered = sorted(values)
    if not ordered:
        return None
    index = min(len(ordered) - 1, int(round(percent / 100 * (len(ordered) - 1))))
    return ordered[index]


def cross_check(harness: dict, raw_frames: list[dict]) -> dict:
    """하네스 CSV와 raw 추출값을 같은 마커·같은 프레임 범위로 맞대어 본다.

    불일치를 수치 선별로 해결하지 않는다. 차이가 나면 프레임 번호·단위·포함시간·스레드 순으로
    점검하라는 뜻이며, 이 함수는 차이를 그대로 보고만 한다(§3-3).
    """
    if not raw_frames:
        return {"status": "raw 없음"}

    report = {}
    for marker in SECTION_MARKERS:
        harness_value = harness.get(marker)
        column = marker + "_ns"
        if harness_value is None or column not in raw_frames[0]:
            report[marker] = None
            continue

        raw_total_ms = sum(to_int(row, column) for row in raw_frames) / NS_PER_MS
        harness_total_ms = harness_value["total_ms"]

        if harness_total_ms == 0 and raw_total_ms == 0:
            report[marker] = {"status": "양쪽 0 - 미발화 또는 계측 한계"}
            continue

        base = max(harness_total_ms, raw_total_ms)
        relative = abs(harness_total_ms - raw_total_ms) / base if base else None

        report[marker] = {
            "harness_ms": harness_total_ms,
            "raw_ms": raw_total_ms,
            "relative_diff": relative,
            "within_tolerance": relative is not None and relative <= CROSS_CHECK_RELATIVE_TOLERANCE,
        }

    return report


def print_repetition_table(aggregates: dict, main_only: bool = True) -> None:
    """변형×시나리오별로 반복 런을 묶어 중앙값과 최소/최대를 낸다.

    런을 고르지 않는다(§2-4) - 유효 런을 전부 넣고, 무효 런은 표에서 빼되 개수를 적는다.
    편차가 큰 칸은 M4에서 원인을 적고 반복을 늘릴지 판단하는 근거가 된다.
    """
    cells: dict = {}
    invalid_counts: dict = {}

    for key, (run, harness, _) in aggregates.items():
        if main_only and not run.get("_is_main_run"):
            continue

        variant = run.get("variant")
        scenario = run.get("scenario")
        key = (variant, scenario)

        if not run.get("is_valid"):
            invalid_counts[key] = invalid_counts.get(key, 0) + 1
            continue

        # aggregate_run은 "_ns" 없는 마커 이름을 키로 쓴다(컬럼 이름과 다르다).
        hover_key = "tnd_bench_s0_hover" if variant == "S0" else "tnd_bench_t1_hover"
        rebuild_key = "tnd_bench_s0_rebuild" if variant == "S0" else "tnd_bench_t1_hoveredborderrebuild"

        hover = harness.get(hover_key) or {}
        rebuild = harness.get(rebuild_key) or {}

        cells.setdefault(key, []).append({
            "run_id": key,
            "hover_total_ms": hover.get("total_ms", 0.0),
            "rebuild_calls": rebuild.get("fired_frames", 0),
            "rebuild_median_ns": rebuild.get("median_ns"),
            "gc_total": harness.get("gc_alloc_total_bytes", 0),
            "frame_median_ms": harness.get("frame_median_ms"),
        })

    if not cells:
        return

    print("## 반복 집계 — 본 측정 런만 (유효 런 전부, 선별 없음)")
    print()
    print("> 파일럿·스모크·시각 검증 런은 접두어가 붙어 여기서 빠진다(§2-4). 위 「런 목록」에는 전부 나온다.")
    print()
    print("| 변형 | 시나리오 | 유효 런 | 무효 | 호버 누적 ms 중앙값 (최소~최대) | 재구성 횟수 중앙값 (최소~최대) | 구간 GC 합계 중앙값 |")
    print("|---|---|---:|---:|---|---|---:|")

    for key in sorted(cells):
        variant, scenario = key
        runs = cells[key]
        hovers = sorted(r["hover_total_ms"] for r in runs)
        rebuilds = sorted(r["rebuild_calls"] for r in runs)
        gcs = sorted(r["gc_total"] for r in runs)

        print(f"| {variant} | {scenario} | {len(runs)} | {invalid_counts.get(key, 0)} | "
              f"{statistics.median(hovers):.3f} ({hovers[0]:.3f}~{hovers[-1]:.3f}) | "
              f"{statistics.median(rebuilds):.0f} ({rebuilds[0]}~{rebuilds[-1]}) | "
              f"{statistics.median(gcs):.0f} |")

    print()
    print("### 반복 간 산포")
    print()
    print("| 변형 | 시나리오 | 호버 누적 ms 최대/최소 비 | 판정 |")
    print("|---|---|---:|---|")

    for key in sorted(cells):
        variant, scenario = key
        hovers = sorted(r["hover_total_ms"] for r in cells[key])

        if len(hovers) < 2 or hovers[0] <= 0:
            print(f"| {variant} | {scenario} | - | 런이 1개뿐이거나 0 - 판정 보류 |")
            continue

        ratio = hovers[-1] / hovers[0]
        verdict = "안정" if ratio <= SPREAD_WARN_RATIO else f"산포 큼 - 원인 확인 후 반복 추가 검토"
        print(f"| {variant} | {scenario} | {ratio:.2f} | {verdict} |")

    print()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output_root", type=Path, help="bench/<날짜>_playmode 경로")
    args = parser.parse_args()

    root: Path = args.output_root
    runs = load_summaries(root / "summary")

    print(f"# 집계 — {root}")
    print()
    print(f"런 {len(runs)}개 (무효 런 포함, 선별 없음)")
    print()

    print("## 런 목록")
    print()
    print("| 런 ID | 변형 | 시나리오 | 유효 | 무효 사유 | 입력 일치 (대조/전체) | input_checked 열 | 프레임 |")
    print("|---|---|---|---:|---|---:|---|---:|")

    aggregates = {}

    for run in runs:
        key = run["_key"]
        run_id = run.get("run_id", "?")
        frames = load_frames(root / "csv" / (key + ".csv"))
        harness = aggregate_run(frames)
        aggregates[key] = (run, harness, frames)

        reasons = "; ".join(run.get("invalid_reasons", [])) or "-"
        match = harness.get("input_match_ratio")
        print(f"| {key} | {run.get('variant')} | {run.get('scenario')} | "
              f"{'O' if run.get('is_valid') else 'X'} | {reasons} | "
              f"{'-' if match is None else f'{match:.4f}'} ({harness.get('input_frames_checked', 0)}/"
              f"{harness.get('input_frames_total', 0)}) | "
              f"{'있음' if harness.get('has_input_checked_column') else '없음(구버전 CSV)'} | "
              f"{harness.get('frame_count', 0)} |")

    print()
    print("## 본편 표 (계획 §3-4)")
    print()
    print("| 런 ID | 실제 재구성 프레임 | 호버 누적 ms | 전환 중앙값 ns / p95 ns | 구간 GC 합계 | "
          "전체 프레임 GC 합계 | 16.7ms 초과 프레임 | Batches / SetPass |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|")

    for run_id, (run, harness, _) in aggregates.items():
        variant = run.get("variant")
        hover_key = "tnd_bench_s0_hover" if variant == "S0" else "tnd_bench_t1_hover"
        rebuild_key = "tnd_bench_s0_rebuild" if variant == "S0" else "tnd_bench_t1_hoveredborderrebuild"

        hover = harness.get(hover_key) or {}
        rebuild = harness.get(rebuild_key) or {}

        print(f"| {key} | {rebuild.get('fired_frames', '-')} | "
              f"{hover.get('total_ms', 0):.3f} | "
              f"{rebuild.get('median_ns', '-')} / {rebuild.get('p95_ns', '-')} | "
              f"(raw 필요) | {harness.get('gc_alloc_total_bytes', 0)} | "
              f"{harness.get('frames_over_16_7ms', 0)} | "
              f"{harness.get('batches_median', '-')} / {harness.get('set_pass_median', '-')} |")

    print()
    print("## 교차 검증 (하네스 CSV ↔ raw)")
    print()

    if CROSS_CHECK_ABSOLUTE_TOLERANCE_NS is None:
        print("> 작은 값에 쓸 절대 오차 기준이 아직 없다 - M3 파일럿에서 정하고 이 파일 상단 상수에 적는다.")
        print()

    for key, (run, harness, _) in aggregates.items():
        raw_frames = load_frames(root / "csv_from_raw" / (run.get("run_id", key) + ".csv"))
        report = cross_check(harness, raw_frames)
        print(f"### {key}")
        print()
        print("```json")
        print(json.dumps(report, ensure_ascii=False, indent=2, default=str))
        print("```")
        print()

    print_repetition_table(aggregates)

    print("## 아직 채우지 않은 것")
    print()
    print("- 구간별 GC Alloc: 하네스는 프레임 전체 GC만 잡는다. 구간 GC는 raw 추출에서 더한다(M6).")
    print("- 배율: 분모가 0이거나 계측 한계 이하인 칸에는 배율을 쓰지 않는다. M6에서 판정 후 추가한다.")
    print("- 변형 간 중앙값·범위 비교표: 유효 런이 모인 뒤 M6에서 추가한다.")


if __name__ == "__main__":
    main()
