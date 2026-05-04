using UnityEngine;

public class ProjectionWalkable : MonoBehaviour
{
    public bool activeInProjection = true;
    public bool includeChildren = true;
    public ProjectionViewMask activeViews = ProjectionViewMask.Front;
    public bool alwaysProjected = false;
    public Vector2 padding = Vector2.zero;

    public bool CanProjectInView(ProjectionView currentView)
    {
        if (!activeInProjection)
            return false;

        if (alwaysProjected)
            return true;

        return (activeViews & ProjectionViewUtility.ToMask(currentView)) != 0;
    }

    public Bounds GetProjectionBounds()
    {
        if (ProjectionBoundsUtility.TryGetBounds(gameObject, includeChildren, out Bounds bounds))
            return bounds;

        return new Bounds(transform.position, Vector3.one);
    }
}
