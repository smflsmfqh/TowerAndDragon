## 검토 범위

측정 결과 보완 작업 N3~N6 전체를 처음부터 독립적으로 검토해 달라. 추가 측정은 없다(N6만 보존 raw를 Profiler로 다시 열어 화면을 찍었다).
이전 검토 기록이 `bench/2026-09-23_playmode/review/` 아래에 있지만, **그 판정에 기대지 말고** 원자료를 직접 확인해 판단해 달라.

- 진행 문서(계획·설계·작업 로그·결정): `bench/2026-09-23_playmode/11_보완작업_마일스톤.md` — 「설계 전 확인 사항」 F1~F7, 「진행 현황」, N3·N4·N5·N6 절과 작업 로그, 「`[결정 필요]` 모음」(결정 5·6은 사용자가 정했다 — 결정 자체는 판정하지 말고 반영이 근거와 맞는지만 봐 달라)
- 인계서(작업 범위·완료 기준 원문): `bench/2026-09-23_playmode/10_보완작업_인계.md` A2 후반·A3·B
- 범위 밖: N0~N2(이미 완료·기록), 원자료(`csv/`·`csv_from_raw/`·`raw/`) 자체, 역사적 기록(`summary/run_order.md`·`snapshots.md`·`summarize_output.md`, `bench/08_측정_마일스톤.md`의 작업 로그)

### N3 — 집계기 추가 기능
- 코드: `bench/summarize.py`(N3 추가분: `floor_percentile`, `section_gc`, `transition_split`, `quantile_pair`, `alignment_run`, `render_rebuild_floor_table`·`render_section_gc`·`render_transition`·`render_alignment`, 상수 `ALIGNMENT_*`·`VARIANT_MARKERS`·`SECTION_GC_LAYOUT`), 테스트 `bench/test_summarize.py`(`N3Tests`)
- 출력: `bench/2026-09-23_playmode/summary/summarize_output_{main,supplemental,all}.md` (명령 `python3 bench/summarize.py bench/2026-09-23_playmode --group <그룹>`)
- 편집 전 사본: `summary/n3_pre_edit/`, 대조·진단 스크립트: `summary/n3_checks/*.py`(각 파일 머리에 명령)
- 재현 대상(원문): `summary/section_gc.md`, `summary/transition_vs_still.md`, `summary/frame_alignment.md`, `09_측정결과.md` §4 재구성 µs 열

### N4 — 문서 해석 일치
- 편집 전 사본: `summary/n4_pre_edit/`(9개). 변경은 `diff summary/n4_pre_edit/<파일> <원위치>`.
- 원위치: `bench/README.md` 1~16행, `bench/08_측정_마일스톤.md`(끝 한 줄만), `bench/2026-09-23_playmode/09_측정결과.md`(전체, 새 §11 변경 이력), `summary/{frame_alignment,transition_vs_still,section_gc,representative_frames,profiler_capture_guide,cross_check}.md`(원문 보존 + "2026-10-01" 날짜 메모 방식)

### N5 — 보관 소스·해시
- `bench/2026-09-23_playmode/source/`: `history/M8/`, `history/2026-10-01_N5/`, `summarize.py`·`test_summarize.py`, `변경이력.md`, `source.sha256`(`source/`에서 `shasum -a 256 -c source.sha256`)
- 확인 가능: `python3 bench/2026-09-23_playmode/source/history/M8/summarize.py bench/2026-09-23_playmode`가 `summary/summarize_output.md`를 재현하는지

### N6 — 대표 Profiler 화면
- `bench/2026-09-23_playmode/screenshots/profiler_*.png` 4장, 캡션 `screenshots/profiler_captions.md`, 캡처 스크립트 `summary/n6_capture/*.cs`, 시작 전 상태 `summary/n6_pre_status.txt`
- 이미지를 열 수 있으면 직접 확인해 달라(Profiler 외 내용, 프레임 번호, 마커 값). 캡션 값은 `csv_from_raw/<런>.csv`의 해당 `frame_index` 행과 대조할 수 있다.

## 이번 작업에서 한 일 — 주장과 원자료

| 주장 | 위치 | 원자료 |
|---|---|---|
| 09 §4 재구성 µs 14개 = 런별 `floor(p·n)` → 런 간 중앙값으로 전부 재현(원본 코드 없음) | 09 §4 각주 ¹, 11 N3 로그 | `summarize_output_main.md` 「(c)」 |
| `section_gc.md` 10런 표·파생 수치 재현, 전체 프레임 GC 출처 판별 불가 | 11 N3 로그, `section_gc.md` 머리 | `summarize_output_supplemental.md` 「구간 GC」 |
| `transition_vs_still.md` 22개 = 하네스·`round((n−1)·p)`(p50·p95 모두)에서만 22/22 | 09 §4 각주 ⁴, `transition_vs_still.md` 머리 | `summarize_output_main.md` 「분할」, `n3_checks/check_transition_rules.py` |
| `frame_alignment.md` 전체 분포 재현, 런별 p50·p99는 `floor(p·n)`로만 54/54, "+1 63,792"는 미재현 | `frame_alignment.md` 머리·28~29행·런별 표 앞 | `summarize_output_all.md` 「프레임 대응 요약」, `n3_checks/check_alignment_vs_doc.py`·`probe_alignment_sets.py` |
| 창 기준 교차 검증 최대 1.03%(이전 0.82%는 여유 프레임 포함) | 09 §7 각주 ³, `cross_check.md` 머리 | `summarize_output_all.md` 「교차 검증」 |
| 하네스−raw 절대차 중앙값 125 ns(54런) | 09 §7, `frame_alignment.md` | 같은 출력 「프레임 대응 요약」 |
| 본 측정 A~D 43,200프레임 전부 16.7 ms 초과(최소 29.65 / 28.36 ms) | 09 §9 | `summarize_output_main.md` 「프레임 시간」 |
| E 상위−내부 = 세션별 차의 중앙값, 잔여를 개별 함수 기여로 확정하지 않음 | 09 §6 | `summarize_output_main.md` 「E 표본」 |
| Profiler 화면 4장의 값, Time ms 버림 표시, CPU 그래프 66 ms 상한 | `profiler_captions.md` | 이미지, `csv_from_raw/*.csv` |

## 특히 봐 주었으면 하는 점
- 수치마다 원자료까지 따라가 직접 재계산해 확인(가능한 범위에서)
- 재현 판정 문구("재현", "판별 불가", "후보", "미재현", "비교한 규칙 중")가 근거보다 강하거나 약한 곳
- 측정하지 않은 원인·작업을 단정한 문장(측정 범위 밖 주장)이 현재 문서 어디에든 남아 있는지
- 테스트가 주장을 실제로 고정하는지(합성 데이터가 우연히 통과시키는 곳)
- 재현 자료(코드·사본·해시·스크립트)만으로 모든 표를 다시 만들 수 있는지
