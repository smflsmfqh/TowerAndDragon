## 검토 범위

N3 — 기존 측정 결과 표(구간 GC · 재구성 발생/미발생 분할 · 프레임 대응 요약 · 재구성 µs 열)를 저장소의 집계기로 다시 만들 수 있게 한 작업. 추가 측정은 없다. 모든 경로는 저장소 루트 기준.

- 대상 파일
  - `bench/2026-09-23_playmode/11_보완작업_마일스톤.md`
    - 「N3」 절 전체(396~535행 부근): 설계 0~4, 재현 목표값, 완료 조건, **작업 로그와 재현 대조 표**
    - N2 「설계 4 — 회귀 테스트」 표의 7~15행(313행 부근)
    - 「N4」 표 중 `frame_alignment.md` 두 행(543~544행 부근)
    - 「`[결정 필요]` 모음」 3·5·6(665행~)
    - 머리의 변경 이력(11~12행), 진행 현황 N3 행(136행)
  - `bench/summarize.py` — N3 추가분: `floor_percentile`(425행~), `section_gc`·`transition_split`·`quantile_pair`·`alignment_run`(588행~), `fmt_*`·`QUANTILE_NOTE`·`render_rebuild_floor_table`·`render_section_gc`·`render_transition`·`render_alignment`·`render_not_produced`(930행~), `summarize_group`의 호출 순서, 상수(`ALIGNMENT_*`, `VARIANT_MARKERS`, `SECTION_GC_LAYOUT`)
  - `bench/test_summarize.py` — `make_run_data`, `N3Tests`(297~435행), `SyntheticRoot.add_run`의 `gc_per_frame` 인자
  - 재생성 출력: `bench/2026-09-23_playmode/summary/summarize_output_{main,supplemental,all}.md`의 새 절
  - 대조·진단 스크립트: `bench/2026-09-23_playmode/summary/n3_checks/*.py` (각 파일 머리에 실행 명령)
- 비교 기준(원본, 이번에 수정하지 않음): `summary/section_gc.md`, `summary/transition_vs_still.md`, `summary/frame_alignment.md`, `09_측정결과.md` §4
- 편집 전 사본: `summary/n3_pre_edit/` (N2 판 `summarize.py`·`test_summarize.py`·출력 3개)
- 범위 밖: N2 이전 작업 내용(이미 완료·기록됨), N4~N6, 원본 summary 문서 자체의 문장 수정(N4 몫). 단 N3 출력이 N4에 넘기는 정정값·판정이 틀렸다면 지적 대상이다.

## 이번 작업에서 한 일

1. 실데이터 실행 **전에** 11 문서에 설계 0(분위수 규칙: 기본 = 현재 집계기 규칙, `floor(p·n)` 대조값 병기), 설계 3(비교 집합·상대 오차 분모·판정선·오프셋 −2~+2·쌍 구성), 설계 4(09 §4 재현 정의), 재현 목표값, 완료 조건을 고정했다.
2. 집계기에 절을 덧붙였다(기존 N2 절은 변경 없음). GC 열은 선택 열로 처리해 GC 열이 없는 main 44런이 그룹 집계를 실패시키지 않게 했다.
3. 실행 명령:
   - `python3 bench/summarize.py bench/2026-09-23_playmode --group {main|supplemental|all}` → `summary/summarize_output_*.md`
   - `python3 -m unittest bench/test_summarize.py` → 37개 통과
   - `python3 bench/2026-09-23_playmode/summary/n3_checks/inject_faults.py` → 결함 9종 중 8종 테스트 실패로 검출, `floor_float`은 동작이 같은 변형이라 미검출(로그에 이유)
   - `check_alignment_vs_doc.py`, `check_transition_rules.py`, `probe_alignment_sets.py`
   - N2 출력 보존: `diff summary/n3_pre_edit/summarize_output_<g>.md summary/summarize_output_<g>.md` → 추가 덩어리 + 「내지 않는 것」 옛 3줄 교체뿐
4. 실행 후 변경 2건(설계 0 「보충」과 작업 로그에 기록): 중앙값 칸에 `round((n−1)·0.5)` 대조값 `⟨ ⟩` 추가, 런별 대응 표 "+1" 열에 대조값 추가.

### 문서에서 새로 주장하는 판정과 원자료 (11 문서 N3 작업 로그의 재현 대조 표)

- 09 §4 재구성 µs 14개 = 런별 `floor(p·n)` → 런 간 중앙값으로 전부 재현 → `summarize_output_main.md` 「(c) `floor(p·n)` 규칙」 절
- `section_gc.md` 10런 표·파생 수치 전부 재현, 전체 프레임 GC 출처는 판별 불가(raw 합 = 하네스 합) → `summarize_output_supplemental.md` 「구간 GC」
- `transition_vs_still.md` 22개 = 하네스 출처 + p50·p95 모두 `round((n−1)·p)`일 때만 22/22, 현재 규칙 20/22, `floor` 18/22 → `summarize_output_main.md` 「재구성 발생/미발생 분할」, `check_transition_rules.py`
- `frame_alignment.md` 전체 분포 재현, 런별 54행 구조 열 54/54, 중앙값·p99는 `floor`로만 54/54(현재 규칙 53/54·25/54) → `summarize_output_all.md` 「프레임 대응 요약」, `check_alignment_vs_doc.py`
- 본문 "중앙 126"은 supplemental 풀링 값(126)과 같다는 후보만, "+0 166 → +1 63,792"는 미재현(사전 고정 정의로 all +0 125 → +1 24,001) → `probe_alignment_sets.py`
- `[결정 필요]` 6: 세 문서가 서로 다른 분위수 규칙으로 만들어졌다는 사실과 선택지. **선택 자체는 사용자 결정 대기 중**이므로 어느 선택지가 옳은지는 판정하지 않아도 된다. 사실 서술·선택지 누락은 지적 대상이다.

### 특히 봐 주었으면 하는 점

- 실행 후 추가한 `⟨ ⟩` 대조값이 "결과에 맞춰 규칙을 고른 것"이 되지 않도록 기록·표현이 충분한지
- 재현 판정 문구가 근거보다 강하게 쓰인 곳(예: "재현", "판별 불가", "후보", "미재현"의 구분)
- 구간 GC 중첩 고정표와 E 런의 `t1_hoveredborder` 처리, 회당 = 호출 수 기준
- 프레임 대응의 쌍 정의(하네스 발화 프레임 기준)와 오프셋 탐색에서 창 가장자리 처리
- 테스트가 실제로 주장을 고정하는지(약한 단언, 우연히 통과하는 합성 데이터)
