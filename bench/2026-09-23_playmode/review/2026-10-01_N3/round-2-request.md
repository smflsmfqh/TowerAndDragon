## 검토 범위

1라운드와 같다. 이번에는 아래 대응 부분과, 그로 인해 바뀐 출력만 보면 된다.
- 범위 밖: 1라운드에서 지적이 없던 부분(새 문제가 보이면 지적해도 된다).

## 이번 작업에서 한 일

- 문서 문구 수정(R1-1), 집계기 잔여 식 수정과 출력 열 추가(R1-3), 테스트 보강(R1-2).
- 재생성: `python3 bench/summarize.py bench/2026-09-23_playmode --group {main|supplemental|all}` → `summary/summarize_output_*.md`.
  `diff summary/n3_pre_edit/summarize_output_<g>.md summary/summarize_output_<g>.md`의 삭제 줄은 여전히 「내지 않는 것」 옛 3줄뿐이다(세 그룹 모두).
- `python3 -m unittest bench/test_summarize.py` → 38개 통과. `python3 bench/2026-09-23_playmode/summary/n3_checks/inject_faults.py` → 11종 중 `floor_float`(동작 같은 변형)을 뺀 10종 검출.
- 대조 스크립트 재실행 결과 변화 없음: 프레임 대응 런별 구조 54/54, 중앙값·p99 `floor` 54/54; 분할 표 20/22 · 22/22 · 18/22.

## 직전 지적에 대한 대응

- **R1-1: 고침.** "재현됨"과 "당시 생성 방식"을 구분했다.
  - `11_보완작업_마일스톤.md:526` — "비교한 규칙 3종 중 22/22를 내는 것은 … 적용한 경우뿐이다(당시 생성 방식은 확인되지 않음)"
  - `:528` — "정의 확정" → "재구성한 비교 집합 정의로 전체 분포와 런별 구조가 재현됨. 상대 오차 분모를 하네스로 두면 23.3%(당시 정의는 확인되지 않음)"
  - `:530` — "`floor` 규칙으로만 재현" → "비교한 규칙 중 `floor`만 54/54 재현 (당시 생성 방식은 확인되지 않음)"
  - `:688` — "세 문서가 서로 다른 규칙으로 만들어졌다" → "비교한 규칙 중 문서별로 전체 값을 재현하는 규칙이 서로 달랐다. 당시 생성 방식은 확인되지 않았다 — 코드가 없고, 확인한 것은 수치 일치뿐"
  - `:696` — "재현 규칙대로라면 두 문서의 '중앙값'도 …"로 조건을 붙였다
  - 같은 표현이 있던 설계 0 보충(`:413`)과 `bench/summarize.py:660~663`(`quantile_pair` 독스트링)도 같은 방식으로 고쳤다.
- **R1-3: 고침 — 설계가 아니라 구현을 고쳤다.**
  - 근거: 실행 전 설계 1(`11 문서:438`)의 정의는 `t1_hoveredborder − t1_hoveredborderrebuild`이고, `summary/section_gc.md`가 이 잔여를 "`ShowHoveredBorder()`의 같은-집합 가드 판정 구간"이라 설명하므로 설계 쪽이 의미에 맞다.
  - `bench/summarize.py:627` `outside = border - rebuild`, 출력 표에 `t1_hoveredborder GC` 열 추가(`:1014`).
  - 값: `summarize_output_supplemental.md` 「T1 A~D — 재구성 구간 비중 · 재구성 바깥 잔여」 — B·C·D의 `t1_hover`·`t1_hoveredborder` GC가 같아(89,032·230,800·598,576) 잔여 440·1,160·3,040 B, 회당 40.0 B로 변화 없음.
  - 작업 로그(`11 문서:534~`)에 차이와 값이 같은 이유를 적었다.
- **R1-2: 고침.**
  - `bench/test_summarize.py:433` `test_floor_column_values_per_run_then_median` — T1/B 3런에 런마다 다른 20개 발화 값(1000·(j+1)+100·k ns)을 넣고, 출력 행 `| T1 / B | 재구성 1회 | 11.1 / 20.1 | 10.6 / 19.1 |`을 명시 단언한다. (c)와 (b), 그리고 풀링 결과가 서로 다르게 나오도록 고른 표본이다. S0/B 행 존재와 E 행 부재도 단언한다.
  - `:342` `test_outside_rebuild_uses_hoveredborder` — 잔여가 `hoveredborder − rebuild`이고 `hover − rebuild`가 아님을 단언한다.
  - 결함 주입에 `floor_cell_pooled`(런 간 중앙값 대신 풀링 floor)와 `outside_from_hover`를 추가했고 둘 다 테스트 실패로 검출된다.
