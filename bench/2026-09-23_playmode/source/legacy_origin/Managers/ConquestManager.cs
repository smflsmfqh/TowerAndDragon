using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

public class ConquestManager : MonoBehaviour
{
    // 열어둘 필요가 있는 것: 
    // 1. 점령에 필요한 자원 - 인구, 돌, 나무, 광물
    // 2. 점령에 소요되는 시간
    // 3. 점령 시 적이 얼마나 강해지는지
    // 4. 점령 보내는 기능 -> 성공 시 해당 청크는 점령 중인 상태 보이게 (여러 점령을 보낼 수 있으니까)
    //                   -> 실패 시, 즉 자원이 부족하거나 조건이 안될 때는 부족한 자원 텍스트를 빨간색으로 하고 점령 보내기 버튼이 회색으로 보여서 안눌러지게 (비활성화된듯이)
    [SerializeField]
    private GridMap _gridMap;

    // To Do: 추후 연결 예정
    // [SerializeField]
    // private CycleManager _cycleManager;

    [SerializeField]
    private ConquestDurationTable _durationTable;

    [SerializeField]
    private ConquestChunkCostTable _chunkCostTable;

    [SerializeField]
    private Building _garrisonPrefab;

    private readonly List<ConquestExpedition> _activeExpeditions = new();
    private EnemyScalingModifier _accumulatedEnemyScaling = EnemyScalingModifier.Neutral;
    public EnemyScalingModifier AccumulatedEnemyScaling => _accumulatedEnemyScaling;

    public UnityEvent<EnemyScalingModifier> OnEnemyScalingChanged;

    private void Awake()
    {
        // To Do: 추후 연결 예정
        //_cycleManager.OnNightEnd.AddListener(OnSettlement);
    }

    private void OnDestroy()
    {
        // To Do: 추후 연결 예정
        //_cycleManager.OnNightEnd.RemoveListener(OnSettlement);
    }

    public bool TryGetExpeditionCost(Vector2Int targetChunkCoord, out ResourceCost cost) =>
        _chunkCostTable.TryResolve(targetChunkCoord, out cost);

    public int GetDaysRequired(Vector2Int chunkCoord)
    {
        Chunk chunk = _gridMap.GetChunk(chunkCoord);
        return _durationTable.ResolveDaysRequired(chunk.DominantTerrain);
    }

    public EnemyScalingModifier PreviewEnemyScaling(Vector2Int chunkCoord) =>
        _chunkCostTable.ResolveEnemyScaling(chunkCoord);

    public TerrainType GetDominantTerrain(Vector2Int chunkCoord) =>
        _gridMap.GetChunk(chunkCoord).DominantTerrain;

    public bool TryGetActiveExpedition(Vector2Int chunkCoord, out ConquestExpedition expedition)
    {
        foreach (ConquestExpedition candidate in _activeExpeditions)
        {
            if (candidate.TargetChunkCoord == chunkCoord)
            {
                expedition = candidate;
                return true;
            }
        }

        expedition = null;
        return false;
    }

    public bool CanSendExpedition(Vector2Int targetChunkCoord)
    {
        Chunk chunk = _gridMap.GetChunk(targetChunkCoord);
        if (chunk == null || chunk.CurrentState != ChunkState.Visible)
            return false;

        // 점령 자체는 상하좌우 4방향으로만 진행 - 시야는 8방향으로 노출/ 대각선 청크는 점령 대상에서 제외
        if (!HasConqueredOrthogonalNeighbor(targetChunkCoord))
            return false;

        foreach (ConquestExpedition expedition in _activeExpeditions)
        {
            if (expedition.TargetChunkCoord == targetChunkCoord)
                return false;
        }

        return true;
    }

    private bool HasConqueredOrthogonalNeighbor(Vector2Int chunkCoord)
    {
        foreach (Chunk neighbor in _gridMap.GetOrthogonalAdjacentChunks(chunkCoord))
        {
            if (neighbor.CurrentState == ChunkState.Conquered)
                return true;
        }

        return false;
    }

    // available: 원정을 보낼 시점의 보유 자원/인구
    // 실제 보유량 조회는 자원/인구 매니저가 생기면 그쪽에서 채워서 넘기고, 
    // 지금은 호출자가 직접 준비해서 넘김
    public bool CanAffordExpedition(Vector2Int targetChunkCoord, ResourceCost available)
    {
        if (!_chunkCostTable.TryResolve(targetChunkCoord, out ResourceCost cost))
            return false;

        return available.CanAfford(cost);
    }

    public bool SendExpedition(Vector2Int targetChunkCoord, ResourceCost available)
    {
        if (!CanSendExpedition(targetChunkCoord))
            return false;

        if (!_chunkCostTable.TryResolve(targetChunkCoord, out ResourceCost cost))
        {
            Debug.LogWarning($"[ConquestManager] 청크 {targetChunkCoord}의 점령 비용이 설정되지 않았습니다.");
            return false;
        }

        if (!available.CanAfford(cost))
        {
            Debug.LogWarning($"[ConquestManager] 청크 {targetChunkCoord} 원정 실패 - 자원이 부족합니다.");
            return false;
        }

        Chunk chunk = _gridMap.GetChunk(targetChunkCoord);
        int daysRequired = _durationTable.ResolveDaysRequired(chunk.DominantTerrain);

        _activeExpeditions.Add(new ConquestExpedition(targetChunkCoord, cost, daysRequired));
        return true;
    }

    // 추후 연결 예정: 낮/밤 주기 정산(OnSettlement)이 CycleManager와 아직 연결되지 않아,
    // 원정 발송 성공 시 소요일수를 기다리지 않고 바로 점령을 완료 처리한다.
    // CycleManager 연결 후에는 UI에서 이 메서드 대신 SendExpedition을 호출하도록 되돌린다.
    public bool SendExpeditionAndComplete(Vector2Int targetChunkCoord, ResourceCost available)
    {
        if (!SendExpedition(targetChunkCoord, available))
            return false;

        ConquestExpedition expedition = _activeExpeditions[_activeExpeditions.Count - 1];
        CompleteConquest(expedition);
        _activeExpeditions.Remove(expedition);
        return true;
    }

    private void OnSettlement(int currentCycle)
    {
        for (int i = _activeExpeditions.Count - 1; i >= 0; i--)
        {
            ConquestExpedition expedition = _activeExpeditions[i];
            expedition.AdvanceDay();

            if (!expedition.IsComplete)
                continue;
            
            CompleteConquest(expedition);
            _activeExpeditions.RemoveAt(i);
        }
    }

    private void CompleteConquest(ConquestExpedition expedition)
    {
        _gridMap.SetChunkState(expedition.TargetChunkCoord, ChunkState.Conquered);
        ExpandVisibility(expedition.TargetChunkCoord);
        PlaceGarrison(expedition.TargetChunkCoord);
        ApplyEnemyScaling(expedition.TargetChunkCoord);
    }

    private void ExpandVisibility(Vector2Int chunkCoord)
    {
        foreach (Chunk neighbor in _gridMap.GetAdjacentChunks(chunkCoord))
        {
            if (neighbor.CurrentState == ChunkState.Hidden)
                _gridMap.SetChunkState(neighbor.ChunkCoord, ChunkState.Visible);
        }
    }

    private void ApplyEnemyScaling(Vector2Int chunkCoord)
    {
        EnemyScalingModifier modifier = _chunkCostTable.ResolveEnemyScaling(chunkCoord);

        _accumulatedEnemyScaling = _accumulatedEnemyScaling.Combine(modifier);
        OnEnemyScalingChanged?.Invoke(_accumulatedEnemyScaling);
    }

    private void PlaceGarrison(Vector2Int chunkCoord)
    {
        if (_garrisonPrefab == null)
        {
            Debug.LogWarning("[ConquestManger] 주둔지 프리팹 없음");
            return;
        }

        Chunk chunk = _gridMap.GetChunk(chunkCoord);
        GridCell anchorCell = FindConstructableCellNearestCenter(chunk);
        if (anchorCell == null)
        {
            Debug.LogWarning($"[ConquestManager] 주둔지 배치 가능한 셀이 없습니다 - {chunkCoord}");
            return;
        }

        _gridMap.ConstructBuilding(_garrisonPrefab, anchorCell.Coord);
    }

    private GridCell FindConstructableCellNearestCenter(Chunk chunk)
    {
        var sum = Vector3Int.zero;
        foreach (GridCell cell in chunk.Cells)
            sum += cell.Coord;
        
        Vector3 center = (Vector3) sum / chunk.Cells.Count;

        GridCell nearestCell = null;
        float nearestSqrDistance = float.MaxValue;

        foreach(GridCell cell in chunk.Cells)
        {
            if (!cell.CanConstruct || cell.ExistTypeOnCell != ExistTypeOnCell.None)
                continue;
            
            float sqrDistance = Vector3.SqrMagnitude((Vector3)cell.Coord - center);
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearestCell = cell;
            }
        }
        return nearestCell;
    }
}
