// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 런 설정을 고르고 플레이를 시작하는 창. 설정은 전부 EditorPrefs(<see cref="BenchConfig"/>)로 넘어간다 -
/// 플레이 진입의 도메인 리로드와 스냅샷 복원의 씬 리로드를 모두 넘어야 하기 때문이다.
///
/// 프로파일러 설정(Frame Count 2,000 · Deep Profile OFF · Profile Editor OFF)도 여기서 맞춘다(§2-5).
/// 설정만으로 전체 로드가 보장되지는 않으므로 실제 로드된 프레임 범위는 추출기가 따로 기록한다.
/// </summary>
public sealed class BenchLauncher : EditorWindow
{
    private const string MENU_PATH = "Tools/Bench/점령 하이라이트 측정";
    private const string SCENE_PATH = "Assets/Scenes/SampleScene.unity";

    [MenuItem(MENU_PATH)]
    private static void Open() => GetWindow<BenchLauncher>("Bench Launcher");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("런 설정", EditorStyles.boldLabel);

        BenchConfig.Variant = (BenchVariant)EditorGUILayout.EnumPopup("변형", BenchConfig.Variant);
        BenchConfig.Scenario = (BenchScenario)EditorGUILayout.EnumPopup("시나리오", BenchConfig.Scenario);
        BenchConfig.RunIndex = EditorGUILayout.IntField("런 번호", BenchConfig.RunIndex);
        BenchConfig.SnapshotSlot = EditorGUILayout.IntField("스냅샷 슬롯", BenchConfig.SnapshotSlot);
        BenchConfig.FilePrefix = EditorGUILayout.TextField("파일 접두어", BenchConfig.FilePrefix);
        BenchConfig.OutputRoot = EditorGUILayout.TextField("출력 루트", BenchConfig.OutputRoot);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("프레임", EditorStyles.boldLabel);
        BenchConfig.WarmupFrames = EditorGUILayout.IntField("워밍업 최소 프레임", BenchConfig.WarmupFrames);
        BenchConfig.MeasureFrames = EditorGUILayout.IntField("측정 프레임", BenchConfig.MeasureFrames);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("파일럿 옵션", EditorStyles.boldLabel);
        BenchConfig.IsInstrumented = EditorGUILayout.Toggle("계측 패치 적용됨", BenchConfig.IsInstrumented);
        BenchConfig.IsHarnessOnly = EditorGUILayout.Toggle("하네스만 (변형 없음)", BenchConfig.IsHarnessOnly);
        BenchConfig.EUsesRealClick = EditorGUILayout.Toggle("E-출발: 실제 클릭", BenchConfig.EUsesRealClick);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            $"런 ID: {BenchConfig.RunId}\n" +
            $"출력: {BenchConfig.AbsoluteOutputRoot}\n" +
            "플레이 시작 시 하네스가 스냅샷을 복원하면서 씬을 한 번 다시 로드한다.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("측정 시작 (씬 열고 플레이)"))
                StartRun();
        }

        if (GUILayout.Button("프로파일러 설정만 적용"))
            ApplyProfilerSettings();

        if (GUILayout.Button("측정 비활성화 (하네스 심지 않기)"))
            BenchConfig.IsEnabled = false;

        EditorGUILayout.LabelField("하네스 활성", BenchConfig.IsEnabled ? "켜짐" : "꺼짐");
    }

    private static void StartRun()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        ApplyProfilerSettings();

        BenchConfig.IsEnabled = true;

        // 씬을 그대로 연다 - 하네스는 씬에 저장하지 않고 런타임에 심는다(BenchBootstrap).
        EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>
    /// Frame Count 2,000 · Deep Profile OFF · Profile Editor OFF. 기본 300이면 raw에서 일부만 추출된다(§2-5).
    /// </summary>
    private static void ApplyProfilerSettings()
    {
        bool frameCountApplied = BenchProfilerSettings.TrySetFrameCount(BenchConfig.PROFILER_FRAME_COUNT);
        BenchProfilerSettings.DisableDeepProfiling();
        ProfilerDriver.ClearAllFrames();

        Debug.Log(
            $"[BENCH] 프로파일러 설정 적용 - frameCount={BenchProfilerSettings.FrameCount}(적용={frameCountApplied}), " +
            $"deepProfiling={ProfilerDriver.deepProfiling}, profileEditor={ProfilerDriver.profileEditor}");
    }
}
