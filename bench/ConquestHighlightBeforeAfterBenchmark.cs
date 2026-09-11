using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;

// [임시 계측 · 포트폴리오 실측용]
// 점령 하이라이트의 "개선 전"(038b253e, 2026-07-15)과 "출시본"(master HEAD) 코드 경로를
// 같은 맵 데이터(CLT_ChunkLayoutTable) 위에서 나란히 돌려 1회 비용과 누적 비용을 비교한다.
//
//   개선 전 : Update() → HighlightHoveredChunk()  — 가드 없음. 매 프레임 호버 청크의 셀 전체를
//             새 List에 담아 셀마다 풀 슬롯을 켜고 위치·색을 다시 쓴다.
//   출시본  : Update() → HandleHover()            — 호버 청크가 바뀐 프레임에만 통과.
//             통과하면 RefreshHoveredBorder() → 덩어리 셀 수집 → 경계 변 수집 → 폐곡선 추적 →
//             LineRenderer 위치 기록. 지형 틴트가 붙은 전체 재계산은 영토가 바뀔 때만.
//
// 경계 추적 알고리즘(CollectBorderEdges / TraceLoops / FindNextEdge)은
// ConqueredChunkBorderRenderer에서 그대로 옮겨 왔다.
//
// 게임 기능이 아니므로 측정이 끝나면 삭제한다.
public class ConquestHighlightBeforeAfterBenchmark
{
    private const string CHUNK_LAYOUT_PATH = "Assets/Data/ConquestData/CLT_ChunkLayoutTable.asset";

    private const int WARMUP = 200;
    private const int ITERATIONS = 2000;

    private const float FRAME_BUDGET_60FPS_MS = 1000f / 60f;
    private const int SESSION_FRAMES = 3600; // 60초 × 60fps

    // TraceLoops의 회전 우선순위 (원본과 동일)
    private const int TURN_PRIORITY_LEFT = 0;
    private const int TURN_PRIORITY_STRAIGHT = 1;
    private const int TURN_PRIORITY_RIGHT = 2;
    private const int TURN_PRIORITY_REVERSE = 3;

    private GameObject _root;
    private Tilemap _tilemap;
    private ComponentPool<SpriteRenderer> _highlightPool;
    private LineRenderer[] _borderLines;

    // 실제 레이아웃에서 읽어온 청크 → 셀
    private readonly List<Vector2Int> _chunkCoords = new();
    private readonly Dictionary<Vector2Int, List<Vector3Int>> _cellsByChunk = new();
    private readonly Dictionary<Vector3Int, Vector2Int> _cellToChunk = new();

    // 경계 추적용 버퍼 (원본과 동일하게 재사용 — 할당이 생기지 않는다)
    private readonly List<(Vector2Int From, Vector2Int To)> _borderEdges = new();
    private readonly Dictionary<Vector2Int, List<int>> _outgoingEdges = new();
    private readonly List<bool> _isEdgeUsed = new();
    private readonly HashSet<Vector3Int> _claimCells = new();
    private readonly HashSet<Vector2Int> _lastHoveredChunkCoords = new();
    private readonly List<List<Vector2Int>> _loops = new();
    private readonly Dictionary<Vector3Int, Color> _tintBuffer = new();

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("BenchRoot");

        var gridGo = new GameObject("Grid");
        gridGo.transform.SetParent(_root.transform);
        gridGo.AddComponent<Grid>();

        var tilemapGo = new GameObject("Tilemap");
        tilemapGo.transform.SetParent(gridGo.transform);
        _tilemap = tilemapGo.AddComponent<Tilemap>();
        tilemapGo.AddComponent<TilemapRenderer>();

        var highlightPrefab = new GameObject("HighlightPrefab").AddComponent<SpriteRenderer>();
        highlightPrefab.transform.SetParent(_root.transform);
        _highlightPool = new ComponentPool<SpriteRenderer>(highlightPrefab, _root.transform);

        // 경계선 렌더러 풀 — 한 덩어리에서 나올 수 있는 폐곡선 수보다 넉넉하게
        _borderLines = new LineRenderer[16];
        for (int i = 0; i < _borderLines.Length; i++)
        {
            var go = new GameObject($"Border{i}");
            go.transform.SetParent(_root.transform);
            _borderLines[i] = go.AddComponent<LineRenderer>();
        }

        LoadLayout();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        _chunkCoords.Clear();
        _cellsByChunk.Clear();
        _cellToChunk.Clear();
    }

    private void LoadLayout()
    {
        var table = AssetDatabase.LoadAssetAtPath<ChunkLayoutTable>(CHUNK_LAYOUT_PATH);
        Assert.IsNotNull(table, $"레이아웃 에셋을 찾지 못했다: {CHUNK_LAYOUT_PATH}");

        table.BuildCellToChunkIndex(_cellToChunk);

        foreach (var pair in _cellToChunk)
        {
            if (!_cellsByChunk.TryGetValue(pair.Value, out List<Vector3Int> cells))
            {
                cells = new List<Vector3Int>();
                _cellsByChunk[pair.Value] = cells;
                _chunkCoords.Add(pair.Value);
            }

            cells.Add(pair.Key);
        }

        _chunkCoords.Sort((a, b) => _cellsByChunk[a].Count.CompareTo(_cellsByChunk[b].Count));
    }

    private Vector2Int MedianChunk => _chunkCoords[_chunkCoords.Count / 2];

    // ────────────────────────────────────────────────────────────────
    // 개선 전 (038b253e) — HighlightHoveredChunk() 1회분
    // ────────────────────────────────────────────────────────────────
    private void Legacy_HighlightHoveredChunk(Vector2Int chunkCoord)
    {
        List<Vector3Int> chunkCells = _cellsByChunk[chunkCoord];

        // GetChunkCellCoords() — 매 프레임 새 List를 만든다
        var coords = new List<Vector3Int>(chunkCells.Count);
        for (int i = 0; i < chunkCells.Count; i++)
            coords.Add(chunkCells[i]);

        // MouseSelectController.HighlightCells()
        Color color = Color.green;
        for (int i = 0; i < coords.Count; i++)
        {
            SpriteRenderer highlight = _highlightPool.Get(i); // gameObject.SetActive(true) 포함
            Vector3 cellPos = _tilemap.GetCellCenterWorld(coords[i]);
            cellPos.y += 0.75f;
            highlight.transform.position = cellPos;
            highlight.color = color;
        }

        _highlightPool.DeactivateFrom(coords.Count);
    }

    // ────────────────────────────────────────────────────────────────
    // 출시본 — HandleHover() 1회분
    //   커서가 같은 청크 위면 가드에서 조기 반환하고, 바뀐 프레임에만 경계를 다시 그린다.
    // ────────────────────────────────────────────────────────────────
    private Vector2Int? _selectedChunkCoord;

    private bool Release_HandleHover(Vector3Int hoveredCell)
    {
        // GetHoveredCell() → GetChunkAt()
        Vector2Int? hoveredChunkCoord =
            _cellToChunk.TryGetValue(hoveredCell, out Vector2Int coord) ? coord : (Vector2Int?)null;

        if (hoveredChunkCoord == _selectedChunkCoord)
            return false; // ← 가드. 커서가 멈춰 있거나 같은 청크 안에서 움직이는 프레임은 여기서 끝난다.

        _selectedChunkCoord = hoveredChunkCoord;
        Release_RefreshHoveredBorder(hoveredChunkCoord);
        return true;
    }

    private void Release_RefreshHoveredBorder(Vector2Int? chunkCoord)
    {
        // ShowHoveredBorder()의 SetEquals 가드 — 같은 덩어리면 재구성하지 않는다
        if (chunkCoord.HasValue
            && _lastHoveredChunkCoords.Count == 1
            && _lastHoveredChunkCoords.Contains(chunkCoord.Value))
            return;

        _lastHoveredChunkCoords.Clear();
        if (!chunkCoord.HasValue)
            return;

        _lastHoveredChunkCoords.Add(chunkCoord.Value);

        // CollectCellsForChunks() → BuildBorderLoops() → SetLoopPositions()
        _claimCells.Clear();
        foreach (Vector3Int cell in _cellsByChunk[chunkCoord.Value])
            _claimCells.Add(cell);

        CollectBorderEdges(_claimCells);
        TraceLoops();

        for (int i = 0; i < _loops.Count && i < _borderLines.Length; i++)
        {
            LineRenderer line = _borderLines[i];
            List<Vector2Int> loop = _loops[i];
            line.positionCount = loop.Count;
            for (int p = 0; p < loop.Count; p++)
                line.SetPosition(p, new Vector3(loop[p].x, loop[p].y, 0f));
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 출시본 — RecomputeConquerableClassification() 1회분 (영토가 바뀔 때만)
    // ────────────────────────────────────────────────────────────────
    private void Release_RecomputeClassification(int visibleChunkCount)
    {
        _tintBuffer.Clear();

        int n = Mathf.Min(visibleChunkCount, _chunkCoords.Count);
        for (int c = 0; c < n; c++)
        {
            foreach (Vector3Int cell in _cellsByChunk[_chunkCoords[c]])
                _tintBuffer.TryAdd(cell, Color.yellow);
        }

        // FogOfWarRenderer.ApplyOverlayTints() → RepaintCells()
        foreach (var pair in _tintBuffer)
            _tilemap.SetColor(pair.Key, pair.Value);
    }

    // ────────────────────────────────────────────────────────────────
    // 경계 추적 — ConqueredChunkBorderRenderer에서 그대로 옮겨 옴
    // ────────────────────────────────────────────────────────────────
    private void CollectBorderEdges(HashSet<Vector3Int> cells)
    {
        _borderEdges.Clear();
        foreach (List<int> edgeIndices in _outgoingEdges.Values)
            edgeIndices.Clear();

        foreach (Vector3Int coord in cells)
        {
            var bottomLeft = new Vector2Int(coord.x, coord.y);
            var bottomRight = new Vector2Int(coord.x + 1, coord.y);
            var topLeft = new Vector2Int(coord.x, coord.y + 1);
            var topRight = new Vector2Int(coord.x + 1, coord.y + 1);

            if (!cells.Contains(coord + Vector3Int.down)) AddDirectedEdge(bottomLeft, bottomRight);
            if (!cells.Contains(coord + Vector3Int.right)) AddDirectedEdge(bottomRight, topRight);
            if (!cells.Contains(coord + Vector3Int.up)) AddDirectedEdge(topRight, topLeft);
            if (!cells.Contains(coord + Vector3Int.left)) AddDirectedEdge(topLeft, bottomLeft);
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

    private void TraceLoops()
    {
        _isEdgeUsed.Clear();
        for (int i = 0; i < _borderEdges.Count; i++)
            _isEdgeUsed.Add(false);

        _loops.Clear();

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
                    break;

                currentEdgeIndex = nextEdgeIndex;
            }

            _loops.Add(loop);
        }
    }

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

    private static int GetTurnPriority(Vector2Int incoming, Vector2Int outgoing)
    {
        if (outgoing == new Vector2Int(-incoming.y, incoming.x)) return TURN_PRIORITY_LEFT;
        if (outgoing == incoming) return TURN_PRIORITY_STRAIGHT;
        if (outgoing == new Vector2Int(incoming.y, -incoming.x)) return TURN_PRIORITY_RIGHT;
        return TURN_PRIORITY_REVERSE;
    }

    // ────────────────────────────────────────────────────────────────
    // 측정 1 — 커서가 멈춰 있는 프레임 1개의 비용
    // ────────────────────────────────────────────────────────────────
    [Test]
    public void A_PerFrameCost_CursorIdle()
    {
        Vector2Int chunk = MedianChunk;
        Vector3Int cell = _cellsByChunk[chunk][0];
        int cellCount = _cellsByChunk[chunk].Count;

        Debug.Log($"[AB] 맵: 청크 {_chunkCoords.Count}개 · 셀 {_cellToChunk.Count}개 · " +
                  $"대표 청크(중앙값) 셀 {cellCount}개");

        // 개선 전 — 가드가 없으므로 멈춰 있어도 전부 다시 그린다
        for (int i = 0; i < WARMUP; i++) Legacy_HighlightHoveredChunk(chunk);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ITERATIONS; i++) Legacy_HighlightHoveredChunk(chunk);
        sw.Stop();
        double legacyMs = sw.Elapsed.TotalMilliseconds / ITERATIONS;

        // 출시본 — 가드에서 조기 반환
        _selectedChunkCoord = null;
        Release_HandleHover(cell); // 첫 진입은 통과시켜 두고
        for (int i = 0; i < WARMUP; i++) Release_HandleHover(cell);
        sw.Restart();
        for (int i = 0; i < ITERATIONS; i++) Release_HandleHover(cell);
        sw.Stop();
        double releaseMs = sw.Elapsed.TotalMilliseconds / ITERATIONS;

        Debug.Log($"[AB] ① 커서 정지 프레임 1개 — 개선 전 {legacyMs * 1000:F2} µs · " +
                  $"출시본 {releaseMs * 1000:F2} µs · {legacyMs / Mathf.Max((float)releaseMs, 1e-9f):F0}배");
        Debug.Log($"[AB]    60fps 1초 환산 — 개선 전 {legacyMs * 60:F2} ms/초 " +
                  $"(프레임 예산의 {legacyMs / FRAME_BUDGET_60FPS_MS * 100:F2}%) · " +
                  $"출시본 {releaseMs * 60:F3} ms/초 ({releaseMs / FRAME_BUDGET_60FPS_MS * 100:F3}%)");
    }

    // ────────────────────────────────────────────────────────────────
    // 측정 2 — 호버 청크가 실제로 바뀐 프레임 1개의 비용
    // ────────────────────────────────────────────────────────────────
    [Test]
    public void B_PerFrameCost_ChunkChanged()
    {
        Vector2Int chunkA = _chunkCoords[_chunkCoords.Count / 2];
        Vector2Int chunkB = _chunkCoords[_chunkCoords.Count / 2 + 1];
        Vector3Int cellA = _cellsByChunk[chunkA][0];
        Vector3Int cellB = _cellsByChunk[chunkB][0];

        for (int i = 0; i < WARMUP; i++) Legacy_HighlightHoveredChunk(i % 2 == 0 ? chunkA : chunkB);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ITERATIONS; i++) Legacy_HighlightHoveredChunk(i % 2 == 0 ? chunkA : chunkB);
        sw.Stop();
        double legacyMs = sw.Elapsed.TotalMilliseconds / ITERATIONS;

        _selectedChunkCoord = null;
        _lastHoveredChunkCoords.Clear();
        for (int i = 0; i < WARMUP; i++) Release_HandleHover(i % 2 == 0 ? cellA : cellB);
        sw.Restart();
        for (int i = 0; i < ITERATIONS; i++) Release_HandleHover(i % 2 == 0 ? cellA : cellB);
        sw.Stop();
        double releaseMs = sw.Elapsed.TotalMilliseconds / ITERATIONS;

        Debug.Log($"[AB] ② 청크가 바뀐 프레임 1개 — 개선 전 {legacyMs * 1000:F2} µs · " +
                  $"출시본 {releaseMs * 1000:F2} µs");
    }

    // ────────────────────────────────────────────────────────────────
    // 측정 3 — 프레임당 GC 가비지
    // ────────────────────────────────────────────────────────────────
    [Test]
    public void C_GarbagePerFrame()
    {
        Vector2Int chunk = MedianChunk;
        List<Vector3Int> chunkCells = _cellsByChunk[chunk];
        const int KEEP = 4000;

        // 개선 전 — GetChunkCellCoords()가 매 프레임 만들던 List 1개
        var keep = new List<Vector3Int>[KEEP];
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        long before = System.GC.GetTotalMemory(true);
        for (int i = 0; i < KEEP; i++)
        {
            var coords = new List<Vector3Int>(chunkCells.Count);
            for (int c = 0; c < chunkCells.Count; c++)
                coords.Add(chunkCells[c]);
            keep[i] = coords;
        }
        long after = System.GC.GetTotalMemory(true);
        double legacyBytes = (after - before) / (double)KEEP;
        System.GC.KeepAlive(keep);

        Debug.Log($"[AB] ③ 프레임당 가비지 — 개선 전 {legacyBytes:F0} B/프레임 " +
                  $"(60fps 기준 {legacyBytes * 60 / 1024:F1} KB/초) · " +
                  $"출시본 0 B/프레임 (가드에서 반환, 버퍼는 전부 재사용)");
    }

    // ────────────────────────────────────────────────────────────────
    // 측정 4 — 점령 모드 60초 체류 누적 비용 (커서 속도별)
    // ────────────────────────────────────────────────────────────────
    [Test]
    public void D_SixtySecondSession()
    {
        // 실제 레이아웃 위를 지나가는 커서 궤적을 만든다 (가로 스윕, 끝에 닿으면 처음으로)
        var path = new List<Vector3Int>();
        int minX = int.MaxValue, maxX = int.MinValue;
        foreach (Vector3Int c in _cellToChunk.Keys)
        {
            minX = Mathf.Min(minX, c.x);
            maxX = Mathf.Max(maxX, c.x);
        }

        for (int x = minX; x <= maxX; x++)
            if (_cellToChunk.ContainsKey(new Vector3Int(x, 0, 0)))
                path.Add(new Vector3Int(x, 0, 0));

        Debug.Log($"[AB] ④ 60초 세션 (3,600프레임) — 커서 궤적 셀 {path.Count}칸");
        Debug.Log("[AB]    (타이머는 3,600프레임 루프 전체를 한 번만 감싼다 — 프레임당 " +
                  "Stopwatch 오버헤드가 출시본 수치에 섞이지 않도록)");

        foreach (int cellsPerSecond in new[] { 0, 5, 15, 40 })
        {
            int PathIndex(int frame) => cellsPerSecond == 0
                ? 0
                : (frame * cellsPerSecond / 60) % path.Count;

            // ── 개선 전: 매 프레임 무조건 재구성
            for (int w = 0; w < 60; w++)
                Legacy_HighlightHoveredChunk(_cellToChunk[path[PathIndex(w)]]);

            var swLegacy = Stopwatch.StartNew();
            for (int frame = 0; frame < SESSION_FRAMES; frame++)
                Legacy_HighlightHoveredChunk(_cellToChunk[path[PathIndex(frame)]]);
            swLegacy.Stop();
            int legacyCalls = SESSION_FRAMES;

            // ── 출시본: 가드를 통과한 프레임에만 실제 작업
            _selectedChunkCoord = null;
            _lastHoveredChunkCoords.Clear();
            for (int w = 0; w < 60; w++)
                Release_HandleHover(path[PathIndex(w)]);

            _selectedChunkCoord = null;
            _lastHoveredChunkCoords.Clear();
            int releaseCalls = 0;
            var swRelease = Stopwatch.StartNew();
            for (int frame = 0; frame < SESSION_FRAMES; frame++)
                if (Release_HandleHover(path[PathIndex(frame)]))
                    releaseCalls++;
            swRelease.Stop();

            double legacyTotal = swLegacy.Elapsed.TotalMilliseconds;
            double releaseTotal = swRelease.Elapsed.TotalMilliseconds;
            double legacyGarbageKb = legacyCalls * 1824.0 / 1024.0;

            string speedLabel = cellsPerSecond == 0 ? "정지" : $"초당 {cellsPerSecond}칸";
            Debug.Log($"[AB]    커서 {speedLabel,-10} — 재계산 횟수 {legacyCalls,5} → {releaseCalls,4}회 " +
                      $"({(releaseCalls == 0 ? "∞" : (legacyCalls / (float)releaseCalls).ToString("F0")),4}배 감소) · " +
                      $"누적 CPU {legacyTotal,7:F1} → {releaseTotal,6:F2} ms " +
                      $"({(releaseTotal <= 0 ? 0 : legacyTotal / releaseTotal),6:F1}배) · " +
                      $"누적 가비지 {legacyGarbageKb,7:F0} → 0 KB");
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 측정 5 — 점령이 진행될수록(대상 청크가 늘수록) 어떻게 벌어지는가
    // ────────────────────────────────────────────────────────────────
    [Test]
    public void E_ScalabilityByProgress()
    {
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.color = Color.white;
        foreach (Vector3Int coord in _cellToChunk.Keys)
        {
            _tilemap.SetTile(coord, tile);
            _tilemap.SetTileFlags(coord, TileFlags.None);
        }

        Debug.Log("[AB] ⑤ 판정 대상 청크 수별 — '영토 변화 1회'의 비용 (출시본은 이때만 실행)");

        foreach (int visibleChunks in new[] { 1, 4, 8, 16, 24, 32, 41 })
        {
            int n = Mathf.Min(visibleChunks, _chunkCoords.Count);
            int cellCount = 0;
            for (int c = 0; c < n; c++)
                cellCount += _cellsByChunk[_chunkCoords[c]].Count;

            for (int i = 0; i < 5; i++) Release_RecomputeClassification(n);

            const int ITER = 60;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < ITER; i++) Release_RecomputeClassification(n);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / ITER;

            Debug.Log($"[AB]    대상 청크 {n,2}개(셀 {cellCount,4}개): {ms:F3} ms/회 " +
                      $"· 매 프레임이었다면 프레임 예산의 {ms / FRAME_BUDGET_60FPS_MS * 100:F1}%");
        }

        Object.DestroyImmediate(tile);
    }
}
