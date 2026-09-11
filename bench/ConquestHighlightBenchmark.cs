using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;

// [임시 계측 · 포트폴리오 실측용] 점령 하이라이트의 "매 프레임 재계산" 경로와
// "상태 변화 시에만 재계산" 경로의 1회 비용을 같은 데이터(실제 CLT_ChunkLayoutTable)로 잰다.
// 게임 기능이 아니므로 측정이 끝나면 삭제한다.
public class ConquestHighlightBenchmark
{
    private const string CHUNK_LAYOUT_PATH = "Assets/Data/ConquestData/CLT_ChunkLayoutTable.asset";
    private const int WARMUP_ITERATIONS = 200;
    private const int MEASURE_ITERATIONS = 2000;

    private GameObject _root;
    private Tilemap _tilemap;
    private SpriteRenderer _highlightPrefab;
    private ComponentPool<SpriteRenderer> _pool;

    // 레이아웃 에셋에서 읽어온 실제 청크 → 셀 목록
    private readonly List<List<Vector3Int>> _chunkCells = new();

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

        var prefabGo = new GameObject("HighlightPrefab");
        prefabGo.transform.SetParent(_root.transform);
        _highlightPrefab = prefabGo.AddComponent<SpriteRenderer>();

        _pool = new ComponentPool<SpriteRenderer>(_highlightPrefab, _root.transform);

        LoadChunkCells();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        _chunkCells.Clear();
    }

    private void LoadChunkCells()
    {
        var table = AssetDatabase.LoadAssetAtPath<ChunkLayoutTable>(CHUNK_LAYOUT_PATH);
        Assert.IsNotNull(table, $"레이아웃 에셋을 찾지 못했다: {CHUNK_LAYOUT_PATH}");

        var cellToChunk = new Dictionary<Vector3Int, Vector2Int>();
        table.BuildCellToChunkIndex(cellToChunk);

        var grouped = new Dictionary<Vector2Int, List<Vector3Int>>();
        foreach (var pair in cellToChunk)
        {
            if (!grouped.TryGetValue(pair.Value, out List<Vector3Int> cells))
            {
                cells = new List<Vector3Int>();
                grouped[pair.Value] = cells;
            }

            cells.Add(pair.Key);
        }

        foreach (var pair in grouped)
            _chunkCells.Add(pair.Value);

        _chunkCells.Sort((a, b) => a.Count.CompareTo(b.Count));
    }

    // 038b253e 시점의 HighlightHoveredChunk() 1회분:
    // 셀 좌표 리스트를 새로 만들고(List 할당) 셀마다 풀 슬롯을 켜고 위치·색을 다시 쓴다.
    private void RunLegacyHighlightOnce(List<Vector3Int> chunkCells)
    {
        var coords = new List<Vector3Int>(chunkCells.Count);
        for (int i = 0; i < chunkCells.Count; i++)
            coords.Add(chunkCells[i]);

        Color color = Color.green;
        for (int i = 0; i < coords.Count; i++)
        {
            SpriteRenderer highlight = _pool.Get(i);
            Vector3 cellPos = _tilemap.GetCellCenterWorld(coords[i]);
            cellPos.y += 0.75f;
            highlight.transform.position = cellPos;
            highlight.color = color;
        }

        _pool.DeactivateFrom(coords.Count);
    }

    [Test]
    public void Benchmark_SingleChunkHighlightRebuild()
    {
        // 대표 청크: 셀 수 중앙값에 해당하는 청크
        List<Vector3Int> median = _chunkCells[_chunkCells.Count / 2];
        List<Vector3Int> smallest = _chunkCells[0];
        List<Vector3Int> largest = _chunkCells[_chunkCells.Count - 1];

        Debug.Log($"[BENCH] 청크 수={_chunkCells.Count} 최소셀={smallest.Count} 중앙값셀={median.Count} 최대셀={largest.Count}");

        foreach (var sample in new[] { (name: "최소", cells: smallest), (name: "중앙값", cells: median), (name: "최대", cells: largest) })
        {
            for (int i = 0; i < WARMUP_ITERATIONS; i++)
                RunLegacyHighlightOnce(sample.cells);

            long allocBefore = System.GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < MEASURE_ITERATIONS; i++)
                RunLegacyHighlightOnce(sample.cells);
            sw.Stop();
            long allocAfter = System.GC.GetAllocatedBytesForCurrentThread();

            double msPerCall = sw.Elapsed.TotalMilliseconds / MEASURE_ITERATIONS;
            double bytesPerCall = (allocAfter - allocBefore) / (double)MEASURE_ITERATIONS;

            Debug.Log($"[BENCH] 하이라이트 재구성 1회 · {sample.name} 청크(셀 {sample.cells.Count}개): " +
                      $"{msPerCall:F4} ms/회 · 60fps 매 프레임 환산 {msPerCall * 60:F2} ms/초 " +
                      $"(프레임 예산 16.67ms 대비 {msPerCall / 16.67 * 100:F2}%) · " +
                      $"관리힙 증가 {bytesPerCall:F0} B/회");
        }
    }

    // 현재(HEAD) RecomputeConquerableClassification의 지형 틴트 단계에 해당하는 비용:
    // Visible 청크 전체를 훑어 셀 좌표별 색을 딕셔너리에 담는다.
    [Test]
    public void Benchmark_FullClassificationScan()
    {
        var tint = new Dictionary<Vector3Int, Color>();
        int totalCells = 0;
        foreach (var cells in _chunkCells)
            totalCells += cells.Count;

        void RunOnce()
        {
            tint.Clear();
            for (int c = 0; c < _chunkCells.Count; c++)
            {
                List<Vector3Int> cells = _chunkCells[c];
                for (int i = 0; i < cells.Count; i++)
                    tint[cells[i]] = Color.yellow;
            }
        }

        for (int i = 0; i < WARMUP_ITERATIONS; i++)
            RunOnce();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MEASURE_ITERATIONS; i++)
            RunOnce();
        sw.Stop();

        double msPerCall = sw.Elapsed.TotalMilliseconds / MEASURE_ITERATIONS;
        Debug.Log($"[BENCH] 전체 청크 분류·틴트 스캔 1회 (청크 {_chunkCells.Count}개 · 셀 {totalCells}개): " +
                  $"{msPerCall:F4} ms/회 · 매 프레임(60fps) 실행 시 {msPerCall * 60:F2} ms/초 " +
                  $"(프레임 예산 16.67ms 대비 {msPerCall / 16.67 * 100:F2}%)");
    }

    // 매 프레임 새로 만들던 List<Vector3Int> 1개의 실제 크기(= 프레임당 버려지던 가비지).
    [Test]
    public void Measure_PerFrameGarbageOfCoordList()
    {
        foreach (var sample in new[] { _chunkCells[0], _chunkCells[_chunkCells.Count / 2], _chunkCells[_chunkCells.Count - 1] })
        {
            const int KEEP = 4000;
            var keep = new List<Vector3Int>[KEEP];

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();
            long before = System.GC.GetTotalMemory(true);

            for (int i = 0; i < KEEP; i++)
            {
                var coords = new List<Vector3Int>(sample.Count);
                for (int c = 0; c < sample.Count; c++)
                    coords.Add(sample[c]);
                keep[i] = coords;
            }

            long after = System.GC.GetTotalMemory(true);
            double bytesEach = (after - before) / (double)KEEP;

            Debug.Log($"[BENCH] 좌표 리스트 1개(셀 {sample.Count}개) = {bytesEach:F0} B · " +
                      $"매 프레임 생성 시 60fps 기준 {bytesEach * 60 / 1024:F1} KB/초 가비지");

            System.GC.KeepAlive(keep);
        }
    }

    // 지형 틴트를 실제로 타일맵에 반영하는 비용(FogOfWarRenderer.RepaintCells에 해당).
    [Test]
    public void Benchmark_TilemapSetColorForAllCells()
    {
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.color = Color.white;

        var coords = new List<Vector3Int>();
        foreach (var cells in _chunkCells)
            coords.AddRange(cells);

        foreach (Vector3Int coord in coords)
        {
            _tilemap.SetTile(coord, tile);
            _tilemap.SetTileFlags(coord, TileFlags.None);
        }

        const int ITER = 60;
        for (int i = 0; i < 5; i++)
            foreach (Vector3Int coord in coords)
                _tilemap.SetColor(coord, Color.yellow);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ITER; i++)
        {
            Color color = (i % 2 == 0) ? Color.yellow : Color.white;
            foreach (Vector3Int coord in coords)
                _tilemap.SetColor(coord, color);
        }
        sw.Stop();

        double msPerCall = sw.Elapsed.TotalMilliseconds / ITER;
        Debug.Log($"[BENCH] 지형 틴트 재도색 1회 (Tilemap.SetColor × {coords.Count}셀): {msPerCall:F3} ms/회 · " +
                  $"매 프레임(60fps) 실행 시 {msPerCall * 60:F1} ms/초 (프레임 예산 16.67ms 대비 {msPerCall / 16.67 * 100:F1}%)");

        Object.DestroyImmediate(tile);
    }

    // 판정 대상 청크 수에 따른 재계산 1회 비용 곡선.
    // 실제 게임에서 대상이 되는 것은 그 시점에 Visible 상태인 청크뿐이라, 41개는 상한이다.
    [Test]
    public void Benchmark_RecomputeCostByChunkCount()
    {
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.color = Color.white;

        var allCoords = new List<Vector3Int>();
        foreach (var cells in _chunkCells)
            allCoords.AddRange(cells);

        foreach (Vector3Int coord in allCoords)
        {
            _tilemap.SetTile(coord, tile);
            _tilemap.SetTileFlags(coord, TileFlags.None);
        }

        var tint = new Dictionary<Vector3Int, Color>();

        foreach (int chunkCount in new[] { 1, 4, 8, 16, 24, 32, 41 })
        {
            int n = Mathf.Min(chunkCount, _chunkCells.Count);
            int cellCount = 0;
            for (int c = 0; c < n; c++)
                cellCount += _chunkCells[c].Count;

            void RunOnce()
            {
                tint.Clear();
                for (int c = 0; c < n; c++)
                {
                    List<Vector3Int> cells = _chunkCells[c];
                    for (int i = 0; i < cells.Count; i++)
                        tint[cells[i]] = Color.yellow;
                }

                foreach (var pair in tint)
                    _tilemap.SetColor(pair.Key, pair.Value);
            }

            for (int i = 0; i < 5; i++)
                RunOnce();

            const int ITER = 60;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < ITER; i++)
                RunOnce();
            sw.Stop();

            double msPerCall = sw.Elapsed.TotalMilliseconds / ITER;
            Debug.Log($"[BENCH] 재계산 1회 · 판정 대상 청크 {n}개(셀 {cellCount}개): {msPerCall:F3} ms " +
                      $"(60fps 프레임 예산 대비 {msPerCall / 16.67 * 100:F1}%)");
        }

        Object.DestroyImmediate(tile);
    }

    // 커서가 청크 경계를 넘는 빈도 = "상태 변화 시에만 재계산"의 실제 재계산 횟수.
    // 실제 레이아웃 위에서 커서를 직선으로 훑을 때 셀 이동 횟수 대비 청크 변경 횟수를 센다.
    [Test]
    public void Measure_ChunkChangeRateAlongSweep()
    {
        var table = AssetDatabase.LoadAssetAtPath<ChunkLayoutTable>(CHUNK_LAYOUT_PATH);
        var cellToChunk = new Dictionary<Vector3Int, Vector2Int>();
        table.BuildCellToChunkIndex(cellToChunk);

        int cellSteps = 0;
        int chunkChanges = 0;

        // 맵 전체를 가로로 훑는 스윕을 y마다 반복 (지그재그 없이 각 행 독립)
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (Vector3Int coord in cellToChunk.Keys)
        {
            minX = Mathf.Min(minX, coord.x); maxX = Mathf.Max(maxX, coord.x);
            minY = Mathf.Min(minY, coord.y); maxY = Mathf.Max(maxY, coord.y);
        }

        for (int y = minY; y <= maxY; y++)
        {
            Vector2Int? previous = null;
            for (int x = minX; x <= maxX; x++)
            {
                if (!cellToChunk.TryGetValue(new Vector3Int(x, y, 0), out Vector2Int chunkCoord))
                    continue;

                cellSteps++;
                if (previous.HasValue && previous.Value != chunkCoord)
                    chunkChanges++;

                previous = chunkCoord;
            }
        }

        Debug.Log($"[BENCH] 가로 스윕: 셀 이동 {cellSteps}회 중 청크 변경 {chunkChanges}회 " +
                  $"(셀 {(double)cellSteps / Mathf.Max(chunkChanges, 1):F1}칸당 1회 재계산)");
    }
}
