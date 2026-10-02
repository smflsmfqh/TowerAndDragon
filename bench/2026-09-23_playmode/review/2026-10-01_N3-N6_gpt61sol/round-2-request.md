## 검토 범위

1라운드와 같다. 이번에는 아래 대응 부분을 보고, 새 문제가 보이면 지적해 달라.

## 이번 작업에서 한 일

- 지적 4건을 원자료로 직접 확인한 뒤 모두 고쳤다(반론 없음).
- 같은 표현을 현재 문서 전체(`bench/README.md`, `09_측정결과.md`, `호환범위.md`, `summary/{section_gc,representative_frames,frame_alignment,transition_vs_still,cross_check,profiler_capture_guide}.md`, `screenshots/profiler_captions.md`)에서 다시 찾았다: "new List", "할당처는", "뒤로 밀린", "포커스", "30프레임", "전이는 30", "전이 프레임". 고친 곳 외에는 남은 단정이 없었다. `section_gc.md:61` 제목 "할당처는 그리기 루프가 아니다"는 `s0_rebuild` 0 B 관측으로 뒷받침돼 그대로 뒀다.
- 확인에 쓴 값: summary JSON의 `snapshot_slot`·`camera_position`·`camera_orthographic_size`(시나리오별 묶음: A~D 32런 슬롯 2·`(8.878, -3.483, -10.000)`·5.906, E-출발 11런 슬롯 3·같은 카메라, E-재진입 11런 슬롯 4·`(-9.372, 5.267, -10.000)`·6.163), `csv_from_raw`의 `t1_conquerui_calls`(E-출발 11런 모두 frame_index 39에서만 양수)·`t1_modeenter_calls`(E-재진입 11런 모두 30), `T1_EReenter_run*.json` 11개의 `territory_before/after`(전부 같은 값)·`warmup_toggles_mode=true`, E-출발 런은 `territory_*`가 null.

## 직전 지적에 대한 대응

- **R1-1: 고침.**
  - `bench/2026-09-23_playmode/summary/section_gc.md:63~68`: 할당을 "`s0_hover` 안·`s0_rebuild` 밖에서 관측"으로 한정했다. 그 범위의 코드상 작업(`GetHoveredCell`·`GetChunkAt`·`CanSendExpedition`·`new List` 생성·좌표 복사 순회)을 나열하고, 개별 비중은 분리하지 않았다고 명시했다. "청크 크기에 비례"도 미측정이라 뺐다. 회당 수치는 유지했고, 이전 문장은 날짜 메모로 남겼다.
  - `09_측정결과.md` §5(168~170행 부근)도 같은 방식으로 고쳤다.
- **R1-2: 고침.** `09_측정결과.md:28·30`: 슬롯과 카메라를 시나리오별로 적었다. 37~39행 부근에 E-재진입의 카메라가 다른 이유, "같은 조건"은 같은 시나리오 안의 S0↔T1·본 측정↔보완 비교에 적용된다는 점, 정정 메모를 더했다.
- **R1-3: 고침.**
  - `09_측정결과.md` §6 머리(181~187행 부근)를 다음과 같이 고쳤다.
    - E-출발: frame_index 30에 패널 열기 시작, 하네스 코드상 34에 버튼 입력 시작(`transitionFrame + PANEL_OPEN_FRAMES`, `PANEL_OPEN_FRAMES = 4`), 기록상 39에 `ConquerUI` 발화. 34~39 지연의 내역은 확인하지 않았다고 적었다.
    - E-재진입: 측정 밖에서 원정을 강제 완료하고, 완료 영토에서 240프레임 워밍업 중 모드를 켰다 끈 뒤, 30에 `ModeEnter` 발화. 처음 켜는 진입이 아닌 재진입 비용이라고 적었다.
  - 상태 전이 검증(207행 부근): "E-재진입의 측정 밖 준비 단계" 기록이며, 본 측정 10 + 보완 1 전부 같은 값이고 E-출발에는 기록이 없다고 바꿨다.
- **R1-4: 고침.** `09_측정결과.md` §8(267~271행 부근): 확정된 무효 사유는 입력 대응·포인터 주입 불일치로 적고, 에디터 포커스 유실은 관찰한 실패 모드에 근거한 추정이며 두 런의 포커스 상태는 따로 기록하지 않았다고 적었다. `ValidatePointerInjection`은 증상을 검출하는 관문이라고 바꿨다.
- 09 §11 변경 이력에 2026-10-02 행 4개를, 진행 문서 `11_보완작업_마일스톤.md` N6 작업 로그 끝에 대응 내역을 적었다.
