using UnityEngine;

public enum SurfaceInteractionState
{
    StateA,
    StateB
}

public class SurfaceInteractionMarker : MonoBehaviour
{
    public SurfaceInteractionState state = SurfaceInteractionState.StateA;
    public float projectionPadding = 0.02f;

    public bool BlocksSurfacePlayer => state == SurfaceInteractionState.StateA;
    public bool AllowsSurfaceInteraction => state == SurfaceInteractionState.StateB;

    public static bool IsStateA(Collider target)
    {
        SurfaceInteractionMarker marker = GetMarker(target);
        return marker != null && marker.BlocksSurfacePlayer;
    }

    public static bool IsStateB(Collider target)
    {
        SurfaceInteractionMarker marker = GetMarker(target);
        return marker != null && marker.AllowsSurfaceInteraction;
    }

    public static SurfaceInteractionMarker GetMarker(Collider target)
    {
        if (target == null)
            return null;

        return target.GetComponentInParent<SurfaceInteractionMarker>();
    }

    public Bounds GetInteractionBounds()
    {
        Collider markerCollider = GetComponentInChildren<Collider>();
        if (markerCollider != null)
            return markerCollider.bounds;

        Renderer markerRenderer = GetComponentInChildren<Renderer>();
        if (markerRenderer != null)
            return markerRenderer.bounds;

        return new Bounds(transform.position, Vector3.one);
    }

    public bool ProjectedOverlaps(Bounds otherBounds, Camera targetCamera)
    {
        return TryGetProjectedRect(GetInteractionBounds(), targetCamera, out Rect ownRect) &&
            TryGetProjectedRect(otherBounds, targetCamera, out Rect otherRect) &&
            PaddedOverlaps(ownRect, otherRect, projectionPadding);
    }

    public static bool TryGetProjectedRect(Bounds bounds, Camera targetCamera, out Rect rect)
    {
        rect = new Rect();

        if (targetCamera == null)
            return false;

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3[] corners =
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

        Vector2 rectMin = Vector2.one * float.PositiveInfinity;
        Vector2 rectMax = Vector2.one * float.NegativeInfinity;
        bool hasPoint = false;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 viewportPoint = targetCamera.WorldToViewportPoint(corners[i]);
            if (!targetCamera.orthographic && viewportPoint.z <= 0f)
                continue;

            Vector2 point = new Vector2(viewportPoint.x, viewportPoint.y);
            rectMin = Vector2.Min(rectMin, point);
            rectMax = Vector2.Max(rectMax, point);
            hasPoint = true;
        }

        if (!hasPoint)
            return false;

        rect = Rect.MinMaxRect(rectMin.x, rectMin.y, rectMax.x, rectMax.y);
        return true;
    }

    static bool PaddedOverlaps(Rect a, Rect b, float padding)
    {
        a.xMin -= padding;
        a.yMin -= padding;
        a.xMax += padding;
        a.yMax += padding;
        return a.Overlaps(b);
    }
}
