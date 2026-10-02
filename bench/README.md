# 점령 하이라이트 벤치마크

점령 모드 하이라이트의 **과거 경로(S0, `038b253e`에서 이식)** 와 **현재 구현(T1, `061cc138`)** 을
같은 맵·같은 세이브·같은 가상 마우스 입력으로 비교한 측정이다.
**현재 기준은 플레이모드 실측(2026-09-23 \~ 10-02)** 이고, 맨 아래 EditMode 벤치마크(2026-09-06)는 참고용 과거 기록이다.

| 읽을 것 | 문서 |
|---|---|
| 결과 (수치·조건·한계의 원본) | [`2026-09-23_playmode/09_측정결과.md`](2026-09-23_playmode/09_측정결과.md) |
| 결과 해설 (수치별 개선·한계, 포트폴리오 문장) | [`2026-09-23_playmode/12_결과분석_포트폴리오용.md`](2026-09-23_playmode/12_결과분석_포트폴리오용.md) |
| 측정 계획 · 진행 기록 (M0\~M8) | [`08_측정계획_가드×표시방식.md`](08_측정계획_가드×표시방식.md) · [`08_측정_마일스톤.md`](08_측정_마일스톤.md) |
| 보완 작업 기록 (N0\~N6) · 남은 한계 | [`2026-09-23_playmode/11_보완작업_마일스톤.md`](2026-09-23_playmode/11_보완작업_마일스톤.md) 「마무리 상태」 |

아래 경로는 따로 적지 않으면 `bench/2026-09-23_playmode/` 기준이다.

## 수행한 테스트와 결과

측정 조건은 모두 Unity 6000.3.15f1 **에디터 플레이모드**, MacBook Air(M1), Game 뷰 1920×1080, 고정 카메라다.
A\~D 시나리오는 1,800프레임 동안 커서를 각각 정지 / 기준초당 5·15·40셀 움직인다.
E 시나리오는 원정 출발(E-출발)과 점령 모드 재진입(E-재진입)을 180프레임 창에서 잰다.

### 1. 측정 — 게임을 실제로 돌려 잰 것

| # | 테스트 | 무엇을 확인했나 | 결과 | 자료 |
|---|---|---|---|---|
| 1 | **파일럿·스모크** (M3) | 측정 장치가 맞게 동작하는지 본다. 입력이 의도한 셀에 들어가는지, S0·T1이 서로 섞이지 않는지, raw 프레임 수, 계측 오버헤드 | 입력 대응 100%, 격리 통과, raw 프레임 전부 로드. 오버헤드는 런 간 변동에 묻혀 **분리 측정 불가** | `summary/pilot_*.json`·`smoke_*.json`, `screenshots/visual_*.png`, 08 마일스톤 「파일럿 결론」, 1차 결함본은 `superseded/` |
| 2 | **본 측정 A\~D** (M4) | S0/T1 × A\~D × 3회, 24런. 재구성 횟수, 호버 처리 누적 시간, 재구성 1회 비용, 프레임 시간 | 재구성 **1,800회 → 0·11·29·76회**(전 칸 최소=최대). 누적 시간 **19.4\~37.6배** 감소. 재구성 1회는 S0 651\~713 µs, T1 334\~372 µs. **전체 프레임 시간(약 44\~46 ms)에서는 일관된 향상을 확인하지 못함** | 09 §4, `summary/summarize_output_main.md`, `csv/`, `csv_from_raw/`, `summary/<런>.json`, 실행 순서 `summary/run_order.md` |
| 3 | **E 표본** (M5) | T1만 측정, E-출발·E-재진입 각 10세션. 상태가 바뀔 때의 단발 비용 (S0에는 이 기능이 없어 N/A) | 이벤트 구간 중앙값 `ConquerUI` 52.3 ms · `ModeEnter` 38.1 ms. 그중 청크 재분류 33.0 · 37.6 ms. 개선 수치가 아니라 남은 비용 | 09 §6, `summarize_output_main.md` 「E 표본」 |
| 4 | **정산 동등성** (M6) | E-재진입 준비에 쓴 디버그 강제 완료가 정상 정산 경로와 같은 영토 상태를 만드는지 | **점령 관련 상태 완전 일치** (자원·인구·일차는 범위 밖) | `summary/settlement_equivalence.md`, `summary/settlement_state_{debug,normal}.txt` |
| 5 | **보완 10런** | 본 측정과 같은 설정으로 A\~D 8런과 E 2런을 더 돌린다. `.raw`를 보존해 아래 7·8의 원자료로 쓴다 | 4회 합산 배율 36.2 / 29.2 / 25.5 / 19.8배, **결론 불변** | `summarize_output_all.md`, `raw/`(Git 제외, 해시는 `raw_manifest.*`) |
| 6 | **무효 런 판정** | 입력 대응·포인터 주입 관문에 걸린 런을 표에서 빼고 다시 측정 | 2건 무효(`S0_D_run3`, `T1_EReenter_run5`) → 재실행 후 유효. 무효본은 보존 | 09 §8, `summary/invalid_*.json`, `csv/invalid_*.csv` |
| 7 | **구간별 GC** | 보완 런 raw에서 하이라이트 마커 구간의 관리 할당만 뽑는다. 전체 프레임 GC와 분리 | 구간 할당 총량 S0 2.19\~2.79 MB → T1 0\~0.60 MB. **1회당은 T1이 약 5배 많다**(D: 7,876 ↔ 1,552 B). **칸당 1런** | 09 §5, `summary/section_gc.md`, `summarize_output_supplemental.md` 「구간 GC」 |
| 8 | **대표 Profiler 화면** (N6) | 보완 런 raw를 Profiler에 올려 대표 프레임 4장을 캡처 | S0/A 정지, T1/A 정지, T1/D 전환, E-재진입 스파이크 | `screenshots/profiler_*.png`, 캡션·읽을 때 주의 `screenshots/profiler_captions.md`, 캡처 스크립트 `summary/n6_capture/` |

### 2. 계측 검증 — 잰 값을 믿어도 되는지

| # | 테스트 | 무엇을 확인했나 | 결과 | 자료 |
|---|---|---|---|---|
| 9 | **하네스 ↔ Profiler raw 교차 검증** | 하네스가 기록한 마커 시간 합계와 raw 원자료 합계가 같은지 | 측정 창 기준 최대 상대차 **1.03%** (목표 ±5%) | 09 §7, `summary/cross_check.md`, `summarize_output_all.md` 「교차 검증」 |
| 10 | **프레임 단위 대응** | 두 출처를 프레임마다 짝지어 발화 프레임과 오프셋이 맞는지 | 54런·91,057쌍, 오프셋 전부 +0, **발화 프레임 불일치 0건**. 판정선(±5% 또는 5,000 ns) 초과 1건, 원인 미상. 판정선은 사후에 정한 탐색적 기준 | 09 §7, `summary/frame_alignment.md`, `summarize_output_all.md` 「프레임 대응 요약」 |
| 11 | **재구성 발생/미발생 분할** | 재구성 마커가 발화한 프레임과 발화하지 않은 프레임의 호버 시간을 나눠 본다 | T1 재구성 없는 프레임 19\~22 µs (계측 오버헤드 포함 가능, 분리 못 함) | 09 §4 소표, `summary/transition_vs_still.md` |

### 3. 집계 코드 검증 — 결과 표를 만드는 스크립트가 맞는지

| # | 테스트 | 결과 | 실행 (저장소 루트) |
|---|---|---|---|
| 12 | **회귀 테스트** `bench/test_summarize.py` | 38개 통과 | `python3 -m unittest bench/test_summarize.py` |
| 13 | **결함 주입** — 집계기 사본을 일부러 망가뜨려 테스트가 잡는지 본다 | 11개 중 10개 검출. `floor_float`는 결함이 아니라 같은 결과를 내는 변형이라 통과가 정상 | `python3 bench/2026-09-23_playmode/summary/n3_checks/inject_faults.py` |
| 14 | **기존 문서 수치 재현** — 집계 이전에 손으로 만든 표를 집계기가 다시 만들어 내는지 | 09 §4 재구성 µs 14/14, `transition_vs_still.md` 22/22, `frame_alignment.md` 런별 54/54. "+1 63,792 ns" 한 값만 미재현 | `summary/n3_checks/check_alignment_vs_doc.py <summarize_output_all.md>`, `check_transition_rules.py`, `probe_alignment_sets.py` |
| 15 | **출력 재현** — 집계 출력이 저장본과 같은지 | main·supplemental·all 3개 모두 바이트 일치. M8 당시 집계기(`source/history/M8/`)도 `summary/summarize_output.md`를 바이트 그대로 재현 | `python3 bench/summarize.py bench/2026-09-23_playmode --group main` (이하 `supplemental`·`all`) |
| 16 | **보관 자료 해시** | 하네스·집계기·manifest·대조 스크립트·Profiler 화면 56파일 일치 | `cd bench/2026-09-23_playmode/source && shasum -a 256 -c source.sha256` |
| 17 | **외부 검토** (Codex, 읽기 전용) | 보완 작업 N3\~N6 PASS, 결과 해설 문서(12) PASS | `review/` 아래 폴더별 `round-N-request.md`·`round-N-review.json` |

### 코드 위치

| 코드 | 역할 |
|---|---|
| `source/bench_harness/Runtime/` | 측정 하네스. `ConquestHighlightProfileHarness`(입력 재생·기록), `LegacyConquestHighlightDriver`(S0 이식 경로), `BenchRecorders`(프레임별 ProfilerRecorder 값 기록)·`BenchOutputWriter`(측정 후 CSV·JSON 저장)·`BenchRunReport`(런 메타·유효성 판정) |
| `source/bench_harness/Editor/` | 실행·추출 도구. `BenchLauncher`·`BenchBatchRunner`(런 설정·연속 실행), `ProfilerRawSummary`(`.raw` → `csv_from_raw/` 추출), `BenchProfilerCapture`(보존 `.raw`를 Profiler 창에 올리기), `BenchSettlementCheck`(정산 동등성), `BenchSnapshotAuthor`(세이브 스냅샷) |
| `source/legacy_origin/` | S0의 출처인 `038b253e` 원본 6파일 (해시 `summary/origin_manifest.json`, 이식 범위 `호환범위.md`) |
| `patches/` | 측정 중에만 적용한 계측 패치 3파일의 diff와 적용 전후 해시 |
| `bench/summarize.py` | 집계기. 런 그룹은 `runs_manifest.json`(main 44 · supplemental 10 · all 54)이 정한다 |

하네스와 계측 패치는 측정 후 `Assets/`에서 제거했다. 다시 측정하려면 [08 마일스톤 「다시 측정하려면」](08_측정_마일스톤.md#다시-측정하려면)을 따른다.

### 결과를 인용할 때 같이 밝힐 한계

- 에디터 플레이모드의 **해당 처리 구간** 비용이다. 게임 전체 FPS나 Windows 빌드 성능을 뜻하지 않는다.
- S0는 과거 커밋을 그대로 실행한 것이 아니라 과거 경로를 현재 프로젝트에 **이식**한 것이다.
- T1은 변경 가드와 외곽선 표시가 함께 바뀐 결과이며, 각각의 기여는 분리하지 않았다.
- 구간 GC는 칸당 1런이다. 계측·하네스 오버헤드는 분리 측정하지 못했다.
- 그 밖의 한계와 쓰면 안 되는 표현은 09 §9와 12 §5에 있다.

---

> **아래는 플레이모드 실측보다 앞선 EditMode 벤치마크(2026-09-06)의 재현 안내이며 수치는 참고용입니다.**
> EditMode·배치모드에서 관리 코드만 잰 것이라 플레이모드 실측과 조건이 다릅니다. 섞어 인용하지 마세요.

---

# (이하 과거 기록) EditMode 벤치마크 — 2026-09-06

벤치마크 파일 2개는 **TowerAndDragon 저장소에 커밋돼 있지 않다.**
최종 제출본을 깨끗하게 두기 위해 측정 후 제거했다. 다시 재려면 아래 순서를 따른다.

| 파일 | 용도 |
|---|---|
| **`ConquestHighlightBeforeAfterBenchmark.cs`** | **과거 개선 전 vs 출시본 비교 (참고용)** — 두 구현을 나란히 실행 |
| `ConquestHighlightBenchmark.cs` | 초기 단일 측정본 (출시본 경로만) |

측정 결과를 해석한 분석 문서는 따로 있다.

| 문서 | 다루는 것 |
|---|---|
| `06_점령하이라이트_개선전후_비교.md` | 과거 개선 전 vs 출시본 A/B 비교 (참고용) |
| `07_초기측정_구간별비용_분석.md` | 초기 측정본의 구간별 비용 · 계측 함정 · `raw/` 파일 성격 |

## 1. 파일 배치

```bash
cp bench/ConquestHighlightBeforeAfterBenchmark.cs \
   ~/unityProject/TowerAndDragon/Assets/Tests/Editor/
```

`Assets/Tests/Editor/`에 있어야 `Assembly-CSharp-Editor`에 포함되어 `AssetDatabase`와
게임 클래스(`ChunkLayoutTable`, `ComponentPool`)를 모두 참조할 수 있다.

## 2. 실행

```bash
/Applications/Unity/Hub/Editor/6000.3.15f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath ~/unityProject/TowerAndDragon \
  -runTests -testPlatform EditMode \
  -testFilter "ConquestHighlightBeforeAfterBenchmark" \
  -testResults /tmp/bench-results.xml \
  -logFile /tmp/bench.log

grep "\[AB\]" /tmp/bench.log
```

Unity 에디터가 같은 프로젝트를 열고 있으면 배치모드 실행이 막힌다. **다른 프로젝트가 열려 있는
것은 상관없다.**

## 3. 측정이 끝나면 제거

```bash
rm ~/unityProject/TowerAndDragon/Assets/Tests/Editor/ConquestHighlight*Benchmark.cs*
git -C ~/unityProject/TowerAndDragon status --porcelain   # 비어 있어야 한다
```

---

## `ConquestHighlightBeforeAfterBenchmark`가 재는 것

| 테스트 | 재는 것 |
|---|---|
| `A_PerFrameCost_CursorIdle` | 커서가 멈춰 있는 프레임 1개 — 개선 전 vs 출시본 |
| `B_PerFrameCost_ChunkChanged` | 호버 청크가 실제로 바뀐 프레임 1개 |
| `C_GarbagePerFrame` | 프레임당 GC 가비지 |
| `D_SixtySecondSession` | 점령 모드 60초(3,600프레임) 누적 — 커서 속도 4종 |
| `E_ScalabilityByProgress` | 판정 대상 청크 1\~41개별 전체 재계산 1회 비용 |

개선 전 경로는 `038b253e`의 `HighlightHoveredChunk()`를, 출시본 경로는 현재 `HandleHover()` →
`RefreshHoveredBorder()`와 `RecomputeConquerableClassification()`을 옮겨 왔다. 경계 추적
(`CollectBorderEdges` / `TraceLoops` / `FindNextEdge`)은 `ConqueredChunkBorderRenderer` 원본 그대로다.

## `ConquestHighlightBenchmark`(초기본)가 재는 것

| 테스트 | 재는 것 | 대응하는 실제 코드 |
|---|---|---|
| `Benchmark_SingleChunkHighlightRebuild` | 호버 청크 셀 전체를 다시 칠하는 1회 비용 + 관리힙 증가 | `038b253e`의 `ConquestModeController.HighlightHoveredChunk()` |
| `Measure_PerFrameGarbageOfCoordList` | 매 프레임 새로 만들던 `List<Vector3Int>` 1개의 실제 크기 | `GetChunkCellCoords()` |
| `Benchmark_FullClassificationScan` | Visible 청크 전체를 훑어 셀별 틴트를 모으는 1회 비용 | 현재 `RecomputeConquerableClassification()` |
| `Benchmark_TilemapSetColorForAllCells` | 모은 틴트를 실제 타일맵에 반영하는 1회 비용 | `FogOfWarRenderer.ApplyOverlayTints()` → `RepaintCells()` |
| `Measure_ChunkChangeRateAlongSweep` | 커서가 셀을 몇 칸 지날 때마다 청크가 바뀌는지 | 이벤트 기반 재계산의 실제 호출 빈도 |

데이터는 전부 실제 `Assets/Data/ConquestData/CLT_ChunkLayoutTable.asset`에서 읽는다
(청크 41개 · 셀 4,705칸). 가짜 데이터로 재지 않는다.

## 한계

- **EditMode · 배치모드 기준값이다.** 실제 빌드의 플레이 모드보다 오버헤드가 크다.
  상대 비교(A 대비 B가 몇 배, 프레임 예산의 몇 %)는 유효하지만, 절대값을 "게임에서 이만큼
  걸린다"로 옮기면 안 된다.
- 렌더링(드로우 콜·배칭)은 포함하지 않는다. **관리 코드와 Unity API 호출 비용만** 잰다.
- `Benchmark_SingleChunkHighlightRebuild`의 `GC.GetAllocatedBytesForCurrentThread()`는 이 환경에서
  0을 돌려준다(Mono 미구현). 그래서 가비지 크기는 `Measure_PerFrameGarbageOfCoordList`에서
  객체를 살려 둔 채 `GC.GetTotalMemory(true)` 차이로 따로 잰다.

## ④(60초 세션) 계측 방식 주의

프레임마다 `Stopwatch.Start()/Stop()`을 부르면 그 오버헤드(수십 ns)가 출시본의 프레임당
비용(0.09 µs)과 같은 자릿수라 **출시본 수치에 섞인다.** 현재 코드는 개선 전 루프와 출시본 루프를
각각 통째로 한 번씩만 감싼다. 이 파일을 고칠 때 이 구조를 되돌리지 말 것.

## 공통 주의

- 두 벤치마크 모두 청크의 **전체 셀**을 대상으로 잰다. 출시본은 실제로는 `LandCellCoords`(육지 셀)만
  칠하므로, **출시본 쪽 비용이 실제보다 다소 크게 나온다** — 개선 폭을 부풀리지 않는 방향의 오차다.
- **실행 간 편차가 20% 정도 있다.** 최소 2회 돌리고, 문서에는 **개선 폭이 작게 나오는 쪽**을 쓴다.
  유효숫자는 두 자리를 넘기지 않는다.

## 측정 환경 (2026-09-06 기록값)

Unity 6000.3.15f1 · `-batchmode -nographics` · Apple Silicon Mac (Darwin 24.6.0) ·
워밍업 200회 후 2,000회 반복 평균 (`Tilemap.SetColor`만 60회)
