// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 하네스가 남긴 .raw를 프레임별 마커 시간·호출 수·GC Alloc으로 펴서 csv_from_raw/에 쓴다.
/// 하네스 CSV와의 교차 검증(§3-3)에 쓰는 독립 출처다.
///
/// 지키는 것:
/// <list type="bullet">
/// <item>로드 전에 Frame Count를 2,000으로 올리고, <b>실제로 로드된 프레임 번호 범위와 개수</b>를
/// 파일마다 기록한다. 설정만으로 전체 로드를 보장하지 않는다(§2-5).</item>
/// <item>같은 이름 샘플이 중첩되면 포함시간을 <b>중복 합산하지 않는다</b> - 같은 마커의 자손 샘플은 건너뛴다.</item>
/// <item>메인 스레드 한 곳만 읽는다. 스레드가 섞이면 하네스 값과 맞지 않는다.</item>
/// </list>
/// </summary>
public static class ProfilerRawSummary
{
    private const string MENU_PATH = "Tools/Bench/raw → csv_from_raw 추출";
    private const string RAW_DIRECTORY = "raw";
    private const string OUTPUT_DIRECTORY = "csv_from_raw";
    private const string RAW_EXTENSION = "*.raw";
    private const string MAIN_THREAD_GROUP = "";
    private const string MAIN_THREAD_NAME = "Main Thread";
    private const string GC_ALLOC_COUNTER = "GC Allocated In Frame";
    private const int MAIN_THREAD_INDEX = 0;

    [MenuItem(MENU_PATH)]
    public static void ExtractAll()
    {
        string rawDirectory = Path.Combine(BenchConfig.AbsoluteOutputRoot, RAW_DIRECTORY);
        string outputDirectory = Path.Combine(BenchConfig.AbsoluteOutputRoot, OUTPUT_DIRECTORY);

        if (!Directory.Exists(rawDirectory))
        {
            Debug.LogError($"[BENCH] raw 디렉터리가 없다: {rawDirectory}");
            return;
        }

        Directory.CreateDirectory(outputDirectory);

        bool frameCountApplied = BenchProfilerSettings.TrySetFrameCount(BenchConfig.PROFILER_FRAME_COUNT);

        var loadLog = new StringBuilder();
        loadLog.AppendLine($"# profiler_frame_count={BenchProfilerSettings.FrameCount} applied={frameCountApplied}");
        loadLog.AppendLine("file,first_frame_index,last_frame_index,loaded_frame_count,main_thread_name");

        foreach (string rawPath in Directory.GetFiles(rawDirectory, RAW_EXTENSION))
        {
            Extract(rawPath, outputDirectory, loadLog);
        }

        File.WriteAllText(Path.Combine(outputDirectory, "load_ranges.csv"), loadLog.ToString(), Encoding.UTF8);
        Debug.Log($"[BENCH] raw 추출 완료 → {outputDirectory}");
    }

    /// <summary>
    /// raw 하나만 추출한다. 배치 러너가 런 직후에 부르고, 성공하면 그 런의 .raw를 지운다.
    /// 로드 범위는 load_ranges.csv에 이어 붙인다.
    /// </summary>
    public static bool ExtractSingle(string rawPath)
    {
        string outputDirectory = Path.Combine(BenchConfig.AbsoluteOutputRoot, OUTPUT_DIRECTORY);
        Directory.CreateDirectory(outputDirectory);

        string rangesPath = Path.Combine(outputDirectory, "load_ranges.csv");
        var loadLog = new StringBuilder();

        if (!File.Exists(rangesPath))
        {
            loadLog.AppendLine("file,first_frame_index,last_frame_index,loaded_frame_count,main_thread_name");
        }

        BenchProfilerSettings.TrySetFrameCount(BenchConfig.PROFILER_FRAME_COUNT);
        Extract(rawPath, outputDirectory, loadLog);

        File.AppendAllText(rangesPath, loadLog.ToString(), Encoding.UTF8);

        string extracted = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(rawPath) + ".csv");
        return File.Exists(extracted);
    }

    private static void Extract(string rawPath, string outputDirectory, StringBuilder loadLog)
    {
        if (!ProfilerDriver.LoadProfile(rawPath, false))
        {
            Debug.LogError($"[BENCH] raw 로드 실패: {rawPath}");
            return;
        }

        int firstFrame = ProfilerDriver.firstFrameIndex;
        int lastFrame = ProfilerDriver.lastFrameIndex;
        string fileName = Path.GetFileNameWithoutExtension(rawPath);
        string mainThreadName = string.Empty;

        var builder = new StringBuilder();
        builder.Append("frame_index,frame_time_ns,gc_alloc_bytes");

        foreach (string markerName in BenchMarkerNames.ALL_SECTION_MARKERS)
        {
            builder.Append(',').Append(ToColumn(markerName)).Append("_ns,")
                .Append(ToColumn(markerName)).Append("_calls,")
                .Append(ToColumn(markerName)).Append("_gc_bytes");
        }

        builder.Append(',').Append(ToColumn(BenchMarkerNames.ENGINE_CONTROLLER_UPDATE)).Append("_ns,");
        builder.Append(ToColumn(BenchMarkerNames.ENGINE_CONTROLLER_UPDATE)).Append("_calls\n");

        var markerTotals = new long[BenchMarkerNames.ALL_SECTION_MARKERS.Length + 1];
        var markerCalls = new int[markerTotals.Length];
        var markerIds = new int[markerTotals.Length];
        var markerGcBytes = new long[markerTotals.Length];
        int loadedFrames = 0;

        for (int frame = firstFrame; frame <= lastFrame && frame >= 0; frame++)
        {
            using RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(frame, MAIN_THREAD_INDEX);

            if (view == null || !view.valid)
                continue;

            if (string.IsNullOrEmpty(mainThreadName))
                mainThreadName = view.threadName;

            ResolveMarkerIds(view, markerIds);
            Array.Clear(markerTotals, 0, markerTotals.Length);
            Array.Clear(markerCalls, 0, markerCalls.Length);
            Array.Clear(markerGcBytes, 0, markerGcBytes.Length);

            AccumulateFrame(view, markerIds, markerTotals, markerCalls);
            AccumulateFrameGcMemory(frame, markerIds, markerGcBytes);

            long gcAlloc = ReadCounter(view, GC_ALLOC_COUNTER);

            builder.Append(frame.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(((long)view.frameTimeNs).ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(gcAlloc.ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < markerTotals.Length; i++)
            {
                builder.Append(',').Append(markerTotals[i].ToString(CultureInfo.InvariantCulture));
                builder.Append(',').Append(markerCalls[i].ToString(CultureInfo.InvariantCulture));
                builder.Append(',').Append(markerGcBytes[i].ToString(CultureInfo.InvariantCulture));
            }

            builder.Append('\n');
            loadedFrames++;
        }

        File.WriteAllText(Path.Combine(outputDirectory, fileName + ".csv"), builder.ToString(), Encoding.UTF8);

        loadLog.Append(fileName).Append(',')
            .Append(firstFrame).Append(',')
            .Append(lastFrame).Append(',')
            .Append(loadedFrames).Append(',')
            .Append(string.IsNullOrEmpty(mainThreadName) ? MAIN_THREAD_NAME : mainThreadName)
            .Append('\n');
    }

    private static void ResolveMarkerIds(RawFrameDataView view, int[] markerIds)
    {
        for (int i = 0; i < BenchMarkerNames.ALL_SECTION_MARKERS.Length; i++)
        {
            markerIds[i] = view.GetMarkerId(BenchMarkerNames.ALL_SECTION_MARKERS[i]);
        }

        markerIds[markerIds.Length - 1] = view.GetMarkerId(BenchMarkerNames.ENGINE_CONTROLLER_UPDATE);
    }

    /// <summary>
    /// 프레임의 모든 샘플을 훑어 마커별 포함시간과 호출 수를 모은다.
    ///
    /// 같은 마커가 자기 안에서 다시 발화하면(재귀) 포함시간을 두 번 더하게 되므로,
    /// 이미 센 샘플의 자손 범위 안에서는 같은 마커를 세지 않는다. 서로 다른 마커의 중첩
    /// (예: HoveredBorder ⊃ HoveredBorderRebuild)은 각각 그대로 남기고, 표를 읽을 때 겹침을 명시한다.
    /// </summary>
    private static void AccumulateFrame(RawFrameDataView view, int[] markerIds, long[] totals, int[] calls)
    {
        int sampleCount = view.sampleCount;
        var skipUntil = new int[markerIds.Length];

        for (int i = 0; i < skipUntil.Length; i++)
        {
            skipUntil[i] = -1;
        }

        for (int sample = 0; sample < sampleCount; sample++)
        {
            int markerId = view.GetSampleMarkerId(sample);

            for (int i = 0; i < markerIds.Length; i++)
            {
                if (markerIds[i] < 0 || markerId != markerIds[i])
                    continue;

                if (sample <= skipUntil[i])
                    break;

                totals[i] += (long)view.GetSampleTimeNs(sample);
                calls[i]++;
                skipUntil[i] = sample + view.GetSampleChildrenCountRecursive(sample);
                break;
            }
        }
    }

    /// <summary>
    /// 구간별 GC Alloc을 계층 뷰에서 읽는다(§2-3이 요구한 "구간 GC와 전체 프레임 GC의 분리").
    ///
    /// RawFrameDataView에는 샘플별 할당량이 없다. Profiler 창의 Hierarchy가 쓰는
    /// <c>HierarchyFrameDataView</c>의 <c>columnGcMemory</c>가 그 값이다.
    /// 같은 이름 샘플을 합쳐(<c>MergeSamplesWithTheSameName</c>) 보므로 한 마커가 한 항목으로 모인다.
    /// 마커가 그 프레임에 없으면 0이 남고, 이는 "발화하지 않았다"는 뜻이다 -
    /// 리코더 미수집과 구분하려면 호출 수 열을 함께 본다.
    /// </summary>
    private static void AccumulateFrameGcMemory(int frameIndex, int[] markerIds, long[] gcBytes)
    {
        using HierarchyFrameDataView hierarchy = ProfilerDriver.GetHierarchyFrameDataView(
            frameIndex,
            MAIN_THREAD_INDEX,
            HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
            HierarchyFrameDataView.columnGcMemory,
            false);

        if (hierarchy == null || !hierarchy.valid)
            return;

        var pending = new Stack<int>();
        pending.Push(hierarchy.GetRootItemID());
        var children = new List<int>();

        while (pending.Count > 0)
        {
            int item = pending.Pop();
            int markerId = hierarchy.GetItemMarkerID(item);

            for (int i = 0; i < markerIds.Length; i++)
            {
                if (markerIds[i] >= 0 && markerId == markerIds[i])
                {
                    gcBytes[i] += (long)hierarchy.GetItemColumnDataAsDouble(item, HierarchyFrameDataView.columnGcMemory);
                    break;
                }
            }

            if (!hierarchy.HasItemChildren(item))
                continue;

            children.Clear();
            hierarchy.GetItemChildren(item, children);

            foreach (int child in children)
            {
                pending.Push(child);
            }
        }
    }

    private static long ReadCounter(RawFrameDataView view, string counterName)
    {
        int markerId = view.GetMarkerId(counterName);

        if (markerId < 0 || !view.HasCounterValue(markerId))
            return 0L;

        return view.GetCounterValueAsLong(markerId);
    }

    private static string ToColumn(string markerName) =>
        markerName.Replace('.', '_').Replace("()", string.Empty).ToLowerInvariant();
}
