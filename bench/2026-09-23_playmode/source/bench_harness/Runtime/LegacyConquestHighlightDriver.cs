// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System.Collections.Generic;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// 038b253e의 점령 하이라이트 처리 경로(S0)를 현재 데이터 위에 이식한 드라이버.
///
/// 이식 방침은 bench/2026-09-23_playmode/호환범위.md(M1 대조표)가 기준이다. 요지:
/// <list type="bullet">
/// <item>입력(<c>GetHoveredCell</c>)·청크 조회(<c>GetChunkAt</c>)·판정(<c>CanSendExpedition</c>)은
/// <b>현재 API를 공유</b>한다. 두 변형이 같은 프레임에 같은 셀을 봐야 입력 충실도 관문(§2-2-1)이 성립한다.</item>
/// <item>표시 루프는 <b>과거 원문 그대로</b>다. 매 프레임 <c>new List&lt;Vector3Int&gt;</c>를 만들고
/// 변경 가드 없이 다시 칠한다 - 그 할당이 곧 측정 대상이므로 줄이지 않는다.</item>
/// <item>현재 <c>MouseSelectController.HighlightCells</c>는 부르지 않는다(초기화 확인·맥동 정리·
/// 스프라이트 복원·표면 좌표가 얹혀 있어 과거 루프가 아니다). 자기 풀을 따로 갖는다.</item>
/// <item>좌표는 현재 <c>ConvertGridToWorld</c>(단차 반영)를 쓰고 과거 루프의 yOffset 덧셈을 유지한다.</item>
/// </list>
///
/// Update 실행 순서는 기본값으로 둔다 - T1의 ConquestModeController.Update와 같은 자리에서 돌아야
/// 같은 조건의 비교가 된다. 하네스만 -1000으로 앞세운다.
/// </summary>
public sealed class LegacyConquestHighlightDriver : MonoBehaviour
{
    private static readonly ProfilerMarker HOVER_MARKER = new(BenchMarkerNames.S0_HOVER);
    private static readonly ProfilerMarker REBUILD_MARKER = new(BenchMarkerNames.S0_REBUILD);

    // 과거 인스펙터 기본값을 그대로 옮긴다(038b253e ConquestModeController).
    private static readonly Color CONQUERABLE_HIGHLIGHT_COLOR = Color.green;
    private static readonly Color BLOCKED_HIGHLIGHT_COLOR = Color.red;

    private const string LEGACY_SEED_OBJECT_NAME = "[BENCH] S0 Highlight Seed";
    private const string CURRENT_SELECTION_RENDERER_FIELD = "_selectionHighlightRenderer";

    private GridMap _gridMap;
    private MouseSelectController _mouseSelectController;
    private ConquestManager _conquestManager;

    private ComponentPool<SpriteRenderer> _highlightPool;
    private SpriteRenderer _seedRenderer;
    private float _yOffset;

    /// <summary>드라이버가 이번 프레임에 실제로 셀을 다시 칠했는가 - 하네스가 프레임별로 읽어 간다.</summary>
    public bool DidRebuildThisFrame { get; private set; }

    /// <summary>이번 프레임에 다시 칠한 셀 수. 0이면 빈 영역이라 풀만 껐다는 뜻이다.</summary>
    public int LastRebuiltCellCount { get; private set; }

    /// <summary>세션 누적 재구성 호출 수 - CSV의 "실제 재구성 횟수"에 들어간다.</summary>
    public int RebuildCallCount { get; private set; }

    /// <summary>초기화에서 잡은 seed 렌더러 - 파일럿(M3)이 스프라이트·정렬·스케일을 원본과 대조한다.</summary>
    public SpriteRenderer SeedRenderer => _seedRenderer;

    public bool IsReady { get; private set; }

    /// <summary>
    /// 풀 생성과 참조 획득은 <b>초기화 구간</b>이다 - 측정 프레임 밖에서 한 번만 부른다.
    /// 실패하면 false를 돌려주고 드라이버는 돌지 않는다(빈 하이라이트로 조용히 측정되지 않게 한다).
    /// </summary>
    public bool Initialize(
        GridMap gridMap,
        MouseSelectController mouseSelectController,
        ConquestManager conquestManager,
        Transform benchRoot)
    {
        _gridMap = gridMap;
        _mouseSelectController = mouseSelectController;
        _conquestManager = conquestManager;

        if (_gridMap == null || _mouseSelectController == null || _conquestManager == null || benchRoot == null)
        {
            Debug.LogError("[BENCH] S0 드라이버 초기화 실패 - 씬 참조가 비어 있다.");
            return false;
        }

        _yOffset = _mouseSelectController.YOffset;

        SpriteRenderer currentTemplate = ResolveCurrentSelectionRenderer(_mouseSelectController);
        if (currentTemplate == null)
        {
            Debug.LogError($"[BENCH] S0 드라이버 초기화 실패 - MouseSelectController.{CURRENT_SELECTION_RENDERER_FIELD}를 찾지 못했다.");
            return false;
        }

        // 현재 선택 풀을 공유하지 않는다 - T1이 같은 렌더러를 쓰면 두 구현의 표시가 섞인다(M3 격리 관문).
        // 대신 같은 에셋을 쓰는 독립 복제본을 벤치 루트 아래에 만들고, 과거와 같이
        // 그 복제본을 템플릿이자 seedInstance로 넘겨 0번 슬롯 재사용을 유지한다.
        _seedRenderer = Instantiate(currentTemplate, benchRoot);
        _seedRenderer.gameObject.name = LEGACY_SEED_OBJECT_NAME;
        _seedRenderer.transform.localScale = currentTemplate.transform.lossyScale;
        _seedRenderer.gameObject.SetActive(false);

        _highlightPool = new ComponentPool<SpriteRenderer>(_seedRenderer, benchRoot, seedInstance: _seedRenderer);
        _highlightPool.DeactivateAll();

        IsReady = true;
        return true;
    }

    /// <summary>현재 컨트롤러의 선택 하이라이트 템플릿을 리플렉션으로 한 번 읽는다(과거 이름은 _spriteRenderer).</summary>
    private static SpriteRenderer ResolveCurrentSelectionRenderer(MouseSelectController mouseSelectController)
    {
        FieldInfo field = typeof(MouseSelectController).GetField(
            CURRENT_SELECTION_RENDERER_FIELD,
            BindingFlags.Instance | BindingFlags.NonPublic);

        return field?.GetValue(mouseSelectController) as SpriteRenderer;
    }

    private void Update()
    {
        if (!IsReady)
            return;

        DidRebuildThisFrame = false;
        LastRebuiltCellCount = 0;

        using (HOVER_MARKER.Auto())
        {
            HighlightHoveredChunk();
        }
    }

    // 038b253e ConquestModeController.HighlightHoveredChunk()의 원문 이식.
    // 변경 가드가 없다 - 같은 칸에 머무는 프레임에도 전부 다시 만든다.
    private void HighlightHoveredChunk()
    {
        Vector3Int hoveredCell = _mouseSelectController.GetHoveredCell();
        Chunk chunk = _gridMap.GetChunkAt(hoveredCell);

        if (chunk == null)
        {
            ClearHighlights();
            return;
        }

        Color color = _conquestManager.CanSendExpedition(chunk.ChunkCoord)
            ? CONQUERABLE_HIGHLIGHT_COLOR
            : BLOCKED_HIGHLIGHT_COLOR;

        HighlightCells(GetChunkCellCoords(chunk), color);
    }

    // 038b253e MouseSelectController.HighlightCells()의 원문 이식.
    // 좌표 변환만 현재 ConvertGridToWorld(단차 반영)를 쓰고, 과거의 yOffset 덧셈은 그대로 둔다.
    private void HighlightCells(List<Vector3Int> coords, Color color)
    {
        using (REBUILD_MARKER.Auto())
        {
            for (int i = 0; i < coords.Count; i++)
            {
                SpriteRenderer highlight = _highlightPool.Get(i);
                Vector3 cellPos = _gridMap.ConvertGridToWorld(coords[i]);
                cellPos.y += _yOffset;
                highlight.transform.position = cellPos;
                highlight.color = color;
            }

            _highlightPool.DeactivateFrom(coords.Count);
        }

        DidRebuildThisFrame = true;
        LastRebuiltCellCount = coords.Count;
        RebuildCallCount++;
    }

    // 038b253e ConquestModeController.GetChunkCellCoords()의 원문 이식 -
    // 매 프레임 새 List를 만들고 셀 좌표를 복사한다. 이 할당을 없애지 않는다.
    private static List<Vector3Int> GetChunkCellCoords(Chunk chunk)
    {
        var coords = new List<Vector3Int>(chunk.Cells.Count);
        foreach (GridCell cell in chunk.Cells)
        {
            coords.Add(cell.Coord);
        }

        return coords;
    }

    // 038b253e MouseSelectController.ClearHighlights() - 자기 풀만 전부 비활성화한다.
    private void ClearHighlights()
    {
        using (REBUILD_MARKER.Auto())
        {
            _highlightPool.DeactivateAll();
        }

        DidRebuildThisFrame = true;
        LastRebuiltCellCount = 0;
        RebuildCallCount++;
    }

    /// <summary>측정 종료 후 표시를 거두고 벤치 오브젝트를 정리한다.</summary>
    public void Teardown()
    {
        IsReady = false;

        _highlightPool?.DeactivateAll();

        if (_seedRenderer != null)
            Destroy(_seedRenderer.gameObject);
    }
}
