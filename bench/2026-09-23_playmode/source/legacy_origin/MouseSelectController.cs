using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class MouseSelectController : MonoBehaviour
{
    [SerializeField]
    private SpriteRenderer _spriteRenderer;

    [SerializeField]
    private GridMap _gridMap;

    [SerializeField]
    private float _yOffset = 0.7f;

    [SerializeField]
    private SpriteRenderer _ghostRenderer;

    [SerializeField]
    private float _ghostAlpha = 0.5f;

    [SerializeField]
    private Color _ghostBlockedTint = new Color(1f, 0.4f, 0.4f);

    [SerializeField]
    private Color _selectionHighlightColor = Color.yellow;

    [SerializeField]
    private Color _occupiedOverlayColor = new Color(1f, 0f, 0f, 0.35f);

    private Camera _cam;
    private ComponentPool<SpriteRenderer> _highlightPool;
    private ComponentPool<SpriteRenderer> _occupiedOverlayPool;
    private FootprintShape _footprintShape = new FootprintShape(new bool[1, 1] { { true } });
    private Vector3 _ghostLocalOffset;
    private bool _isPlacementActive;
    private Building _selectedBuildingRef; // 재배치 중이면 실제 인스턴스 - 자기 자신과 겹치는 위치도 유효하게 판정하기 위함

    public Vector3Int CurrentAnchor { get; private set; }
    public bool CanConstruct { get; private set; }
    public Color SelectionHighlightColor => _selectionHighlightColor;
    public float YOffset => _yOffset;

    // 참조가 비어 있어도(=null) 안전하게 0을 반환 - Y 오프셋을 쓰는 다른 오버레이 스크립트들이 공용으로 사용.
    public static float GetYOffsetOrZero(MouseSelectController mouseSelectController) =>
        mouseSelectController != null ? mouseSelectController.YOffset : 0f;

    private void Awake()
    {
        _cam = Camera.main;
        _highlightPool = new ComponentPool<SpriteRenderer>(_spriteRenderer, transform, seedInstance: _spriteRenderer);
        _occupiedOverlayPool = new ComponentPool<SpriteRenderer>(_spriteRenderer, transform);
        Deactivate();
    }

    public void SetPlacementActive(bool isActive)
    {
        _isPlacementActive = isActive;

        if (!_isPlacementActive)
            Deactivate();
    }

    public Vector3Int GetHoveredCell()
    {
        Vector3 worldPos = _cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        worldPos.z = 0f;
        worldPos.y -= _yOffset;

        return _gridMap.ConvertWorldToGrid(worldPos);
    }

    public Vector3Int GetHoveredAnchor(FootprintShape shape) => GetFootprintAnchor(GetHoveredCell(), shape);

    private void Update()
    {
        if (!_isPlacementActive)
            return;

        Vector3Int hoveredCell = GetHoveredCell();
        Vector3Int anchor = GetFootprintAnchor(hoveredCell, _footprintShape);
        List<Vector3Int> footprint = _gridMap.GetFootprintCoords(anchor, _footprintShape);
        bool canConstruct = _gridMap.CanConstructFootPrint(anchor, _footprintShape, _selectedBuildingRef);

        CurrentAnchor = anchor;
        CanConstruct = canConstruct;

        DrawFootprint(footprint, canConstruct);
        DrawGhost(anchor, canConstruct);
    }

    public void BeginPlacementPreview(Building prefab) => SetPreviewTarget(prefab);
    public void BeginRepositionPreview(Building building) => SetPreviewTarget(building);

    private void SetPreviewTarget(Building building)
    {
        _selectedBuildingRef = building;
        _footprintShape = building.FootprintShape;
        _ghostLocalOffset = building.PlacementOffset;

        if (_ghostRenderer == null)
            return;

        SpriteRenderer prefabRenderer = building.GetComponent<SpriteRenderer>();
        Sprite ghostSprite = prefabRenderer != null ? prefabRenderer.sprite : null;

        _ghostRenderer.sprite = ghostSprite;
        _ghostRenderer.transform.localScale = building.transform.localScale;
        _ghostRenderer.gameObject.SetActive(ghostSprite != null);
    }

    private Vector3Int GetFootprintAnchor(Vector3Int hoveredCell, FootprintShape shape) =>
        hoveredCell - shape.CenterOffset;

    private void DrawFootprint(List<Vector3Int> footprint, bool canConstruct)
    {
        Color highlightColor = canConstruct ? Color.green : Color.red;
        HighlightCells(footprint, highlightColor);
    }

    public void HighlightCells(List<Vector3Int> coords, Color color) => HighlightCells(coords, color, _highlightPool);

    private void HighlightCells(List<Vector3Int> coords, Color color, ComponentPool<SpriteRenderer> pool)
    {
        for (int i = 0; i < coords.Count; i++)
        {
            SpriteRenderer highlight = pool.Get(i);
            Vector3 cellPos = _gridMap.ConvertGridToWorld(coords[i]);
            cellPos.y += _yOffset;
            highlight.transform.position = cellPos;
            highlight.color = color;
        }

        pool.DeactivateFrom(coords.Count);
    }

    public void HighlightSelection(List<Vector3Int> coords) => HighlightCells(coords, _selectionHighlightColor);

    // 건설 모드에서 이미 건물이 배치된 타일을 표시 - 어떤 땅이 비어있는지 한눈에 파악 가능
    public void ShowOccupiedOverlay(List<Vector3Int> coords) => HighlightCells(coords, _occupiedOverlayColor, _occupiedOverlayPool);

    public void ClearOccupiedOverlay() => _occupiedOverlayPool.DeactivateAll();

    public void ClearHighlights() => _highlightPool.DeactivateAll();

    private void DrawGhost(Vector3Int anchor, bool canConstruct)
    {
        if (_ghostRenderer == null || !_ghostRenderer.gameObject.activeSelf)
            return;

        _ghostRenderer.transform.position = _gridMap.GetFootprintCenterWorld(anchor, _footprintShape) + _ghostLocalOffset;

        Color color = canConstruct ? Color.white : _ghostBlockedTint;
        color.a = _ghostAlpha;
        _ghostRenderer.color = color;
    }

    private void Deactivate()
    {
        if (_ghostRenderer != null)
            _ghostRenderer.gameObject.SetActive(false);

        ClearHighlights();
        CanConstruct = false;
    }
}
