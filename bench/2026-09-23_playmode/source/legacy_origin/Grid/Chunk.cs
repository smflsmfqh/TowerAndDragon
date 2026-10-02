using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class Chunk 
{
    public const int CHUNK_SIZE = 9;
    
    private readonly List<GridCell> _cells;
    public IReadOnlyList<GridCell> Cells => _cells;

    public Vector2Int ChunkCoord { get; }
    public ChunkState CurrentState { get; private set; }
    public TerrainType DominantTerrain { get; }

    public Chunk(Vector2Int chunkCoord, List<GridCell> cells)
    {
        ChunkCoord = chunkCoord;
        _cells = cells;
        CurrentState = ChunkState.Hidden;
        DominantTerrain = ResolveDominantTerrain(cells);
    }

    public void SetState(ChunkState newState)
    {
        CurrentState = newState;

        foreach(GridCell cell in _cells)
        {
            cell.SetState(newState);
        }
    }

    // 물(Default)은 맵 경계 장식일 뿐 실제 지형이 아니므로 대표 지형 계산에서 제외한다.
    private static TerrainType ResolveDominantTerrain(List<GridCell> cells)
    {
        var counts = new Dictionary<TerrainType, int>();

        foreach (GridCell cell in cells)
        {
            if (cell.TerrainType == TerrainType.Default)
                continue;

            counts.TryGetValue(cell.TerrainType, out int count);
            counts[cell.TerrainType] = count + 1;
        }

        TerrainType dominant = TerrainType.Default;
        int maxCount = 0;

        foreach (var pair in counts)
        {
            if (pair.Value > maxCount)
            {
                maxCount = pair.Value;
                dominant = pair.Key;
            }
        }

        return dominant;
    }
}
