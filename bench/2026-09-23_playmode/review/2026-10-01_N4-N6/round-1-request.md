## 검토 범위

N4·N5·N6을 한 번에 검토한다(Codex 사용량 한도로 각 마일스톤 직후 검토를 받지 못해 몰아서 받는다). N3는 `review/2026-10-01_N3/`에서 PASS를 받았고 그 뒤 집계기 코드는 바뀌지 않았다(`bench/summarize.py` = `bench/2026-09-23_playmode/source/summarize.py`).
진행 문서: `bench/2026-09-23_playmode/11_보완작업_마일스톤.md` — 「진행 현황」(133~141행), N4·N5·N6 절과 각 작업 로그, 「`[결정 필요]` 모음」 5·6. 인계서: `10_보완작업_인계.md` A3·A2 마지막 항목·B.

결정 6은 사용자가 직접 정했다((가) 기존 값 유지 + "p50" 이름 + 재현 정의 각주). **결정 자체는 판정하지 말고**, 반영이 근거와 맞게 됐는지만 봐 달라.

### N4 — 문서 해석 일치 (인계서 A3)
- 편집 전 사본: `bench/2026-09-23_playmode/summary/n4_pre_edit/`(9개). 변경은 `diff summary/n4_pre_edit/<파일> <원위치>`.
- 원위치: `bench/README.md`(상단 1~16행만, 그 아래는 2026-09-06 과거 기록이라 범위 밖), `bench/08_측정_마일스톤.md`(끝 한 줄 추가만), `bench/2026-09-23_playmode/09_측정결과.md`(전체, 특히 §4·§5·§6·§7·§9·§10·새 §11), `summary/{frame_alignment,transition_vs_still,section_gc,representative_frames,profiler_capture_guide,cross_check}.md`.
- 근거 출력: `summary/summarize_output_{main,supplemental,all}.md`(N2·N3 집계기 출력), `summary/n3_checks/*.py`.
- 상세 근거 문서는 원문을 지우지 않고 "2026-10-01" 날짜 메모로 정정하는 방식을 썼다. 역사적 기록(`run_order.md`·`snapshots.md`·`summarize_output.md`·08 작업 로그)은 고치지 않았다.

### N5 — 보관 소스·해시 갱신
- `bench/2026-09-23_playmode/source/`: `history/M8/`(M8 집계기·M8 해시 목록 보존), `summarize.py`·`test_summarize.py`(현재판 사본), `변경이력.md`, 새 `source.sha256`(48항목, `source/`에서 `shasum -a 256 -c source.sha256`).
- 확인한 것: M8 사본이 원본·옛 목록 항목과 일치, `python3 bench/2026-09-23_playmode/source/history/M8/summarize.py bench/2026-09-23_playmode`의 출력이 `summary/summarize_output.md`와 바이트 일치.

### N6 — 대표 Profiler 화면 4장 (인계서 B)
- 산출물: `bench/2026-09-23_playmode/screenshots/profiler_{s0_a_still,t1_a_still,t1_d_transition,e_reenter_spike}.png`, 캡션 `screenshots/profiler_captions.md`, 캡처 스크립트 `summary/n6_capture/*.cs`, 시작 전 상태 `summary/n6_pre_status.txt`.
- 계획과 다르게 한 점(작업 로그에 이유): 헬퍼를 `Assets/`에 복원하지 않고 스크래치 스크립트로 같은 일을 함, 자동 캡처 대신 사용자 창 선택 캡처, E 장만 Frame Count 200(사용자 결정), 대조 기준을 `representative_frames.md`(run1) 대신 같은 런의 `csv_from_raw/`·가이드 지점 값으로.
- 이미지는 열어서 확인할 수 있으면 확인해 달라(Profiler 창 외 내용이 없는지, 캡션의 프레임·값이 화면과 맞는지). 열 수 없으면 그렇다고 적어 달라.
- 캡션의 정확한 값은 `csv_from_raw/<런>.csv` 해당 `frame_index` 행과 대조할 수 있다.

## 이번 작업에서 한 일 — 새로 주장하는 수치와 원자료

| 주장 | 위치 | 원자료 |
|---|---|---|
| 창 합계 교차 검증 최대 상대차 1.03%(`T1_EReenter_run6`), `T1_A_run2` 0.85% | 09 §7 각주 ³, `cross_check.md` 머리 | `summarize_output_main.md`·`_all.md` 「교차 검증」 |
| 하네스−raw 절대차 중앙값 125 ns(54런 91,057쌍), supplemental만이면 126 | 09 §7, `frame_alignment.md` 55행 부근 | `summarize_output_all.md`·`_supplemental.md` 「프레임 대응 요약」 |
| "+0 166 → +1 63,792 ns"는 미재현, 사전 정의로는 +0 125 → +1 24,001 | `frame_alignment.md` 28~29행 | `summarize_output_all.md` 「오프셋별」 |
| 본 측정 A~D 43,200프레임 전부 16.7 ms 초과, 최소 main_thread 29.65 / unscaled_delta 28.36 ms | 09 §9 | `summarize_output_main.md` 「프레임 시간 — 두 열을 따로」 |
| 재구성 µs `p50 / p95` 각주(정의, T1/D p95 539.7, 나머지 0~0.8 µs 차) | 09 §4 각주 ¹ | `summarize_output_main.md` 「(c)」와 「두 계산법」 (b) 열 |
| 프레임 시간 열 근거 = main_thread 런별 중앙값의 런 간 중앙값, unscaled로는 43.81~45.41 | 09 §4 각주 ² | 같은 출력 「프레임 시간」 |
| 재구성 발생/미발생 소표 p50 정의, 현재 규칙 값 752.7·386.2 | 09 §4 각주 ⁴, `transition_vs_still.md` 머리 | `summarize_output_main.md` 「재구성 발생/미발생 분할」 |
| `frame_alignment.md` 런별 p50·p99는 `floor(p·n)`, 현재 규칙으로는 중앙값 1런(`T1_EReenter_run8` 146)·p99 29런 다름 | `frame_alignment.md` 런별 표 앞 | `summarize_output_all.md` 「런별」, `n3_checks/check_alignment_vs_doc.py` |
| E 상위−내부는 세션별 차의 중앙값, 잔여를 개별 함수 기여로 확정하지 않음 | 09 §6 | `summarize_output_main.md` 「E 표본」 |
| Profiler 화면 4장의 마커 값·프레임 GC | `profiler_captions.md` | `csv_from_raw/S0_A_run4.csv` 900행, `T1_A_run4` 900, `T1_D_run4` 896, `T1_EReenter_run11` 30 |
| Profiler Time ms 열은 버림 표시(56.3052 → 56.30), CPU 그래프 66 ms 위 잘림(런별 66.7 ms 초과 프레임 수) | `profiler_captions.md` 「읽을 때 주의」 | 이미지, `csv_from_raw/*.csv`의 `frame_time_ns` |

## 특히 봐 주었으면 하는 점
- 정정·각주 문구가 근거보다 강하거나 약한 곳, 특히 09 §6 잔여 문장, §9 16.7 ms 문장, `frame_alignment.md`의 미재현 메모
- 같은 옛 수치(0.82%, 126 ns, "54개", "전환", "중앙값")가 다른 줄·문서에 남아 있는지
- 날짜 메모 방식이 역사적 기록을 고친 셈이 되는 곳이 있는지
- N5 해시 목록이 재현에 충분한지(빠진 파일), `history/M8/` 보존이 맞는지
- N6 캡션이 화면·원자료와 맞는지, "보완 런의 대표 프레임이며 본 측정 중앙값이 아님" 같은 범위 표시가 충분한지
