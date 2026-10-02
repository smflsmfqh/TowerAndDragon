// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// §2-1 E가 요구한 검증: <b>정상 정산(OnSettlement)과 디버그 완료가 같은 상태를 만드는가.</b>
///
/// M5는 완료 상태를 <c>DebugForceCompleteAllExpeditions()</c>로 준비했다. 그 전제가 맞는지
/// 확인하지 않으면 "완료된 영토에서의 재진입"을 쟀다고 말할 수 없다.
///
/// 두 경로를 각각 다른 플레이 세션에서 같은 스냅샷(슬롯 4)에 대해 돌리고,
/// <b>표시 판정에 쓰이는 상태만</b> 골라 같은 형식으로 덤프해 파일로 남긴다.
/// 비교는 파일끼리 한다 - 눈으로 훑고 "같아 보인다"로 넘기지 않는다.
///
/// 정상 경로는 밤 전투를 치르지 않고 <c>ForceEndDay()</c> → <c>EndNight()</c>로 주기를 넘긴다.
/// <c>OnSettlement</c>은 <c>OnNightEnd</c>에 걸려 있으므로 <b>실제 정산 코드가 그대로 실행된다</b> -
/// 완료 이벤트를 손으로 Invoke하는 것이 아니다. 전투를 건너뛰므로 자원·인구·적 상태는 실제 밤과 다르고,
/// 그래서 이 검증은 <b>점령 관련 상태</b>에 한정한다.
/// </summary>
public static class BenchSettlementCheck
{
    private const string MENU_ROOT = "Tools/Bench/정산 동등성/";
    private const string OUTPUT_DIRECTORY = "summary";
    private const int MAX_CYCLES = 12;

    [MenuItem(MENU_ROOT + "디버그 완료 경로")]
    public static string RunDebugPath() => Run("debug", RunDebugCompletion);

    [MenuItem(MENU_ROOT + "정상 정산 경로")]
    public static string RunNormalPath() => Run("normal", RunNormalSettlement);

    private static string Run(string label, System.Func<string> action)
    {
        if (!Application.isPlaying)
            return "플레이 중에만 돌릴 수 있다.";

        string before = CaptureState();
        string note = action();
        string after = CaptureState();

        var sb = new StringBuilder();
        sb.AppendLine($"# 경로: {label}");
        sb.AppendLine($"# 메모: {note}");
        sb.AppendLine("## BEFORE");
        sb.Append(before);
        sb.AppendLine("## AFTER");
        sb.Append(after);

        string directory = Path.Combine(BenchConfig.AbsoluteOutputRoot, OUTPUT_DIRECTORY);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"settlement_state_{label}.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        return $"{label}: {note} → {path}";
    }

    private static string RunDebugCompletion()
    {
        var conquestManager = Object.FindFirstObjectByType<ConquestManager>();
        int before = conquestManager.ActiveExpeditions.Count;
        conquestManager.DebugForceCompleteAllExpeditions();
        return $"DebugForceCompleteAllExpeditions() 1회, 활성 원정 {before} → {conquestManager.ActiveExpeditions.Count}";
    }

    /// <summary>원정이 완료될 때까지 주기를 넘긴다. OnNightEnd에 걸린 OnSettlement이 실제로 실행된다.</summary>
    private static string RunNormalSettlement()
    {
        var conquestManager = Object.FindFirstObjectByType<ConquestManager>();
        var cycleManager = Object.FindFirstObjectByType<CycleManager>();

        if (cycleManager == null)
            return "CycleManager를 찾지 못했다.";

        int before = conquestManager.ActiveExpeditions.Count;
        int cycles = 0;

        while (conquestManager.ActiveExpeditions.Count > 0 && cycles < MAX_CYCLES)
        {
            cycleManager.ForceEndDay();
            cycleManager.EndNight();
            cycles++;
        }

        return $"ForceEndDay+EndNight {cycles}주기, 활성 원정 {before} → {conquestManager.ActiveExpeditions.Count}";
    }

    /// <summary>표시 판정에 쓰이는 상태만 고른다. 정렬해 두 파일을 그대로 diff할 수 있게 한다.</summary>
    private static string CaptureState()
    {
        var gridMap = Object.FindFirstObjectByType<GridMap>();
        var conquestManager = Object.FindFirstObjectByType<ConquestManager>();

        var conquered = new List<string>();
        var visible = new List<string>();
        var conquerable = new List<string>();
        var unreachable = new List<string>();

        foreach (Chunk chunk in gridMap.GetAllChunks())
        {
            string coord = $"({chunk.ChunkCoord.x},{chunk.ChunkCoord.y})";

            if (chunk.CurrentState == ChunkState.Conquered)
                conquered.Add(coord);

            if (chunk.CurrentState != ChunkState.Visible)
                continue;

            visible.Add(coord);

            if (!conquestManager.HasExpeditionCost(chunk.ChunkCoord))
                continue;

            if (conquestManager.CanSendExpedition(chunk.ChunkCoord))
                conquerable.Add(coord);
            else if (!conquestManager.TryGetActiveExpedition(chunk.ChunkCoord, out _))
                unreachable.Add(coord);
        }

        conquered.Sort();
        visible.Sort();
        conquerable.Sort();
        unreachable.Sort();

        var expeditions = new List<string>();
        foreach (ConquestExpedition expedition in conquestManager.ActiveExpeditions)
        {
            expeditions.Add($"({expedition.TargetChunkCoord.x},{expedition.TargetChunkCoord.y})" +
                            $" {expedition.DaysProgressed}/{expedition.DaysRequired}");
        }

        expeditions.Sort();

        var sb = new StringBuilder();
        sb.AppendLine($"conquered({conquered.Count}): {string.Join(" ", conquered)}");
        sb.AppendLine($"visible({visible.Count}): {string.Join(" ", visible)}");
        sb.AppendLine($"conquerable({conquerable.Count}): {string.Join(" ", conquerable)}");
        sb.AppendLine($"unreachable({unreachable.Count}): {string.Join(" ", unreachable)}");
        sb.AppendLine($"activeExpeditions({expeditions.Count}): {string.Join(" | ", expeditions)}");
        sb.AppendLine($"enhancedChunks({conquestManager.EnhancedChunkCoords.Count})");
        return sb.ToString();
    }
}
