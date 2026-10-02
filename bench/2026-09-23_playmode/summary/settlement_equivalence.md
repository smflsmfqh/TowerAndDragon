# M6 보완 — 정상 정산과 디버그 완료의 상태 동등성 (2026-09-24)

M5는 완료된 영토 상태를 `DebugForceCompleteAllExpeditions()`로 준비했다.
§2-1 E는 그 전제를 **정상 완료 경로와 비교해 확인하라**고 요구한다. 확인하지 않으면
"완료된 영토에서의 재진입을 쟀다"고 말할 수 없다. 앞선 보고에는 이 근거가 빠져 있었다.

## 방법

같은 스냅샷(슬롯 4 `snap_e_complete`, 활성 원정 `(0,-2) 0/2` 하나)에 대해
**서로 다른 플레이 세션에서** 두 경로를 각각 돌리고 표시 판정에 쓰이는 상태를 같은 형식으로 덤프했다.

| 경로 | 수행 |
|---|---|
| 디버그 | `ConquestManager.DebugForceCompleteAllExpeditions()` 1회 |
| 정상 | `CycleManager.ForceEndDay()` → `EndNight()` **2주기** (원정이 2일 필요) |

정상 경로는 `OnNightEnd`에 걸린 `OnSettlement`이 **실제로 실행된다** — 완료 이벤트를 손으로 Invoke하지 않는다.
다만 밤 전투는 치르지 않는다. 그래서 이 검증은 **점령 관련 상태에 한정**하며,
자원·인구·적 상태·일차는 두 경로가 당연히 다르다(한쪽은 밤을 두 번 넘겼다).

## 결과 — 완전 일치

`settlement_state_debug.txt`와 `settlement_state_normal.txt`의 `## AFTER` 구간을 `diff`한 결과 **차이 없음**.

```
conquered(6): (0,0) (0,1) (0,-1) (0,-2) (1,0) (-1,0)
visible(14): (0,2) (0,-3) (1,2) (1,-2) (-1,2) (-1,-2) (1,-3) (-1,-3) (2,0) (-2,0) (2,1) (2,-1) (-2,1) (-2,-1)
conquerable(13): (0,2) (0,-3) (1,2) (1,-2) (-1,2) (-1,-2) (-1,-3) (2,0) (-2,0) (2,1) (2,-1) (-2,1) (-2,-1)
unreachable(1): (1,-3)
activeExpeditions(0):
enhancedChunks(1)
```

점령 청크, 공개된 청크, 점령 가능/불가 분류, 편입 결과, 활성 원정, 적 강화 청크가 모두 같다.
이것들이 곧 재진입 시 `RecomputeConquerableClassification`이 읽는 입력이므로,
**M5가 잰 재진입은 정상 완료로 도달한 상태에서의 재진입과 같은 입력 위에서 돈 것**이다.

## 왜 같은가 (코드 근거)

두 경로가 같은 `CompleteConquest()`를 부른다.

```
OnSettlement(int)                     DebugForceCompleteAllExpeditions()
  └ expedition.AdvanceDay()             └ (일수 검사 없음)
  └ if (!IsComplete) continue
  └ CompleteConquest(coord)  ←── 같은 함수 ──→  CompleteConquest(coord)
  └ _activeExpeditions.RemoveAt(i)      └ _activeExpeditions.RemoveAt(i)
  OnExpeditionsChanged?.Invoke()        OnExpeditionsChanged?.Invoke()
```

차이는 **일수 진행과 완료 판정**뿐이다. 스냅샷에 원정이 하나만 있고 그 하나를 완료시키는 한
결과 상태가 같다. 실측이 이를 뒷받침한다.

## 남는 한계 — 이것까지 같다고 주장하지 않는다

- **밤 전투를 치르지 않았다.** 자원·인구·적 상태·일차는 두 경로가 다르다.
  M5가 재는 것은 재진입 시점의 **점령 분류 갱신**이고 그 입력이 같음을 보인 것이지,
  게임 전체 정산과 동등하다는 뜻이 아니다(§2-1 E가 금지한 주장).
- `SETTLEMENT_MARKER`(`TND.Conquest.Settlement`)는 디버그 경로를 통과하지 않는다.
  **정산 자체의 비용은 이 문서에서 재지 않았다** — M7 선택 항목이며 수행하지 않았다.
- 원정이 2개 이상인 스냅샷에서는 확인하지 않았다. `DebugForceCompleteAllExpeditions()`가
  일수와 무관하게 전부 완료시키므로, 그 경우 두 경로는 **다를 수 있다**.
  M5 스냅샷은 원정이 정확히 하나이고 하네스가 매 세션 그것을 검사한다.
