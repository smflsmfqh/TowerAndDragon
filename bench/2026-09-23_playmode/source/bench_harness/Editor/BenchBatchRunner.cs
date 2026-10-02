// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 본 측정(M4·M5)의 런 24~44개를 순서대로 이어 돌린다.
///
/// 손으로 돌리면 교대 순서를 틀리기 쉽고, 런마다 추출·삭제·기록을 빠뜨리기 쉽다.
/// 이 러너가 하는 일은 사람이 할 일과 같다:
/// <list type="number">
/// <item>다음 런 설정을 <see cref="BenchConfig"/>에 넣고 플레이를 시작한다.</item>
/// <item>하네스가 측정을 끝내면 스스로 플레이를 끝낸다(<see cref="BenchConfig.ExitPlayModeWhenDone"/>).</item>
/// <item>에디트 모드로 돌아오면 그 런의 .raw를 <c>csv_from_raw</c>로 추출한다.</item>
/// <item><b>추출에 성공하면</b> 그 런의 .raw를 지운다 - 유효·무효와 무관하게 똑같이 적용한다.
/// 유효 런만 지우면 §2-4가 금지하는 선별이 뒷문으로 들어온다. 추출에 실패하면 .raw를 남기고 기록한다.</item>
/// <item>순서·시각·유효 여부·raw 크기를 <c>summary/run_order.md</c>에 이어 적는다.</item>
/// </list>
///
/// 한 런 = 한 플레이 세션이다(§2-4). 씬 리로드로 대체하지 않는다 - 스냅샷 복원의 리로드는 그 안에서 따로 일어난다.
/// </summary>
[InitializeOnLoad]
public static class BenchBatchRunner
{
    private const string KEY_QUEUE = "TND.Bench.Batch.Queue";
    private const string KEY_INDEX = "TND.Bench.Batch.Index";
    private const string KEY_ACTIVE = "TND.Bench.Batch.Active";
    private const string RUN_ORDER_FILE = "run_order.md";
    private const string SUMMARY_DIRECTORY = "summary";
    private const string RAW_DIRECTORY = "raw";
    private const string CSV_FROM_RAW_DIRECTORY = "csv_from_raw";
    private const string ENTRY_SEPARATOR = ",";
    private const string FIELD_SEPARATOR = ":";
    private const double BYTES_PER_MB = 1024d * 1024d;

    static BenchBatchRunner()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static bool IsActive
    {
        get => EditorPrefs.GetBool(KEY_ACTIVE, false);
        private set => EditorPrefs.SetBool(KEY_ACTIVE, value);
    }

    private static string Queue
    {
        get => EditorPrefs.GetString(KEY_QUEUE, string.Empty);
        set => EditorPrefs.SetString(KEY_QUEUE, value);
    }

    private static int Index
    {
        get => EditorPrefs.GetInt(KEY_INDEX, 0);
        set => EditorPrefs.SetInt(KEY_INDEX, value);
    }

    /// <summary>
    /// 큐는 "변형:시나리오:런번호:슬롯" 항목을 쉼표로 이은 문자열이다.
    /// 예: "S0:A:1:2,T1:A:1:2,...". 문자열로 두는 이유는 EditorPrefs가 도메인 리로드를 넘는 유일한 저장소라서다.
    /// </summary>
    public static string StartBatch(string queue, string label) => StartBatch(queue, label, false);

    /// <param name="keepRaw">이 배치의 모든 런에 대해 추출 뒤에도 .raw를 남긴다(배치 단위 결정).</param>
    public static string StartBatch(string queue, string label, bool keepRaw)
    {
        if (string.IsNullOrEmpty(queue))
            return "빈 큐로는 시작하지 않는다.";

        BenchConfig.KeepRawAfterExtract = keepRaw;

        Queue = queue;
        Index = 0;
        IsActive = true;

        AppendRunOrder($"\n## {label} — 시작 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                       $"큐 {queue.Split(ENTRY_SEPARATOR).Length}런: `{queue}`\n\n" +
                       $".raw 보존: {(keepRaw ? "예 - 구간 GC 재추출·Profiler 캡처용" : "아니오 - 추출 직후 삭제")}\n\n" +
                       "| # | 런 ID | 시작 | 종료 | 유효 | 무효 사유 | 프레임당 ms | raw MB | 추출 |\n" +
                       "|---:|---|---|---|---|---|---:|---:|---|\n");

        return ConfigureAndPlayNext();
    }

    [MenuItem("Tools/Bench/배치 중단")]
    public static void StopBatch()
    {
        IsActive = false;
        Debug.Log("[BENCH] 배치 중단 - 진행 중인 플레이는 그대로 끝난다.");
    }

    public static string Status()
    {
        string[] entries = Queue.Split(ENTRY_SEPARATOR);
        return $"active={IsActive} index={Index}/{entries.Length} " +
               $"다음={(IsActive && Index < entries.Length ? entries[Index] : "-")}";
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode || !IsActive)
            return;

        // 플레이가 끝난 직후다. 방금 런의 뒤처리를 하고 다음 런으로 넘어간다.
        EditorApplication.delayCall += () =>
        {
            FinishCurrentRun();
            Index++;

            string result = ConfigureAndPlayNext();
            Debug.Log($"[BENCH] 배치 - {result}");
        };
    }

    private static void FinishCurrentRun()
    {
        string[] entries = Queue.Split(ENTRY_SEPARATOR);

        if (Index >= entries.Length)
            return;

        string runId = BenchConfig.RunId;
        string rawPath = Path.Combine(BenchConfig.AbsoluteOutputRoot, RAW_DIRECTORY, runId + ".raw");

        double rawMegabytes = File.Exists(rawPath) ? new FileInfo(rawPath).Length / BYTES_PER_MB : 0d;
        bool extracted = File.Exists(rawPath) && ProfilerRawSummary.ExtractSingle(rawPath);

        // 추출에 성공한 런의 .raw만 지운다. 유효·무효를 보지 않는다(§2-4의 선별 금지).
        // KeepRawAfterExtract는 배치 단위로 미리 정하는 값이라 런별 선별이 아니다.
        if (extracted && !BenchConfig.KeepRawAfterExtract)
            File.Delete(rawPath);

        AppendRunOrder(FormatRunRow(entries.Length, runId, rawMegabytes, extracted));
    }

    private static string FormatRunRow(int total, string runId, double rawMegabytes, bool extracted)
    {
        string summaryDirectory = Path.Combine(BenchConfig.AbsoluteOutputRoot, SUMMARY_DIRECTORY);
        string validPath = Path.Combine(summaryDirectory, runId + ".json");
        string invalidPath = Path.Combine(summaryDirectory, "invalid_" + runId + ".json");

        bool isValid = File.Exists(validPath);
        bool exists = isValid || File.Exists(invalidPath);

        string reasons = "-";
        string perFrame = "-";

        if (exists)
        {
            string json = File.ReadAllText(isValid ? validPath : invalidPath);
            reasons = isValid ? "-" : ExtractJsonField(json, "invalid_reasons");
            perFrame = ComputePerFrameMs(json);
        }

        return $"| {Index + 1}/{total} | `{runId}` | - | {DateTime.Now:HH:mm:ss} | " +
               $"{(exists ? (isValid ? "O" : "X") : "없음")} | {reasons} | {perFrame} | " +
               $"{rawMegabytes:0.#} | {(extracted ? "O" : "실패-raw 보존")} |\n";
    }

    // 결과 파일에서 숫자 두 개만 꺼내 표에 넣는다. 정식 집계는 summarize.py가 한다.
    private static string ComputePerFrameMs(string json)
    {
        string elapsed = ExtractJsonField(json, "measure_elapsed_ms");
        string frames = ExtractJsonField(json, "measure_frames");

        if (!double.TryParse(elapsed, NumberStyles.Float, CultureInfo.InvariantCulture, out double elapsedMs)
            || !double.TryParse(frames, NumberStyles.Integer, CultureInfo.InvariantCulture, out double frameCount)
            || frameCount <= 0)
        {
            return "-";
        }

        return (elapsedMs / frameCount).ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string ExtractJsonField(string json, string key)
    {
        int keyStart = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);

        if (keyStart < 0)
            return "-";

        int valueStart = json.IndexOf(':', keyStart) + 1;
        int valueEnd = json.IndexOf('\n', valueStart);

        if (valueStart <= 0 || valueEnd < valueStart)
            return "-";

        return json.Substring(valueStart, valueEnd - valueStart).Trim().TrimEnd(',').Replace("|", "/");
    }

    private static string ConfigureAndPlayNext()
    {
        string[] entries = Queue.Split(ENTRY_SEPARATOR);

        if (!IsActive || Index >= entries.Length)
        {
            IsActive = false;
            AppendRunOrder($"\n배치 종료 {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {Index}/{entries.Length}런 수행\n");
            return $"배치 종료 ({Index}/{entries.Length})";
        }

        string[] fields = entries[Index].Split(FIELD_SEPARATOR);

        if (fields.Length != 4)
        {
            IsActive = false;
            return $"큐 항목 형식 오류: {entries[Index]}";
        }

        BenchConfig.Variant = (BenchVariant)Enum.Parse(typeof(BenchVariant), fields[0]);
        BenchConfig.Scenario = (BenchScenario)Enum.Parse(typeof(BenchScenario), fields[1]);
        BenchConfig.RunIndex = int.Parse(fields[2], CultureInfo.InvariantCulture);
        BenchConfig.SnapshotSlot = int.Parse(fields[3], CultureInfo.InvariantCulture);

        BenchConfig.FilePrefix = string.Empty;
        BenchConfig.WarmupFrames = BenchConfig.DEFAULT_WARMUP_FRAMES;
        BenchConfig.MeasureFrames = BenchConfig.DEFAULT_MEASURE_FRAMES;
        BenchConfig.IsInstrumented = true;
        BenchConfig.IsHarnessOnly = false;
        BenchConfig.AutoFrameCandidates = true;
        BenchConfig.ScreenshotFrame = -1;
        BenchConfig.ExitPlayModeWhenDone = true;
        BenchConfig.IsEnabled = true;
        // KeepRawAfterExtract는 StartBatch가 정한 값을 그대로 둔다 - 런마다 바꾸지 않는다.

        BenchProfilerSettings.TrySetFrameCount(BenchConfig.PROFILER_FRAME_COUNT);
        BenchProfilerSettings.DisableDeepProfiling();

        EditorApplication.EnterPlaymode();
        return $"{Index + 1}/{entries.Length} 시작 - {BenchConfig.RunId}";
    }

    private static void AppendRunOrder(string text)
    {
        string directory = Path.Combine(BenchConfig.AbsoluteOutputRoot, SUMMARY_DIRECTORY);
        Directory.CreateDirectory(directory);
        File.AppendAllText(Path.Combine(directory, RUN_ORDER_FILE), text, Encoding.UTF8);
    }
}
