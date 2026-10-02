// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.
// 계획: bench/08_측정계획_가드×표시방식.md (§2, §3), 순서: bench/08_측정_마일스톤.md (M2~M5)

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.UI;

/// <summary>
/// 점령 하이라이트 측정 하네스. 씬에 저장하지 않고 <see cref="BenchBootstrap"/>이 런타임으로 심는다.
///
/// 실행 순서를 -1000으로 두는 이유: 프레임 맨 앞에서 (1) 직전 완료 프레임의 리코더 값을 읽고
/// (2) 이번 프레임의 가상 마우스 위치를 넣어야, 측정 대상(ConquestModeController.Update 또는
/// S0 드라이버)이 그 입력을 같은 프레임에 보게 된다. 실제로 같은 프레임에 반영되는지는 M3에서 검증한다.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class ConquestHighlightProfileHarness : MonoBehaviour
{
    private const string BENCH_ROOT_NAME = "[BENCH] Root";
    private const string VIRTUAL_MOUSE_NAME = "BenchVirtualMouse";
    private const string SCREENSHOT_DIRECTORY = "screenshots";
    private const string REAL_CLICK_MODE_NAME = "virtual mouse real click";
    private const string ON_CLICK_INVOKE_MODE_NAME = "Button.onClick.Invoke";
    private const int SCENE_READY_TIMEOUT_FRAMES = 1200;
    private const int LOAD_TIMEOUT_FRAMES = 1800;
    private const int SETTLE_FRAMES = 30;
    private const float SCREEN_MARGIN_RATIO = 0.08f;

    // 프레이밍할 때 청크 행 바깥으로 남겨 두는 월드 여백.
    private const float FRAMING_MARGIN_WORLD = 1.5f;

    // E-출발 단계 사이의 프레임 간격 - 입력 큐와 UI 누름/뗌 처리에 프레임이 필요하다.
    private const int PANEL_OPEN_FRAMES = 4;
    private const int BUTTON_HOLD_FRAMES = 4;

    private enum Phase
    {
        Preparing,
        Warmup,
        Measuring,
        Finished,
    }

    // --- 씬 참조 ---
    private GridMap _gridMap;
    private MouseSelectController _mouseSelectController;
    private ConquestManager _conquestManager;
    private ConquestModeController _conquestModeController;
    private UI_ConquestWindow _conquestWindow;
    private SaveService _saveService;
    private Camera _camera;

    // --- 실행 상태 ---
    private Phase _phase = Phase.Preparing;
    private Transform _benchRoot;
    private LegacyConquestHighlightDriver _legacyDriver;
    private BenchRecorders _recorders;
    private BenchRunReport _report;

    private Mouse _virtualMouse;
    private readonly List<InputDevice> _disabledDevices = new();
    private bool _isLoadCompleted;

    // 스냅샷 복원 리로드로 다음 씬의 하네스에게 넘기고 끝나는 경로 - 이 경우는 "측정을 마쳤다"가 아니다.
    private bool _isHandingOffToReloadedScene;

    private readonly List<Vector3Int> _hoverPath = new();
    private Vector3Int[] _intendedCells;
    private Vector3Int[] _actualCells;
    private bool[] _isMouseCurrentValid;

    // 이 프레임의 의도 셀을 실제 읽은 셀과 대조해야 하는가.
    // E-출발은 전이 뒤 커서가 버튼 위로 가므로 청크 셀과 일치할 수 없다 - 그 프레임은 관문에서 뺀다.
    // 뺀 사실을 CSV·summary에 남겨 "일치율이 좋아 보이게" 만든 것이 아님을 드러낸다(§2-2-1).
    private bool[] _isInputChecked;

    // 하네스가 주입한 화면 좌표와, 그 프레임에 장치에서 실제로 읽힌 화면 좌표.
    // 입력 대응이 깨졌을 때 "주입이 안 먹혔다"와 "셀 판정이 다르게 나왔다"를 가른다.
    private Vector2[] _injectedPointer;
    private Vector2[] _readPointer;

    private int _measureStartFrame = -1;
    private int _measuredFrameCount;
    private int _warmupFrameCount;
    private int _currentFrameIndex = -1;

    private Vector2 _buttonScreenPosition;
    private Vector3 _cameraStartPosition;
    private Quaternion _cameraStartRotation;
    private float _cameraStartSize;
    private bool _hasCameraDrift;

    private int _originalTargetFrameRate;
    private int _originalVSyncCount;
    private string _originalLogFile;
    private bool _originalBinaryLog;
    private bool _isCaptureStarted;
    private bool _isEnvironmentApplied;

    private readonly List<Behaviour> _suppressedBackgroundBehaviours = new();
    private readonly List<GameObject> _suppressedBackgroundObjects = new();

    private void Awake()
    {
        // IsEnabled(EditorPrefs)가 아니라 세션 상태를 본다 - 플래그는 플레이 진입 때
        // BenchBootstrap이 이미 소비했다.
        if (!BenchSession.IsActiveRun || BenchSession.HasFinished)
        {
            enabled = false;
            return;
        }

        ResolveSceneReferences();

        // 구독은 Awake에서 한다(CLAUDE.md 이벤트 초기화 규칙). 스냅샷 복원 리로드 뒤
        // GameManager.Start가 BeginLoadFlow를 걸고, 복원이 끝나면 이 이벤트가 한 번 온다.
        if (_saveService != null)
            _saveService.LoadCompleted.AddListener(HandleLoadCompleted);
    }

    private void OnDestroy()
    {
        if (_saveService != null)
            _saveService.LoadCompleted.RemoveListener(HandleLoadCompleted);

        RestoreEnvironment();
    }

    private void Start()
    {
        if (!enabled)
            return;

        RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void HandleLoadCompleted() => _isLoadCompleted = true;

    // ------------------------------------------------------------------
    // 진행 흐름
    // ------------------------------------------------------------------

    private async UniTaskVoid RunAsync(CancellationToken cancellationToken)
    {
        _report = new BenchRunReport();
        _report.BeginRun();

        try
        {
            if (!await TryPrepareStateAsync(cancellationToken))
                return;

            ApplyEnvironment();
            PrepareVirtualMouse();

            // E-재진입의 완료 상태는 경로를 만들기 전에 조성한다 - 완료로 영토가 바뀌면
            // 점령 가능 청크가 달라져 경로도 달라진다. 조성은 측정 밖이다(§2-1 E).
            if (!await TryPrepareCompletedTerritoryAsync(cancellationToken))
                return;

            FrameCameraOnCandidateBand();
            CaptureCameraBaseline();
            BuildHoverPath();

            Debug.Log($"[BENCH] 준비 완료 - 경로 {_hoverPath.Count}셀 / 서로 다른 청크 {_report.PathDistinctChunkCount}개, "
                      + $"화면 안 후보 {_report.OnScreenCandidateCells}셀 · 밖 {_report.OffScreenCellsSkipped}셀 "
                      + $"· 겨냥 불가 {_report.NotPickableCellsSkipped}셀, "
                      + $"해상도 {Screen.width}x{Screen.height}, 카메라 ortho={_report.CameraOrthographicSize}, "
                      + $"변형 {BenchConfig.Variant}, 시나리오 {BenchConfig.Scenario}.");

            if (_hoverPath.Count == 0)
            {
                Fail("점령 가능한 청크의 화면 안 셀을 찾지 못했다 - 스냅샷이나 카메라 위치를 확인한다.");
                return;
            }

            if (!await TryActivateVariantAsync(cancellationToken))
                return;

            await WarmupAsync(cancellationToken);

            Debug.Log($"[BENCH] 워밍업 완료 - {_report.WarmupFrames}프레임. 여기서부터 측정이 끝날 때까지 로그를 내지 않는다.");

            _recorders = new BenchRecorders(MeasureFrameCapacity, BenchMarkerNames.ALL_SECTION_MARKERS);
            _recorders.Start();
            _report.CaptureRecorderValidity(_recorders);

            StartProfilerCapture();

            _phase = Phase.Measuring;
            await UniTask.WaitUntil(() => _measuredFrameCount > MeasureFrameCapacity, cancellationToken: cancellationToken);

            _phase = Phase.Finished;
            StopProfilerCapture();

            ValidateInputCorrespondence();
            _report.EndRun(this, _recorders);
            BenchOutputWriter.Write(_report, _recorders, _intendedCells, _actualCells, _isMouseCurrentValid, _isInputChecked,
                _injectedPointer, _readPointer);

            Debug.Log($"[BENCH] 완료 - {BenchConfig.RunId}");
            RequestExitPlayModeIfBatched();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            _report.MarkInvalid(exception.GetType().Name);
            TryWriteFailureReport();
        }
        finally
        {
            // 인계(스냅샷 복원 리로드)일 때는 세션을 끝내지 않는다 - 끝낸 것으로 표시하면
            // 리로드된 씬의 하네스가 Awake에서 스스로를 꺼 버려 측정이 영영 시작되지 않는다.
            if (!_isHandingOffToReloadedScene)
            {
                BenchSession.HasFinished = true;
                StopProfilerCapture();
                TeardownVariant();
                RestoreEnvironment();
                _recorders?.Dispose();
            }
        }
    }

    /// <summary>
    /// 씬이 준비되기를 기다리고, 아직이면 스냅샷 복원을 위한 씬 리로드를 <b>이 안에서</b> 요청한다.
    ///
    /// 런처가 미리 심을 수 없는 이유: <c>SaveLoadRequest</c>는 순수 static이라 플레이 진입의
    /// 도메인 리로드에서 초기화된다. 그래서 플레이 안에서 <c>RequestLoadAndReloadScene</c>을 부르고,
    /// 리로드된 씬에서 다시 생성된 하네스가 <c>LoadCompleted</c>를 기다려 이어간다.
    /// </summary>
    private async UniTask<bool> TryPrepareStateAsync(CancellationToken cancellationToken)
    {
        if (!await WaitForSceneReadyAsync(cancellationToken))
        {
            Fail("씬 준비 대기 시간 초과 - GridMap 청크 또는 매니저를 찾지 못했다.");
            return false;
        }

        if (BenchSession.IsRestoreSkipped)
            return true;

        if (!BenchSession.HasRequestedSnapshotRestore)
        {
            BenchSession.HasRequestedSnapshotRestore = true;

            if (_saveService == null || !File.Exists(SavePaths.SaveFilePath(BenchConfig.SnapshotSlot)))
            {
                BenchSession.IsRestoreSkipped = true;
                _report.MarkInvalid($"스냅샷 슬롯 {BenchConfig.SnapshotSlot} 없음");
                Debug.LogError($"[BENCH] 스냅샷 슬롯 {BenchConfig.SnapshotSlot}이 없다 - 복원 없이 진행하며 이 런은 무효로 표시한다.");
                return true;
            }

            // 씬이 다시 로드되고 새 하네스가 아래 분기로 이어받는다. 이 인스턴스는 여기서 끝난다.
            _isHandingOffToReloadedScene = true;
            Debug.Log($"[BENCH] 스냅샷 슬롯 {BenchConfig.SnapshotSlot} 복원 요청 - 씬을 다시 로드한다.");
            _saveService.RequestLoadAndReloadScene(BenchConfig.SnapshotSlot);
            return false;
        }

        // 복원 리로드 뒤의 인스턴스 - 복원이 끝날 때까지 기다린다.
        int waitedFrames = 0;
        while (!_isLoadCompleted && waitedFrames < LOAD_TIMEOUT_FRAMES)
        {
            await UniTask.NextFrame(cancellationToken);
            waitedFrames++;
        }

        if (!_isLoadCompleted)
        {
            Fail("스냅샷 복원 완료 신호(LoadCompleted)를 받지 못했다.");
            return false;
        }

        // 복원이 촉발한 비동기 표시 작업(안개·테두리 등)이 가라앉은 뒤에 시작한다.
        await WaitFramesAsync(SETTLE_FRAMES, cancellationToken);
        _report.SnapshotSlot = BenchConfig.SnapshotSlot;
        Debug.Log($"[BENCH] 스냅샷 복원 완료 - 슬롯 {BenchConfig.SnapshotSlot}.");
        return true;
    }

    private async UniTask<bool> WaitForSceneReadyAsync(CancellationToken cancellationToken)
    {
        for (int i = 0; i < SCENE_READY_TIMEOUT_FRAMES; i++)
        {
            ResolveSceneReferences();

            if (IsSceneReady())
                return true;

            await UniTask.NextFrame(cancellationToken);
        }

        return false;
    }

    private bool IsSceneReady()
    {
        if (_gridMap == null || _conquestManager == null || _conquestModeController == null
            || _mouseSelectController == null || _camera == null)
            return false;

        foreach (Chunk _ in _gridMap.GetAllChunks())
        {
            return true;
        }

        return false;
    }

    private void ResolveSceneReferences()
    {
        if (_gridMap == null)
            _gridMap = FindFirstObjectByType<GridMap>();

        if (_mouseSelectController == null)
            _mouseSelectController = FindFirstObjectByType<MouseSelectController>();

        if (_conquestManager == null)
            _conquestManager = FindFirstObjectByType<ConquestManager>();

        if (_conquestModeController == null)
            _conquestModeController = FindFirstObjectByType<ConquestModeController>();

        if (_conquestWindow == null)
            _conquestWindow = FindFirstObjectByType<UI_ConquestWindow>(FindObjectsInactive.Include);

        if (_saveService == null)
            _saveService = FindFirstObjectByType<SaveService>();

        if (_camera == null)
            _camera = Camera.main;
    }

    // ------------------------------------------------------------------
    // 환경·입력
    // ------------------------------------------------------------------

    private void ApplyEnvironment()
    {
        _originalTargetFrameRate = Application.targetFrameRate;
        _originalVSyncCount = QualitySettings.vSyncCount;

        Application.targetFrameRate = BenchConfig.TARGET_FRAME_RATE;
        QualitySettings.vSyncCount = 0;

        // 창이 떠 있지 않아도 측정 중 카메라가 움직이면 안 된다 - 프로젝트의 공용 차단 레지스트리를 쓴다.
        CameraInputBlockRegistry.Register(this);

        SuppressBackgroundOverlays();

        var rootObject = new GameObject(BENCH_ROOT_NAME);
        _benchRoot = rootObject.transform;

        _isEnvironmentApplied = true;
    }

    // ChunkDebugger 같은 배경 오버레이가 켜져 있으면 배치 수에 섞인다(M1 함정 메모).
    // 가이드(조언자) 오버레이는 세이브 복원마다 다시 떠서 화면을 덮고 입력을 가로막으므로
    // 측정 동안 통째로 내린다 - E-출발의 버튼 클릭도 이 오버레이에 막힌다.
    // 끈 대상은 보고서에 남기고 측정이 끝나면 되돌린다.
    private void SuppressBackgroundOverlays()
    {
        foreach (ChunkDebugger debugger in FindObjectsByType<ChunkDebugger>(FindObjectsSortMode.None))
        {
            if (!debugger.enabled)
                continue;

            debugger.enabled = false;
            _suppressedBackgroundBehaviours.Add(debugger);
        }

        foreach (UI_GuideOverlay overlay in
                 FindObjectsByType<UI_GuideOverlay>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            overlay.gameObject.SetActive(false);
            _suppressedBackgroundObjects.Add(overlay.gameObject);
        }

        // CameraController는 입력이 막혀 있어도 매 프레임 ApplyMovement()로 카메라를 자기 목표 위치·줌으로
        // 되돌린다(SmoothDamp + 경계 클램프). CameraInputBlockRegistry는 <b>입력만</b> 막을 뿐이라,
        // 하네스가 transform을 직접 옮겨도 다음 프레임에 원위치된다 -
        // 실제로 프레이밍 첫 시도가 "카메라 이동 감지(frameIndex=0)"로 무효 처리됐다.
        // 측정 동안에는 컨트롤러를 통째로 멈춰 pose를 고정한다(§2-4의 카메라 불변).
        foreach (CameraController controller in
                 FindObjectsByType<CameraController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!controller.enabled)
                continue;

            controller.enabled = false;
            _suppressedBackgroundBehaviours.Add(controller);
        }
    }

    private void PrepareVirtualMouse()
    {
        // 물리 마우스가 살아 있으면 사람 손이 측정에 섞인다. 전부 끄고 가상 마우스만 남긴다.
        foreach (InputDevice device in InputSystem.devices)
        {
            if (device is Mouse && device.enabled)
            {
                InputSystem.DisableDevice(device);
                _disabledDevices.Add(device);
            }
        }

        _virtualMouse = InputSystem.AddDevice<Mouse>(VIRTUAL_MOUSE_NAME);
        InputSystem.EnableDevice(_virtualMouse);
        InputSystem.SetDeviceUsage(_virtualMouse, VIRTUAL_MOUSE_NAME);
    }

    private void RestoreEnvironment()
    {
        if (!_isEnvironmentApplied)
            return;

        _isEnvironmentApplied = false;

        CameraInputBlockRegistry.Unregister(this);

        Application.targetFrameRate = _originalTargetFrameRate;
        QualitySettings.vSyncCount = _originalVSyncCount;

        foreach (Behaviour behaviour in _suppressedBackgroundBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = true;
        }

        _suppressedBackgroundBehaviours.Clear();

        foreach (GameObject suppressed in _suppressedBackgroundObjects)
        {
            if (suppressed != null)
                suppressed.SetActive(true);
        }

        _suppressedBackgroundObjects.Clear();

        if (_virtualMouse != null)
        {
            InputSystem.RemoveDevice(_virtualMouse);
            _virtualMouse = null;
        }

        foreach (InputDevice device in _disabledDevices)
        {
            if (device != null)
                InputSystem.EnableDevice(device);
        }

        _disabledDevices.Clear();

        if (_benchRoot != null)
            Destroy(_benchRoot.gameObject);
    }

    // ------------------------------------------------------------------
    // 경로
    // ------------------------------------------------------------------

    private void CaptureCameraBaseline()
    {
        _cameraStartPosition = _camera.transform.position;
        _cameraStartRotation = _camera.transform.rotation;
        _cameraStartSize = _camera.orthographicSize;
    }

    /// <summary>
    /// 점령 후보가 가장 많이 모인 <b>청크 행</b>을 화면에 담도록 카메라를 옮기고 줌을 맞춘다.
    ///
    /// 필요한 이유: 스냅샷의 기본 카메라(ortho 5, 성 중심)는 후보 땅 셀 1,331개 중 49개만 비추고
    /// 그 49개가 한 청크 반쯤에 몰려 있어, 경로가 <b>한두 청크 안에서만</b> 움직인다. 그러면 T1은
    /// 해석된 청크가 거의 안 바뀌어 다시 그리지 않고, S0는 매 프레임 다시 그린다 -
    /// 전환 비용이 통째로 빠진 채 비교되어 §2-3이 경고한 왜곡이 그대로 일어난다.
    ///
    /// 줌은 두 변형의 <b>계산량</b> 비교를 왜곡하지 않는다 - 분류·후보 선·틴트는 화면 보임과 무관하게
    /// 점령 가능 청크 전체를 돌고, S0도 청크 하나의 셀 전부를 칠한다. 늘어나는 것은 렌더링·배치 수이며
    /// 두 변형에 똑같이 붙고 Batches/SetPass는 별도 지표다. 결정한 pose는 보고서에 남긴다.
    /// </summary>
    private void FrameCameraOnCandidateBand()
    {
        if (!BenchConfig.AutoFrameCandidates)
            return;

        var chunksByRow = new Dictionary<int, List<Chunk>>();

        foreach (Chunk chunk in _gridMap.GetAllChunks())
        {
            if (chunk.CurrentState != ChunkState.Visible
                || !_conquestManager.HasExpeditionCost(chunk.ChunkCoord)
                || !_conquestManager.CanSendExpedition(chunk.ChunkCoord))
                continue;

            if (!chunksByRow.TryGetValue(chunk.ChunkCoord.y, out List<Chunk> row))
            {
                row = new List<Chunk>();
                chunksByRow[chunk.ChunkCoord.y] = row;
            }

            row.Add(chunk);
        }

        List<Chunk> bestRow = null;
        foreach (KeyValuePair<int, List<Chunk>> pair in chunksByRow)
        {
            if (bestRow == null || pair.Value.Count > bestRow.Count)
                bestRow = pair.Value;
        }

        if (bestRow == null || bestRow.Count == 0)
        {
            _report.MarkInvalid("카메라 프레이밍 - 점령 가능 청크를 찾지 못했다.");
            return;
        }

        var bounds = new Bounds(_gridMap.ConvertGridToWorld(FirstLandCell(bestRow[0])), Vector3.zero);

        foreach (Chunk chunk in bestRow)
        {
            foreach (Vector3Int cellCoord in chunk.LandCellCoords)
            {
                bounds.Encapsulate(_gridMap.ConvertGridToWorld(cellCoord));
            }
        }

        // 세로는 높이의 절반, 가로는 화면비로 나눈 절반이 orthographicSize다. 둘 중 큰 쪽을 쓴다.
        float halfHeight = bounds.extents.y + FRAMING_MARGIN_WORLD;
        float halfWidthAsSize = (bounds.extents.x + FRAMING_MARGIN_WORLD) / Mathf.Max(0.01f, _camera.aspect);
        float size = Mathf.Max(halfHeight, halfWidthAsSize);

        Vector3 cameraPosition = _camera.transform.position;
        cameraPosition.x = bounds.center.x;
        cameraPosition.y = bounds.center.y;

        _camera.transform.position = cameraPosition;
        _camera.orthographicSize = size;

        _report.FramedChunkRow = bestRow[0].ChunkCoord.y;
        _report.FramedChunkCount = bestRow.Count;
    }

    private static Vector3Int FirstLandCell(Chunk chunk)
    {
        foreach (Vector3Int cellCoord in chunk.LandCellCoords)
        {
            return cellCoord;
        }

        return default;
    }

    /// <summary>
    /// 호버 경로를 만든다 - 점령 가능 청크(와 그 편입 후보)의 땅 셀 중 화면 안에 들어오는 것만 모아,
    /// 셀이 가장 많은 행을 골라 x 순으로 늘어놓는다. 시나리오 B~D는 이 경로를 왕복한다.
    /// 경로 자체는 summary에 좌표로 남겨 재현 가능하게 한다.
    /// </summary>
    private void BuildHoverPath()
    {
        var cellsByRow = new Dictionary<int, List<Vector3Int>>();
        int skippedOffScreen = 0;
        int skippedNotPickable = 0;

        foreach (Chunk chunk in _gridMap.GetAllChunks())
        {
            if (chunk.CurrentState != ChunkState.Visible)
                continue;

            if (!_conquestManager.HasExpeditionCost(chunk.ChunkCoord))
                continue;

            foreach (Vector3Int cellCoord in chunk.LandCellCoords)
            {
                Vector3 screenPoint = CellToScreenPoint(cellCoord);

                if (!IsOnScreen(screenPoint))
                {
                    skippedOffScreen++;
                    continue;
                }

                // 그 점을 겨냥했을 때 실제로 이 셀이 읽히는지 미리 확인한다.
                // 더 높은 이웃 타일이 이 셀의 중심점을 덮고 있으면 PickCellAtWorldPoint가 높은 쪽을 고르므로
                // 플레이어도 그 지점으로는 이 셀을 겨냥할 수 없다 - 경로에 넣으면 §2-2-1의 입력 대응이
                // 영원히 깨진다. 실제로 셀 (-7,-18)이 (-8,-19)에 가려져 D 시나리오를 무효로 만들었다.
                if (!TryRoundTripCell(cellCoord, screenPoint))
                {
                    skippedNotPickable++;
                    continue;
                }

                if (!cellsByRow.TryGetValue(cellCoord.y, out List<Vector3Int> row))
                {
                    row = new List<Vector3Int>();
                    cellsByRow[cellCoord.y] = row;
                }

                row.Add(cellCoord);
            }
        }

        List<Vector3Int> bestRow = null;
        foreach (KeyValuePair<int, List<Vector3Int>> pair in cellsByRow)
        {
            if (bestRow == null || pair.Value.Count > bestRow.Count)
                bestRow = pair.Value;
        }

        if (bestRow != null)
        {
            bestRow.Sort((left, right) => left.x.CompareTo(right.x));
            _hoverPath.AddRange(bestRow);
        }

        _report.PathCells = _hoverPath;
        _report.OffScreenCellsSkipped = skippedOffScreen;
        _report.NotPickableCellsSkipped = skippedNotPickable;

        // 경로가 짧을 때 원인을 가리기 위해 화면 안 셀 수와 행 분포를 함께 남긴다 -
        // "해상도가 낮아서"인지 "카메라가 좁게 잡아서"인지는 이 둘로 갈린다.
        int onScreen = 0;
        foreach (KeyValuePair<int, List<Vector3Int>> pair in cellsByRow)
        {
            onScreen += pair.Value.Count;
        }

        _report.OnScreenCandidateCells = onScreen;
        _report.CandidateRowCount = cellsByRow.Count;
        _report.PathDistinctChunkCount = CountDistinctChunks(_hoverPath);
        _report.CameraPosition = _camera.transform.position;
        _report.CameraOrthographicSize = _camera.orthographicSize;
    }

    /// <summary>
    /// 셀을 화면 좌표로 옮긴다. <c>MouseSelectController.GetHoveredCell</c>이 화면점을 월드로 바꾼 뒤
    /// y에서 YOffset을 빼고 <c>PickCellAtWorldPoint</c>에 넘기므로, 그 역순으로 YOffset을 더해 준다.
    /// 실제로 의도한 셀이 읽히는지는 프레임마다 기록해 M3에서 100% 일치를 확인한다(§2-2-1).
    /// </summary>
    private Vector3 CellToScreenPoint(Vector3Int cellCoord)
    {
        Vector3 worldPoint = _gridMap.ConvertGridToWorld(cellCoord);
        worldPoint.y += _mouseSelectController.YOffset;
        return _camera.WorldToScreenPoint(worldPoint);
    }

    /// <summary>
    /// 이 셀의 화면 좌표를 다시 셀로 되돌렸을 때 자기 자신이 나오는지 확인한다.
    /// <see cref="MouseSelectController.GetHoveredCell"/>과 <b>같은 경로</b>로 계산해야 의미가 있다 -
    /// 화면점 → 월드(카메라) → y에서 YOffset 빼기 → PickCellAtWorldPoint.
    /// </summary>
    private bool TryRoundTripCell(Vector3Int cellCoord, Vector3 screenPoint)
    {
        Vector3 worldPoint = _camera.ScreenToWorldPoint(screenPoint);
        worldPoint.z = 0f;
        worldPoint.y -= _mouseSelectController.YOffset;

        return _gridMap.PickCellAtWorldPoint(worldPoint) == cellCoord;
    }

    private static bool IsOnScreen(Vector3 screenPoint)
    {
        float marginX = Screen.width * SCREEN_MARGIN_RATIO;
        float marginY = Screen.height * SCREEN_MARGIN_RATIO;

        return screenPoint.x >= marginX && screenPoint.x <= Screen.width - marginX
            && screenPoint.y >= marginY && screenPoint.y <= Screen.height - marginY;
    }

    /// <summary>
    /// 경로가 지나는 서로 다른 청크 수. <b>경로 길이보다 이 값이 중요하다</b> -
    /// T1은 해석된 <b>청크</b>가 바뀔 때만 다시 그리고 S0는 매 프레임 다시 그리므로,
    /// 한 청크 안에서만 움직이는 경로는 T1의 전환 비용을 0으로 만들어 비교를 T1에 유리하게 왜곡한다(§2-3).
    /// </summary>
    private int CountDistinctChunks(List<Vector3Int> cells)
    {
        var chunkCoords = new HashSet<Vector2Int>();

        foreach (Vector3Int cell in cells)
        {
            Chunk chunk = _gridMap.GetChunkAt(cell);

            if (chunk != null)
                chunkCoords.Add(chunk.ChunkCoord);
        }

        return chunkCoords.Count;
    }

    /// <summary>워밍업 전용 - 시나리오를 보지 않고 경로를 한 칸씩 왕복한다.</summary>
    private Vector3Int ResolveWarmupCell(int frameIndex) => _hoverPath[PingPongIndex(frameIndex)];

    private int PingPongIndex(int step)
    {
        int span = Mathf.Max(1, _hoverPath.Count - 1);
        int cycle = step % (span * 2);
        return cycle < span ? cycle : span * 2 - cycle;
    }

    /// <summary>프레임 인덱스로 이번 프레임의 목표 셀을 고른다 - 시간이 아니라 프레임으로 계산한다(§2-1).</summary>
    private Vector3Int ResolveIntendedCell(int frameIndex)
    {
        if (BenchConfig.Scenario == BenchScenario.A || _hoverPath.Count == 1)
            return _hoverPath[_hoverPath.Count / 2];

        int cellsPerReferenceSecond = BenchConfig.Scenario switch
        {
            BenchScenario.B => 5,
            BenchScenario.C => 15,
            BenchScenario.D => 40,
            _ => 0,
        };

        if (cellsPerReferenceSecond == 0)
            return _hoverPath[_hoverPath.Count / 2];

        int step = frameIndex * cellsPerReferenceSecond / BenchConfig.REFERENCE_FRAMES_PER_SECOND;

        // 왕복 - 경로 끝에서 되돌아온다.
        return _hoverPath[PingPongIndex(step)];
    }

    // ------------------------------------------------------------------
    // 변형 활성화·워밍업
    // ------------------------------------------------------------------

    private async UniTask<bool> TryActivateVariantAsync(CancellationToken cancellationToken)
    {
        if (BenchConfig.IsHarnessOnly)
            return true;

        switch (BenchConfig.Variant)
        {
            case BenchVariant.T1:
                // E-재진입은 "꺼진 상태에서 켜는 것" 자체가 측정 대상이다 - 여기서 켜 두면
                // 측정 시점에 이미 켜져 있어 모든 런이 무효가 된다.
                if (BenchConfig.Scenario != BenchScenario.EReenter)
                    _conquestModeController.SetConquestModeActive(true);

                break;

            case BenchVariant.S0:
                // T1의 표시가 남아 있으면 두 구현이 섞인다 - 모드를 끈 상태를 명시적으로 만든다.
                _conquestModeController.SetConquestModeActive(false);

                _legacyDriver = _benchRoot.gameObject.AddComponent<LegacyConquestHighlightDriver>();
                if (!_legacyDriver.Initialize(_gridMap, _mouseSelectController, _conquestManager, _benchRoot))
                {
                    Fail("S0 드라이버 초기화 실패.");
                    return false;
                }

                break;

            case BenchVariant.S1p:
                Fail("S1′는 M7 선택 항목이며 아직 구현하지 않았다.");
                return false;
        }

        await UniTask.NextFrame(cancellationToken);
        return true;
    }

    private void TeardownVariant()
    {
        if (_legacyDriver != null)
            _legacyDriver.Teardown();

        if (_conquestModeController != null && _conquestModeController.IsActive)
            _conquestModeController.SetConquestModeActive(false);
    }

    /// <summary>
    /// 워밍업 - 최소 프레임 수에 더해 측정 경로 전체와 최대 풀 용량을 한 번씩 거치게 한다(§2-1).
    /// 이 구간의 첫 진입·풀 확장 비용은 측정에 섞지 않고 보고서에 따로 남긴다.
    /// </summary>
    private async UniTask WarmupAsync(CancellationToken cancellationToken)
    {
        _phase = Phase.Warmup;

        int pathSweepFrames = _hoverPath.Count * 2;
        int totalWarmupFrames = Mathf.Max(BenchConfig.WarmupFrames, pathSweepFrames);

        _report.WarmupFrames = totalWarmupFrames;
        _report.WarmupStartTicks = DateTime.UtcNow.Ticks;

        // E-재진입은 측정 시점에 모드가 꺼져 있어야 하지만, 워밍업은 켠 채로 돈다 -
        // 그러지 않으면 측정하는 재진입이 후보 선·틴트 풀을 처음 만드는 비용까지 안게 된다(§2-1).
        // 그래서 이 런의 재진입은 "한 번도 켠 적 없는 첫 진입"이 아니다. 보고서에 그대로 남긴다.
        bool warmsWithModeToggle = BenchConfig.Scenario == BenchScenario.EReenter
                                   && BenchConfig.Variant == BenchVariant.T1
                                   && !BenchConfig.IsHarnessOnly;

        if (warmsWithModeToggle)
            _conquestModeController.SetConquestModeActive(true);

        await UniTask.WaitUntil(() => _warmupFrameCount >= totalWarmupFrames, cancellationToken: cancellationToken);

        if (warmsWithModeToggle)
            _conquestModeController.SetConquestModeActive(false);

        _report.WarmupTogglesMode = warmsWithModeToggle;
        _report.WarmupEndTicks = DateTime.UtcNow.Ticks;
        _phase = Phase.Preparing;
    }

    private async UniTask WaitFramesAsync(int frameCount, CancellationToken cancellationToken)
    {
        for (int i = 0; i < frameCount; i++)
        {
            await UniTask.NextFrame(cancellationToken);
        }
    }

    // ------------------------------------------------------------------
    // 프레임 루프
    // ------------------------------------------------------------------

    private int MeasureFrameCapacity => IsEventScenario
        ? BenchConfig.E_WINDOW_FRAMES
        : BenchConfig.MeasureFrames;

    private bool IsEventScenario =>
        BenchConfig.Scenario == BenchScenario.EDepart || BenchConfig.Scenario == BenchScenario.EReenter;

    private void Update()
    {
        switch (_phase)
        {
            case Phase.Warmup:
                // 시나리오와 무관하게 경로 전체를 왕복한다 - A나 E처럼 한 칸에 머무는 시나리오에서도
                // 풀이 최대 크기까지 자라고 경로의 모든 셀을 한 번은 거쳐야 한다(§2-1).
                MoveVirtualMouseToCell(ResolveWarmupCell(_warmupFrameCount));
                _warmupFrameCount++;
                break;

            case Phase.Measuring:
                TickMeasuringFrame();
                break;
        }
    }

    private void TickMeasuringFrame()
    {
        if (_measureStartFrame < 0)
        {
            _measureStartFrame = Time.frameCount;
            _intendedCells = new Vector3Int[MeasureFrameCapacity];
            _actualCells = new Vector3Int[MeasureFrameCapacity];
            _isMouseCurrentValid = new bool[MeasureFrameCapacity];
            _isInputChecked = new bool[MeasureFrameCapacity];
            _injectedPointer = new Vector2[MeasureFrameCapacity];
            _readPointer = new Vector2[MeasureFrameCapacity];
            _report.MeasureStartFrame = _measureStartFrame;
            _report.MeasureStartTicks = DateTime.UtcNow.Ticks;
        }

        _currentFrameIndex = Time.frameCount - _measureStartFrame;

        // 리코더 값은 직전 완료 프레임의 것이다 - 한 칸 앞에 넣는다(§3-2-4).
        _recorders.Sample(
            _currentFrameIndex - 1,
            Time.frameCount,
            (long)(Time.unscaledDeltaTime * 1e9f));

        _measuredFrameCount = _currentFrameIndex + 1;

        if (_currentFrameIndex >= MeasureFrameCapacity)
            return;

        if (IsEventScenario)
        {
            TickEventFrame(_currentFrameIndex);
            return;
        }

        Vector3Int intendedCell = ResolveIntendedCell(_currentFrameIndex);
        _intendedCells[_currentFrameIndex] = intendedCell;
        _isInputChecked[_currentFrameIndex] = true;
        MoveVirtualMouseToCell(intendedCell);
        TryCaptureVerificationScreenshot(_currentFrameIndex);
    }

    /// <summary>
    /// 표시 정확성을 눈으로 확인하기 위한 전용 캡처. 파일 쓰기가 측정 프레임에 섞이므로
    /// 본 측정에서는 끈다(<see cref="BenchConfig.ScreenshotFrame"/> 기본값 -1).
    /// </summary>
    private void TryCaptureVerificationScreenshot(int frameIndex)
    {
        if (BenchConfig.ScreenshotFrame < 0 || frameIndex != BenchConfig.ScreenshotFrame)
            return;

        string directory = Path.Combine(BenchConfig.AbsoluteOutputRoot, SCREENSHOT_DIRECTORY);
        Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, BenchConfig.RunId + ".png"));
    }

    private void LateUpdate()
    {
        if (_phase != Phase.Measuring || _currentFrameIndex < 0 || _currentFrameIndex >= MeasureFrameCapacity)
            return;

        // 측정 대상이 이번 프레임에 실제로 읽은 셀 - 의도한 셀과 시퀀스가 100% 같아야 한다(§2-2-1).
        _actualCells[_currentFrameIndex] = _mouseSelectController.GetHoveredCell();
        _isMouseCurrentValid[_currentFrameIndex] = ReferenceEquals(Mouse.current, _virtualMouse);
        _readPointer[_currentFrameIndex] =
            Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        CheckCameraDrift();
    }

    /// <summary>
    /// §2-2-1의 입력 충실도 관문. 의도 셀과 실제 읽은 셀은 <b>전부</b> 일치해야 하며
    /// 허용 비율로 넘기지 않는다 - 하나라도 어긋나면 이 런은 무효다.
    /// 무효 런도 버리지 않고 invalid_ 접두어로 보존한다(§2-4).
    /// </summary>
    private void ValidateInputCorrespondence()
    {
        if (_intendedCells == null || _actualCells == null)
            return;

        int checkedFrames = 0;
        int mismatched = 0;
        int firstMismatchIndex = -1;

        for (int i = 0; i < _intendedCells.Length; i++)
        {
            if (_isInputChecked != null && i < _isInputChecked.Length && !_isInputChecked[i])
                continue;

            checkedFrames++;

            if (_intendedCells[i] == _actualCells[i])
                continue;

            mismatched++;

            if (firstMismatchIndex < 0)
                firstMismatchIndex = i;
        }

        if (BenchConfig.Scenario == BenchScenario.EDepart && _recorders != null)
        {
            int conquerUiIndex = System.Array.IndexOf(_recorders.MarkerNames, BenchMarkerNames.T1_CONQUER_UI);
            bool fired = false;

            if (conquerUiIndex >= 0)
            {
                long[] values = _recorders.MarkerTimeNs[conquerUiIndex];

                for (int i = 0; i < values.Length && !fired; i++)
                {
                    fired = values[i] > 0;
                }
            }

            if (!fired)
            {
                _report.MarkInvalid(
                    "E-출발인데 OnConquerButtonClicked 마커가 한 번도 발화하지 않았다 - 버튼 클릭이 실제로 전달되지 않았다.");
            }
        }

        if (mismatched > 0)
        {
            _report.MarkInvalid(
                $"입력 대응 불일치 {mismatched}/{checkedFrames}프레임 (첫 불일치 frameIndex={firstMismatchIndex})");
        }

        ValidatePointerInjection();

        int nonVirtualMouseFrames = 0;

        for (int i = 0; _isMouseCurrentValid != null && i < _isMouseCurrentValid.Length; i++)
        {
            if (!_isMouseCurrentValid[i])
                nonVirtualMouseFrames++;
        }

        if (nonVirtualMouseFrames > 0)
            _report.MarkInvalid($"가상 마우스가 아닌 프레임 {nonVirtualMouseFrames}개");
    }

    /// <summary>
    /// 주입한 화면 좌표가 그 프레임에 실제로 장치에서 읽혔는지 확인한다.
    ///
    /// 에디터가 뒤로 밀리면 플레이 루프가 사실상 멈춰 <b>주입이 반영되지 않고 읽힌 좌표가 얼어붙는다.</b>
    /// 실제로 M4의 `S0_D_run3`이 1710프레임부터 그렇게 되어 마지막 90프레임이 어긋났다.
    /// 프레임 시간 평균으로 보는 <c>CheckStalledRun</c>은 런의 일부만 멈춘 경우를 놓치므로,
    /// 주입 ↔ 읽힘을 프레임마다 직접 대조한다. 정상 런에서는 둘이 정확히 일치한다.
    /// </summary>
    private void ValidatePointerInjection()
    {
        if (_injectedPointer == null || _readPointer == null)
            return;

        int notApplied = 0;
        int firstIndex = -1;

        for (int i = 0; i < _injectedPointer.Length; i++)
        {
            // 주입하지 않은 프레임(E-출발의 버튼 구간)은 대조 대상이 아니다.
            if (_isInputChecked != null && i < _isInputChecked.Length && !_isInputChecked[i])
                continue;

            if (_injectedPointer[i] == _readPointer[i])
                continue;

            notApplied++;

            if (firstIndex < 0)
                firstIndex = i;
        }

        if (notApplied > 0)
        {
            _report.MarkInvalid(
                $"주입한 포인터가 반영되지 않은 프레임 {notApplied}개 (첫 frameIndex={firstIndex}) " +
                "- 측정 중 에디터가 앞에 있지 않았다");
        }
    }

    private void CheckCameraDrift()
    {
        if (_hasCameraDrift)
            return;

        if (_camera.transform.position != _cameraStartPosition
            || _camera.transform.rotation != _cameraStartRotation
            || !Mathf.Approximately(_camera.orthographicSize, _cameraStartSize))
        {
            _hasCameraDrift = true;
            _report.MarkInvalid($"카메라 이동 감지(frameIndex={_currentFrameIndex})");
        }
    }

    private void MoveVirtualMouseToCell(Vector3Int cellCoord)
    {
        if (_virtualMouse == null)
            return;

        Vector3 screenPoint = CellToScreenPoint(cellCoord);

        if (_phase == Phase.Measuring && _currentFrameIndex >= 0 && _currentFrameIndex < MeasureFrameCapacity)
            _injectedPointer[_currentFrameIndex] = new Vector2(screenPoint.x, screenPoint.y);

        // 큐에 넣으면 다음 프레임 Update 앞에서 처리된다 - 같은 프레임에 보이도록 상태를 직접 바꾼다.
        // 그래도 한 프레임 어긋남이 없는지는 의도/실제 셀 기록으로 M3에서 확인한다.
        InputState.Change(_virtualMouse.position, new Vector2(screenPoint.x, screenPoint.y));
    }

    // ------------------------------------------------------------------
    // E 시나리오
    // ------------------------------------------------------------------

    private void TickEventFrame(int frameIndex)
    {
        // 전이 전 프레임은 대상 청크 위에 커서를 고정해 기준선을 만든다.
        Vector3Int targetCell = _hoverPath[_hoverPath.Count / 2];
        _intendedCells[frameIndex] = targetCell;

        // E-출발은 전이 뒤 커서가 버튼 위에 있어야 한다 - 매 프레임 청크로 되돌리지 않는다.
        bool holdsCursorOnChunk = BenchConfig.Scenario != BenchScenario.EDepart
                                  || frameIndex < BenchConfig.E_TRANSITION_FRAME_INDEX;

        _isInputChecked[frameIndex] = holdsCursorOnChunk;

        if (holdsCursorOnChunk)
            MoveVirtualMouseToCell(targetCell);

        if (BenchConfig.Scenario == BenchScenario.EReenter)
        {
            if (frameIndex == BenchConfig.E_TRANSITION_FRAME_INDEX)
            {
                _report.TransitionFrameIndex = frameIndex;
                TriggerReentry();
            }

            return;
        }

        TickDepartureSequence(frameIndex);
    }

    /// <summary>
    /// E-출발은 한 프레임에 끝나지 않는다 - 패널을 열고, 버튼 위로 커서를 옮기고, 누르고, 뗀다.
    /// 입력 이벤트는 큐에 들어가 <b>다음 프레임</b>에 처리되고 UI는 누름과 뗌 사이에 프레임이 필요하므로
    /// 단계마다 프레임을 띄운다. 실제 프레임 대응은 M3에서 확인한다.
    /// </summary>
    private void TickDepartureSequence(int frameIndex)
    {
        int transitionFrame = BenchConfig.E_TRANSITION_FRAME_INDEX;

        if (frameIndex == transitionFrame)
        {
            _report.TransitionFrameIndex = frameIndex;
            OpenConquestPanelForDeparture();
            return;
        }

        if (frameIndex == transitionFrame + PANEL_OPEN_FRAMES)
        {
            PressConquerButton();
            return;
        }

        if (frameIndex == transitionFrame + PANEL_OPEN_FRAMES + BUTTON_HOLD_FRAMES)
        {
            ReleaseConquerButton();
        }
    }

    /// <summary>
    /// E-출발 1단계 - 대상 청크를 선택해 패널을 연다. 매니저 직접 호출은 금지다(§2-1 E).
    /// </summary>
    private void OpenConquestPanelForDeparture()
    {
        Chunk chunk = _gridMap.GetChunkAt(_hoverPath[_hoverPath.Count / 2]);
        if (chunk == null || _conquestWindow == null)
        {
            _report.MarkInvalid("E-출발 대상 청크 또는 점령 창을 찾지 못했다.");
            return;
        }

        _report.EButtonMode = BenchConfig.EUsesRealClick ? REAL_CLICK_MODE_NAME : ON_CLICK_INVOKE_MODE_NAME;
        _report.ETargetChunk = chunk.ChunkCoord;
        _conquestWindow.OnChunkSelected(chunk.ChunkCoord);
        _report.EPanelOpenedAfterSelect = _conquestWindow.HasSelectedChunk;
    }

    /// <summary>E-출발 2단계 - 버튼 위로 커서를 옮기고 누른다(또는 onClick을 직접 부른다).</summary>
    private void PressConquerButton()
    {
        Button conquerButton = ResolveConquerButton(_conquestWindow);
        if (conquerButton == null)
        {
            _report.MarkInvalid("점령 버튼 참조를 찾지 못했다.");
            return;
        }

        if (!conquerButton.interactable)
        {
            _report.MarkInvalid("점령 버튼이 비활성이다 - 스냅샷의 자원·인구가 이 청크의 비용에 못 미친다.");
            return;
        }

        if (!BenchConfig.EUsesRealClick)
        {
            conquerButton.onClick.Invoke();
            return;
        }

        var rectTransform = conquerButton.transform as RectTransform;
        if (rectTransform == null)
        {
            _report.MarkInvalid("점령 버튼의 RectTransform을 찾지 못했다.");
            return;
        }

        _buttonScreenPosition = ResolveButtonScreenPosition(rectTransform);
        _report.EButtonScreenPosition = _buttonScreenPosition;
        _report.EButtonInteractable = true;
        QueueMouseState(_buttonScreenPosition, true);
    }

    private void ReleaseConquerButton()
    {
        if (BenchConfig.EUsesRealClick)
            QueueMouseState(_buttonScreenPosition, false);
    }

    /// <summary>
    /// 버튼 누름/뗌은 <c>InputState.Change</c>로 만질 수 없다 - 버튼은 비트필드 컨트롤이라
    /// "Cannot change state of bitfield control" 예외가 난다(E-출발 첫 스모크 런에서 실제로 났다).
    /// 마우스 상태 전체를 이벤트로 큐에 넣는다. 큐된 이벤트는 다음 프레임 Update 앞에서 처리된다.
    /// </summary>
    private void QueueMouseState(Vector2 screenPosition, bool isLeftButtonPressed)
    {
        var state = new MouseState { position = screenPosition };
        state = state.WithButton(MouseButton.Left, isLeftButtonPressed);
        InputSystem.QueueStateEvent(_virtualMouse, state);
    }

    private Vector2 ResolveButtonScreenPosition(RectTransform rectTransform)
    {
        Canvas canvas = rectTransform.GetComponentInParent<Canvas>();

        // Overlay 캔버스는 이미 화면 좌표라 카메라를 넘기면 안 된다.
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        return RectTransformUtility.WorldToScreenPoint(uiCamera, rectTransform.position);
    }

    /// <summary>
    /// E-재진입 - 완료된 영토 상태에서 점령 모드를 다시 켤 때의 갱신을 잰다.
    /// 완료 상태 준비(DebugForceCompleteAllExpeditions)는 <b>측정 밖에서</b> 끝나 있어야 한다(§2-1 E).
    /// </summary>
    private void TriggerReentry()
    {
        if (_conquestModeController.IsActive)
        {
            _report.MarkInvalid("E-재진입인데 점령 모드가 이미 켜져 있다.");
            return;
        }

        _conquestModeController.SetConquestModeActive(true);
    }

    private static Button ResolveConquerButton(UI_ConquestWindow window)
    {
        FieldInfo field = typeof(UI_ConquestWindow).GetField(
            "_conquerButton",
            BindingFlags.Instance | BindingFlags.NonPublic);

        return field?.GetValue(window) as Button;
    }

    /// <summary>
    /// E-재진입용 완료 영토 상태를 <b>측정 밖에서</b> 만든다(§2-1 E).
    ///
    /// 스냅샷에 활성 원정이 정확히 하나만 있어야 한다 - <c>DebugForceCompleteAllExpeditions()</c>는
    /// 일수와 무관하게 <b>모든</b> 활성 원정을 완료시키기 때문이다. 하나가 아니면 무효로 표시한다.
    /// 이 디버그 경로가 정상 정산과 같다고 주장하지 않는다. 전이 전후 상태를 둘 다 기록해 대조할 수 있게 한다.
    /// </summary>
    private async UniTask<bool> TryPrepareCompletedTerritoryAsync(CancellationToken cancellationToken)
    {
        if (BenchConfig.Scenario != BenchScenario.EReenter || BenchConfig.IsHarnessOnly)
            return true;

        if (_conquestModeController.IsActive)
        {
            Fail("E-재진입 준비 - 스냅샷에서 점령 모드가 이미 켜져 있다.");
            return false;
        }

        _report.CaptureTerritoryBefore(_gridMap, _conquestManager);

        if (_conquestManager.ActiveExpeditions.Count != 1)
        {
            Fail($"E-재진입 준비 - 활성 원정이 {_conquestManager.ActiveExpeditions.Count}개다. " +
                 "스냅샷에 정확히 하나만 있어야 의도한 하나만 완료된다.");
            return false;
        }

        _conquestManager.DebugForceCompleteAllExpeditions();

        // 완료가 촉발한 비동기 표시 작업(안개·테두리·마커)이 가라앉은 뒤에 경로를 만들고 측정한다.
        await WaitFramesAsync(SETTLE_FRAMES * 2, cancellationToken);

        _report.CaptureTerritoryAfter(_gridMap, _conquestManager);

        if (_conquestManager.ActiveExpeditions.Count != 0)
        {
            Fail("E-재진입 준비 - 완료 후에도 활성 원정이 남아 있다.");
            return false;
        }

        Debug.Log("[BENCH] E-재진입 완료 상태 준비됨 - " + _report.DescribeTerritoryTransition());
        return true;
    }

    // ------------------------------------------------------------------
    // 프로파일러 raw
    // ------------------------------------------------------------------

    private void StartProfilerCapture()
    {
        string rawDirectory = Path.Combine(BenchConfig.AbsoluteOutputRoot, "raw");
        Directory.CreateDirectory(rawDirectory);

        _originalLogFile = Profiler.logFile;
        _originalBinaryLog = Profiler.enableBinaryLog;

        // 확장자는 Unity가 .raw로 붙인다.
        string logPath = Path.Combine(rawDirectory, BenchConfig.RunId);

        Profiler.logFile = logPath;
        Profiler.enableBinaryLog = true;
        Profiler.enabled = true;

        _isCaptureStarted = true;
        _report.RawLogPath = logPath + ".raw";
    }

    private void StopProfilerCapture()
    {
        if (!_isCaptureStarted)
            return;

        _isCaptureStarted = false;

        Profiler.enabled = false;
        Profiler.enableBinaryLog = _originalBinaryLog;
        Profiler.logFile = _originalLogFile;
    }

    // ------------------------------------------------------------------
    // 보고
    // ------------------------------------------------------------------

    private void Fail(string reason)
    {
        _report.MarkInvalid(reason);
        Debug.LogError($"[BENCH] {reason}");
        TryWriteFailureReport();
        RequestExitPlayModeIfBatched();
    }

    /// <summary>
    /// 배치 러너가 다음 런을 이어 붙일 수 있도록 플레이 모드를 끝낸다.
    /// 한 런 = 한 플레이 세션이어야 하므로(§2-4) 씬 리로드가 아니라 플레이를 실제로 끝낸다.
    /// </summary>
    private void RequestExitPlayModeIfBatched()
    {
#if UNITY_EDITOR
        if (BenchConfig.ExitPlayModeWhenDone)
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void TryWriteFailureReport()
    {
        try
        {
            _report.EndRun(this, _recorders);
            BenchOutputWriter.Write(_report, _recorders, _intendedCells, _actualCells, _isMouseCurrentValid, _isInputChecked,
                _injectedPointer, _readPointer);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    internal bool HasCameraDrift => _hasCameraDrift;
    internal LegacyConquestHighlightDriver LegacyDriver => _legacyDriver;
    internal ConquestModeController ModeController => _conquestModeController;
    internal Camera MeasurementCamera => _camera;
}
