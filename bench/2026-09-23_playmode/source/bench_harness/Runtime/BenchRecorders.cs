// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System;
using Unity.Profiling;

/// <summary>
/// 프레임마다 읽을 ProfilerRecorder 묶음과, 값을 담을 <b>미리 할당한 숫자 버퍼</b>.
///
/// 측정 프레임 안에서는 배열에 숫자만 넣는다(§2-5). 문자열·CSV·로그는 전부 측정이 끝난 뒤
/// <see cref="BenchOutputWriter"/>가 만든다.
///
/// 주의 두 가지:
/// <list type="bullet">
/// <item><c>LastValue</c>는 <b>직전에 완료된 프레임</b>의 값이다. 그래서 프레임 N의 Update 맨 앞에서 읽어
/// 프레임 N-1 칸에 넣는다. 입력을 넣은 프레임 번호와 이 완료 프레임 번호를 따로 남긴다(§3-2-4).</item>
/// <item>스크립트 마커 리코더는 그 마커가 <b>한 번이라도 발화한 뒤</b>에야 Valid다. 그래서 워밍업이
/// 끝난 뒤에 만든다. 발화하지 않아 0인 것과 리코더가 없어서 0인 것을 구분하려고
/// <see cref="MarkerValid"/>를 따로 기록한다(§3-2-7).</item>
/// </list>
/// </summary>
public sealed class BenchRecorders : IDisposable
{
    // options를 넘기면 ProfilerRecorderOptions.Default가 통째로 대체된다 - 기본값에 들어 있던
    // WrapAroundWhenCapacityReached까지 같이 빠진다. 용량 1짜리 버퍼가 한 번 차면 그 뒤 프레임 값이
    // 버려져 LastValue가 첫 값에 그대로 멈춘다.
    //
    // 실제로 이 실수 때문에 스모크 런에서 마커 시간이 120프레임 내내 같은 값이었다
    // (S0.Hover 172875 ns 고정). 같은 런의 raw에서는 프레임마다 값이 달랐으므로(distinct 118)
    // 마커와 프로파일러가 아니라 이 리코더 설정이 원인이었다.
    // 한 프레임에 여러 번 발화하는 마커가 있어 SumAllSamplesInFrame도 함께 준다.
    private const ProfilerRecorderOptions MARKER_RECORDER_OPTIONS =
        ProfilerRecorderOptions.SumAllSamplesInFrame
        | ProfilerRecorderOptions.WrapAroundWhenCapacityReached
        | ProfilerRecorderOptions.StartImmediately;

    private const int MARKER_RECORDER_CAPACITY = 1;

    private const string GC_ALLOC_STAT = "GC Allocated In Frame";
    private const string MAIN_THREAD_STAT = "Main Thread";
    private const string BATCHES_STAT = "Batches Count";
    private const string SET_PASS_STAT = "SetPass Calls Count";
    private const string DRAW_CALLS_STAT = "Draw Calls Count";

    private readonly ProfilerRecorder[] _markerRecorders;
    private ProfilerRecorder _engineUpdateRecorder;
    private ProfilerRecorder _gcAllocRecorder;
    private ProfilerRecorder _mainThreadRecorder;
    private ProfilerRecorder _batchesRecorder;
    private ProfilerRecorder _setPassRecorder;
    private ProfilerRecorder _drawCallsRecorder;

    /// <summary>수집 대상 마커 이름 - 인덱스가 <see cref="MarkerTimeNs"/>의 첫 차원과 같다.</summary>
    public string[] MarkerNames { get; }

    /// <summary>리코더를 만든 시점에 그 마커가 유효했는가. false면 그 열의 0은 "미수집"이다.</summary>
    public bool[] MarkerValid { get; }

    // [마커][프레임] - 프레임 안 모든 샘플의 합(ns).
    public long[][] MarkerTimeNs { get; }

    public long[] EngineUpdateNs { get; }
    public long[] GcAllocBytes { get; }
    public long[] MainThreadNs { get; }
    public long[] Batches { get; }
    public long[] SetPassCalls { get; }
    public long[] DrawCalls { get; }

    /// <summary>값을 읽은 프레임(= 완료 프레임 + 1)의 Time.frameCount.</summary>
    public int[] ReadFrameCount { get; }

    /// <summary>그 프레임의 실제 경과시간(ns). 1,800프레임을 30초라고 부르지 않기 위해 따로 남긴다.</summary>
    public long[] UnscaledDeltaNs { get; }

    public bool IsEngineUpdateValid { get; private set; }
    public bool IsGcAllocValid { get; private set; }
    public bool IsMainThreadValid { get; private set; }
    public bool IsBatchesValid { get; private set; }

    public int Capacity { get; }

    public BenchRecorders(int frameCapacity, string[] markerNames)
    {
        Capacity = frameCapacity;
        MarkerNames = markerNames;

        _markerRecorders = new ProfilerRecorder[markerNames.Length];
        MarkerValid = new bool[markerNames.Length];
        MarkerTimeNs = new long[markerNames.Length][];

        for (int i = 0; i < markerNames.Length; i++)
        {
            MarkerTimeNs[i] = new long[frameCapacity];
        }

        EngineUpdateNs = new long[frameCapacity];
        GcAllocBytes = new long[frameCapacity];
        MainThreadNs = new long[frameCapacity];
        Batches = new long[frameCapacity];
        SetPassCalls = new long[frameCapacity];
        DrawCalls = new long[frameCapacity];
        ReadFrameCount = new int[frameCapacity];
        UnscaledDeltaNs = new long[frameCapacity];
    }

    /// <summary>워밍업이 끝나 모든 마커가 한 번씩 발화한 뒤에 부른다.</summary>
    public void Start()
    {
        for (int i = 0; i < MarkerNames.Length; i++)
        {
            _markerRecorders[i] = StartMarkerRecorder(MarkerNames[i]);
            MarkerValid[i] = _markerRecorders[i].Valid;
        }

        _engineUpdateRecorder = StartMarkerRecorder(BenchMarkerNames.ENGINE_CONTROLLER_UPDATE);
        IsEngineUpdateValid = _engineUpdateRecorder.Valid;

        _gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, GC_ALLOC_STAT);
        IsGcAllocValid = _gcAllocRecorder.Valid;

        _mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, MAIN_THREAD_STAT);
        IsMainThreadValid = _mainThreadRecorder.Valid;

        _batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, BATCHES_STAT);
        _setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, SET_PASS_STAT);
        _drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, DRAW_CALLS_STAT);
        IsBatchesValid = _batchesRecorder.Valid;
    }

    /// <summary>
    /// 프레임 맨 앞에서 부른다. 담기는 값은 <b>직전 완료 프레임</b>의 것이므로 호출자가
    /// slot에 "그 직전 프레임의 인덱스"를 넣는다. 이 안에서는 숫자만 만진다.
    /// </summary>
    public void Sample(int slot, int currentFrameCount, long unscaledDeltaNs)
    {
        if (slot < 0 || slot >= Capacity)
            return;

        for (int i = 0; i < _markerRecorders.Length; i++)
        {
            MarkerTimeNs[i][slot] = _markerRecorders[i].Valid ? _markerRecorders[i].LastValue : 0L;
        }

        EngineUpdateNs[slot] = _engineUpdateRecorder.Valid ? _engineUpdateRecorder.LastValue : 0L;
        GcAllocBytes[slot] = _gcAllocRecorder.Valid ? _gcAllocRecorder.LastValue : 0L;
        MainThreadNs[slot] = _mainThreadRecorder.Valid ? _mainThreadRecorder.LastValue : 0L;
        Batches[slot] = _batchesRecorder.Valid ? _batchesRecorder.LastValue : 0L;
        SetPassCalls[slot] = _setPassRecorder.Valid ? _setPassRecorder.LastValue : 0L;
        DrawCalls[slot] = _drawCallsRecorder.Valid ? _drawCallsRecorder.LastValue : 0L;
        ReadFrameCount[slot] = currentFrameCount;
        UnscaledDeltaNs[slot] = unscaledDeltaNs;
    }

    private static ProfilerRecorder StartMarkerRecorder(string markerName) =>
        ProfilerRecorder.StartNew(
            ProfilerCategory.Scripts,
            markerName,
            MARKER_RECORDER_CAPACITY,
            MARKER_RECORDER_OPTIONS);

    public void Dispose()
    {
        for (int i = 0; i < _markerRecorders.Length; i++)
        {
            _markerRecorders[i].Dispose();
        }

        _engineUpdateRecorder.Dispose();
        _gcAllocRecorder.Dispose();
        _mainThreadRecorder.Dispose();
        _batchesRecorder.Dispose();
        _setPassRecorder.Dispose();
        _drawCallsRecorder.Dispose();
    }
}
