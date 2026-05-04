using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProjectionSurfaceGrid))]
public class ProjectionSurfaceGridEditor : Editor
{
    const string GeneratedRootName = "GeneratedProjectionAreas";

    ProjectionSurfaceGrid grid;
    int lastPaintedIndex = -1;
    bool boxSelecting = false;
    int selectionStartColumn = -1;
    int selectionStartRow = -1;
    int selectionEndColumn = -1;
    int selectionEndRow = -1;

    public override void OnInspectorGUI()
    {
        grid = target as ProjectionSurfaceGrid;
        if (grid == null)
            return;

        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("size"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("columns"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("rows"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("localCenter"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("localRight"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("localUp"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("generatedAreaThickness"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("editorVisualOffset"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableScenePainting"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("boxSelectionMode"));

        if (GUILayout.Button("Fit To Object Bounds"))
            FitToObjectBounds();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Painter", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("editingView"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("brushType"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("commonAreaMode"));

        if ((ProjectionCommonAreaMode)serializedObject.FindProperty("commonAreaMode").enumValueIndex ==
            ProjectionCommonAreaMode.Custom)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("customActiveViews"));

        serializedObject.ApplyModifiedProperties();
        grid.EnsureCellArray();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear Current View"))
                ClearCurrentView();
            if (GUILayout.Button("Fill Current View"))
                Fill(grid.GetEditingViewMask());
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear All"))
                ClearAll();
            if (GUILayout.Button("Fill Common Area"))
                Fill(grid.GetBrushViews());
        }

        if (GUILayout.Button("Generate Projection Areas"))
            GenerateProjectionAreas();
    }

    void FitToObjectBounds()
    {
        Undo.RecordObject(grid, "Fit Projection Surface Grid To Object Bounds");
        grid.FitToAttachedBounds();
        EditorUtility.SetDirty(grid);
        SceneView.RepaintAll();
    }

    void OnSceneGUI()
    {
        grid = target as ProjectionSurfaceGrid;
        if (grid == null ||
            !grid.enableScenePainting ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
            return;

        grid.EnsureCellArray();
        DrawGrid();
        DrawSelectionPreview();
        HandlePainting();
    }

    void DrawGrid()
    {
        if (!HasValidGridData())
            return;

        ProjectionViewMask editingMask = grid.GetEditingViewMask();

        UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        for (int row = 0; row < grid.rows; row++)
        {
            for (int column = 0; column < grid.columns; column++)
            {
                if (!TryGetCell(column, row, out _, out ProjectionSurfaceCell cell))
                    continue;

                Vector3[] corners = grid.GetCellWorldCorners(column, row, grid.editorVisualOffset);
                if (!HasFourCorners(corners))
                    continue;

                bool belongsToEditingView = (cell.activeViews & editingMask) != 0;
                Color fill = GetCellColor(cell.cellType);
                fill.a = belongsToEditingView || cell.activeViews == ProjectionViewMask.None ? 0.38f : 0.1f;
                Color outline = IsCommonArea(cell.activeViews) ? Color.yellow : Color.black;
                outline.a = belongsToEditingView ? 0.9f : 0.25f;

                Handles.DrawSolidRectangleWithOutline(corners, fill, outline);
            }
        }

        DrawSurfaceOutline();
        Handles.zTest = previousZTest;
    }

    void DrawSurfaceOutline()
    {
        Vector3[] corners = grid.GetSurfaceWorldCorners(grid.editorVisualOffset);
        if (!HasFourCorners(corners))
            return;

        Handles.DrawSolidRectangleWithOutline(
            corners,
            new Color(1f, 1f, 1f, 0.06f),
            new Color(1f, 0.9f, 0.1f, 1f));
    }

    void DrawSelectionPreview()
    {
        if (!boxSelecting ||
            selectionStartColumn < 0 ||
            selectionStartRow < 0 ||
            selectionEndColumn < 0 ||
            selectionEndRow < 0)
            return;

        int minColumn = Mathf.Min(selectionStartColumn, selectionEndColumn);
        int maxColumn = Mathf.Max(selectionStartColumn, selectionEndColumn);
        int minRow = Mathf.Min(selectionStartRow, selectionEndRow);
        int maxRow = Mathf.Max(selectionStartRow, selectionEndRow);

        ClampCellRange(ref minColumn, ref minRow, ref maxColumn, ref maxRow);
        if (minColumn > maxColumn || minRow > maxRow)
            return;

        Color fill = GetCellColor(grid.brushType);
        fill.a = 0.42f;
        Color outline = new Color(1f, 0.9f, 0.1f, 1f);

        for (int row = minRow; row <= maxRow; row++)
        {
            for (int column = minColumn; column <= maxColumn; column++)
            {
                Vector3[] corners = grid.GetCellWorldCorners(column, row, grid.editorVisualOffset + 0.005f);
                if (!HasFourCorners(corners))
                    continue;

                Handles.DrawSolidRectangleWithOutline(
                    corners,
                    fill,
                    outline);
            }
        }
    }

    void HandlePainting()
    {
        Event current = Event.current;
        if (current == null || current.alt)
            return;

        if (current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (grid.boxSelectionMode)
        {
            HandleBoxSelection(current);
            return;
        }

        HandleBrushPainting(current);
    }

    void HandleBoxSelection(Event current)
    {
        if (current.button != 0)
            return;

        if (current.type == EventType.MouseDown)
        {
            if (!TryGetMouseCell(current, out int column, out int row))
                return;

            boxSelecting = true;
            selectionStartColumn = column;
            selectionStartRow = row;
            selectionEndColumn = column;
            selectionEndRow = row;
            current.Use();
            SceneView.RepaintAll();
            return;
        }

        if (current.type == EventType.MouseDrag && boxSelecting)
        {
            if (TryGetMouseCell(current, out int column, out int row))
            {
                selectionEndColumn = column;
                selectionEndRow = row;
                SceneView.RepaintAll();
            }

            current.Use();
            return;
        }

        if (current.type == EventType.MouseUp && boxSelecting)
        {
            PaintCellRange(selectionStartColumn, selectionStartRow, selectionEndColumn, selectionEndRow);
            ClearSelectionState();
            current.Use();
            SceneView.RepaintAll();
        }
    }

    void HandleBrushPainting(Event current)
    {
        bool isPaintEvent = current.type == EventType.MouseDown || current.type == EventType.MouseDrag;
        if (!isPaintEvent || current.button != 0)
        {
            if (current.type == EventType.MouseUp)
                lastPaintedIndex = -1;
            return;
        }

        if (!TryGetMouseCell(current, out int column, out int row))
            return;

        int index = grid.GetIndex(column, row);
        if (!IsValidCellIndex(index))
            return;

        if (index == lastPaintedIndex && current.type == EventType.MouseDrag)
            return;

        PaintCell(index);
        lastPaintedIndex = index;
        current.Use();
        SceneView.RepaintAll();
    }

    bool TryGetMouseCell(Event current, out int column, out int row)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
        return grid.TryGetCellIndex(ray, out column, out row);
    }

    void PaintCell(int index)
    {
        if (!IsValidCellIndex(index))
            return;

        Undo.RecordObject(grid, "Paint Projection Surface Cell");
        grid.cells[index] = GetPaintedCell(grid.cells[index]);
        EditorUtility.SetDirty(grid);
    }

    void PaintCellRange(int startColumn, int startRow, int endColumn, int endRow)
    {
        if (startColumn < 0 || startRow < 0 || endColumn < 0 || endRow < 0)
            return;

        Undo.RecordObject(grid, "Box Paint Projection Surface Cells");
        grid.EnsureCellArray();

        int minColumn = Mathf.Min(startColumn, endColumn);
        int maxColumn = Mathf.Max(startColumn, endColumn);
        int minRow = Mathf.Min(startRow, endRow);
        int maxRow = Mathf.Max(startRow, endRow);
        ClampCellRange(ref minColumn, ref minRow, ref maxColumn, ref maxRow);
        if (minColumn > maxColumn || minRow > maxRow)
            return;

        ProjectionViewMask activeViews = grid.brushType == ProjectionCellType.Empty
            ? ProjectionViewMask.None
            : grid.GetBrushViews();

        for (int row = minRow; row <= maxRow; row++)
        {
            for (int column = minColumn; column <= maxColumn; column++)
            {
                if (!TryGetCell(column, row, out int index, out ProjectionSurfaceCell cell))
                    continue;

                grid.cells[index] = GetPaintedCell(cell, activeViews);
            }
        }

        EditorUtility.SetDirty(grid);
    }

    ProjectionSurfaceCell GetPaintedCell(ProjectionSurfaceCell current)
    {
        ProjectionViewMask activeViews = grid.brushType == ProjectionCellType.Empty
            ? ProjectionViewMask.None
            : grid.GetBrushViews();
        return GetPaintedCell(current, activeViews);
    }

    ProjectionSurfaceCell GetPaintedCell(ProjectionSurfaceCell current, ProjectionViewMask activeViews)
    {
        if (grid.brushType == ProjectionCellType.Empty || activeViews == ProjectionViewMask.None)
            return default;

        ProjectionViewMask mergedViews = activeViews;
        if (current.cellType != ProjectionCellType.Empty &&
            current.cellType == grid.brushType)
            mergedViews |= current.activeViews;

        return new ProjectionSurfaceCell
        {
            cellType = grid.brushType,
            activeViews = mergedViews
        };
    }

    void ClearSelectionState()
    {
        boxSelecting = false;
        selectionStartColumn = -1;
        selectionStartRow = -1;
        selectionEndColumn = -1;
        selectionEndRow = -1;
    }

    void ClearCurrentView()
    {
        Undo.RecordObject(grid, "Clear Projection Current View");
        ProjectionViewMask editingMask = grid.GetEditingViewMask();
        grid.EnsureCellArray();

        for (int i = 0; i < grid.cells.Length; i++)
        {
            ProjectionSurfaceCell cell = grid.cells[i];
            cell.activeViews &= ~editingMask;
            if (cell.activeViews == ProjectionViewMask.None)
                cell.cellType = ProjectionCellType.Empty;
            grid.cells[i] = cell;
        }

        EditorUtility.SetDirty(grid);
    }

    void ClearAll()
    {
        Undo.RecordObject(grid, "Clear Projection Surface Grid");
        grid.EnsureCellArray();
        for (int i = 0; i < grid.cells.Length; i++)
            grid.cells[i] = default;
        EditorUtility.SetDirty(grid);
    }

    void Fill(ProjectionViewMask activeViews)
    {
        Undo.RecordObject(grid, "Fill Projection Surface Grid");
        grid.EnsureCellArray();
        for (int i = 0; i < grid.cells.Length; i++)
        {
            grid.cells[i] = new ProjectionSurfaceCell
            {
                cellType = grid.brushType,
                activeViews = grid.brushType == ProjectionCellType.Empty
                    ? ProjectionViewMask.None
                    : activeViews
            };
        }

        EditorUtility.SetDirty(grid);
    }

    void GenerateProjectionAreas()
    {
        grid.EnsureCellArray();
        Transform root = GetOrCreateGeneratedRoot();

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Generate Projection Areas");
        for (int i = root.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);

        Vector3 normal = grid.WorldNormal;

        for (int row = 0; row < grid.rows; row++)
        {
            for (int column = 0; column < grid.columns; column++)
            {
                if (!TryGetCell(column, row, out _, out ProjectionSurfaceCell cell))
                    continue;

                if (cell.cellType == ProjectionCellType.Empty || cell.activeViews == ProjectionViewMask.None)
                    continue;

                Vector3[] corners = grid.GetCellWorldCorners(column, row);
                if (!HasFourCorners(corners))
                    continue;

                Vector3 center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
                Vector3 cellUp = corners[1] - corners[0];
                Vector3 cellRight = corners[3] - corners[0];
                float cellWidth = Mathf.Max(0.001f, cellRight.magnitude);
                float cellHeight = Mathf.Max(0.001f, cellUp.magnitude);

                GameObject area = new GameObject($"projection_area_{row}_{column}_{cell.cellType}");
                Undo.RegisterCreatedObjectUndo(area, "Create Projection Area");
                area.transform.position = center + normal * grid.generatedAreaThickness * 0.5f;
                area.transform.rotation = Quaternion.LookRotation(normal, cellUp.normalized);
                area.transform.localScale = Vector3.one;
                area.transform.SetParent(root, true);

                BoxCollider collider = area.AddComponent<BoxCollider>();
                collider.size = new Vector3(cellWidth, cellHeight, grid.generatedAreaThickness);

                ProjectionArea projectionArea = area.AddComponent<ProjectionArea>();
                projectionArea.activeViews = cell.activeViews;

                AddAreaTypeComponent(area, cell);
            }
        }
    }

    Transform GetOrCreateGeneratedRoot()
    {
        Transform existing = grid.transform.Find(GeneratedRootName);
        if (existing != null)
            return existing;

        GameObject root = new GameObject(GeneratedRootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Generated Projection Areas Root");
        root.transform.SetParent(grid.transform, false);
        return root.transform;
    }

    void AddAreaTypeComponent(GameObject area, ProjectionSurfaceCell cell)
    {
        switch (cell.cellType)
        {
            case ProjectionCellType.Walkable:
                ProjectionWalkable walkable = area.AddComponent<ProjectionWalkable>();
                walkable.activeViews = cell.activeViews;
                break;
            case ProjectionCellType.Solid:
                ProjectionSolid solid = area.AddComponent<ProjectionSolid>();
                solid.activeViews = cell.activeViews;
                solid.colliderMode = ProjectionSolidColliderMode.Box;
                break;
            case ProjectionCellType.Interactable:
                ProjectionInteractable interactable = area.AddComponent<ProjectionInteractable>();
                interactable.activeViews = cell.activeViews;
                break;
        }
    }

    Color GetCellColor(ProjectionCellType type)
    {
        switch (type)
        {
            case ProjectionCellType.Walkable:
                return Color.green;
            case ProjectionCellType.Solid:
                return Color.red;
            case ProjectionCellType.Interactable:
                return Color.blue;
        }

        return Color.white;
    }

    bool IsCommonArea(ProjectionViewMask activeViews)
    {
        return activeViews != ProjectionViewMask.None &&
            activeViews != ProjectionViewMask.Front &&
            activeViews != ProjectionViewMask.Right &&
            activeViews != ProjectionViewMask.Back &&
            activeViews != ProjectionViewMask.Left;
    }

    bool HasValidGridData()
    {
        if (grid == null)
            return false;

        grid.EnsureCellArray();
        return grid.cells != null && grid.cells.Length >= grid.CellCount;
    }

    bool TryGetCell(int column, int row, out int index, out ProjectionSurfaceCell cell)
    {
        index = -1;
        cell = default;

        if (!HasValidGridData())
            return false;

        if (column < 0 || row < 0 || column >= grid.columns || row >= grid.rows)
            return false;

        index = grid.GetIndex(column, row);
        if (!IsValidCellIndex(index))
            return false;

        cell = grid.cells[index];
        return true;
    }

    bool IsValidCellIndex(int index)
    {
        return grid != null &&
            grid.cells != null &&
            index >= 0 &&
            index < grid.cells.Length;
    }

    void ClampCellRange(ref int minColumn, ref int minRow, ref int maxColumn, ref int maxRow)
    {
        if (grid == null)
            return;

        int lastColumn = Mathf.Max(0, grid.columns - 1);
        int lastRow = Mathf.Max(0, grid.rows - 1);
        minColumn = Mathf.Clamp(minColumn, 0, lastColumn);
        maxColumn = Mathf.Clamp(maxColumn, 0, lastColumn);
        minRow = Mathf.Clamp(minRow, 0, lastRow);
        maxRow = Mathf.Clamp(maxRow, 0, lastRow);
    }

    bool HasFourCorners(Vector3[] corners)
    {
        return corners != null && corners.Length >= 4;
    }
}
