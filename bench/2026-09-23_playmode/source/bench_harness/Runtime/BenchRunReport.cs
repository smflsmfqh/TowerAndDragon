// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런 하나의 메타데이터와 유효성 판정. 문자열은 전부 <b>측정이 끝난 뒤</b> 만들어진다(§2-5).
/// 측정 중에는 <see cref="MarkInvalid"/>가 목록에 이유를 담기만 한다.
/// </summary>
public sealed class BenchRunReport
{
    public string RunId { get; private set; }
    public string Variant { get; private set; }
    public string Scenario { get; private set; }
    public int RunIndex { get; private set; }
    public int SnapshotSlot { get; set; } = -1;

    public bool IsInstrumented { get; private set; }
    public bool IsHarnessOnly { get; private set; }

    public long RunStartTicks { get; private set; }
    public long RunEndTicks { get; private set; }
    public long MeasureStartTicks { get; set; }
    public long MeasureEndTicks { get; private set; }
    public long WarmupStartTicks { get; set; }
    public long WarmupEndTicks { get; set; }

    public int WarmupFrames { get; set; }
    public int MeasureFrames { get; private set; }
    public int MeasureStartFrame { get; set; } = -1;
    public int TransitionFrameIndex { get; set; } = -1;

    public string RawLogPath { get; set; }
    public string EButtonMode { get; set; }

    /// <summary>E-출발 진단 - 대상 청크, 패널이 실제로 열렸는지, 버튼 화면 좌표.</summary>
    public Vector2Int ETargetChunk { get; set; }

    public bool EPanelOpenedAfterSelect { get; set; }

    public bool EButtonInteractable { get; set; }

    public Vector2 EButtonScreenPosition { get; set; }

    /// <summary>워밍업이 점령 모드를 한 번 켰다 껐는가 - E-재진입에서 "첫 진입"이 아님을 남긴다.</summary>
    public bool WarmupTogglesMode { get; set; }
    public int OffScreenCellsSkipped { get; set; }

    /// <summary>화면 안에 들어온 점령 후보 땅 셀 수 - 경로가 짧은 원인을 가릴 때 쓴다.</summary>
    public int OnScreenCandidateCells { get; set; }

    /// <summary>화면 안이지만 더 높은 이웃 타일에 가려 그 지점으로 겨냥할 수 없어 경로에서 뺀 셀 수.</summary>
    public int NotPickableCellsSkipped { get; set; }

    /// <summary>화면 안 후보 셀이 걸쳐 있는 셀 행 수. 경로는 이 중 가장 긴 행 하나다.</summary>
    public int CandidateRowCount { get; set; }

    /// <summary>경로가 지나는 서로 다른 청크 수. B~D를 돌리기 전 <b>3 이상</b>이어야 한다 -
    /// 그보다 적으면 T1의 전환이 거의 일어나지 않아 비교가 T1에 유리하게 기운다.</summary>
    public int PathDistinctChunkCount { get; set; }

    /// <summary>측정에 쓴 카메라 pose - 보이는 범위가 곧 경로 길이를 정하므로 재현에 필요하다.</summary>
    public Vector3 CameraPosition { get; set; }

    public float CameraOrthographicSize { get; set; }

    /// <summary>프레이밍 대상으로 고른 청크 행과 그 행의 점령 가능 청크 수.</summary>
    public int FramedChunkRow { get; set; } = int.MinValue;

    public int FramedChunkCount { get; set; }

    public IReadOnlyList<Vector3Int> PathCells { get; set; }

    public string UnityVersion { get; private set; }
    public string DeviceModel { get; private set; }
    public string OperatingSystem { get; private set; }
    public string GraphicsDeviceName { get; private set; }
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }
    public int TargetFrameRate { get; private set; }
    public int VSyncCount { get; private set; }
    public bool IsIncrementalGcEnabled { get; private set; }
    public bool IsDeepProfileEnabled { get; private set; }

    public int LegacyRebuildCallCount { get; private set; }
    public bool HasCameraDrift { get; private set; }

    public string[] MarkerNames { get; private set; }
    public bool[] MarkerValid { get; private set; }
    public bool IsEngineUpdateRecorderValid { get; private set; }
    public bool IsGcAllocRecorderValid { get; private set; }
    public bool IsMainThreadRecorderValid { get; private set; }
    public bool IsBatchesRecorderValid { get; private set; }

    // E-재진입 전이 전/후 상태 - 디버그 완료 경로가 무엇을 바꿨는지 대조할 수 있게 둘 다 남긴다.
    private TerritorySnapshot _territoryBefore;
    private TerritorySnapshot _territoryAfter;

    public readonly struct TerritorySnapshot
    {
        public int VisibleChunks { get; }
        public int ConqueredChunks { get; }
        public int ConquerableChunks { get; }
        public int ActiveExpeditions { get; }
        public bool IsCaptured { get; }

        public TerritorySnapshot(int visible, int conquered, int conquerable, int activeExpeditions)
        {
            VisibleChunks = visible;
            ConqueredChunks = conquered;
            ConquerableChunks = conquerable;
            ActiveExpeditions = activeExpeditions;
            IsCaptured = true;
        }

        public override string ToString() =>
            $"visible={VisibleChunks} conquered={ConqueredChunks} " +
            $"conquerable={ConquerableChunks} expeditions={ActiveExpeditions}";
    }

    public TerritorySnapshot TerritoryBefore => _territoryBefore;
    public TerritorySnapshot TerritoryAfter => _territoryAfter;

    public void CaptureTerritoryBefore(GridMap gridMap, ConquestManager conquestManager) =>
        _territoryBefore = CaptureTerritory(gridMap, conquestManager);

    public void CaptureTerritoryAfter(GridMap gridMap, ConquestManager conquestManager) =>
        _territoryAfter = CaptureTerritory(gridMap, conquestManager);

    public string DescribeTerritoryTransition() => $"전: [{_territoryBefore}] → 후: [{_territoryAfter}]";

    private static TerritorySnapshot CaptureTerritory(GridMap gridMap, ConquestManager conquestManager)
    {
        int visible = 0;
        int conquered = 0;
        int conquerable = 0;

        foreach (Chunk chunk in gridMap.GetAllChunks())
        {
            if (chunk.CurrentState == ChunkState.Conquered)
                conquered++;

            if (chunk.CurrentState != ChunkState.Visible)
                continue;

            visible++;

            if (conquestManager.HasExpeditionCost(chunk.ChunkCoord)
                && conquestManager.CanSendExpedition(chunk.ChunkCoord))
            {
                conquerable++;
            }
        }

        return new TerritorySnapshot(visible, conquered, conquerable, conquestManager.ActiveExpeditions.Count);
    }

    private readonly List<string> _invalidReasons = new();

    public IReadOnlyList<string> InvalidReasons => _invalidReasons;

    /// <summary>§2-4의 무효 조건이 하나도 없을 때만 유효하다. 무효 런도 버리지 않고 접두어를 붙여 보존한다.</summary>
    public bool IsValid => _invalidReasons.Count == 0;

    public void BeginRun()
    {
        RunId = BenchConfig.RunId;
        Variant = BenchConfig.Variant.ToString();
        Scenario = BenchConfig.Scenario.ToString();
        RunIndex = BenchConfig.RunIndex;
        IsInstrumented = BenchConfig.IsInstrumented;
        IsHarnessOnly = BenchConfig.IsHarnessOnly;
        RunStartTicks = DateTime.UtcNow.Ticks;
    }

    public void MarkInvalid(string reason)
    {
        if (!_invalidReasons.Contains(reason))
            _invalidReasons.Add(reason);
    }

    public void CaptureRecorderValidity(BenchRecorders recorders)
    {
        MarkerNames = recorders.MarkerNames;
        MarkerValid = recorders.MarkerValid;
        IsEngineUpdateRecorderValid = recorders.IsEngineUpdateValid;
        IsGcAllocRecorderValid = recorders.IsGcAllocValid;
        IsMainThreadRecorderValid = recorders.IsMainThreadValid;
        IsBatchesRecorderValid = recorders.IsBatchesValid;
    }

    /// <summary>
    /// 이 프레임 시간을 넘으면 에디터가 앞에 있지 않았다고 본다.
    /// 포커스 상태에서 약 45 ms, 비포커스에서 약 430 ms였다(M3 실측). 그 사이에 넉넉히 둔다.
    /// 사람이 "다른 창을 클릭했으면 버려라"로 지키는 대신 데이터로 잡는다.
    /// </summary>
    private const double STALLED_FRAME_MS_THRESHOLD = 150d;

    /// <summary>B~D는 경로가 이만큼의 서로 다른 청크를 지나야 T1의 전환이 실제로 일어난다.</summary>
    private const int MIN_PATH_DISTINCT_CHUNKS = 3;

    public void EndRun(ConquestHighlightProfileHarness harness, BenchRecorders recorders)
    {
        RunEndTicks = DateTime.UtcNow.Ticks;
        MeasureEndTicks = RunEndTicks;
        MeasureFrames = recorders?.Capacity ?? 0;

        CheckStalledRun();
        CheckPathCoverage();

        UnityVersion = Application.unityVersion;
        DeviceModel = SystemInfo.deviceModel;
        OperatingSystem = SystemInfo.operatingSystem;
        GraphicsDeviceName = SystemInfo.graphicsDeviceName;
        ScreenWidth = Screen.width;
        ScreenHeight = Screen.height;
        TargetFrameRate = Application.targetFrameRate;
        VSyncCount = QualitySettings.vSyncCount;
        IsIncrementalGcEnabled = UnityEngine.Scripting.GarbageCollector.isIncremental;
        IsDeepProfileEnabled = UnityEngine.Profiling.Profiler.enabled && IsDeepProfilingActive();

        if (harness != null)
        {
            HasCameraDrift = harness.HasCameraDrift;
            LegacyRebuildCallCount = harness.LegacyDriver != null ? harness.LegacyDriver.RebuildCallCount : 0;
        }
    }

    /// <summary>
    /// 측정 중 에디터가 뒤로 밀려 플레이 루프가 멈춘 런을 잡는다.
    /// 그런 런은 마우스 주입이 반영되지 않아 실제 읽힌 포인터가 프레임 내내 고정되기도 한다 -
    /// 파일럿 1차의 `pilot_iter1_T1_D_run1`이 그 형태였다.
    /// 버리지 않고 invalid_ 접두어로 보존한다(§2-4).
    /// </summary>
    private void CheckStalledRun()
    {
        if (MeasureFrames <= 0 || MeasureStartTicks <= 0)
            return;

        double elapsedMs = TimeSpan.FromTicks(MeasureEndTicks - MeasureStartTicks).TotalMilliseconds;
        double perFrameMs = elapsedMs / MeasureFrames;

        if (perFrameMs > STALLED_FRAME_MS_THRESHOLD)
        {
            MarkInvalid($"프레임당 {perFrameMs:0.#} ms - 에디터가 앞에 있지 않았을 가능성이 높다 " +
                        $"(기준 {STALLED_FRAME_MS_THRESHOLD} ms)");
        }
    }

    /// <summary>
    /// B~D는 경로가 여러 청크를 지나야 T1의 전환 비용이 측정된다. 스냅샷마다 지형·영토가 다르므로
    /// 한 번 확인한 값을 다른 스냅샷에 가정하지 않고 런마다 검사한다.
    /// </summary>
    private void CheckPathCoverage()
    {
        bool needsChunkCrossing =
            Scenario == nameof(BenchScenario.B) ||
            Scenario == nameof(BenchScenario.C) ||
            Scenario == nameof(BenchScenario.D);

        if (!needsChunkCrossing)
            return;

        if (PathDistinctChunkCount < MIN_PATH_DISTINCT_CHUNKS)
        {
            MarkInvalid($"경로가 지나는 청크가 {PathDistinctChunkCount}개뿐이다 " +
                        $"(최소 {MIN_PATH_DISTINCT_CHUNKS}) - T1의 전환이 거의 일어나지 않아 비교가 왜곡된다");
        }
    }

    // Deep Profile은 에디터 설정이라 런타임에서 직접 읽을 수 없다 - 런처가 끄고, 여기서는 확인용으로만 남긴다.
    private static bool IsDeepProfilingActive() => false;
}
