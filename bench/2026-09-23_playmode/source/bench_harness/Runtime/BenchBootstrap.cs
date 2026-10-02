// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 하네스를 <b>씬에 저장하지 않고</b> 플레이 시작 시 런타임으로 심는다.
///
/// 씬에 넣지 않는 이유: M2 완료 판정이 "git status -- Assets/에 사전 지정한 계측 파일만 잡힌다"이다.
/// SampleScene.unity에 하네스 오브젝트를 저장하면 그 조건이 깨지고, 씬 저장 자체가
/// 되돌리기 어려운 변경이 된다. 런타임 생성은 스냅샷 복원의 씬 리로드도 자연스럽게 살아남는다.
/// </summary>
public static class BenchBootstrap
{
    private const string HARNESS_OBJECT_NAME = "[BENCH] ConquestHighlightProfileHarness";

    /// <summary>
    /// 플레이 진입 시 딱 한 번 - EditorPrefs의 "측정 켜짐" 플래그를 <b>소비</b>해
    /// 이 플레이 세션 안에서만 유효한 상태로 옮긴다.
    ///
    /// 소비하는 이유: EditorPrefs는 이 PC에 눌어붙는다. 켜진 채로 남으면 다음에 무심코 누른 Play가
    /// 물리 마우스를 끄고 <c>RequestLoadAndReloadScene</c>으로 사용자의 진행을 벤치 스냅샷으로 덮어쓴다.
    /// 세션 상태로 옮기므로 스냅샷 복원의 씬 리로드는 그대로 살아남는다(플레이 중 LoadScene은
    /// 도메인 리로드를 일으키지 않아 static이 유지된다).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        // 플레이 진입마다 세션 상태를 반드시 비운다.
        //
        // static은 도메인 리로드에서 초기화되지만, 이 프로젝트는 Enter Play Mode Options로
        // 도메인 리로드가 꺼져 있어 스크립트를 고치지 않으면 <b>이전 런의 값이 그대로 남는다</b>.
        // 실제로 E 스모크 런이 이것 때문에 시작되지 않았다 - 직전 런의 HasFinished=true가 남아
        // 하네스가 Awake에서 스스로를 껐고, HasRequestedSnapshotRestore=true 탓에 오지도 않을
        // LoadCompleted를 기다렸다. 컴파일을 끼워 넣었을 때만 우연히 동작하는 상태였다.
        BenchSession.Reset();

        if (BenchConfig.IsEnabled)
        {
            BenchConfig.IsEnabled = false;
            BenchSession.IsActiveRun = true;
        }

        if (!BenchSession.IsActiveRun)
            return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// 플레이를 누른 그 씬에는 <c>sceneLoaded</c>가 오지 않는다 - 에디터에서는 씬이 이미 열려 있고
    /// 그 로드 이벤트는 <c>BeforeSceneLoad</c> 훅보다 앞서기 때문이다. 실제로 이것 때문에 첫
    /// 스모크 런에서 하네스가 아예 만들어지지 않았다. 그래서 첫 씬은 이 훅이 직접 심고,
    /// 스냅샷 복원으로 다시 로드되는 씬은 위 <c>sceneLoaded</c>가 받는다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SpawnForInitialScene() => TrySpawnHarness();

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawnHarness();

    private static void TrySpawnHarness()
    {
        if (!BenchSession.IsActiveRun)
            return;

        // 부트스트랩 씬(StartScene)에는 측정 대상이 없다 - GridMap이 있는 씬에서만 심는다.
        if (Object.FindFirstObjectByType<GridMap>() == null)
            return;

        if (Object.FindFirstObjectByType<ConquestHighlightProfileHarness>() != null)
            return;

        var harnessObject = new GameObject(HARNESS_OBJECT_NAME);
        harnessObject.AddComponent<ConquestHighlightProfileHarness>();
        Debug.Log("[BENCH] 하네스를 심었다 - " + BenchConfig.RunId);
    }
}

/// <summary>
/// 씬 리로드를 넘어 살아남아야 하는 런 진행 상태.
///
/// 플레이 중 <c>SceneManager.LoadScene</c>은 도메인 리로드를 일으키지 않으므로 static이 유지된다.
/// 플레이를 껐다 켜면(도메인 리로드) 초기화되는 것이 맞다 - 런마다 새 세션이어야 한다.
/// 런 설정 자체는 도메인 리로드도 넘어야 하므로 <see cref="BenchConfig"/>(EditorPrefs)에 둔다.
/// </summary>
public static class BenchSession
{
    /// <summary>이 플레이 세션이 측정 런인가 - 플레이 진입 때 EditorPrefs 플래그를 소비해 세운다.</summary>
    public static bool IsActiveRun { get; set; }

    /// <summary>스냅샷 복원을 위한 씬 리로드를 이미 요청했는가.</summary>
    public static bool HasRequestedSnapshotRestore { get; set; }

    /// <summary>복원 없이 현재 상태로 측정하는 모드인가(스냅샷 파일이 없을 때의 폴백).</summary>
    public static bool IsRestoreSkipped { get; set; }

    /// <summary>이 플레이 세션에서 하네스가 측정을 끝냈는가 - 리로드 후 중복 실행을 막는다.</summary>
    public static bool HasFinished { get; set; }

    /// <summary>플레이 진입 때 호출한다. 도메인 리로드가 꺼져 있어도 런마다 새 세션이 되게 한다.</summary>
    public static void Reset()
    {
        IsActiveRun = false;
        HasRequestedSnapshotRestore = false;
        IsRestoreSkipped = false;
        HasFinished = false;
    }
}
