using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using DG.Tweening;

// 점령 정보 패널. 점령 모드 진입/청크 클릭은 ConquestModeController가 담당하고,
// 이 창은 선택된 청크의 정보 표시(비용/보상/기간/지형/적 강화)와 점령 실행만 맡는다.
// 점령 모드 버튼은 UI_IngameWindow 등 외부에서 ToggleConquestMode()를 호출해 연결한다.
// 기간/지형 슬롯은 모든 청크에 항상 존재하므로 씬에 미리 고정 배치해 두고 값만 갱신하고,
// 자원 비용/보상/적 강화 슬롯은 청크마다 항목이 달라 매번 새로 생성한다(건물 슬롯과 동일 패턴).
// 자원 아이콘은 수동 배열 대신 ResourceCatalog(ResourceData.Icon)에서 조회한다.
public class UI_ConquestWindow : MonoBehaviour
{
    private const string HELD_OVER_REQUIRED_FORMAT = "{0}/{1}";
    private const string PLUS_VALUE_FORMAT = "+{0}";

    // 소요 기간 표기는 UI_ClaimListSlot 등과 동일한 스트링테이블 키를 공유한다.
    // 코드베이스 관례상 일수는 단/복수 분기 없이 "{0} 일" / "{0} Day(s)" 한 형태로 쓴다.
    private const string DURATION_LABEL_LOC_KEY = "claim_panel_duration";
    private const string DURATION_DAYS_FORMAT_LOC_KEY = "claim_panel_duration_time";

    // 점령 시 적 강화는 현재 "특정 몹 스폰 수 +N" 하나뿐이라, 몹 이름과 증가량을 한 줄로 합친다.
    // 예: "{0} +{1}" → "소환사 +1".
    private const string ENEMY_SPAWN_BONUS_FORMAT_LOC_KEY = "conquest_panel_enemy_spawn_bonus";

    private static string DurationLabel => StringTable.GetString(DURATION_LABEL_LOC_KEY);

    [SerializeField]
    private Button _conquerButton;

    [SerializeField]
    private GameObject _conquestModePanel;

    [Tooltip("자원 비용 슬롯 프리팹(Slot_CostToConquer).")]
    [SerializeField]
    private UI_ResourceCostSlot _resourceCostSlotPrefab;

    [Tooltip("생성된 자원 비용 슬롯이 들어갈 부모.")]
    [SerializeField]
    private Transform _resourceSlotContainer;

    private readonly List<UI_ResourceCostSlot> _spawnedResourceSlots = new();

    [Header("보상(해금 자원 종류) 슬롯 - 실제 지급 수량이 아닌 해금 표시용.")]
    [Tooltip("보상 슬롯 프리팹(Slot_ConquestReward).")]
    [SerializeField]
    private UI_ConquestRewardSlot _rewardSlotPrefab;

    [Tooltip("생성된 보상 슬롯이 들어갈 부모.")]
    [SerializeField]
    private Transform _rewardSlotContainer;

    [Tooltip("인구 아이콘(비용/보상 슬롯 공용). 인구는 자원이 아니라 ResourceData가 없어 별도 지정한다.")]
    [SerializeField]
    private Sprite _populationIcon;

    // 해금 표시 순서. 아이콘은 ResourceCatalog에서 종류로 조회한다.
    private static readonly ResourceType[] REWARD_RESOURCE_TYPES =
    {
        ResourceType.Food,
        ResourceType.Wood,
        ResourceType.Stone,
        ResourceType.FlameHeart,
        ResourceType.SnowCrystal,
        ResourceType.TimeSand,
        ResourceType.PhilosopherStone,
    };

    private readonly List<UI_ConquestRewardSlot> _spawnedRewardSlots = new();

    [Header("고정 정보 슬롯 (기간/지형) - 씬에 미리 배치된 인스턴스")]
    [Tooltip("소요 기간 슬롯 - 아이콘 + \"N Day(s)\" 텍스트.")]
    [SerializeField]
    private UI_ConquestInfoSlot _durationInfoSlot;

    [SerializeField]
    private Sprite _durationIcon;

    [Tooltip("지형 아이콘 이미지 - 텍스트 없이 지형에 맞는 스프라이트만 교체한다.")]
    [SerializeField]
    private Image _terrainImage;

    [Tooltip("지형 스프라이트. 인덱스는 TerrainType 선언 순서(Grass/Rock/Volcano/Desert/Snow/Default)와 일치해야 한다.")]
    [SerializeField]
    private Sprite[] _terrainSprites;

    [Header("몬스터 강화 효과 슬롯 - 적용되는 항목만 생성")]
    [Tooltip("몬스터 강화 효과 슬롯 프리팹(Slot__EnemyConquest).")]
    [SerializeField]
    private UI_ConquestInfoSlot _enemyScalingSlotPrefab;

    [Tooltip("생성된 몬스터 강화 효과 슬롯이 들어갈 부모.")]
    [SerializeField]
    private Transform _enemyScalingSlotContainer;

    [SerializeField]
    private Sprite _spawnCountIcon;

    private readonly List<UI_ConquestInfoSlot> _spawnedEnemyScalingSlots = new();

    private static readonly Color SUFFICIENT_COLOR_DEFAULT = new Color(0f, 0.6f, 0f);

    [SerializeField]
    private Color _sufficientColor = SUFFICIENT_COLOR_DEFAULT;

    [SerializeField]
    private Color _insufficientColor = Color.red;

    [SerializeField]
    private ConquestManager _conquestManager;

    [Tooltip("자원 보유량 관리자. 보유량 조회/차감과 아이콘 카탈로그 조회에 사용한다.")]
    [SerializeField]
    private ResourceManager _resourceManager;

    [Tooltip("인구 보유량 관리자 - 원정 인구 비용 충족 여부 확인용.")]
    [SerializeField]
    private PopulationManager _populationManager;

    [Tooltip("이 청크의 랜드마크를 보상 목록에 표시하기 위해 참조한다. 비워두면 랜드마크 행이 생략된다.")]
    [WiringOptional]
    [SerializeField]
    private LandmarkManager _landmarkManager;

    [SerializeField]
    private ConquestModeController _conquestModeController;

    [Tooltip("원정 발송 시 성에서 크루가 걸어 나오는 연출. 비워두면 연출만 생략된다.")]
    [SerializeField]
    private VillagerDispatchSystem _villagerDispatch;

    [SerializeField]
    private UIManager _uiManager;

    [Tooltip("밤이 시작되면 점령 모드를 자동으로 끈다.")]
    [SerializeField]
    private CycleManager _cycleManager;

    [Tooltip("밤에 점령을 시도했을 때 경고 메시지를 띄울 창.")]
    [SerializeField]
    private UI_WarningWindow _warningWindow;

    [Header("패널 열림/닫힘 연출")]
    [SerializeField]
    private float _slideDuration = 0.5f;

    [SerializeField]
    private Vector2 _openFromOffset = new Vector2(50f, 0f);

    [SerializeField]
    private Vector2 _closeToOffset = new Vector2(500f, 0f);

    private RectTransform _panelRect;
    private Vector2 _homePos;
    private Tween _panelTween;
    private Vector2Int? _selectedChunkCoord;

    private void Awake()
    {
        if (_conquerButton != null)
        {
            _conquerButton.onClick.AddListener(OnConquerButtonClicked);
        }

        _panelRect = _conquestModePanel.GetComponent<RectTransform>();
        _homePos = _panelRect.anchoredPosition;

        // OnEnable이 아닌 Awake에서 구독한다 → 패널(이 오브젝트)이 닫혀(SetActive false) 있어도,
        // 즉 점령 모드는 켜졌지만 패널은 안 열린 상태에서도 밤 이벤트를 받아 모드를 끌 수 있다.
        if (_cycleManager != null)
        {
            _cycleManager.OnNightStart.AddListener(HandleNightStart);
        }

        _conquestModePanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_cycleManager != null)
        {
            _cycleManager.OnNightStart.RemoveListener(HandleNightStart);
        }
    }

    private void OnEnable()
    {
        if (_conquestManager != null)
        {
            _conquestManager.OnConquestCompleted.AddListener(OnConquestCompleted);
        }

        // 언어가 바뀌면 이미 생성된 비용/보상/강화 슬롯이 이전 언어로 남으므로 선택된 청크로 다시 그린다.
        // 패널이 열려 있는 동안(= 이 오브젝트가 활성인 동안)만 유효하면 충분하다 - 닫힌 뒤 다시 열 때는
        // OnChunkSelected가 Refresh를 호출하므로 항상 현재 언어로 만들어진다.
        // 여기서 Refresh를 한 번 더 호출하지는 않는다 - OnChunkSelected가 OpenPanel 직후 이미 호출하므로
        // 패널을 열 때마다 슬롯 리빌드(Destroy/Instantiate)가 두 번 돌게 된다.
        StringTable.OnLanguageChanged += Refresh;
    }

    // 패널이 열려 있는 상태에서 점령 모드가 꺼지면 ConquestModeController.SetConquestModeActive(false)가
    // 알아서 패널을 닫는다 - 여기서 중복 구현하지 않는다.
    private void OnDisable()
    {
        if (_conquestManager != null)
        {
            _conquestManager.OnConquestCompleted.RemoveListener(OnConquestCompleted);
        }

        StringTable.OnLanguageChanged -= Refresh;
    }

    // 밤이 시작되면 점령 모드를 끈다(Claim 버튼을 토글해 끈 것과 동일 - 패널 닫힘 + 하이라이트 제거).
    private void HandleNightStart(int cycle)
    {
        if (_conquestModeController != null && _conquestModeController.IsActive)
        {
            _conquestModeController.SetConquestModeActive(false);
        }
    }

    // 원정이 실제로 완료된 시점(며칠 뒤 밤 정산)에 ConquestManager가 발행한다.
    // 인구 보상 지급과 원정 인구 반환은 ConquestPopulationCoordinator가 같은 이벤트를 구독해 처리한다.
    private void OnConquestCompleted(Vector2Int chunkCoord)
    {
        _conquestModeController.RefreshConquerableHighlights();
    }

    public void ToggleConquestMode()
    {
        // 클릭음을 내지 않는다 - 점령 모드 진입/해제는 SetConquestModeActive가 창음을 낸다.
        bool nextActive = !_conquestModeController.IsActive;

        if (!nextActive && !CanCloseFromShortcut())
        {
            return;
        }

        // 켤 때는 UIManager를 거쳐 다른 배타 모드(건설 등)를 정리한다. 끌 때는 바로 끈다.
        // 밤 금지 판정은 여기서 하지 않는다 - OpenExclusive가 CanEnterConquestMode를 물으므로
        // 단축키로 켜는 경로와 같은 한 곳에서 판정된다(여기서 또 물으면 경고창이 두 번 뜬다).
        if (nextActive)
            _uiManager.OpenExclusive(_conquestModeController);
        else
            _conquestModeController.SetConquestModeActive(false);
    }

    /// <summary>
    /// 지금 점령 모드에 들어갈 수 있는지 - 밤에는 켤 수 없고, 그 이유를 경고창으로 알린다.
    /// 낮밤과 경고창을 아는 것이 이 창이라 판정도 여기 두고, 모드 쪽(ConquestModeController)이
    /// <see cref="IExclusiveModeEntryGuard"/>로 넘겨받는다. 그래서 버튼도 단축키도 이 판정을 지난다.
    /// CycleManager가 없는 씬(튜토리얼·테스트)에서는 막지 않는다.
    /// </summary>
    public bool CanEnterConquestMode()
    {
        if (_cycleManager == null || _cycleManager.CurrentCycle != CycleManager.CycleState.Night)
        {
            return true;
        }

        if (_warningWindow != null)
        {
            _warningWindow.Show(UI_WarningWindow.MessageId.Claim);
        }

        return false;
    }

    /// <summary>점령 컨트롤러가 직접 받는 ESC도 튜토리얼 닫기 관문을 거치게 한다.</summary>
    public bool CanCloseFromShortcut()
    {
        return _uiManager == null || _uiManager.CanCloseExclusive(_conquestModeController);
    }

    /// <summary>
    /// 점령지를 골라 패널이 열린 시점. 청크 클릭은 어느 경로로든 여기 하나를 지나므로
    /// 안내가 "땅을 고르세요"를 기다릴 곳도 여기다.
    /// </summary>
    public UnityEvent<Vector2Int> ChunkSelected = new();

    /// <summary>
    /// 지금 고른 점령지가 있는지. 안내가 "땅을 고르세요"를 이벤트가 아니라 상태로도 확인할 수 있어야
    /// 안내보다 먼저 고른 경우에 그 단계에 갇히지 않는다.
    /// </summary>
    public bool HasSelectedChunk => _selectedChunkCoord.HasValue;

    // ConquestModeController가 점령 가능한 청크를 클릭했을 때 호출하는 진입점.
    public void OnChunkSelected(Vector2Int chunkCoord)
    {
        _selectedChunkCoord = chunkCoord;
        _conquestModeController.LockChunkSelection(chunkCoord);
        OpenPanel();
        Refresh();

        // 패널이 열린 뒤에 알린다 - 구독자가 패널 안의 앵커를 잡을 수 있어야 한다.
        ChunkSelected.Invoke(chunkCoord);
    }

    // 씬에 미리 세팅해 둔 위치(_homePos)에서 슬라이드 인 시킨다.
    private void OpenPanel()
    {
        SoundManager.Play(SoundId.UiWindowOpen);

        _panelTween?.Kill();

        _conquestModePanel.SetActive(true);
        _panelRect.anchoredPosition = _homePos + _openFromOffset;
        _panelTween = _panelRect.DOAnchorPos(_homePos, _slideDuration)
            .SetEase(Ease.OutBack)
            .SetLink(_conquestModePanel);
    }

    // 패널만 닫는다 - 점령 모드 자체는 유지되어 이어서 다른 청크를 선택할 수 있다.
    // 청크 하이라이트는 지우지 않는다 - 호버로 계속 갱신되고, 점령 모드가 꺼질 때까지 유지된다.
    public void Close()
    {
        SoundManager.Play(SoundId.UiWindowClose);

        _selectedChunkCoord = null;
        _conquestModeController.UnlockChunkSelection();

        _panelTween?.Kill();
        _panelTween = _panelRect.DOAnchorPos(_homePos + _closeToOffset, _slideDuration)
            .SetEase(Ease.InCubic)
            .SetLink(_conquestModePanel)
            .OnComplete(() => _conquestModePanel.SetActive(false));
    }

    private void Refresh()
    {
        if (!_selectedChunkCoord.HasValue)
        {
            return;
        }

        Vector2Int coord = _selectedChunkCoord.Value;
        bool hasCost = _conquestManager.TryGetExpeditionCost(coord, out ResourceCost cost);

        ResourceCost held = _resourceManager.GetHoldingsSnapshot();
        held.Population = _populationManager != null ? _populationManager.AvailablePopulation : cost.Population;

        RebuildResourceSlots(held, cost);
        RebuildRewardSlots(
            coord,
            _conquestManager.GetPopulationReward(coord),
            _conquestManager.GetUnlockedResources(coord));

        if (_durationInfoSlot != null)
        {
            int daysRequired = _conquestManager.GetDaysRequired(coord);
            _durationInfoSlot.Setup(_durationIcon, Color.white, DurationLabel, FormatDuration(daysRequired));
        }

        if (_terrainImage != null)
        {
            TerrainType terrain = _conquestManager.GetDominantTerrain(coord);
            Sprite terrainSprite = ResolveTerrainSprite(terrain);
            if (terrainSprite != null)
                _terrainImage.sprite = terrainSprite;
        }

        EnemyEnhancementProfileSO profile =
            _conquestManager.PreviewEnemyEnhancementProfile(coord);
        RebuildEnemyEnhancementSlots(profile);

        bool canSend = _conquestManager.CanSendExpedition(coord);
        bool canAfford = _conquestManager.CanAffordExpedition(coord, held);
        _conquerButton.interactable = hasCost && canSend && canAfford;
    }

    // 1일이면 단수(Day), 2일 이상이면 복수(Days) 표기.
    private static string FormatDuration(int days) =>
        string.Format(StringTable.GetString(DURATION_DAYS_FORMAT_LOC_KEY), days);

    // 목록을 다시 그릴 때 이전 청크에서 내려둔 스크롤 위치가 남지 않도록 맨 위로 되돌린다.
    // 컨테이너가 곧 ScrollRect의 Content라 anchoredPosition만 0으로 두면 된다
    // (normalizedPosition은 내용 높이에 의존해 슬롯 생성 전에는 값이 틀어진다).
    private static void ScrollToTop(Transform container)
    {
        if (container is RectTransform content)
        {
            content.anchoredPosition = Vector2.zero;
        }
    }

    private Sprite ResolveTerrainSprite(TerrainType terrain)
    {
        int index = (int)terrain;
        return _terrainSprites != null && index < _terrainSprites.Length ? _terrainSprites[index] : null;
    }

    // 자원 아이콘은 데이터 에셋(ResourceData)이 단일 출처 - 카탈로그에서 종류로 조회한다.
    private Sprite ResolveResourceIcon(ResourceType type)
    {
        if (TryGetResourceData(type, out ResourceData data))
        {
            return data.Icon;
        }

        return null;
    }

    // 자원 이름도 아이콘과 같은 출처(ResourceData.NameLocKey)에서 가져와 현재 언어로 표시한다.
    // 카탈로그에 없는 종류는 종류 이름을 그대로 쓴다(UI_PopulationAllocationWindow와 동일한 대체 방식).
    private string ResolveResourceName(ResourceType type)
    {
        return TryGetResourceData(type, out ResourceData data)
            ? StringTable.GetString(data.NameLocKey)
            : type.ToString();
    }

    private bool TryGetResourceData(ResourceType type, out ResourceData data)
    {
        if (_resourceManager != null && _resourceManager.Catalog != null)
        {
            return _resourceManager.Catalog.TryGet(type, out data);
        }

        data = null;
        return false;
    }

    // 이전에 생성된 자원 슬롯을 지우고, 이번 청크가 실제로 요구하는 자원(요구량 > 0)만큼만 새로 생성한다.
    private void RebuildResourceSlots(ResourceCost held, ResourceCost cost)
    {
        foreach (UI_ResourceCostSlot slot in _spawnedResourceSlots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        _spawnedResourceSlots.Clear();

        if (_resourceCostSlotPrefab == null || _resourceSlotContainer == null)
            return;

        if (cost.Population > 0)
        {
            SpawnResourceCostSlot(_populationIcon, Color.white, held.Population, cost.Population);
        }

        (ResourceType Type, int Held, int Required)[] entries =
        {
            (ResourceType.Food, held.Food, cost.Food),
            (ResourceType.Wood, held.Wood, cost.Wood),
            (ResourceType.Stone, held.Stone, cost.Stone),
        };

        foreach ((ResourceType type, int heldAmount, int requiredAmount) in entries)
        {
            if (requiredAmount <= 0)
                continue;

            SpawnResourceCostSlot(ResolveResourceIcon(type), Color.white, heldAmount, requiredAmount);
        }
    }

    private void SpawnResourceCostSlot(Sprite icon, Color iconColor, int heldAmount, int requiredAmount)
    {
        Color textColor = heldAmount < requiredAmount ? _insufficientColor : _sufficientColor;
        string countText = string.Format(HELD_OVER_REQUIRED_FORMAT, heldAmount, requiredAmount);

        UI_ResourceCostSlot slot = Instantiate(_resourceCostSlotPrefab, _resourceSlotContainer);
        slot.Setup(icon, iconColor, countText, textColor);
        _spawnedResourceSlots.Add(slot);
    }

    // 이전에 생성된 보상 슬롯을 지우고 다시 생성한다.
    // 인구는 실제 지급 수량(populationReward > 0)을, 나머지 자원은 해금된 종류(unlockedResources)만 표시한다.
    private void RebuildRewardSlots(
        Vector2Int coord,
        int populationReward,
        ResourceType unlockedResources)
    {
        foreach (UI_ConquestRewardSlot slot in _spawnedRewardSlots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        _spawnedRewardSlots.Clear();

        if (_rewardSlotPrefab == null || _rewardSlotContainer == null)
            return;

        ScrollToTop(_rewardSlotContainer);

        // 랜드마크를 맨 앞에 둔다 - 자원·인구와 달리 그 청크에만 있는 보상이라 선택의 근거가 된다.
        // 아이콘이 아직 없어도 UI_ConquestRewardSlot이 이름 라벨만으로 그려 준다.
        SpawnLandmarkRewardSlot(coord);

        if (populationReward > 0)
        {
            SpawnRewardSlot(_populationIcon, Color.white, string.Format(PLUS_VALUE_FORMAT, populationReward));
        }

        foreach (ResourceType type in REWARD_RESOURCE_TYPES)
        {
            if ((unlockedResources & type) == 0)
                continue;

            SpawnRewardSlot(ResolveResourceIcon(type), Color.white, ResolveResourceName(type));
        }
    }

    // 이 청크에 랜드마크가 있으면 보상 목록 맨 앞에 이름을 띄운다.
    // 랜드마크가 없는 청크(대부분)나 매니저가 없는 씬에서는 아무 것도 하지 않는다.
    private void SpawnLandmarkRewardSlot(Vector2Int coord)
    {
        if (_landmarkManager == null ||
            !_landmarkManager.TryGetLandmarkAt(coord, out Landmark landmark) ||
            landmark.Data == null)
        {
            return;
        }

        SpawnRewardSlot(
            landmark.Data.Icon,
            Color.white,
            StringTable.GetString(landmark.Data.NameLocKey));
    }

    private void SpawnRewardSlot(Sprite icon, Color iconColor, string label)
    {
        UI_ConquestRewardSlot slot = Instantiate(_rewardSlotPrefab, _rewardSlotContainer);
        slot.Setup(icon, iconColor, label);
        _spawnedRewardSlots.Add(slot);
    }

    private void RebuildEnemyEnhancementSlots(
        EnemyEnhancementProfileSO profile)
    {
        ClearEnemyEnhancementSlots();

        if (profile == null ||
            _enemyScalingSlotPrefab == null ||
            _enemyScalingSlotContainer == null)
        {
            return;
        }

        ScrollToTop(_enemyScalingSlotContainer);

        string labelFormat = StringTable.GetString(ENEMY_SPAWN_BONUS_FORMAT_LOC_KEY);

        foreach (EnemyEnhancementRule rule in profile.Rules)
        {
            if (rule == null || rule.TargetMonster == null || rule.SpawnCountBonus == 0)
            {
                continue;
            }

            string label = string.Format(
                labelFormat,
                StringTable.GetString(rule.TargetMonster.NameLocKey),
                rule.SpawnCountBonus);

            SpawnEnemyScalingSlot(_spawnCountIcon, label);
        }
    }

    private void ClearEnemyEnhancementSlots()
    {
        foreach (UI_ConquestInfoSlot slot in _spawnedEnemyScalingSlots)
        {
            if (slot != null)
            {
                Destroy(slot.gameObject);
            }
        }

        _spawnedEnemyScalingSlots.Clear();
    }

    // 강화 줄은 "소환사 +1"처럼 한 문장이라 값 칸은 비워 두고 이름 칸만 쓴다.
    private void SpawnEnemyScalingSlot(Sprite icon, string label)
    {
        // 몬스터 강화 아이콘은 전용 스프라이트라 틴트가 필요 없다.
        UI_ConquestInfoSlot slot = Instantiate(_enemyScalingSlotPrefab, _enemyScalingSlotContainer);
        slot.Setup(icon, Color.white, label, string.Empty);
        _spawnedEnemyScalingSlots.Add(slot);
    }

    private void OnConquerButtonClicked()
    {
        // 클릭음을 내지 않는다 - 마지막에 Close()가 창 닫힘음을 낸다.
        if (!_selectedChunkCoord.HasValue)
        {
            return;
        }

        Vector2Int coord = _selectedChunkCoord.Value;
        bool hasCost = _conquestManager.TryGetExpeditionCost(coord, out ResourceCost cost);

        ResourceCost held = _resourceManager.GetHoldingsSnapshot();
        held.Population = _populationManager != null ? _populationManager.AvailablePopulation : cost.Population;

        bool sent = _conquestManager.SendExpedition(coord, held);
        if (sent)
        {
            if (hasCost)
                _resourceManager.Spend(cost); // 자원 차감(원정 발송 시점) - 인구 배치/반환/보상은 ConquestPopulationCoordinator가 처리

            _conquestModeController.RefreshConquerableHighlights(); // 원정 중인 청크는 CanSendExpedition이 false가 되므로 즉시 갱신

            // 크루가 성에서 걸어 나오는 연출. ConquestManager.OnExpeditionSent가 아니라 여기서 알리는
            // 이유: 그 이벤트는 세이브 복원(RestoreExpeditions)이 재발화하므로, 구독하면 로드할 때마다
            // 성에서 인파가 쏟아진다. 사용자가 실제로 버튼을 누른 이 지점은 복원 경로에 없다.
            if (_villagerDispatch != null)
            {
                _villagerDispatch.NotifyExpeditionSent(coord);
            }
        }

        Close();
    }
}
