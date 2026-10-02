using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

// 인접한 Conquered 청크들을 하나의 영역으로 보고 바깥 경계에만 녹색 테두리를, 원정 중인(점령
// 진행 중) 청크들에는 같은 방식으로 회색 테두리를 그린다 (청크끼리 맞닿은 내부 경계선은 그리지 않음).
// 셀 단위로 이웃 셀의 소속 여부를 검사해 바깥 경계 변(edge)을 모으고, 변끼리 공유하는
// 꼭짓점을 이어붙여 폐곡선(loop)을 구성한 뒤 LineRenderer로 그린다.
[RequireComponent(typeof(GridMap))]
public class ConqueredChunkBorderRenderer : MonoBehaviour
{
    // 네 종류 선이 맞닿는 자리에서 어느 쪽이 위로 오는지가 렌더러 순서에 따라 불안정하게 결정되는 것을
    // 막기 위해 순서를 고정한다. 후보 청크는 정의상 점령지와 맞닿아 있어 초록 선과 같은 자리에 포개지는데,
    // 내 영토가 어디까지인지는 항상 읽혀야 하므로 초록을 가장 위에 둔다.
    //
    // 네 값 모두 같은 정렬 레이어(Default)에 있는 지형 타일맵보다 커야 한다
    // (Ground 타일맵 order 0 · 폴리지 스프라이트 order 0 · Structures 타일맵 order 1).
    // 지형과 order가 같으면 z가 전부 0이라 정렬 키가 동률이 되고, Ground 타일맵이 Individual
    // 모드라 타일마다 위아래가 제각각 결정되어 선이 지형에 파묻힌 것처럼 일부만 보인다.
    private const int IN_PROGRESS_BORDER_SORTING_ORDER = 2;
    private const int CANDIDATE_BORDER_SORTING_ORDER = 3;
    private const int HOVERED_BORDER_SORTING_ORDER = 4;
    private const int CONQUERED_BORDER_SORTING_ORDER = 5;

    private const float PULSE_HALF_RANGE = 0.5f;

    private const int TURN_PRIORITY_LEFT = 0;
    private const int TURN_PRIORITY_STRAIGHT = 1;
    private const int TURN_PRIORITY_RIGHT = 2;
    private const int TURN_PRIORITY_REVERSE = 3;

    // [임시 계측] 밤→낮 전환 프리즈 조사용. 점령 확정마다 OnChunkStateChanged를 통해
    // 맵 전체 재추적이 몇 번·얼마나 도는지 확인한다.
    private const string REFRESH_BORDERS_MARKER_NAME = "TND.ChunkBorder.Refresh";
    private static readonly ProfilerMarker REFRESH_BORDERS_MARKER = new(REFRESH_BORDERS_MARKER_NAME);

    [SerializeField]
    private LineRenderer _borderLineRendererPrefab;

    [SerializeField]
    private MouseSelectController _mouseSelectController;

    [SerializeField]
    private ConquestManager _conquestManager;

    [SerializeField]
    private Color _borderColor = Color.green;

    [Tooltip("점령 모드일 때 내 영토 테두리 색 - 점령 대상(하늘색)과 갈라 놓아 어디까지가 내 땅인지 " +
             "한눈에 보이게 한다. 모드를 벗어나면 평소 색으로 돌아간다.")]
    [SerializeField]
    private Color _conquestModeBorderColor = new Color(1f, 0.922f, 0.016f);

    [SerializeField]
    private Color _inProgressBorderColor = Color.gray;

    [Tooltip("점령 가능한 청크를 감싸는 선 색 - 하늘색. 초록(점령 완료)·회색(원정 중)·붉은색(도달 불가)과 " +
             "겹치지 않고, 따뜻한 지형 위에서 가장 잘 떨어진다. 선은 곱연산이 아니라 절대색이므로 " +
             "지형 색에 상관없이 그대로 나온다.")]
    [SerializeField]
    private Color _candidateBorderColor = new Color(0.35f, 0.8f, 1f);

    [Tooltip("인접 점령지가 없어 아직 못 가는 청크를 호버했을 때의 선 색 - 상시 표시는 면이 담당한다.")]
    [SerializeField]
    private Color _unreachableBorderColor = Color.red;

    [SerializeField]
    private float _lineWidth = 0.05f;

    [Tooltip("호버한 후보 선이 맥동하는 속도(초당 왕복 횟수).")]
    [SerializeField]
    private float _hoveredPulseSpeed = 1.5f;

    [Tooltip("맥동이 가장 굵어진 시점의 선 굵기 배율.")]
    [SerializeField]
    private float _hoveredPulseWidthScale = 2.2f;

    [Tooltip("맥동하지 않는 상태(원정 중·도달 불가)를 호버했을 때의 선 굵기 배율 - 맥동 범위 중간값이라 " +
             "세 상태의 호버 강조가 비슷한 무게로 보인다.")]
    [SerializeField]
    private float _hoveredWidthScale = 1.6f;

    [Tooltip("후보·호버·원정 중 선을 영역 안쪽으로 밀어 점령지 선과 나란히 보이게 하는 거리. " +
             "점령지 선 절반 굵기 + 맥동이 가장 굵어진 시점의 절반 굵기보다 커야 겹치지 않는다. 0이면 인셋 없음.")]
    [SerializeField]
    private float _conquestBorderInset = 0.08f;

    private GridMap _gridMap;
    private ComponentPool<LineRenderer> _borderPool;
    private ComponentPool<LineRenderer> _inProgressBorderPool;
    private ComponentPool<LineRenderer> _candidateBorderPool;
    private ComponentPool<LineRenderer> _hoveredBorderPool;
    private readonly HashSet<Vector2Int> _lastInProgressChunkCoords = new();
    private readonly HashSet<Vector2Int> _lastHoveredChunkCoords = new();
    private readonly HashSet<Vector2Int> _inProgressChunkBuffer = new();
    private ConquestTargetState _lastHoveredState;
    private int _candidateLoopIndex;
    private int _activeConqueredLoopCount;
    private bool _isConquestModeActive;

    // 매 프레임 굵기를 갱신할 호버 선 - 풀 인덱스로 다시 조회하지 않도록 참조를 들고 있는다.
    private readonly List<LineRenderer> _pulsingLines = new();

    // 경계선 추적용 작업 버퍼 - 호출마다 새로 만들지 않도록 재사용한다.
    private readonly List<(Vector2Int From, Vector2Int To)> _borderEdges = new();
    private readonly Dictionary<Vector2Int, List<int>> _outgoingEdges = new();
    private readonly List<bool> _isEdgeUsed = new();
    private readonly List<Vector2Int> _annexBuffer = new();

    private void Awake()
    {
        _gridMap = GetComponent<GridMap>();
        _borderPool = new ComponentPool<LineRenderer>(_borderLineRendererPrefab, transform);
        _inProgressBorderPool = new ComponentPool<LineRenderer>(_borderLineRendererPrefab, transform);
        _candidateBorderPool = new ComponentPool<LineRenderer>(_borderLineRendererPrefab, transform);
        _hoveredBorderPool = new ComponentPool<LineRenderer>(_borderLineRendererPrefab, transform);
        _gridMap.OnChunkStateChanged.AddListener(RefreshBorders);

        // ConquestManager는 Grid.prefab을 쓰는 씬(다른 팀원 테스트 씬 등)에 항상 있는 게 아니므로,
        // 없는 씬에서는 회색(원정 중) 테두리 기능만 조용히 비활성화한다.
        
        _conquestManager.OnExpeditionsChanged.AddListener(RefreshInProgressBorders);
        RefreshInProgressBorders();
        
    }

    // Castle.SetUpInitialTerritory()도 Start()에서 성 주변 청크를 Conquered로 세팅하는데,
    // 서로 다른 스크립트의 Start() 호출 순서는 보장되지 않는다(성보다 먼저 실행되면 최초 점령 청크를
    // 놓친 채로 갱신되어 게임 시작 시 테두리가 보이지 않음). 한 프레임 뒤로 미뤄 모든 Start()가
    // 끝난 다음에 갱신되도록 한다.
    private void Start()
    {
        RefreshBordersNextFrameAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid RefreshBordersNextFrameAsync(CancellationToken cancellationToken)
    {
        await UniTask.Yield(cancellationToken);
        RefreshBorders();
    }

    private void OnDestroy()
    {
        if (_gridMap != null)
            _gridMap.OnChunkStateChanged.RemoveListener(RefreshBorders);

        if (_conquestManager != null)
            _conquestManager.OnExpeditionsChanged.RemoveListener(RefreshInProgressBorders);
    }

    private void RefreshBorders()
    {
        using (REFRESH_BORDERS_MARKER.Auto())
        {
            List<List<Vector2Int>> loops = BuildBorderLoops(CollectConqueredCells());
            Color borderColor = ResolveConqueredBorderColor();

            for (int i = 0; i < loops.Count; i++)
            {
                LineRenderer lineRenderer = _borderPool.Get(i);

                // 점령지 선만 인셋 없이 실제 영토 경계에 얹는다 - 나머지 선들이 이 선을 기준으로 비켜난다.
                SetLoopPositions(lineRenderer, loops[i], borderColor, CONQUERED_BORDER_SORTING_ORDER, null);
            }

            _borderPool.DeactivateFrom(loops.Count);
            _activeConqueredLoopCount = loops.Count;
        }
    }

    // 점령 모드에 들어가고 나올 때 내 영토 테두리 색을 바꾼다.
    // 모양은 그대로이므로 경계를 다시 추적하지 않고 이미 그려진 선의 색만 갈아준다 -
    // 재추적은 맵 전체 청크를 훑으므로 색만 바뀔 때 치를 비용이 아니다.
    public void SetConquestModeActive(bool isActive)
    {
        if (_isConquestModeActive == isActive)
            return;

        _isConquestModeActive = isActive;

        if (_borderPool == null)
            return;

        Color borderColor = ResolveConqueredBorderColor();

        for (int i = 0; i < _activeConqueredLoopCount; i++)
        {
            LineRenderer lineRenderer = _borderPool.Get(i);
            lineRenderer.startColor = borderColor;
            lineRenderer.endColor = borderColor;
        }
    }

    private Color ResolveConqueredBorderColor() =>
        _isConquestModeActive ? _conquestModeBorderColor : _borderColor;

    // 원정 세트가 실제로 바뀌지 않은 날(날짜만 하루 늘어난 경우)에는 경계 재계산을 건너뛴다.
    private void RefreshInProgressBorders()
    {
        CollectInProgressChunkCoords(_inProgressChunkBuffer);
        if (_inProgressChunkBuffer.SetEquals(_lastInProgressChunkCoords))
            return;

        _lastInProgressChunkCoords.Clear();
        _lastInProgressChunkCoords.UnionWith(_inProgressChunkBuffer);

        HashSet<Vector3Int> cells = CollectCellsForChunks(_inProgressChunkBuffer);
        List<List<Vector2Int>> loops = BuildBorderLoops(cells);

        for (int i = 0; i < loops.Count; i++)
        {
            LineRenderer lineRenderer = _inProgressBorderPool.Get(i);
            SetLoopPositions(lineRenderer, loops[i], _inProgressBorderColor, IN_PROGRESS_BORDER_SORTING_ORDER, cells);
        }

        _inProgressBorderPool.DeactivateFrom(loops.Count);
    }

    // 후보 선은 "이 후보를 고르면 얻게 될 땅" 덩어리마다 하나씩 그린다. 덩어리가 청크 하나로 끝나지
    // 않고(편입될 짜투리 청크가 붙는다) 후보마다 따로 그려야 하므로, 호출자가 덩어리를 하나씩 넘기는
    // 3단계 방식을 쓴다 - 덩어리 목록을 통째로 만들어 넘기면 갱신마다 컬렉션이 새로 할당된다.
    // 인접한 후보끼리는 합치지 않는다: 청크 하나가 곧 하나의 선택지이므로, 인접 청크를 한 덩어리로 보는
    // 영토 경계선과는 의도적으로 다른 처리다.
    public void BeginCandidateBorders()
    {
        _candidateLoopIndex = 0;
    }

    public void DrawCandidateBorder(HashSet<Vector2Int> chunkCoords)
    {
        HashSet<Vector3Int> cells = CollectCellsForChunks(chunkCoords);

        foreach (List<Vector2Int> loop in BuildBorderLoops(cells))
        {
            SetLoopPositions(_candidateBorderPool.Get(_candidateLoopIndex), loop,
                _candidateBorderColor, CANDIDATE_BORDER_SORTING_ORDER, cells);
            _candidateLoopIndex++;
        }
    }

    public void EndCandidateBorders()
    {
        _candidateBorderPool.DeactivateFrom(_candidateLoopIndex);
    }

    // 호버/선택한 청크와 그 청크를 점령하면 함께 편입될 짜투리 청크를 한 덩어리로 감싼다.
    // 색은 언제나 상태가 정하고 호버는 굵기만 바꾼다 - 호버해도 색이 뒤집히지 않으므로 지금 무엇을
    // 보고 있는지가 유지되고, 지금 보낼 수 있는 것만 맥동해서 구별된다.
    //
    // state도 함께 비교해야 한다. 원정을 보낸 직후처럼 좌표는 그대로인데 분류만 바뀌는 경우가 있어,
    // 좌표만 비교하면 색이 갱신되지 않고 이전 색으로 남는다.
    public void ShowHoveredBorder(HashSet<Vector2Int> chunkCoords, ConquestTargetState state)
    {
        if (state == _lastHoveredState && chunkCoords.SetEquals(_lastHoveredChunkCoords))
            return;

        // 인자는 호출자가 매번 Clear()해서 재사용하는 버퍼다 - 참조를 들고 있으면 다음 호버에
        // 호출자가 지우면서 캐시가 조용히 오염되므로 내용을 복사한다(집합을 재사용해 할당은 없앤다).
        _lastHoveredChunkCoords.Clear();
        _lastHoveredChunkCoords.UnionWith(chunkCoords);
        _lastHoveredState = state;

        // 맥동은 지금 보낼 수 있는 곳에만 준다 - 못 가는 곳이 눈에 띄게 움직이면 보낼 수 있다는 오해를 준다.
        bool shouldPulse = state == ConquestTargetState.Conquerable;
        float width = shouldPulse ? _lineWidth : _lineWidth * _hoveredWidthScale;

        HashSet<Vector3Int> cells = CollectCellsForChunks(chunkCoords);
        List<List<Vector2Int>> loops = BuildBorderLoops(cells);

        _pulsingLines.Clear();

        for (int i = 0; i < loops.Count; i++)
        {
            LineRenderer lineRenderer = _hoveredBorderPool.Get(i);
            SetLoopPositions(lineRenderer, loops[i],
                ResolveBorderColor(state), HOVERED_BORDER_SORTING_ORDER, cells, width);

            // 맥동 대상만 참조를 담아둔다 - 비어 있으면 Update가 굵기를 건드리지 않으므로
            // 위에서 정한 정적 굵기가 유지된다.
            if (shouldPulse)
                _pulsingLines.Add(lineRenderer);
        }

        _hoveredBorderPool.DeactivateFrom(loops.Count);
    }

    private Color ResolveBorderColor(ConquestTargetState state) => state switch
    {
        ConquestTargetState.Conquerable => _candidateBorderColor,
        ConquestTargetState.InProgress => _inProgressBorderColor,
        _ => _unreachableBorderColor,
    };

    // 점령 모드를 벗어날 때 후보/호버 선을 모두 치운다.
    public void ClearConquestBorders()
    {
        _lastHoveredChunkCoords.Clear();
        _lastHoveredState = ConquestTargetState.Conquerable;
        _pulsingLines.Clear();
        _candidateBorderPool?.DeactivateAll();
        _hoveredBorderPool?.DeactivateAll();
    }

    // 호버 선만 굵기를 맥동시킨다. 풀의 Get()은 매번 SetActive(true)를 부르므로 매 프레임 경로에서
    // 쓰지 않고, 선을 만들 때 담아둔 참조만 훑는다.
    private void Update()
    {
        if (_pulsingLines.Count == 0)
            return;

        float pulse = (Mathf.Sin(Time.unscaledTime * _hoveredPulseSpeed * Mathf.PI * 2f) + 1f) * PULSE_HALF_RANGE;
        float width = Mathf.Lerp(_lineWidth, _lineWidth * _hoveredPulseWidthScale, pulse);

        foreach (LineRenderer lineRenderer in _pulsingLines)
        {
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
        }
    }

    // insetCells가 null이면 경계선을 셀 경계에 정확히 얹고, 넘기면 그 영역 안쪽으로 밀어 그린다
    // (점령지 초록 선과 같은 자리에 포개지는 선들이 나란히 보이도록).
    private void SetLoopPositions(LineRenderer lineRenderer, List<Vector2Int> loop, Color color, int sortingOrder,
        HashSet<Vector3Int> insetCells) =>
        SetLoopPositions(lineRenderer, loop, color, sortingOrder, insetCells, _lineWidth);

    private void SetLoopPositions(LineRenderer lineRenderer, List<Vector2Int> loop, Color color, int sortingOrder,
        HashSet<Vector3Int> insetCells, float width)
    {
        lineRenderer.gameObject.SetActive(true);
        lineRenderer.loop = true;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.sortingOrder = sortingOrder;
        lineRenderer.positionCount = loop.Count;

        for (int i = 0; i < loop.Count; i++)
        {
            lineRenderer.SetPosition(i, insetCells != null
                ? GetInsetCornerPosition(loop[i], insetCells)
                : GetCornerWorldPosition(loop[i]));
        }
    }

    // 격자 꼭짓점을 영역 안쪽으로 살짝 밀어, 같은 자리에 포개지는 점령지 경계선과 나란히 보이게 한다.
    // 꼭짓점에 닿는 네 셀 중 영역에 속한 셀들의 중심 평균 쪽이 곧 "안쪽"이다 - 폐곡선의 진행 방향(와인딩)에
    // 의존하지 않으므로, ㄱ자처럼 오목한 영역이나 트레이서가 루프를 어느 방향으로 돌든 바깥으로 튀지 않는다.
    private Vector3 GetInsetCornerPosition(Vector2Int corner, HashSet<Vector3Int> cells)
    {
        // 지면 좌표를 한 번만 구해 두 용도로 나눠 쓴다 - 꼭짓점 하나당 타일맵 조회가 두 번이면 충분하다.
        Vector3 groundPos = GetCornerGroundPosition(corner);
        Vector3 cornerPos = groundPos;
        cornerPos.y += MouseSelectController.GetYOffsetOrZero(_mouseSelectController);

        if (_conquestBorderInset <= 0f)
            return cornerPos;

        Vector3 memberCenterSum = Vector3.zero;
        int memberCount = 0;

        // 꼭짓점 (x,y)에 닿는 네 셀은 (x-1,y-1) / (x-1,y) / (x,y-1) / (x,y).
        for (int dx = -1; dx <= 0; dx++)
        {
            for (int dy = -1; dy <= 0; dy++)
            {
                var cellCoord = new Vector3Int(corner.x + dx, corner.y + dy, 0);
                if (!cells.Contains(cellCoord))
                    continue;

                memberCenterSum += _gridMap.ConvertGridToWorld(cellCoord);
                memberCount++;
            }
        }

        if (memberCount == 0)
            return cornerPos;

        // 방향은 y 오프셋을 더하지 않은 지면 좌표끼리 비교해야 한다 - 오프셋은 모든 꼭짓점에 같은 값으로
        // 더해지는 평행이동이라, 한쪽에만 섞이면 방향이 아래로 크게 기울어진다.
        Vector3 inwardDirection = memberCenterSum / memberCount - groundPos;

        // 대각선으로만 맞물린 꼭짓점에서는 안쪽 방향이 서로 상쇄되어 0에 가까워진다 - 그때는 밀지 않는다.
        return inwardDirection.sqrMagnitude > 0f
            ? cornerPos + inwardDirection.normalized * _conquestBorderInset
            : cornerPos;
    }

    private List<List<Vector2Int>> BuildBorderLoops(HashSet<Vector3Int> cells)
    {
        CollectBorderEdges(cells);
        return TraceLoops();
    }

    // 청크 경계와 무관하게, 점령된 셀 전체를 하나의 영역으로 보고 바깥 경계 변만 수집한다.
    private HashSet<Vector3Int> CollectConqueredCells()
    {
        var conqueredCells = new HashSet<Vector3Int>();

        foreach (Chunk chunk in _gridMap.GetAllChunks())
        {
            if (chunk.CurrentState != ChunkState.Conquered)
                continue;

            AddLandCells(chunk, conqueredCells);
        }

        return conqueredCells;
    }

    // 원정을 보낸(아직 완료되지 않은) 청크와, 그 원정이 끝나면 함께 편입될 짜투리 청크를 모은다 -
    // 점령 후보의 노란 선과 같은 덩어리를 그려야 "보내기 전에 보던 그 범위가 그대로 진행 중"으로 읽힌다.
    // 캐시와 비교해 재계산 여부를 판단하는 용도이기도 하다.
    private void CollectInProgressChunkCoords(HashSet<Vector2Int> chunkCoords)
    {
        chunkCoords.Clear();

        foreach (ConquestExpedition expedition in _conquestManager.ActiveExpeditions)
        {
            chunkCoords.Add(expedition.TargetChunkCoord);

            _conquestManager.CollectRevealedAnnexableNeighbors(expedition.TargetChunkCoord, _annexBuffer);
            foreach (Vector2Int annexCoord in _annexBuffer)
            {
                chunkCoords.Add(annexCoord);
            }
        }
    }

    // 주어진 청크들의 육지 셀을 수집한다.
    private HashSet<Vector3Int> CollectCellsForChunks(HashSet<Vector2Int> chunkCoords)
    {
        var cells = new HashSet<Vector3Int>();

        foreach (Vector2Int chunkCoord in chunkCoords)
        {
            Chunk chunk = _gridMap.GetChunk(chunkCoord);
            if (chunk == null)
                continue;

            AddLandCells(chunk, cells);
        }

        return cells;
    }

    // 경계선의 셀 기준은 네 종류(점령 완료·원정 중·미리보기) 모두 동일하게 Chunk가 캐싱해 둔
    // LandCellCoords(물이 아닌 셀)다. 청크 전체 셀을 쓰면 해안에 걸친 청크도 사각형으로 그려지고,
    // 물만 빼면 통행로(Road)가 구멍이 되어 몬스터 스폰 길 둘레에 선이 생긴다 - Road는 물이 아니므로
    // LandCellCoords에 자동으로 포함된다.
    private static void AddLandCells(Chunk chunk, HashSet<Vector3Int> target)
    {
        foreach (Vector3Int coord in chunk.LandCellCoords)
        {
            target.Add(coord);
        }
    }

    // 이웃 셀이 영역 밖인 변만 경계로 수집하되, 방향을 "내부가 항상 왼쪽"으로 통일한다(셀 하나를
    // 반시계로 돈다). 이러면 모든 꼭짓점에서 들어오는 변 수와 나가는 변 수가 같아져, 추적이
    // "나가는 변만 따라간다"로 단순해지고 어느 변을 고를지가 셀 집합의 순회 순서에 좌우되지 않는다.
    // 꼭짓점은 셀 좌표계의 정수 격자 인덱스다 - 셀 (x,y)의 네 꼭짓점은
    // (x,y) / (x+1,y) / (x,y+1) / (x+1,y+1).
    private void CollectBorderEdges(HashSet<Vector3Int> cells)
    {
        _borderEdges.Clear();

        foreach (List<int> edgeIndices in _outgoingEdges.Values)
        {
            edgeIndices.Clear();
        }

        foreach (Vector3Int coord in cells)
        {
            var bottomLeft = new Vector2Int(coord.x, coord.y);
            var bottomRight = new Vector2Int(coord.x + 1, coord.y);
            var topLeft = new Vector2Int(coord.x, coord.y + 1);
            var topRight = new Vector2Int(coord.x + 1, coord.y + 1);

            if (!cells.Contains(coord + Vector3Int.down))
                AddDirectedEdge(bottomLeft, bottomRight);

            if (!cells.Contains(coord + Vector3Int.right))
                AddDirectedEdge(bottomRight, topRight);

            if (!cells.Contains(coord + Vector3Int.up))
                AddDirectedEdge(topRight, topLeft);

            if (!cells.Contains(coord + Vector3Int.left))
                AddDirectedEdge(topLeft, bottomLeft);
        }
    }

    private void AddDirectedEdge(Vector2Int from, Vector2Int to)
    {
        if (!_outgoingEdges.TryGetValue(from, out List<int> edgeIndices))
        {
            edgeIndices = new List<int>();
            _outgoingEdges[from] = edgeIndices;
        }

        edgeIndices.Add(_borderEdges.Count);
        _borderEdges.Add((from, to));
    }

    // 방향이 통일된 경계 변을 이어붙여 폐곡선(들)을 구성한다.
    private List<List<Vector2Int>> TraceLoops()
    {
        _isEdgeUsed.Clear();
        for (int i = 0; i < _borderEdges.Count; i++)
        {
            _isEdgeUsed.Add(false);
        }

        var loops = new List<List<Vector2Int>>();

        for (int startEdgeIndex = 0; startEdgeIndex < _borderEdges.Count; startEdgeIndex++)
        {
            if (_isEdgeUsed[startEdgeIndex])
                continue;

            var loop = new List<Vector2Int>();
            Vector2Int startCorner = _borderEdges[startEdgeIndex].From;
            int currentEdgeIndex = startEdgeIndex;

            while (true)
            {
                _isEdgeUsed[currentEdgeIndex] = true;
                (Vector2Int from, Vector2Int to) = _borderEdges[currentEdgeIndex];
                loop.Add(from);

                if (to == startCorner)
                    break;

                int nextEdgeIndex = FindNextEdge(to, to - from);
                if (nextEdgeIndex < 0)
                {
                    // 방향을 통일하면 꼭짓점마다 들어온 만큼 나갈 수 있으므로 여기에 도달할 수 없다.
                    Debug.LogWarning($"[ConqueredChunkBorderRenderer] 경계선 루프가 닫히지 않았습니다 - 꼭짓점 {to}");
                    break;
                }

                currentEdgeIndex = nextEdgeIndex;
            }

            loops.Add(loop);
        }

        return loops;
    }

    // 두 영역이 대각선으로만 스치는 꼭짓점에는 변이 네 개 모인다. 어느 가지로 이어갈지를 들어온 방향
    // 기준 회전 순서로 정해, 가장 왼쪽으로 도는 변을 고른다 - 내부를 왼쪽에 두고 도는 규칙과 맞물려
    // 각 영역의 테두리가 서로를 침범하지 않고 따로 닫힌다. 목록 순서를 보지 않으므로 결과가 항상 같다.
    private int FindNextEdge(Vector2Int corner, Vector2Int incomingDirection)
    {
        if (!_outgoingEdges.TryGetValue(corner, out List<int> candidates))
            return -1;

        int bestEdgeIndex = -1;
        int bestTurnPriority = int.MaxValue;

        foreach (int edgeIndex in candidates)
        {
            if (_isEdgeUsed[edgeIndex])
                continue;

            (Vector2Int from, Vector2Int to) = _borderEdges[edgeIndex];
            int turnPriority = GetTurnPriority(incomingDirection, to - from);

            if (turnPriority >= bestTurnPriority)
                continue;

            bestTurnPriority = turnPriority;
            bestEdgeIndex = edgeIndex;
        }

        return bestEdgeIndex;
    }

    // 왼쪽으로 크게 도는 쪽일수록 먼저 고른다 - 되돌아가는(U턴) 변이 마지막이다.
    private static int GetTurnPriority(Vector2Int incoming, Vector2Int outgoing)
    {
        if (outgoing == new Vector2Int(-incoming.y, incoming.x))
            return TURN_PRIORITY_LEFT;

        if (outgoing == incoming)
            return TURN_PRIORITY_STRAIGHT;

        if (outgoing == new Vector2Int(incoming.y, -incoming.x))
            return TURN_PRIORITY_RIGHT;

        return TURN_PRIORITY_REVERSE;
    }

    private Vector3 GetCornerWorldPosition(Vector2Int corner)
    {
        Vector3 worldPos = GetCornerGroundPosition(corner);
        worldPos.y += MouseSelectController.GetYOffsetOrZero(_mouseSelectController);
        return worldPos;
    }

    // 표시용 y 오프셋을 더하지 않은 지면 좌표 - 인셋 방향 계산이 셀 중심과 같은 기준을 쓰기 위해 분리했다.
    private Vector3 GetCornerGroundPosition(Vector2Int corner) => _gridMap.GetCellCornerWorld(corner);
}
