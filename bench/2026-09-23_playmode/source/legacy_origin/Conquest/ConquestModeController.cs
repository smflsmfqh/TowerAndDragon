using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 점령 모드 진입/청크 선택을 담당한다. 건설 모드(BuildingPlacementController)와 동일한
// 입력 처리 패턴(InputActionReference Enable/Disable, 포인터-오버-UI 가드)을 따른다.
public class ConquestModeController : MonoBehaviour
{
    [SerializeField]
    private GridMap _gridMap;

    [SerializeField]
    private MouseSelectController _mouseSelectController;

    [SerializeField]
    private ConquestManager _conquestManager;

    [SerializeField]
    private ConquestUIExample _conquestUI;

    [SerializeField]
    private BuildingPlacementController _buildingPlacementController;

    [SerializeField]
    private InputActionReference _selectAction;

    [SerializeField]
    private Color _conquerableHighlightColor = Color.green;

    [SerializeField]
    private Color _blockedHighlightColor = Color.red;

    public bool IsActive { get; private set; }

    private void OnEnable()
    {
        if (_selectAction != null)
            _selectAction.action.Enable();
    }

    private void OnDisable()
    {
        if (_selectAction != null)
            _selectAction.action.Disable();
    }

    private void Update()
    {
        if (!IsActive)
            return;

        HighlightHoveredChunk();
        HandleSelectInput();
    }

    public void SetConquestModeActive(bool isActive)
    {
        IsActive = isActive;

        if (isActive)
        {
            _buildingPlacementController.CancelAll();
        }
        else
        {
            ClearSelection();
        }
    }

    // 패널이 모드는 유지한 채 닫힐 때(선택만 취소) 호출
    public void ClearSelection()
    {
        _mouseSelectController.ClearHighlights();
    }

    private void HighlightHoveredChunk()
    {
        Vector3Int hoveredCell = _mouseSelectController.GetHoveredCell();
        Chunk chunk = _gridMap.GetChunkAt(hoveredCell);

        if (chunk == null)
        {
            _mouseSelectController.ClearHighlights();
            return;
        }

        Color color = _conquestManager.CanSendExpedition(chunk.ChunkCoord)
            ? _conquerableHighlightColor
            : _blockedHighlightColor;

        _mouseSelectController.HighlightCells(GetChunkCellCoords(chunk), color);
    }

    private void HandleSelectInput()
    {
        if (_selectAction == null || !_selectAction.action.WasPerformedThisFrame())
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector3Int hoveredCell = _mouseSelectController.GetHoveredCell();
        Chunk chunk = _gridMap.GetChunkAt(hoveredCell);

        if (chunk == null || chunk.CurrentState != State.Visible)
            return;

        _conquestUI.OnChunkSelected(chunk.ChunkCoord);
    }

    private static List<Vector3Int> GetChunkCellCoords(Chunk chunk)
    {
        var coords = new List<Vector3Int>(chunk.Cells.Count);
        foreach (GridCell cell in chunk.Cells)
        {
            coords.Add(cell.Coord);
        }

        return coords;
    }
}
