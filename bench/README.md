# 재현 벤치마크 — 실행 방법

벤치마크 파일 2개는 **TowerAndDragon 저장소에 커밋돼 있지 않다.**
최종 제출본을 깨끗하게 두기 위해 측정 후 제거했다. 다시 재려면 아래 순서를 따른다.

| 파일 | 용도 |
|---|---|
| **`ConquestHighlightBeforeAfterBenchmark.cs`** | **개선 전 vs 출시본 비교 (주력)** — 두 구현을 나란히 실행 |
| `ConquestHighlightBenchmark.cs` | 초기 단일 측정본 (출시본 경로만) |

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
| `E_ScalabilityByProgress` | 판정 대상 청크 1~41개별 전체 재계산 1회 비용 |

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
