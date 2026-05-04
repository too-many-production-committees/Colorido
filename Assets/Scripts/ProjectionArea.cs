using UnityEngine;

public class ProjectionArea : MonoBehaviour
{
    public ProjectionViewMask activeViews = ProjectionViewMask.All;

    public bool IsActiveForView(ProjectionViewMask view)
    {
        return view != ProjectionViewMask.None && (activeViews & view) != 0;
    }
}
