# 대표 Profiler 화면 — 확보 방법 (2026-09-25)

계획은 네 지점을 Profiler 창 화면으로 남기라고 한다(§3-4). **자동으로 찍지 못했다.**

## 왜 자동화하지 않았나

Unity 에디터에는 창 하나만 이미지로 뽑는 API가 없다. macOS `screencapture`로 전체 화면을 찍어 봤더니
Unity가 아닌 **다른 디스플레이의 창이 찍혔다.** 무엇이 잡힐지 통제할 수 없는 방식이라
프로젝트 디렉터리에 쓰지 않기로 하고 그 파일은 지웠다. 이후로는 쓰지 않는다.

## 준비는 끝나 있다

`.raw` 10개를 보존해 두었다. M8에서 `Assets/Bench/`는 삭제했으므로 아래 메서드는 현재 프로젝트에서 바로 호출할 수 없다.

**수동 확인:** Unity Profiler의 Frame Count를 2,000으로 설정하고 아래 표의 raw를 Load한다.
로드된 프레임 범위와 선택한 프레임 번호를 확인한 뒤 해당 마커를 펼쳐 **Profiler 창만 직접 캡처**한다.
이미지는 측정 루트의 `screenshots/`에 저장한다. 기존 raw를 읽는 데 플레이 진입이나 계측 패치 재적용은 필요 없다.

**헬퍼를 이용할 경우:** 보관 사본 `source/bench_harness/` 아래의 다음 3파일을
임시 에디터 폴더(예: `Assets/Bench/Editor/`)로 복원한다.

- `Editor/BenchProfilerCapture.cs`
- `Runtime/BenchConfig.cs`
- `Runtime/BenchVariant.cs`

이 세 파일로 raw 로드·프레임 선택 헬퍼를 사용할 수 있다. 런타임 부트스트랩 전체를 복원할 필요는 없다.
Coplay MCP로 컴파일을 확인하고 `BenchConfig.OutputRoot`를 `bench/2026-09-23_playmode`로 설정한 뒤
아래 메서드를 실행한다. 반환된 사용 가능 범위와 선택 결과를 확인한다.
완료 후 `Restore()`를 호출하고, 이번에 복원한 파일과 Unity가 생성한 해당 메타만 제거한다.
기존 파일이 같은 위치에 있다면 덮어쓰지 않는다.

| 지점 | 메서드 | raw | frameIndex | 그 프레임의 값 |
|---|---|---|---:|---|
| S0 정지 | `ShowS0Still()` | `S0_A_run4.raw` | 900 | 호버 856,500 ns |
| T1 정지 | `ShowT1Still()` | `T1_A_run4.raw` | 900 | 호버 14,916 ns |
| T1 전환 | `ShowT1Transition()` | `T1_D_run4.raw` | 896 | 재구성이 일어난 프레임 |
| E-재진입 스파이크 | `ShowReentrySpike()` | `T1_EReenter_run11.raw` | 30 | ModeEnter 56,841,791 ns |

프레임 번호는 raw의 `frame_index`이고, 프레임 대응이 오프셋 0으로 확인됐으므로
하네스 CSV의 `frame_index`와 같은 값이다. 찍은 뒤 `Restore()`로 창 최대화를 푼다.

권장 파일 이름: `profiler_s0_a_still.png` · `profiler_t1_a_still.png` ·
`profiler_t1_d_transition.png` · `profiler_e_reenter_spike.png`

## 화면 없이도 같은 정보가 있다

`representative_frames.md`에 같은 네 지점의 프레임별 수치가 표로 있다.
Profiler 화면은 **그 수치를 눈으로 확인하는 보조 자료**이지 유일한 근거가 아니다.
