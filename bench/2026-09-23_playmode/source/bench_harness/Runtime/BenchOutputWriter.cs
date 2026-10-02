// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 숫자 버퍼를 CSV·JSON으로 옮긴다. <b>측정이 끝난 뒤에만</b> 불린다 - 이 안의 문자열 생성과
/// 파일 쓰기가 측정 프레임에 섞이면 안 된다(§2-5).
///
/// 출력 위치는 bench/&lt;날짜&gt;_playmode/ 아래 csv/ 와 summary/ 다. raw/ 는 프로파일러가 직접 쓴다.
/// </summary>
public static class BenchOutputWriter
{
    private const string CSV_DIRECTORY = "csv";
    private const string SUMMARY_DIRECTORY = "summary";
    private const string INVALID_PREFIX = "invalid_";

    public static void Write(
        BenchRunReport report,
        BenchRecorders recorders,
        Vector3Int[] intendedCells,
        Vector3Int[] actualCells,
        bool[] mouseValid,
        bool[] inputChecked,
        Vector2[] injectedPointer,
        Vector2[] readPointer)
    {
        if (report == null)
            return;

        string fileName = report.IsValid ? report.RunId : INVALID_PREFIX + report.RunId;

        string csvDirectory = Path.Combine(BenchConfig.AbsoluteOutputRoot, CSV_DIRECTORY);
        string summaryDirectory = Path.Combine(BenchConfig.AbsoluteOutputRoot, SUMMARY_DIRECTORY);
        Directory.CreateDirectory(csvDirectory);
        Directory.CreateDirectory(summaryDirectory);

        if (recorders != null)
        {
            File.WriteAllText(
                Path.Combine(csvDirectory, fileName + ".csv"),
                BuildFrameCsv(recorders, intendedCells, actualCells, mouseValid, inputChecked, injectedPointer, readPointer),
                Encoding.UTF8);
        }

        File.WriteAllText(
            Path.Combine(summaryDirectory, fileName + ".json"),
            BuildSummaryJson(report, recorders, intendedCells, actualCells, mouseValid, inputChecked),
            Encoding.UTF8);
    }

    private static string BuildFrameCsv(
        BenchRecorders recorders,
        Vector3Int[] intendedCells,
        Vector3Int[] actualCells,
        bool[] mouseValid,
        bool[] inputChecked,
        Vector2[] injectedPointer,
        Vector2[] readPointer)
    {
        var builder = new StringBuilder(recorders.Capacity * 200);

        builder.Append("frame_index,read_frame_count,unscaled_delta_ns,main_thread_ns,gc_alloc_bytes,");
        builder.Append("batches,set_pass_calls,draw_calls,engine_controller_update_ns,");
        builder.Append("intended_cell_x,intended_cell_y,actual_cell_x,actual_cell_y,cell_match,input_checked,mouse_is_virtual,");
        builder.Append("injected_x,injected_y,read_x,read_y");

        foreach (string markerName in recorders.MarkerNames)
        {
            builder.Append(',').Append(ToColumnName(markerName));
        }

        builder.Append('\n');

        for (int frame = 0; frame < recorders.Capacity; frame++)
        {
            bool hasCells = intendedCells != null && actualCells != null && frame < intendedCells.Length;
            Vector3Int intended = hasCells ? intendedCells[frame] : default;
            Vector3Int actual = hasCells ? actualCells[frame] : default;

            builder.Append(frame.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(recorders.ReadFrameCount[frame].ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(recorders.UnscaledDeltaNs[frame].ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(recorders.MainThreadNs[frame].ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(recorders.GcAllocBytes[frame].ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(recorders.Batches[frame].ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(recorders.SetPassCalls[frame].ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(recorders.DrawCalls[frame].ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(recorders.EngineUpdateNs[frame].ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(intended.x.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(intended.y.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(actual.x.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(actual.y.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(hasCells && intended == actual ? '1' : '0').Append(',');
            builder.Append(inputChecked != null && frame < inputChecked.Length && inputChecked[frame] ? '1' : '0').Append(',');
            builder.Append(mouseValid != null && frame < mouseValid.Length && mouseValid[frame] ? '1' : '0').Append(',');

            Vector2 injected = injectedPointer != null && frame < injectedPointer.Length ? injectedPointer[frame] : default;
            Vector2 read = readPointer != null && frame < readPointer.Length ? readPointer[frame] : default;
            builder.Append(injected.x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(injected.y.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(read.x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(read.y.ToString("0.##", CultureInfo.InvariantCulture));

            for (int marker = 0; marker < recorders.MarkerNames.Length; marker++)
            {
                builder.Append(',').Append(recorders.MarkerTimeNs[marker][frame].ToString(CultureInfo.InvariantCulture));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string ToColumnName(string markerName) =>
        markerName.Replace('.', '_').Replace("()", string.Empty).ToLowerInvariant() + "_ns";

    private static string BuildSummaryJson(
        BenchRunReport report,
        BenchRecorders recorders,
        Vector3Int[] intendedCells,
        Vector3Int[] actualCells,
        bool[] mouseValid,
        bool[] inputChecked)
    {
        var builder = new StringBuilder(4096);
        builder.Append("{\n");

        AppendString(builder, "run_id", report.RunId);
        AppendString(builder, "variant", report.Variant);
        AppendString(builder, "scenario", report.Scenario);
        AppendNumber(builder, "run_index", report.RunIndex);
        AppendNumber(builder, "snapshot_slot", report.SnapshotSlot);
        AppendBool(builder, "is_instrumented", report.IsInstrumented);
        AppendBool(builder, "is_harness_only", report.IsHarnessOnly);
        AppendBool(builder, "is_valid", report.IsValid);
        AppendStringArray(builder, "invalid_reasons", report.InvalidReasons);

        AppendString(builder, "run_start_utc", TicksToIso(report.RunStartTicks));
        AppendString(builder, "run_end_utc", TicksToIso(report.RunEndTicks));
        AppendNumber(builder, "warmup_elapsed_ms", TicksToMilliseconds(report.WarmupStartTicks, report.WarmupEndTicks));
        AppendNumber(builder, "measure_elapsed_ms", TicksToMilliseconds(report.MeasureStartTicks, report.MeasureEndTicks));
        AppendNumber(builder, "warmup_frames", report.WarmupFrames);
        AppendNumber(builder, "measure_frames", report.MeasureFrames);
        AppendNumber(builder, "measure_start_frame", report.MeasureStartFrame);
        AppendNumber(builder, "transition_frame_index", report.TransitionFrameIndex);
        AppendString(builder, "raw_log_path", report.RawLogPath);
        AppendString(builder, "e_button_mode", report.EButtonMode);
        AppendBool(builder, "warmup_toggles_mode", report.WarmupTogglesMode);
        AppendString(builder, "e_target_chunk", report.ETargetChunk.ToString());
        AppendBool(builder, "e_panel_opened", report.EPanelOpenedAfterSelect);
        AppendBool(builder, "e_button_interactable", report.EButtonInteractable);
        AppendString(builder, "e_button_screen_position", report.EButtonScreenPosition.ToString("F1"));
        AppendTerritory(builder, "territory_before", report.TerritoryBefore);
        AppendTerritory(builder, "territory_after", report.TerritoryAfter);

        AppendString(builder, "unity_version", report.UnityVersion);
        AppendString(builder, "device_model", report.DeviceModel);
        AppendString(builder, "operating_system", report.OperatingSystem);
        AppendString(builder, "graphics_device", report.GraphicsDeviceName);
        AppendNumber(builder, "screen_width", report.ScreenWidth);
        AppendNumber(builder, "screen_height", report.ScreenHeight);
        AppendNumber(builder, "target_frame_rate", report.TargetFrameRate);
        AppendNumber(builder, "vsync_count", report.VSyncCount);
        AppendBool(builder, "incremental_gc", report.IsIncrementalGcEnabled);
        AppendBool(builder, "camera_drift", report.HasCameraDrift);
        AppendNumber(builder, "legacy_rebuild_call_count", report.LegacyRebuildCallCount);
        AppendNumber(builder, "off_screen_cells_skipped", report.OffScreenCellsSkipped);
        AppendNumber(builder, "path_cell_count", report.PathCells != null ? report.PathCells.Count : 0);
        AppendNumber(builder, "on_screen_candidate_cells", report.OnScreenCandidateCells);
        AppendNumber(builder, "not_pickable_cells_skipped", report.NotPickableCellsSkipped);
        AppendNumber(builder, "candidate_row_count", report.CandidateRowCount);
        AppendNumber(builder, "path_distinct_chunk_count", report.PathDistinctChunkCount);
        AppendString(builder, "camera_position", report.CameraPosition.ToString("F3"));
        AppendNumber(builder, "camera_orthographic_size", report.CameraOrthographicSize);
        AppendNumber(builder, "framed_chunk_row", report.FramedChunkRow);
        AppendNumber(builder, "framed_chunk_count", report.FramedChunkCount);

        AppendCellMatchStats(builder, intendedCells, actualCells, mouseValid, inputChecked, recorders);
        AppendRecorderValidity(builder, report);
        AppendPathCells(builder, report);

        builder.Append("  \"schema\": 1\n");
        builder.Append("}\n");
        return builder.ToString();
    }

    private static void AppendCellMatchStats(
        StringBuilder builder,
        Vector3Int[] intendedCells,
        Vector3Int[] actualCells,
        bool[] mouseValid,
        bool[] inputChecked,
        BenchRecorders recorders)
    {
        int compared = 0;
        int matched = 0;
        int virtualMouseFrames = 0;
        int firstMismatchIndex = -1;

        if (intendedCells != null && actualCells != null)
        {
            for (int i = 0; i < intendedCells.Length; i++)
            {
                if (mouseValid != null && i < mouseValid.Length && mouseValid[i])
                    virtualMouseFrames++;

                // 대조 대상이 아닌 프레임(E-출발의 버튼 클릭 구간)은 분모에서 뺀다.
                if (inputChecked != null && i < inputChecked.Length && !inputChecked[i])
                    continue;

                compared++;

                if (intendedCells[i] == actualCells[i])
                {
                    matched++;
                }
                else if (firstMismatchIndex < 0)
                {
                    firstMismatchIndex = i;
                }
            }
        }

        AppendNumber(builder, "input_frames_compared", compared);
        AppendNumber(builder, "input_frames_total", intendedCells != null ? intendedCells.Length : 0);
        AppendNumber(builder, "input_frames_matched", matched);
        AppendNumber(builder, "input_first_mismatch_index", firstMismatchIndex);
        AppendNumber(builder, "virtual_mouse_frames", virtualMouseFrames);

        // 마커가 한 번도 값을 내지 않은 프레임 수는 "실제 미발생"과 "리코더 미수집"을 가르는 근거다(§3-2-7).
        if (recorders == null)
            return;

        builder.Append("  \"marker_nonzero_frames\": {");

        for (int marker = 0; marker < recorders.MarkerNames.Length; marker++)
        {
            int nonZero = 0;
            long[] values = recorders.MarkerTimeNs[marker];

            for (int frame = 0; frame < values.Length; frame++)
            {
                if (values[frame] > 0)
                    nonZero++;
            }

            if (marker > 0)
                builder.Append(',');

            builder.Append('"').Append(recorders.MarkerNames[marker]).Append("\": ").Append(nonZero);
        }

        builder.Append("},\n");
    }

    private static void AppendRecorderValidity(StringBuilder builder, BenchRunReport report)
    {
        builder.Append("  \"recorder_valid\": {");

        if (report.MarkerNames != null)
        {
            for (int i = 0; i < report.MarkerNames.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');

                builder.Append('"').Append(report.MarkerNames[i]).Append("\": ")
                    .Append(report.MarkerValid[i] ? "true" : "false");
            }
        }

        builder.Append(", \"").Append(BenchMarkerNames.ENGINE_CONTROLLER_UPDATE).Append("\": ")
            .Append(report.IsEngineUpdateRecorderValid ? "true" : "false");
        builder.Append(", \"GC Allocated In Frame\": ").Append(report.IsGcAllocRecorderValid ? "true" : "false");
        builder.Append(", \"Main Thread\": ").Append(report.IsMainThreadRecorderValid ? "true" : "false");
        builder.Append(", \"Batches Count\": ").Append(report.IsBatchesRecorderValid ? "true" : "false");
        builder.Append("},\n");
    }

    private static void AppendPathCells(StringBuilder builder, BenchRunReport report)
    {
        builder.Append("  \"path_cells\": [");

        if (report.PathCells != null)
        {
            for (int i = 0; i < report.PathCells.Count; i++)
            {
                if (i > 0)
                    builder.Append(',');

                Vector3Int cell = report.PathCells[i];
                builder.Append('[').Append(cell.x).Append(',').Append(cell.y).Append(',').Append(cell.z).Append(']');
            }
        }

        builder.Append("],\n");
    }

    private static void AppendTerritory(StringBuilder builder, string key, BenchRunReport.TerritorySnapshot snapshot)
    {
        builder.Append("  \"").Append(key).Append("\": ");

        if (!snapshot.IsCaptured)
        {
            // 기록하지 않은 것과 "전부 0"을 구분한다.
            builder.Append("null,\n");
            return;
        }

        builder.Append("{\"visible\": ").Append(snapshot.VisibleChunks)
            .Append(", \"conquered\": ").Append(snapshot.ConqueredChunks)
            .Append(", \"conquerable\": ").Append(snapshot.ConquerableChunks)
            .Append(", \"active_expeditions\": ").Append(snapshot.ActiveExpeditions)
            .Append("},\n");
    }

    private static void AppendString(StringBuilder builder, string key, string value)
    {
        builder.Append("  \"").Append(key).Append("\": ");

        if (value == null)
            builder.Append("null");
        else
            builder.Append('"').Append(Escape(value)).Append('"');

        builder.Append(",\n");
    }

    private static void AppendStringArray(StringBuilder builder, string key, System.Collections.Generic.IReadOnlyList<string> values)
    {
        builder.Append("  \"").Append(key).Append("\": [");

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                builder.Append(',');

            builder.Append('"').Append(Escape(values[i])).Append('"');
        }

        builder.Append("],\n");
    }

    private static void AppendNumber(StringBuilder builder, string key, long value) =>
        builder.Append("  \"").Append(key).Append("\": ").Append(value.ToString(CultureInfo.InvariantCulture)).Append(",\n");

    private static void AppendNumber(StringBuilder builder, string key, double value) =>
        builder.Append("  \"").Append(key).Append("\": ").Append(value.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");

    private static void AppendBool(StringBuilder builder, string key, bool value) =>
        builder.Append("  \"").Append(key).Append("\": ").Append(value ? "true" : "false").Append(",\n");

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    private static string TicksToIso(long ticks) =>
        ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture) : null;

    private static double TicksToMilliseconds(long startTicks, long endTicks) =>
        startTicks > 0 && endTicks > startTicks ? TimeSpan.FromTicks(endTicks - startTicks).TotalMilliseconds : 0d;
}
