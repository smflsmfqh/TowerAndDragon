// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// M2의 스냅샷 3종을 만들고 상태를 확인하는 도구. 플레이 중에만 의미가 있다.
///
/// 스냅샷은 SaveService의 정상 저장 경로로 만든다(§3-2-3). 상태 조성에 디버그 함수를 쓰는 부분은
/// summary/snapshots.md에 그대로 적어, 정상 플레이 경로와 같다고 주장하지 않는다.
/// 슬롯 0은 자동저장, 슬롯 1은 사용자 세이브라 건드리지 않는다.
/// </summary>
public static class BenchSnapshotAuthor
{
    private const string MENU_ROOT = "Tools/Bench/스냅샷/";

    public const int SLOT_BASE = 2;
    public const int SLOT_E_DEPART = 3;
    public const int SLOT_E_COMPLETE = 4;

    [MenuItem(MENU_ROOT + "현재 상태 보고")]
    public static void ReportState()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[BENCH] 플레이 중에만 상태를 읽을 수 있다.");
            return;
        }

        var gridMap = Object.FindFirstObjectByType<GridMap>();
        var conquestManager = Object.FindFirstObjectByType<ConquestManager>();
        var cycleManager = Object.FindFirstObjectByType<CycleManager>();
        var resourceManager = Object.FindFirstObjectByType<ResourceManager>();
        var saveService = Object.FindFirstObjectByType<SaveService>();
        var modeController = Object.FindFirstObjectByType<ConquestModeController>();

        if (gridMap == null || conquestManager == null)
        {
            Debug.LogError("[BENCH] GridMap 또는 ConquestManager를 찾지 못했다.");
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("[BENCH] 현재 상태");
        builder.AppendLine($"  cycle={(cycleManager != null ? cycleManager.CurrentCycle.ToString() : "?")}" +
                           $" day={(cycleManager != null ? cycleManager.CurrentDayNumber : -1)}");
        builder.AppendLine($"  saveable={(saveService != null && saveService.IsSaveablePhase)}");
        builder.AppendLine($"  conquestModeActive={(modeController != null && modeController.IsActive)}");

        int hidden = 0;
        int visible = 0;
        int conquered = 0;
        var conquerable = new List<Vector2Int>();
        var unreachable = new List<Vector2Int>();

        foreach (Chunk chunk in gridMap.GetAllChunks())
        {
            switch (chunk.CurrentState)
            {
                case ChunkState.Hidden: hidden++; break;
                case ChunkState.Visible: visible++; break;
                case ChunkState.Conquered: conquered++; break;
            }

            if (chunk.CurrentState != ChunkState.Visible || !conquestManager.HasExpeditionCost(chunk.ChunkCoord))
                continue;

            if (conquestManager.CanSendExpedition(chunk.ChunkCoord))
                conquerable.Add(chunk.ChunkCoord);
            else
                unreachable.Add(chunk.ChunkCoord);
        }

        builder.AppendLine($"  chunks: hidden={hidden} visible={visible} conquered={conquered}");
        builder.AppendLine($"  conquerable({conquerable.Count}): {string.Join(" ", conquerable)}");
        builder.AppendLine($"  unreachable({unreachable.Count}): {string.Join(" ", unreachable)}");
        builder.AppendLine($"  activeExpeditions={conquestManager.ActiveExpeditions.Count}");

        foreach (ConquestExpedition expedition in conquestManager.ActiveExpeditions)
        {
            builder.AppendLine($"    → {expedition.TargetChunkCoord}");
        }

        if (resourceManager != null)
            builder.AppendLine($"  holdings={resourceManager.GetHoldingsSnapshot()}");

        Debug.Log(builder.ToString());
    }

    [MenuItem(MENU_ROOT + "자원 넉넉히 지급")]
    public static void GrantResources()
    {
        var resourceManager = Object.FindFirstObjectByType<ResourceManager>();

        if (resourceManager == null)
        {
            Debug.LogError("[BENCH] ResourceManager를 찾지 못했다.");
            return;
        }

        int changed = resourceManager.DebugAddToAllCatalogResources(RESOURCE_GRANT_AMOUNT);
        Debug.Log($"[BENCH] 자원 지급 - 자원 종류 {changed}개에 {RESOURCE_GRANT_AMOUNT}씩.");
    }

    private const int RESOURCE_GRANT_AMOUNT = 5000;

    /// <summary>
    /// 점령 가능 청크를 늘리기 위해 지금 보낼 수 있는 곳에 원정을 보내고 바로 완료시킨다.
    /// <b>스냅샷 조성 전용 디버그 경로</b>다 - 정상 플레이(밤을 치르는 정산)와 같다고 주장하지 않으며,
    /// 사용 사실을 summary/snapshots.md에 그대로 적는다.
    /// </summary>
    [MenuItem(MENU_ROOT + "영토 한 겹 넓히기 (디버그)")]
    public static void ExpandTerritoryOneRing()
    {
        var gridMap = Object.FindFirstObjectByType<GridMap>();
        var conquestManager = Object.FindFirstObjectByType<ConquestManager>();

        if (gridMap == null || conquestManager == null)
        {
            Debug.LogError("[BENCH] GridMap 또는 ConquestManager를 찾지 못했다.");
            return;
        }

        var targets = new List<Vector2Int>();

        foreach (Chunk chunk in gridMap.GetAllChunks())
        {
            if (chunk.CurrentState == ChunkState.Visible
                && conquestManager.HasExpeditionCost(chunk.ChunkCoord)
                && conquestManager.CanSendExpedition(chunk.ChunkCoord))
            {
                targets.Add(chunk.ChunkCoord);
            }
        }

        var unlimited = new ResourceCost
        {
            Population = UNLIMITED_AMOUNT,
            Food = UNLIMITED_AMOUNT,
            Wood = UNLIMITED_AMOUNT,
            Stone = UNLIMITED_AMOUNT,
        };

        int sent = 0;

        foreach (Vector2Int coord in targets)
        {
            if (conquestManager.SendExpedition(coord, unlimited))
                sent++;
        }

        conquestManager.DebugForceCompleteAllExpeditions();
        Debug.Log($"[BENCH] 영토 확장 - 원정 {sent}개를 보내고 즉시 완료시켰다.");
    }

    /// <summary>
    /// E-재진입용: 지정한 청크 하나에만 원정을 걸어 둔다. 활성 원정이 <b>정확히 하나</b>여야
    /// DebugForceCompleteAllExpeditions가 의도한 하나만 완료시킨다(§2-1 E 함정).
    /// </summary>
    [MenuItem(MENU_ROOT + "원정 1개만 남기기 (E용)")]
    public static void LeaveSingleExpedition()
    {
        var gridMap = Object.FindFirstObjectByType<GridMap>();
        var conquestManager = Object.FindFirstObjectByType<ConquestManager>();

        if (gridMap == null || conquestManager == null)
        {
            Debug.LogError("[BENCH] GridMap 또는 ConquestManager를 찾지 못했다.");
            return;
        }

        if (conquestManager.ActiveExpeditions.Count > 0)
        {
            Debug.LogError($"[BENCH] 이미 활성 원정이 {conquestManager.ActiveExpeditions.Count}개 있다 - " +
                           "새 런에서 다시 만든다.");
            return;
        }

        var unlimited = new ResourceCost
        {
            Population = UNLIMITED_AMOUNT,
            Food = UNLIMITED_AMOUNT,
            Wood = UNLIMITED_AMOUNT,
            Stone = UNLIMITED_AMOUNT,
        };

        foreach (Chunk chunk in gridMap.GetAllChunks())
        {
            if (chunk.CurrentState != ChunkState.Visible
                || !conquestManager.HasExpeditionCost(chunk.ChunkCoord)
                || !conquestManager.CanSendExpedition(chunk.ChunkCoord))
                continue;

            if (conquestManager.SendExpedition(chunk.ChunkCoord, unlimited))
            {
                Debug.Log($"[BENCH] 원정 1개 생성 - 대상 {chunk.ChunkCoord}, " +
                          $"활성 원정 {conquestManager.ActiveExpeditions.Count}개.");
                return;
            }
        }

        Debug.LogError("[BENCH] 원정을 보낼 수 있는 청크를 찾지 못했다.");
    }

    private const int UNLIMITED_AMOUNT = 999999;

    [MenuItem(MENU_ROOT + "슬롯 2에 저장 (snap_base)")]
    public static void SaveBase() => SaveToSlot(SLOT_BASE);

    [MenuItem(MENU_ROOT + "슬롯 3에 저장 (snap_e_depart)")]
    public static void SaveEDepart() => SaveToSlot(SLOT_E_DEPART);

    [MenuItem(MENU_ROOT + "슬롯 4에 저장 (snap_e_complete)")]
    public static void SaveEComplete() => SaveToSlot(SLOT_E_COMPLETE);

    /// <summary>스냅샷을 정상 로드 경로로 되살린다 - 씬이 다시 로드되고 GameManager.Start가 복원을 잇는다.</summary>
    [MenuItem(MENU_ROOT + "슬롯 2 불러오기")]
    public static void LoadBase() => LoadSlot(SLOT_BASE);

    [MenuItem(MENU_ROOT + "슬롯 3 불러오기")]
    public static void LoadEDepart() => LoadSlot(SLOT_E_DEPART);

    [MenuItem(MENU_ROOT + "슬롯 4 불러오기")]
    public static void LoadEComplete() => LoadSlot(SLOT_E_COMPLETE);

    private static void LoadSlot(int slotIndex)
    {
        var saveService = Object.FindFirstObjectByType<SaveService>();

        if (saveService == null)
        {
            Debug.LogError("[BENCH] SaveService를 찾지 못했다.");
            return;
        }

        saveService.RequestLoadAndReloadScene(slotIndex);
    }

    /// <summary>Game 뷰를 그대로 찍어 screenshots/에 남긴다 - 스냅샷 상태의 눈 확인 증거.</summary>
    public static void CaptureScreenshot(string fileName)
    {
        string directory = Path.Combine(BenchConfig.AbsoluteOutputRoot, "screenshots");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, fileName + ".png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log($"[BENCH] 스크린샷 요청 - {path} (다음 프레임 끝에 기록된다)");
    }

    private static void SaveToSlot(int slotIndex)
    {
        var saveService = Object.FindFirstObjectByType<SaveService>();

        if (saveService == null)
        {
            Debug.LogError("[BENCH] SaveService를 찾지 못했다.");
            return;
        }

        if (!saveService.CanSaveToSlot(slotIndex))
        {
            Debug.LogError($"[BENCH] 지금은 슬롯 {slotIndex}에 저장할 수 없다 " +
                           $"(saveablePhase={saveService.IsSaveablePhase}). 낮인지 확인한다.");
            return;
        }

        SaveSlotAsync(saveService, slotIndex).Forget();
    }

    private static async UniTaskVoid SaveSlotAsync(SaveService saveService, int slotIndex)
    {
        SaveResult result = await saveService.SaveAsync(slotIndex);
        Debug.Log($"[BENCH] 슬롯 {slotIndex} 저장 - 성공={result.IsSuccess} 사유={result.Reason} " +
                  $"경로={SavePaths.SaveFilePath(slotIndex)}");
    }
}
