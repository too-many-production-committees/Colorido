using UnityEngine;

public enum ProjectionCellType
{
    Empty,
    Walkable,
    Solid,
    Interactable
}

public enum ProjectionEditingView
{
    Front,
    Right,
    Back,
    Left
}

public enum ProjectionCommonAreaMode
{
    CurrentView,
    CommonAll,
    FrontRight,
    RightBack,
    BackLeft,
    LeftFront,
    Custom
}

[System.Serializable]
public struct ProjectionSurfaceCell
{
    public ProjectionCellType cellType;
    public ProjectionViewMask activeViews;
}

public class ProjectionSurfaceGrid : MonoBehaviour
{
    public Vector2 size = new Vector2(8f, 4f);
    public int columns = 8;
    public int rows = 4;
    public Vector3 localCenter = Vector3.zero;
    public Vector3 localRight = Vector3.right;
    public Vector3 localUp = Vector3.forward;
    public ProjectionSurfaceCell[] cells;

    public ProjectionEditingView editingView = ProjectionEditingView.Front;
    public ProjectionCellType brushType = ProjectionCellType.Walkable;
    public ProjectionCommonAreaMode commonAreaMode = ProjectionCommonAreaMode.CurrentView;
    public ProjectionViewMask customActiveViews = ProjectionViewMask.Front;
    public float generatedAreaThickness = 0.08f;
    public float editorVisualOffset = 0.02f;
    public bool enableScenePainting = false;
    public bool boxSelectionMode = true;

    public int CellCount => Mathf.Max(1, columns) * Mathf.Max(1, rows);
    public Vector3 WorldCenter => transform.TransformPoint(localCenter);
    public Vector3 WorldRight => transform.TransformDirection(GetLocalRight()).normalized;
    public Vector3 WorldUp => transform.TransformDirection(GetLocalUp()).normalized;
    public Vector3 WorldNormal => GetWorldNormal();

    void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        size.x = Mathf.Max(0.01f, size.x);
        size.y = Mathf.Max(0.01f, size.y);
        generatedAreaThickness = Mathf.Max(0.001f, generatedAreaThickness);
        EnsureCellArray();
    }

    public void EnsureCellArray()
    {
        int count = CellCount;
        if (cells != null && cells.Length == count)
            return;

        ProjectionSurfaceCell[] resized = new ProjectionSurfaceCell[count];
        if (cells != null)
        {
            int copyCount = Mathf.Min(cells.Length, resized.Length);
            for (int i = 0; i < copyCount; i++)
                resized[i] = cells[i];
        }

        cells = resized;
    }

    public int GetIndex(int column, int row)
    {
        return row * Mathf.Max(1, columns) + column;
    }

    public bool TryGetCellIndex(Ray worldRay, out int column, out int row)
    {
        column = -1;
        row = -1;

        Vector3 normal = WorldNormal;
        if (normal.sqrMagnitude < 0.0001f)
            return false;

        Plane plane = new Plane(normal, WorldCenter);
        if (!plane.Raycast(worldRay, out float enter))
            return false;

        Vector3 worldPoint = worldRay.GetPoint(enter);
        Vector3 localDelta = transform.InverseTransformPoint(worldPoint) - localCenter;
        Vector3 right = GetLocalRight();
        Vector3 up = GetLocalUp();
        float x = Vector3.Dot(localDelta, right);
        float y = Vector3.Dot(localDelta, up);

        float normalizedX = x / size.x + 0.5f;
        float normalizedY = y / size.y + 0.5f;
        if (normalizedX < 0f || normalizedX > 1f || normalizedY < 0f || normalizedY > 1f)
            return false;

        column = Mathf.Clamp(Mathf.FloorToInt(normalizedX * columns), 0, columns - 1);
        row = Mathf.Clamp(Mathf.FloorToInt(normalizedY * rows), 0, rows - 1);
        return true;
    }

    public Vector3 GetCellWorldCenter(int column, int row)
    {
        Vector2 cellSize = GetCellSize();
        float x = -size.x * 0.5f + cellSize.x * (column + 0.5f);
        float y = -size.y * 0.5f + cellSize.y * (row + 0.5f);
        Vector3 local = localCenter + GetLocalRight() * x + GetLocalUp() * y;
        return transform.TransformPoint(local);
    }

    public Vector3[] GetCellWorldCorners(int column, int row, float normalOffset = 0f)
    {
        Vector2 cellSize = GetCellSize();
        float xMin = -size.x * 0.5f + cellSize.x * column;
        float xMax = xMin + cellSize.x;
        float yMin = -size.y * 0.5f + cellSize.y * row;
        float yMax = yMin + cellSize.y;
        Vector3 right = GetLocalRight();
        Vector3 up = GetLocalUp();
        Vector3 offset = WorldNormal * normalOffset;

        return new[]
        {
            transform.TransformPoint(localCenter + right * xMin + up * yMin) + offset,
            transform.TransformPoint(localCenter + right * xMin + up * yMax) + offset,
            transform.TransformPoint(localCenter + right * xMax + up * yMax) + offset,
            transform.TransformPoint(localCenter + right * xMax + up * yMin) + offset
        };
    }

    public Vector3[] GetSurfaceWorldCorners(float normalOffset = 0f)
    {
        Vector3 right = GetLocalRight();
        Vector3 up = GetLocalUp();
        Vector3 offset = WorldNormal * normalOffset;

        return new[]
        {
            transform.TransformPoint(localCenter - right * size.x * 0.5f - up * size.y * 0.5f) + offset,
            transform.TransformPoint(localCenter - right * size.x * 0.5f + up * size.y * 0.5f) + offset,
            transform.TransformPoint(localCenter + right * size.x * 0.5f + up * size.y * 0.5f) + offset,
            transform.TransformPoint(localCenter + right * size.x * 0.5f - up * size.y * 0.5f) + offset
        };
    }

    public void FitToAttachedBounds()
    {
        if (!ProjectionBoundsUtility.TryGetBounds(gameObject, true, out Bounds worldBounds))
            return;

        Vector3 right = GetLocalRight();
        Vector3 up = GetLocalUp();
        Vector3 localBoundsCenter = transform.InverseTransformPoint(worldBounds.center);
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        Vector3[] worldCorners =
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z)
        };

        float minRight = float.PositiveInfinity;
        float maxRight = float.NegativeInfinity;
        float minUp = float.PositiveInfinity;
        float maxUp = float.NegativeInfinity;

        for (int i = 0; i < worldCorners.Length; i++)
        {
            Vector3 localDelta = transform.InverseTransformPoint(worldCorners[i]) - localBoundsCenter;
            float rightDistance = Vector3.Dot(localDelta, right);
            float upDistance = Vector3.Dot(localDelta, up);
            minRight = Mathf.Min(minRight, rightDistance);
            maxRight = Mathf.Max(maxRight, rightDistance);
            minUp = Mathf.Min(minUp, upDistance);
            maxUp = Mathf.Max(maxUp, upDistance);
        }

        localCenter = localBoundsCenter + right * ((minRight + maxRight) * 0.5f) + up * ((minUp + maxUp) * 0.5f);
        size = new Vector2(
            Mathf.Max(0.01f, maxRight - minRight),
            Mathf.Max(0.01f, maxUp - minUp));
    }

    public Vector2 GetCellSize()
    {
        return new Vector2(size.x / Mathf.Max(1, columns), size.y / Mathf.Max(1, rows));
    }

    public ProjectionViewMask GetEditingViewMask()
    {
        return EditingViewToMask(editingView);
    }

    public ProjectionViewMask GetBrushViews()
    {
        switch (commonAreaMode)
        {
            case ProjectionCommonAreaMode.CommonAll:
                return ProjectionViewMask.All;
            case ProjectionCommonAreaMode.FrontRight:
                return ProjectionViewMask.Front | ProjectionViewMask.Right;
            case ProjectionCommonAreaMode.RightBack:
                return ProjectionViewMask.Right | ProjectionViewMask.Back;
            case ProjectionCommonAreaMode.BackLeft:
                return ProjectionViewMask.Back | ProjectionViewMask.Left;
            case ProjectionCommonAreaMode.LeftFront:
                return ProjectionViewMask.Left | ProjectionViewMask.Front;
            case ProjectionCommonAreaMode.Custom:
                return customActiveViews;
        }

        return GetEditingViewMask();
    }

    public static ProjectionViewMask EditingViewToMask(ProjectionEditingView view)
    {
        switch (view)
        {
            case ProjectionEditingView.Right:
                return ProjectionViewMask.Right;
            case ProjectionEditingView.Back:
                return ProjectionViewMask.Back;
            case ProjectionEditingView.Left:
                return ProjectionViewMask.Left;
        }

        return ProjectionViewMask.Front;
    }

    Vector3 GetLocalRight()
    {
        return localRight.sqrMagnitude > 0.0001f ? localRight.normalized : Vector3.right;
    }

    Vector3 GetLocalUp()
    {
        return localUp.sqrMagnitude > 0.0001f ? localUp.normalized : Vector3.forward;
    }

    Vector3 GetWorldNormal()
    {
        Vector3 normal = Vector3.Cross(WorldRight, WorldUp);
        if (normal.sqrMagnitude < 0.0001f)
            return transform.forward;

        return normal.normalized;
    }
}
