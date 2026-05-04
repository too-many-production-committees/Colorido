public static class ProjectionViewUtility
{
    public static ProjectionViewMask ToMask(ProjectionView view)
    {
        switch (view)
        {
            case ProjectionView.Right:
                return ProjectionViewMask.Right;
            case ProjectionView.Back:
                return ProjectionViewMask.Back;
            case ProjectionView.Left:
                return ProjectionViewMask.Left;
        }

        return ProjectionViewMask.Front;
    }
}
