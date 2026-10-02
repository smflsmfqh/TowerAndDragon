// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System.IO;
using UnityEngine;

/// <summary>
/// 런처(에디터)가 쓰고 하네스(플레이)가 읽는 런 설정.
///
/// EditorPrefs를 쓰는 이유: 플레이 진입 시 도메인 리로드가 일어나면 static 필드는 초기화되고,
/// 측정 중 스냅샷 복원은 씬 리로드를 한 번 더 태운다. 두 경계를 모두 넘어 살아남는 것은
/// EditorPrefs뿐이다. (SaveLoadRequest가 순수 static이라 런처가 미리 심을 수 없는 것도 같은 이유다.)
/// 에디터 플레이모드 전용 측정이므로 빌드에서는 비활성 기본값으로 읽힌다.
/// </summary>
public static class BenchConfig
{
    private const string KEY_PREFIX = "TND.Bench.";

    private const string KEY_ENABLED = KEY_PREFIX + "Enabled";
    private const string KEY_VARIANT = KEY_PREFIX + "Variant";
    private const string KEY_SCENARIO = KEY_PREFIX + "Scenario";
    private const string KEY_RUN_INDEX = KEY_PREFIX + "RunIndex";
    private const string KEY_SNAPSHOT_SLOT = KEY_PREFIX + "SnapshotSlot";
    private const string KEY_WARMUP_FRAMES = KEY_PREFIX + "WarmupFrames";
    private const string KEY_MEASURE_FRAMES = KEY_PREFIX + "MeasureFrames";
    private const string KEY_OUTPUT_ROOT = KEY_PREFIX + "OutputRoot";
    private const string KEY_FILE_PREFIX = KEY_PREFIX + "FilePrefix";
    private const string KEY_INSTRUMENTED = KEY_PREFIX + "Instrumented";
    private const string KEY_HARNESS_ONLY = KEY_PREFIX + "HarnessOnly";
    private const string KEY_E_REAL_CLICK = KEY_PREFIX + "EUsesRealClick";
    private const string KEY_AUTO_FRAME = KEY_PREFIX + "AutoFrameCandidates";
    private const string KEY_SHOT_FRAME = KEY_PREFIX + "ScreenshotFrame";
    private const string KEY_EXIT_WHEN_DONE = KEY_PREFIX + "ExitPlayModeWhenDone";
    private const string KEY_KEEP_RAW = KEY_PREFIX + "KeepRawAfterExtract";

    public const int DEFAULT_WARMUP_FRAMES = 240;
    public const int DEFAULT_MEASURE_FRAMES = 1800;
    public const int DEFAULT_SNAPSHOT_SLOT = 2;
    public const int PROFILER_FRAME_COUNT = 2000;
    public const int TARGET_FRAME_RATE = 60;

    /// <summary>기준 초. 시나리오 B~D의 "초당 N셀"은 실제 경과시간이 아니라 이 프레임 수 기준이다.</summary>
    public const int REFERENCE_FRAMES_PER_SECOND = 60;

    private const string DEFAULT_OUTPUT_ROOT = "bench/2026-09-23_playmode";
    private const string DEFAULT_FILE_PREFIX = "pilot";

    public static bool IsEnabled
    {
        get => GetBool(KEY_ENABLED, false);
        set => SetBool(KEY_ENABLED, value);
    }

    public static BenchVariant Variant
    {
        get => (BenchVariant)GetInt(KEY_VARIANT, (int)BenchVariant.T1);
        set => SetInt(KEY_VARIANT, (int)value);
    }

    public static BenchScenario Scenario
    {
        get => (BenchScenario)GetInt(KEY_SCENARIO, (int)BenchScenario.A);
        set => SetInt(KEY_SCENARIO, (int)value);
    }

    public static int RunIndex
    {
        get => GetInt(KEY_RUN_INDEX, 1);
        set => SetInt(KEY_RUN_INDEX, value);
    }

    /// <summary>복원할 세이브 슬롯. 0은 자동저장, 1은 사용자 세이브라 2 이상만 쓴다.</summary>
    public static int SnapshotSlot
    {
        get => GetInt(KEY_SNAPSHOT_SLOT, DEFAULT_SNAPSHOT_SLOT);
        set => SetInt(KEY_SNAPSHOT_SLOT, value);
    }

    public static int WarmupFrames
    {
        get => GetInt(KEY_WARMUP_FRAMES, DEFAULT_WARMUP_FRAMES);
        set => SetInt(KEY_WARMUP_FRAMES, value);
    }

    public static int MeasureFrames
    {
        get => GetInt(KEY_MEASURE_FRAMES, DEFAULT_MEASURE_FRAMES);
        set => SetInt(KEY_MEASURE_FRAMES, value);
    }

    /// <summary>저장소 루트 기준 상대 경로. raw/·csv/·summary/가 이 아래에 생긴다.</summary>
    public static string OutputRoot
    {
        get => GetString(KEY_OUTPUT_ROOT, DEFAULT_OUTPUT_ROOT);
        set => SetString(KEY_OUTPUT_ROOT, value);
    }

    /// <summary>파일 이름 접두어. 파일럿은 "pilot", 본 측정은 "" 또는 "invalid"를 붙인다.</summary>
    public static string FilePrefix
    {
        get => GetString(KEY_FILE_PREFIX, DEFAULT_FILE_PREFIX);
        set => SetString(KEY_FILE_PREFIX, value);
    }

    /// <summary>실제 코드에 계측 패치가 적용된 상태인지. M3의 마커 오버헤드 실측에서 두 상태를 구분한다.</summary>
    public static bool IsInstrumented
    {
        get => GetBool(KEY_INSTRUMENTED, false);
        set => SetBool(KEY_INSTRUMENTED, value);
    }

    /// <summary>변형을 켜지 않고 하네스만 돌린다 - M3의 하네스 오버헤드 측정용.</summary>
    public static bool IsHarnessOnly
    {
        get => GetBool(KEY_HARNESS_ONLY, false);
        set => SetBool(KEY_HARNESS_ONLY, value);
    }

    /// <summary>
    /// E-출발에서 점령 버튼을 가상 마우스로 실제 클릭할지(true), Button.onClick.Invoke()로 부를지(false).
    /// 어느 쪽을 쓸지는 M3 파일럿에서 하나로 고정하고 이유를 기록한다(§2-1 E). 매니저 직접 호출은 금지다.
    /// </summary>
    public static bool EUsesRealClick
    {
        get => GetBool(KEY_E_REAL_CLICK, true);
        set => SetBool(KEY_E_REAL_CLICK, value);
    }

    /// <summary>
    /// 측정 전에 카메라를 점령 후보 청크 행에 맞출지. 끄면 스냅샷의 카메라를 그대로 쓴다.
    /// 기본 카메라(ortho 5)는 후보를 거의 비추지 못해 경로가 한두 청크에 갇힌다 - 그러면 T1의
    /// 전환 비용이 0으로 잡혀 비교가 왜곡된다. 두 변형이 같은 pose를 쓰고 pose는 결과에 기록된다.
    /// </summary>
    public static bool AutoFrameCandidates
    {
        get => GetBool(KEY_AUTO_FRAME, true);
        set => SetBool(KEY_AUTO_FRAME, value);
    }

    /// <summary>
    /// 측정 중 이 프레임 인덱스에서 Game 뷰를 찍는다. -1이면 찍지 않는다.
    /// <b>본 측정(M4·M5)에서는 쓰지 않는다</b> - 파일 쓰기가 측정 프레임에 섞이기 때문이다(§2-5).
    /// 파일럿의 표시 정확성 확인(§2-2)처럼 눈으로 봐야 하는 전용 런에서만 켠다.
    /// </summary>
    public static int ScreenshotFrame
    {
        get => GetInt(KEY_SHOT_FRAME, -1);
        set => SetInt(KEY_SHOT_FRAME, value);
    }

    /// <summary>
    /// 측정이 끝나면 하네스가 스스로 플레이 모드를 끝낼지. 배치 러너가 런을 이어 붙일 때 쓴다.
    /// 손으로 한 런만 돌릴 때는 꺼 둔다.
    /// </summary>
    public static bool ExitPlayModeWhenDone
    {
        get => GetBool(KEY_EXIT_WHEN_DONE, false);
        set => SetBool(KEY_EXIT_WHEN_DONE, value);
    }

    /// <summary>
    /// 추출이 끝난 뒤에도 그 런의 .raw를 남길지. 기본은 지운다(런당 약 1.2 GB).
    /// 구간별 GC 재추출이나 Profiler 창 캡처가 필요한 런에만 켠다 -
    /// <b>런의 유효·무효를 보고 정하지 않는다.</b> 그렇게 하면 §2-4가 금지한 선별이 된다.
    /// 켜는 기준은 "이 런으로 뭘 더 할 것인가"이고, 그 이유를 결과 문서에 적는다.
    /// </summary>
    public static bool KeepRawAfterExtract
    {
        get => GetBool(KEY_KEEP_RAW, false);
        set => SetBool(KEY_KEEP_RAW, value);
    }

    /// <summary>E 시나리오의 기록 창 길이(프레임). 전이 프레임 앞뒤의 후속 작업까지 담는다.</summary>
    public const int E_WINDOW_FRAMES = 180;

    /// <summary>E 기록 창에서 실제 전이를 일으키는 프레임 인덱스 - 앞뒤 기준선을 함께 남긴다.</summary>
    public const int E_TRANSITION_FRAME_INDEX = 30;

    /// <summary>저장소 루트의 절대 경로. Application.dataPath가 &lt;repo&gt;/Assets를 가리킨다.</summary>
    public static string RepositoryRoot => Directory.GetParent(Application.dataPath).FullName;

    public static string AbsoluteOutputRoot => Path.Combine(RepositoryRoot, OutputRoot);

    /// <summary>변형_시나리오_run별 파일 이름의 몸통. 접두어가 있으면 앞에 붙인다.</summary>
    public static string RunId
    {
        get
        {
            string body = $"{Variant}_{Scenario}_run{RunIndex}";
            string prefix = FilePrefix;
            return string.IsNullOrEmpty(prefix) ? body : $"{prefix}_{body}";
        }
    }

#if UNITY_EDITOR
    private static bool GetBool(string key, bool fallback) => UnityEditor.EditorPrefs.GetBool(key, fallback);
    private static void SetBool(string key, bool value) => UnityEditor.EditorPrefs.SetBool(key, value);
    private static int GetInt(string key, int fallback) => UnityEditor.EditorPrefs.GetInt(key, fallback);
    private static void SetInt(string key, int value) => UnityEditor.EditorPrefs.SetInt(key, value);
    private static string GetString(string key, string fallback) => UnityEditor.EditorPrefs.GetString(key, fallback);
    private static void SetString(string key, string value) => UnityEditor.EditorPrefs.SetString(key, value);
#else
    // 빌드에서는 측정이 돌지 않는다 - 하네스가 아예 만들어지지 않도록 비활성 기본값을 돌려준다.
    private static bool GetBool(string key, bool fallback) => false;
    private static void SetBool(string key, bool value) { }
    private static int GetInt(string key, int fallback) => fallback;
    private static void SetInt(string key, int value) { }
    private static string GetString(string key, string fallback) => fallback;
    private static void SetString(string key, string value) { }
#endif
}
