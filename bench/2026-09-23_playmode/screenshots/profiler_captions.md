# 대표 Profiler 화면 4장 — 캡션 (2026-10-01)

**보완 런의 대표 프레임이며 본 측정 중앙값이 아니다.** 각 장은 런 하나의 프레임 하나다.
본편 표(09 §4)의 근거는 본 측정 44런의 집계이고, 이 화면은 그 수치가 어떤 프레임에서 어떻게 보이는지 눈으로 확인하는 보조 자료다.

## 공통 조건

- 원본: 보존한 `raw/<런>.raw`(`raw_manifest.tsv`로 해시 확인 가능). 재측정하지 않았다.
- Unity 6000.3.15f1 에디터 Profiler, CPU Usage 모듈 **Hierarchy** 보기, 스레드 Main Thread, 검색 필터 `TND.Bench`(측정 마커만 평평하게 표시).
- 프레임 선택·raw 로드는 `summary/n6_capture/N6Show.cs`(Coplay `execute_script`로 실행), E 장의 Frame Count 변경은 `N6FrameCount.cs`.
- **캡처 방식: 사용자가 macOS 창 선택 캡처(⌘⇧4 → Space → ⌥+클릭)로 Profiler 독립 창 하나만 찍었다.** 자동 캡처(`screencapture -l`)는 터미널에 화면 기록 권한이 적용되지 않아(창 제목 조회 불가) 쓰지 않았다. 저장 후 에이전트가 이미지를 열어 Profiler 외 내용이 없음을 확인했다.

## 읽을 때 주의

- **프레임 번호**: Profiler는 1부터 세어 표시한다. 화면의 `Frame: 901`은 raw·하네스의 `frame_index` 900이다.
- **Time ms 열은 소수 둘째 자리에서 버림 표시된다**(반올림 아님). 예: 실제 56.3052 ms가 56.30으로, 0.4194 ms가 0.41로 보인다. 아래 표의 정확한 값은 같은 raw에서 `HierarchyFrameDataView`로 읽은 값이며 `csv_from_raw/<런>.csv`와 일치한다.
- **CPU Usage 그래프는 66 ms 위를 잘라 그린다.** 선택 프레임의 정확한 시간은 그래프가 아니라 Hierarchy 위 `CPU:` 표시다. 각 런에서 66.7 ms를 넘는 프레임 수: S0_A_run4 237/1802(최대 112.9 ms), T1_A_run4 215/1802(97.0), T1_D_run4 278/1802(96.3), T1_EReenter_run11 26/182(135.6).
- 화면의 GC Alloc 열은 KB 표시(1 KB = 1,024 B)이며, Memory 모듈의 `GC Allocated In Frame`은 프레임 전체 GC다(구간 GC가 아님).
- 이미지에는 글자를 넣지 않았다.

## 장별

### `profiler_s0_a_still.png` — S0 정지: 커서가 멈춰도 매 프레임 재구성

| 항목 | 값 |
|---|---|
| 런 · 프레임 | `S0_A_run4` · frame_index 900 (화면 `Frame: 901 / 1802`) · Frame Count 2,000 |
| 프레임 CPU | 47.54 ms |
| `TND.Bench.S0.Hover` | 0.8564 ms · 1회 · GC 1,216 B |
| `TND.Bench.S0.Rebuild` | 0.8001 ms · 1회 · GC 0 B |
| 프레임 전체 GC | 23,289 B (화면 22.7 KB) |

### `profiler_t1_a_still.png` — T1 정지: 가드에 걸려 재구성하지 않음

| 항목 | 값 |
|---|---|
| 런 · 프레임 | `T1_A_run4` · frame_index 900 (화면 `Frame: 901 / 1802`) · Frame Count 2,000 |
| 프레임 CPU | 33.80 ms |
| `TND.Bench.T1.Hover` | 0.0148 ms(14.8 µs, 화면 0.01) · 1회 · GC 0 B |
| 재구성 마커 | 이 프레임에 발화 없음(Hierarchy에 행이 없다) |
| 프레임 전체 GC | 22,073 B (화면 21.6 KB) |

`profiler_capture_guide.md`의 "호버 14,916 ns"는 하네스 값이고, 화면·이 표는 raw 값(14.8 µs)이다. 두 출처의 차이는 `summary/frame_alignment.md`의 양의 차이와 같은 성격이다.

### `profiler_t1_d_transition.png` — T1 재구성 마커가 발화한 프레임

| 항목 | 값 |
|---|---|
| 런 · 프레임 | `T1_D_run4` · frame_index 896 (화면 `Frame: 897 / 1802`) · Frame Count 2,000 |
| 프레임 CPU | 40.20 ms |
| `TND.Bench.T1.Hover` | 0.4424 ms · 1회 · GC 10,272 B |
| `TND.Bench.T1.HoveredBorder` | 0.4231 ms · 1회 · GC 10,272 B |
| `TND.Bench.T1.HoveredBorderRebuild` | 0.4194 ms · 1회 · GC 10,232 B |
| 프레임 전체 GC | 32,409 B (화면 31.6 KB) |

파일 이름의 "transition"은 가이드의 권장 이름을 따랐다. 뜻은 **재구성 마커 발화 프레임**이다(커서 이동 여부와 같은 뜻이 아니다).

### `profiler_e_reenter_spike.png` — E-재진입: 점령 모드 재진입 프레임

| 항목 | 값 |
|---|---|
| 런 · 프레임 | `T1_EReenter_run11` · frame_index 30 (화면 `Frame: 31 / 182`) · **Frame Count 200** |
| 프레임 CPU | 135.58 ms (그래프에서는 66 ms 위가 잘려 꼭대기가 보이지 않는다) |
| `TND.Bench.T1.ModeEnter` | 56.8413 ms · 1회 · GC 142,394 B |
| `TND.Bench.T1.Reclassify` | 56.3052 ms · 1회 · GC 142,394 B |
| `TND.Bench.T1.Hover` | 0.5805 ms · 1회 · GC 5,440 B |
| `TND.Bench.T1.HoveredBorder` | 두 행: 0.5472 ms · GC 5,440 B (호버 안) / 0.0098 ms · GC 40 B (재분류 안) — 합 2회 · 5,480 B |
| `TND.Bench.T1.HoveredBorderRebuild` | 0.5460 ms · 1회 · GC 5,440 B |
| 프레임 전체 GC | 170,065 B (화면 166.1 KB) |

- **Frame Count를 이 장만 200으로 낮췄다**(가이드는 2,000). 182프레임 런이 2,000프레임 축에서는 그래프 오른쪽 9%에 몰려 보이기 때문이다. 182프레임 전부(0~181)가 로드됐고, 캡처 후 2,000으로 되돌렸다.
- `HoveredBorder`가 두 행인 것은 E 런에서 이 마커가 호버 경로 밖(재분류 끝)에서도 불리기 때문이다(`summary/section_gc.md`).

## 화면 해시 (SHA-256)

```
bdea7c4d1179894539aec41aa5e667c72927494cbea654d50ffe9b7f8ead40a1  profiler_s0_a_still.png
b99e2de66e9a9638cb97c02d9295c5d33728ae5654be923d046b6e1242a0c2af  profiler_t1_a_still.png
c7b66b055a556e971247fc4809a7f45c52160ae91628d1304476979f3c2fa7ec  profiler_t1_d_transition.png
41647aec6a56a8f2605ad8ba8e670825b0d6c18085edc847d09497f93835d3cf  profiler_e_reenter_spike.png
```
