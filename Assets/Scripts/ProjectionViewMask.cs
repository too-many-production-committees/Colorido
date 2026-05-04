using System;

public enum ProjectionView
{
    Front,
    Right,
    Back,
    Left
}

public enum ProjectionViewReferenceMode
{
    CameraIndex,
    UnityWorldAxis
}

[Flags]
public enum ProjectionViewMask
{
    None = 0,
    Front = 1 << 0,
    Right = 1 << 1,
    Back = 1 << 2,
    Left = 1 << 3,
    All = Front | Right | Back | Left
}
