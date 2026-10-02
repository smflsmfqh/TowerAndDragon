# 측정용 스냅샷 3종 (M2, 2026-09-23)

세이브 위치는 `Application.persistentDataPath` 아래다. 이 프로젝트의 실제 경로는
`~/Library/Application Support/TowerAndDragon/TowerAndDragon/Saves/` 다.
(`~/Library/Application Support/DefaultCompany/TowerAndDragon/` 에도 폴더가 있으나
회사명이 바뀌기 전의 **죽은 경로**이고 이번 작업에서 읽지도 쓰지도 않았다.)

작업 시작 시점에 실제 경로의 `Saves/`는 **비어 있었다** — 덮어쓴 사용자 세이브는 없다.
슬롯 0(자동저장)·1은 그대로 비워 두고 2·3·4만 썼다.

## 슬롯 배정

| 슬롯 | 이름 | 쓰는 곳 |
|---|---|---|
| 2 | `snap_base` | M4 시나리오 A\~D |
| 3 | `snap_e_depart` | M5 E-출발 |
| 4 | `snap_e_complete` | M5 E-재진입 |

## 저장된 상태 (저장 직후 · 복원 후 둘 다 확인)

| 항목 | snap_base (2) | snap_e_depart (3) | snap_e_complete (4) |
|---|---|---|---|
| 주기 / 일차 | Day / 1 | Day / 1 | Day / 1 |
| 청크 hidden / visible / conquered | 24 / 12 / 5 | 24 / 12 / 5 | 24 / 12 / 5 |
| 점령 가능 | 12 | 12 | 11 |
| 아직 못 가는 곳 | 0 | 0 | 1 — (0, -2) |
| 활성 원정 | 0 | 0 | **1개, 대상 (0, -2)** |
| 점령 모드 | 꺼짐 | 꺼짐 | 꺼짐 |
| 자원 | 카탈로그 12종에 +5000 | 동일 | 동일 |

점령 가능 청크 12개: `(0,-2) (-1,-2) (1,-2) (-2,-1) (2,-1) (-2,0) (2,0) (-2,1) (2,1) (-1,2) (1,2) (0,2)`

복원 확인은 **정상 로드 경로**(`SaveService.RequestLoadAndReloadScene` → 씬 리로드 →
`GameManager.Start` → `BeginLoadFlow`)로 했고, 복원 후 상태가 위 표와 같음을 셋 다 확인했다.
화면은 `screenshots/snap_base.png`, `snap_e_depart.png`, `snap_e_complete.png`.

## 상태를 어떻게 만들었나 (정상 플레이가 아니다)

새 런(Day 1)에서 시작해 **디버그 경로**로 조성했다. 정상 플레이(밤을 치르는 정산)와 같다고 주장하지 않는다.

1. `ConquestManager.SendExpedition()`을 무제한 자원으로 4개 보내고 곧바로
   `ConquestManager.DebugForceCompleteAllExpeditions()`로 완료 → 영토 한 겹 확장(점령 1 → 5).
2. `ResourceManager.DebugAddToAllCatalogResources(5000)` → 자원 지급.
3. 슬롯 2·3 저장.
4. `SendExpedition()`으로 원정을 **정확히 하나만** 더 걸고(대상 `(0,-2)`) 슬롯 4 저장.
   원정이 하나뿐이어야 M5의 `DebugForceCompleteAllExpeditions()`가 의도한 하나만 완료시킨다(§2-1 E 함정).

도구는 `Assets/Bench/Editor/BenchSnapshotAuthor.cs`이고, 사본은 `source/bench_harness/`에 있다.

## snap_base와 snap_e_depart는 내용이 같다

두 슬롯은 같은 게임 상태에서 연달아 저장했으므로 저장 시각 외에는 같은 내용이다(해시는 다르다).
파일을 나눠 둔 이유는 E 쪽 준비 조건이 나중에 달라져도 A\~D 스냅샷이 흔들리지 않게 하기 위해서다.
E-출발은 자원·인구가 충분해야 하는데 두 스냅샷 모두 그 조건을 만족한다.

## 해시 (SHA-256)

`snapshots/` 아래 사본 기준. `shasum -a 256 bench/2026-09-23_playmode/snapshots/*.json`으로 다시 낼 수 있다.

```
044ef0f72614096c500ac4dadbb00b6381bf224e00f49d2eab53ba648b767bd8  snap_base.meta.json
84e67ab3f661f44fbd1cdb9ff2a72dfd4e9a895b5e6846f49dfcaf4550814b44  snap_base.save.json
e807444ee7914d6510dfcd3ba862c0f5d76199311c2b3207c4340fb1cd9cebcd  snap_e_complete.meta.json
be59d27a5b6ab456ead3b4316f807c16861fd8f0c5211b89e027c8e5da1931e0  snap_e_complete.save.json
a7cb3ae1230da45cf64c626b1543783f25540ac677d2282d5f287c322ffb7ed2  snap_e_depart.meta.json
0599bfdf1fd3b54285a4e81f70b7154edb06b6458231af85b404ce8c0999b297  snap_e_depart.save.json
```

## 측정 전 반드시 확인할 것 (M3에서 겪은 것)

1. **콘솔의 Error Pause를 끈다.** 씬이 로드될 때마다 `ExpeditionMarkerRenderer.OnEnable`에서
   NullReferenceException이 난다(이번 측정과 무관한 기존 문제). Error Pause가 켜져 있으면
   그 예외에 플레이 모드가 **일시정지**되어 프레임이 더 이상 흐르지 않고, 스냅샷 복원도 끝나지 않는다.
   증상은 "`Time.frameCount`는 멈춰 있는데 `realtimeSinceStartup`만 올라간다"이다.
2. **Unity 에디터 창을 앞으로 둔다.** 비포커스 상태에서는 플레이 루프가 돌지 않는다.
   `Application.runInBackground = true`로도 풀리지 않았다.
3. **Game 뷰를 1920×1080으로 맞춘다**(§2-5). 스냅샷 확인 스크린샷은 616×347에서 찍혔다 —
   이 해상도는 측정에 쓰지 않는다.
4. 가이드(조언자) 오버레이가 복원 때마다 다시 뜬다. 하네스가 측정 동안 통째로 내리고
   끝나면 되돌린다(`ConquestHighlightProfileHarness.SuppressBackgroundOverlays`).
