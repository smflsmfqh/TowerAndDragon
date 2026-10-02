using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 점령 모드 진입/청크 선택을 담당한다. 건설 모드(BuildingPlacementController)와 동일한
// 입력 처리 패턴(포인터-오버-UI 가드)을 따른다.
// _selectAction("Confirm")은 BuildingPlacementController._placeAction과 같은 공유 액션이며
// GlobalInputBootstrap이 한 번만 Enable한다 - 이 컨트롤러는 스스로 Enable/Disable하지 않는다.
public class ConquestModeController : MonoBehaviour, IExclusiveMode, IExclusiveModeEntryGuard
{
    [SerializeField]
    private GridMap _gridMap;

    [SerializeField]
    private MouseSelectController _mouseSelectController;

    [SerializeField]
    private ConquestManager _conquestManager;

    [SerializeField]
    private UI_ConquestWindow _conquestUI;

    [SerializeField]
    private BuildingPlacementController _buildingPlacementController;

    [SerializeField]
    private ChunkInfoOverlayRenderer _chunkInfoRenderer;

    [Tooltip("점령 후보 선과 호버 선을 그린다. 씬에서 Grid 오브젝트를 연결한다.")]
    [SerializeField]
    private ConqueredChunkBorderRenderer _borderRenderer;

    [SerializeField]
    private InputActionReference _selectAction;

    [Tooltip("점령 모드를 닫는 액션 - 패널이 열려 있으면 패널만 닫고, 없으면 점령 모드 자체를 끈다.")]
    [SerializeField]
    private InputActionReference _closeAction;

    [Tooltip("지형 틴트를 합성할 안개 렌더러. 씬에서 Grid 오브젝트를 연결한다.")]
    [SerializeField]
    private FogOfWarRenderer _fogOfWarRenderer;

    [Tooltip("지금 원정을 보낼 수 있는 지역의 지형에 곱할 색조 - 하늘색 계열(R을 낮춤). " +
             "곱연산은 색을 깎기만 하므로, 이 맵 지형처럼 따뜻한 색(R이 높음)에는 R을 깎는 쪽이 " +
             "가장 크게 움직인다. 파랑이 낮은 지형에서는 하늘색이 아니라 '더 차가운 색'으로 읽힌다.")]
    [SerializeField]
    private Color _conquerableTintColor = new Color(0.55f, 0.92f, 1f, 1f);

    [Tooltip("인접 점령지가 없어 아직 원정을 보낼 수 없는 지역의 지형에 곱할 색조 - 붉은 계열(G·B를 낮춤). " +
             "안개 밝기에 곱해지므로 알파는 1로 두고 RGB로만 표현한다. " +
             "너무 낮추면 안개가 짙은 곳에서 검붉게 보이므로 옅게 유지한다.")]
    [SerializeField]
    private Color _unreachableTintColor = new Color(1f, 0.62f, 0.6f, 1f);

    [Tooltip("누른 뒤 이 픽셀 이상 포인터가 움직이면 클릭이 아니라 드래그로 간주해 선택/닫힘을 무시한다.")]
    [SerializeField]
    private float _dragThreshold = 10f;

    /// <summary>
    /// 아직 갈 수 없는 땅(붉은 영역)을 눌렀는데 아래 질의가 패널 열기를 막았다.
    /// 막은 쪽이 왜 안 되는지 알려 주라고 보내는 신호다 - 막지 않는 씬에서는 발행되지 않는다.
    /// </summary>
    [SerializeField]
    private UnityEvent _unreachableChunkBlocked = new();

    public UnityEvent UnreachableChunkBlocked => _unreachableChunkBlocked;

    // 튜토리얼이 배선한다 - 배선되지 않은 씬에서는 null로 남아 패널이 그대로 열린다(기존 동작 유지).
    public IUnreachableChunkSelectQuery UnreachableSelectQuery { get; set; }

    public bool IsActive { get; private set; }

    private Vector2Int? _selectedChunkCoord;
    private bool _isSelectionLocked;
    private Vector2 _pressScreenPosition;

    // 영토가 바뀔 때만 재계산되는 청크 분류 캐시 - 호버마다 다시 계산하지 않는다.
    // "지금 보낼 수 없다"를 두 갈래로 나눈다: 원정 중은 회색 선이 이미 표현하고 있고,
    // 인접 점령지가 없어 아직 못 가는 곳은 면으로 덮어 호버 없이도 구분되게 한다.
    private readonly List<Vector2Int> _conquerableChunkBuffer = new();
    private readonly List<Vector2Int> _inProgressChunkBuffer = new();
    private readonly List<Vector2Int> _unreachableChunkBuffer = new();

    // 점령 대상 청크별로 "이 청크를 점령하면 딸려올 짜투리 청크" - 분류와 같이 갱신되는 캐시.
    private readonly Dictionary<Vector2Int, List<Vector2Int>> _annexableNeighborsByChunk = new();

    // 위 캐시의 역방향 - 짜투리 청크를 호버/클릭했을 때 어느 점령 대상으로 이어줄지.
    private readonly Dictionary<Vector2Int, Vector2Int> _annexOwnerByChunk = new();
    private readonly List<Vector2Int> _annexPreviewChunkBuffer = new();

    // "점령하면 얻게 될 땅" 덩어리 - 호버 선용과 후보 선 그리기용 스크래치.
    private readonly HashSet<Vector2Int> _hoveredChunkCoords = new();
    private readonly HashSet<Vector2Int> _claimGroupBuffer = new();

    // 지형에 입힐 셀별 색조 - 짜투리 청크가 점령 가능·불가 양쪽의 편입 후보일 수 있어 좌표별로 담는다.
    private readonly Dictionary<Vector3Int, Color> _terrainTintBuffer = new();

    private static readonly List<Vector2Int> EMPTY_CHUNK_COORDS = new();

    private void Update()
    {
        if (!IsActive)
            return;

        HandleHover();
        HandleSelectInput();
        HandleCloseInput();
    }

    // ESC 등 닫기 입력 처리 2단계 - 패널(청크 정보)이 열려 있으면 패널만 닫고,
    // 패널이 없는 상태(청크만 하이라이트된 상태)면 점령 모드 자체를 끈다.
    private void HandleCloseInput()
    {
        if (_closeAction == null || !_closeAction.action.WasPerformedThisFrame())
            return;

        if (_conquestUI != null && !_conquestUI.CanCloseFromShortcut())
            return;

        if (_isSelectionLocked)
            _conquestUI.Close();
        else
            SetConquestModeActive(false);
    }

    // 매 프레임 호버된 청크를 확인해, 바뀐 경우에만 노란색 선택 표시를 다시 계산한다(클릭을 기다리지 않는다).
    private void HandleHover()
    {
        if (_isSelectionLocked)
            return;

        Vector3Int hoveredCell = _mouseSelectController.GetHoveredCell();
        Chunk chunk = _gridMap.GetChunkAt(hoveredCell);

        Vector2Int? hoveredChunkCoord = TryResolveConquestTarget(chunk, out Vector2Int targetChunkCoord)
            ? targetChunkCoord
            : (Vector2Int?)null;

        if (hoveredChunkCoord == _selectedChunkCoord)
            return;

        _selectedChunkCoord = hoveredChunkCoord;
        RefreshHoveredBorder();
    }

    // 호버 선이 편입 예정 짜투리 청크까지 감싸므로 플레이어는 그 위도 똑같이 겨냥한다.
    // 하지만 짜투리 청크는 코스트 테이블에 없어 그 자체로는 호버/선택 대상이 아니므로,
    // 그 위에서는 자기를 편입시킬 점령 대상 청크를 대신 가리키게 한다.
    private bool TryResolveConquestTarget(Chunk chunk, out Vector2Int targetChunkCoord)
    {
        targetChunkCoord = default;

        if (chunk == null)
            return false;

        if (chunk.CurrentState == ChunkState.Visible && _conquestManager.HasExpeditionCost(chunk.ChunkCoord))
        {
            targetChunkCoord = chunk.ChunkCoord;
            return true;
        }

        return _annexOwnerByChunk.TryGetValue(chunk.ChunkCoord, out targetChunkCoord);
    }

    public void SetConquestModeActive(bool isActive)
    {
        IsActive = isActive;
        _selectedChunkCoord = null;

        // 내 영토 테두리를 점령 모드용 색으로 갈아, 점령 대상과 갈라 놓는다.
        if (_borderRenderer != null)
            _borderRenderer.SetConquestModeActive(isActive);

        // 점령 모드 동안에는 건물 배치 컨트롤러가 같은 클릭을 처리해 하이라이트를 지우지 못하도록 입력을 억제한다.
        _buildingPlacementController.InputSuppressed = isActive;

        if (isActive)
        {
            SoundManager.Play(SoundId.UiWindowOpen);

            _buildingPlacementController.CancelAll();
            RecomputeConquerableClassification();
        }
        else
        {
            // 패널이 열려 있으면 _conquestUI.Close()가 닫힘음을 낸다 - 여기서 또 내지 않는다.
            if (_isSelectionLocked)
                _conquestUI.Close();
            else
                SoundManager.Play(SoundId.UiWindowClose);

            _isSelectionLocked = false;

            if (_borderRenderer != null)
                _borderRenderer.ClearConquestBorders();

            if (_fogOfWarRenderer != null)
                _fogOfWarRenderer.ClearOverlayTint();

            if (_chunkInfoRenderer != null)
                _chunkInfoRenderer.Clear();
        }
    }

    // 점령을 완료한 직후처럼 청크 상태가 바뀐 뒤 하이라이트를 다시 계산할 때 호출.
    public void RefreshConquerableHighlights()
    {
        if (IsActive)
            RecomputeConquerableClassification();
    }

    // 패널이 열릴 때 호출 — 선택 셀을 고정하고 hover 갱신을 중단한다.
    public void LockChunkSelection(Vector2Int chunkCoord)
    {
        _isSelectionLocked = true;
        _selectedChunkCoord = chunkCoord;
        RefreshHoveredBorder();
    }

    // 패널이 닫힐 때 호출 — 잠금을 해제해 hover가 다시 하이라이트를 갱신하도록 한다.
    public void UnlockChunkSelection()
    {
        _isSelectionLocked = false;
    }

    // 코스트 테이블 조회 + 원정 가능 여부 판정으로 Visible 청크를 세 상태로 분류한다 - 무거운 부분.
    private void RecomputeConquerableClassification()
    {
        _conquerableChunkBuffer.Clear();
        _inProgressChunkBuffer.Clear();
        _unreachableChunkBuffer.Clear();
        _annexableNeighborsByChunk.Clear();
        _annexOwnerByChunk.Clear();

        foreach (Chunk chunk in _gridMap.GetAllChunks())
        {
            if (chunk.CurrentState != ChunkState.Visible)
                continue;

            if (!_conquestManager.HasExpeditionCost(chunk.ChunkCoord))
                continue;

            if (_conquestManager.CanSendExpedition(chunk.ChunkCoord))
                _conquerableChunkBuffer.Add(chunk.ChunkCoord);
            else if (_conquestManager.TryGetActiveExpedition(chunk.ChunkCoord, out _))
                _inProgressChunkBuffer.Add(chunk.ChunkCoord);
            else
                _unreachableChunkBuffer.Add(chunk.ChunkCoord);
        }

        // 순서가 중요하다 - TryAdd가 먼저 들어온 쪽을 짜투리의 주인으로 남기므로,
        // 짜투리가 여러 대상의 편입 후보일 때 호버가 점령 가능한 쪽을 겨냥하게 된다.
        CacheAnnexableNeighbors(_conquerableChunkBuffer);
        CacheAnnexableNeighbors(_unreachableChunkBuffer);
        CacheAnnexableNeighbors(_inProgressChunkBuffer);

        if (_chunkInfoRenderer != null)
            _chunkInfoRenderer.Refresh(_conquerableChunkBuffer);

        RefreshCandidateBorders();
        RefreshTerrainTints();
        RefreshHoveredBorder();
    }

    // 점령 대상 지역의 지형 자체를 상태색으로 물들인다 - 편입될 짜투리까지 포함해 선과 같은 범위를 쓴다.
    // 셀마다 평면 조각을 얹으면 이 맵의 연속적인 고저차(셀마다 높이가 다르다) 때문에 조각 사이가
    // 벌어져 깨져 보인다. 지형 스프라이트는 이미 정확한 높이와 절벽면을 갖고 있으므로 그 색을 바꾸면
    // 틈이 생길 지오메트리가 애초에 없다.
    // 호버로 바뀌지 않으므로 분류와 같은 주기(영토 변화 시)로만 갱신된다.
    private void RefreshTerrainTints()
    {
        if (_fogOfWarRenderer == null)
            return;

        _terrainTintBuffer.Clear();

        // 점령 가능을 먼저 담는다 - 짜투리가 양쪽 후보일 때 TryAdd가 먼저 들어온 쪽을 남기므로,
        // 지금 행동할 수 있는 색이 이긴다(_annexOwnerByChunk의 우선순위와 같은 규칙).
        AddClaimGroupTints(_conquerableChunkBuffer, _conquerableTintColor);
        AddClaimGroupTints(_unreachableChunkBuffer, _unreachableTintColor);

        _fogOfWarRenderer.ApplyOverlayTints(_terrainTintBuffer);
    }

    private void AddClaimGroupTints(List<Vector2Int> chunkCoords, Color tint)
    {
        foreach (Vector2Int chunkCoord in chunkCoords)
        {
            AddChunkLandCellTints(chunkCoord, tint);

            foreach (Vector2Int annexCoord in GetAnnexableNeighbors(chunkCoord))
            {
                AddChunkLandCellTints(annexCoord, tint);
            }
        }
    }

    private void AddChunkLandCellTints(Vector2Int chunkCoord, Color tint)
    {
        Chunk chunk = _gridMap.GetChunk(chunkCoord);
        if (chunk == null)
            return;

        foreach (Vector3Int cellCoord in chunk.LandCellCoords)
        {
            _terrainTintBuffer.TryAdd(cellCoord, tint);
        }
    }

    // 각 점령 대상 청크를 점령했을 때 함께 편입될 짜투리 청크를 미리 구해 둔다 - 판정 결과는
    // 영토 상태(이웃의 Conquered 여부)에만 의존하므로 분류와 같은 주기로만 갱신하면 된다.
    private void CacheAnnexableNeighbors(List<Vector2Int> chunkCoords)
    {
        foreach (Vector2Int coord in chunkCoords)
        {
            _conquestManager.CollectRevealedAnnexableNeighbors(coord, _annexPreviewChunkBuffer);
            if (_annexPreviewChunkBuffer.Count == 0)
                continue;

            _annexableNeighborsByChunk[coord] = new List<Vector2Int>(_annexPreviewChunkBuffer);

            foreach (Vector2Int annexCoord in _annexPreviewChunkBuffer)
            {
                // 짜투리 청크 하나가 여러 점령 대상의 편입 후보일 수 있다 - 점령 가능 목록을 먼저
                // 캐싱하므로, TryAdd가 실제로 선을 그리는 쪽(후보)을 주인으로 남긴다.
                _annexOwnerByChunk.TryAdd(annexCoord, coord);
            }
        }
    }

    // 호버/선택한 후보와 그 청크를 점령하면 딸려올 짜투리 청크를 한 덩어리로 감싸는 선을 갱신한다 -
    // 호버가 바뀔 때마다 도는 가벼운 부분. 후보 선 자체는 영토가 바뀔 때만 다시 그린다.
    private void RefreshHoveredBorder()
    {
        _hoveredChunkCoords.Clear();
        var hoveredState = ConquestTargetState.Conquerable;

        // 선택 상태가 잠긴 채로 점령이 완료되면 그 청크가 분류에서 빠지므로(이미 Conquered),
        // 여전히 표시 대상인지 확인한 뒤에만 그린다.
        if (_selectedChunkCoord.HasValue
            && TryResolveTargetState(_selectedChunkCoord.Value, out hoveredState))
        {
            FillClaimGroup(_selectedChunkCoord.Value, _hoveredChunkCoords);
        }

        if (_borderRenderer != null)
            _borderRenderer.ShowHoveredBorder(_hoveredChunkCoords, hoveredState);
    }

    private bool TryResolveTargetState(Vector2Int chunkCoord, out ConquestTargetState state)
    {
        if (_conquerableChunkBuffer.Contains(chunkCoord))
        {
            state = ConquestTargetState.Conquerable;
            return true;
        }

        if (_unreachableChunkBuffer.Contains(chunkCoord))
        {
            state = ConquestTargetState.Unreachable;
            return true;
        }

        if (_inProgressChunkBuffer.Contains(chunkCoord))
        {
            state = ConquestTargetState.InProgress;
            return true;
        }

        state = default;
        return false;
    }

    // 점령 가능한 후보마다 "고르면 얻게 될 땅" 전체를 감싸는 선을 그린다 - 호버하기 전에도
    // 편입될 짜투리 청크가 함께 들어온다는 것이 보여야 하므로, 호버 선과 같은 덩어리를 쓴다.
    // 영토가 바뀔 때만 호출되므로 호버 경로의 비용에는 들어가지 않는다.
    private void RefreshCandidateBorders()
    {
        if (_borderRenderer == null)
            return;

        _borderRenderer.BeginCandidateBorders();

        foreach (Vector2Int chunkCoord in _conquerableChunkBuffer)
        {
            FillClaimGroup(chunkCoord, _claimGroupBuffer);
            _borderRenderer.DrawCandidateBorder(_claimGroupBuffer);
        }

        _borderRenderer.EndCandidateBorders();
    }

    // 이 청크를 점령하면 실제로 얻게 되는 땅 - 청크 자신 + 함께 편입될 짜투리 청크.
    private void FillClaimGroup(Vector2Int chunkCoord, HashSet<Vector2Int> target)
    {
        target.Clear();
        target.Add(chunkCoord);

        foreach (Vector2Int annexCoord in GetAnnexableNeighbors(chunkCoord))
        {
            target.Add(annexCoord);
        }
    }

    private List<Vector2Int> GetAnnexableNeighbors(Vector2Int chunkCoord) =>
        _annexableNeighborsByChunk.TryGetValue(chunkCoord, out List<Vector2Int> annexableNeighbors)
            ? annexableNeighbors
            : EMPTY_CHUNK_COORDS;

    private void HandleSelectInput()
    {
        if (_selectAction == null)
            return;

        // 누른 순간의 포인터 위치를 기록해 둔다.
        if (_selectAction.action.WasPressedThisFrame())
        {
            _pressScreenPosition = PointerScreenPosition();
        }

        // 판정은 뗄 때 한다. 누른 지점에서 임계값 이상 움직였으면 드래그로 보고 선택/닫힘을 무시한다.
        if (!_selectAction.action.WasReleasedThisFrame())
            return;

        if (Vector2.Distance(_pressScreenPosition, PointerScreenPosition()) > _dragThreshold)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector3Int hoveredCell = _mouseSelectController.GetHoveredCell();
        Chunk chunk = _gridMap.GetChunkAt(hoveredCell);

        // 짜투리 청크를 클릭하면 그 청크가 아니라 자기를 편입시킬 점령 대상 청크의 패널을 연다
        // (짜투리 청크 자체는 코스트 테이블에 없어 패널에 띄울 비용/보상 데이터가 없다).
        if (TryResolveConquestTarget(chunk, out Vector2Int targetChunkCoord))
        {
            // 아직 갈 수 없는 땅에서 패널을 열지 말라는 씬(튜토리얼)에서는 이유만 알리고 만다.
            // 질의가 없으면 기존대로 열어 준다.
            if (UnreachableSelectQuery != null &&
                !UnreachableSelectQuery.CanSelectUnreachableChunk() &&
                _unreachableChunkBuffer.Contains(targetChunkCoord))
            {
                _unreachableChunkBlocked.Invoke();
                return;
            }

            _conquestUI.OnChunkSelected(targetChunkCoord);
            return;
        }

        // 선택 가능한 청크(그리드 표시가 뜬 곳)가 아닌 빈 공간을 클릭하면, 열려 있던 패널을 닫는다.
        if (_isSelectionLocked)
            _conquestUI.Close();
    }

    private static Vector2 PointerScreenPosition() =>
        Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

    /// <summary>
    /// 밤에는 점령 모드를 켤 수 없다. 판정과 경고 표시는 낮밤·경고창을 아는 <see cref="UI_ConquestWindow"/>가
    /// 갖고 있으므로 그대로 넘긴다 - 창이 연결되지 않은 씬에서는 막지 않는다.
    /// </summary>
    public bool CanEnterNow() => _conquestUI == null || _conquestUI.CanEnterConquestMode();

    bool IExclusiveMode.IsOpen => IsActive;
    void IExclusiveMode.Open() => SetConquestModeActive(true);
    void IExclusiveMode.Close() => SetConquestModeActive(false);
}
