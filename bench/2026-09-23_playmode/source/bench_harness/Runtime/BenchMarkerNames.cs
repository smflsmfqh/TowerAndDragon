// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

/// <summary>
/// 측정에 쓰는 ProfilerMarker 이름의 단일 출처.
///
/// 하네스(ProfilerRecorder)와 추출기(ProfilerRawSummary)가 같은 상수를 참조한다.
/// <b>Assets/Scripts의 계측 패치는 이 클래스를 참조하지 않고 같은 문자열을 자기 파일에 다시 선언한다</b> -
/// 이 프로젝트는 asmdef이 없어 참조 자체는 가능하지만, 그렇게 이으면 M8에서 Assets/Bench를
/// 먼저 지우는 순간 실제 코드가 컴파일 에러로 무너진다. 계측 패치 쪽 상수를 고칠 일이 생기면
/// 여기와 함께 고친다. 대조 위치는 patches/instrumentation.diff 머리말에 적혀 있다.
///
/// 이름 규칙은 이미 커밋된 ConquestManager.SETTLEMENT_MARKER_NAME("TND.Conquest.Settlement")를 따른다.
/// 기존 마커는 이번 패치가 넣은 것이 아니므로 M8에서 제거하지 않는다.
/// </summary>
public static class BenchMarkerNames
{
    // --- 계측 패치가 실제 코드에 넣는 마커 (M8에서 제거) ---

    /// <summary>ConquestModeController.HandleHover() 전체 - 조기 반환 프레임도 포함한다.</summary>
    public const string T1_HOVER = "TND.Bench.T1.Hover";

    /// <summary>ConquestModeController.RecomputeConquerableClassification() 전체.</summary>
    public const string T1_RECLASSIFY = "TND.Bench.T1.Reclassify";

    /// <summary>ConqueredChunkBorderRenderer.ShowHoveredBorder() 전체 - 같은 집합 가드에 걸린 프레임 포함.</summary>
    public const string T1_HOVERED_BORDER = "TND.Bench.T1.HoveredBorder";

    /// <summary>위 가드를 통과해 실제로 선을 다시 만든 구간만.</summary>
    public const string T1_HOVERED_BORDER_REBUILD = "TND.Bench.T1.HoveredBorderRebuild";

    /// <summary>ConquestModeController.SetConquestModeActive(true) 전체 - E-재진입 상위 구간.</summary>
    public const string T1_MODE_ENTER = "TND.Bench.T1.ModeEnter";

    /// <summary>UI_ConquestWindow.OnConquerButtonClicked() 전체 - E-출발 상위 구간.</summary>
    public const string T1_CONQUER_UI = "TND.Bench.T1.ConquerUI";

    // --- 하네스 안(Assets/Bench)에서만 발화하는 마커 ---

    /// <summary>LegacyConquestHighlightDriver의 한 프레임 처리 전체 - T1_HOVER와 같은 범위.</summary>
    public const string S0_HOVER = "TND.Bench.S0.Hover";

    /// <summary>S0가 실제로 셀 하이라이트를 다시 만든 구간 - T1_HOVERED_BORDER_REBUILD의 대응.</summary>
    public const string S0_REBUILD = "TND.Bench.S0.Rebuild";

    // --- 교차 확인용 엔진 자동 샘플 ---

    /// <summary>엔진이 자동으로 다는 MonoBehaviour 샘플. 선택·닫기 입력을 포함하므로 호버 마커와 같은 값이 아니다.</summary>
    public const string ENGINE_CONTROLLER_UPDATE = "ConquestModeController.Update()";

    /// <summary>이미 커밋돼 있던 정산 마커. M7 선택 항목에서만 읽고, 이번 패치가 넣은 것으로 오인해 지우지 않는다.</summary>
    public const string EXISTING_SETTLEMENT = "TND.Conquest.Settlement";

    /// <summary>하네스와 추출기가 기본으로 함께 수집하는 마커 목록.</summary>
    public static readonly string[] ALL_SECTION_MARKERS =
    {
        T1_HOVER,
        T1_RECLASSIFY,
        T1_HOVERED_BORDER,
        T1_HOVERED_BORDER_REBUILD,
        T1_MODE_ENTER,
        T1_CONQUER_UI,
        S0_HOVER,
        S0_REBUILD,
    };
}
