// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.
// 계획: bench/08_측정계획_가드×표시방식.md, 순서: bench/08_측정_마일스톤.md

/// <summary>
/// 비교할 구현 변형. 본편은 S0 ↔ T1이고 S1p는 선택(M7)이다.
/// </summary>
public enum BenchVariant
{
    /// <summary>061cc138 현재 구현 - ConquestModeController를 그대로 켠다.</summary>
    T1 = 0,

    /// <summary>038b253e 과거 처리 경로를 현재 데이터 어댑터 위에 이식한 드라이버.</summary>
    S0 = 1,

    /// <summary>(선택) 4cb924c5의 RebuildHighlightBuffers 경로. M7에서만 쓴다.</summary>
    S1p = 2,
}

/// <summary>
/// 측정 시나리오. A~D는 1,800프레임 입력 재생, E 둘은 상태 전이 표본이다.
/// </summary>
public enum BenchScenario
{
    /// <summary>정지 - 점령 가능 청크 위에 커서 고정.</summary>
    A = 0,

    /// <summary>기준 60프레임당 5셀 이동.</summary>
    B = 1,

    /// <summary>기준 60프레임당 15셀 이동.</summary>
    C = 2,

    /// <summary>기준 60프레임당 40셀 이동.</summary>
    D = 3,

    /// <summary>E-출발 - 실제 UI 버튼 경로로 원정을 보낼 때의 갱신.</summary>
    EDepart = 4,

    /// <summary>E-재진입 - 완료된 영토 상태에서 점령 모드를 다시 켤 때의 갱신.</summary>
    EReenter = 5,
}
