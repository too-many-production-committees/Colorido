using UnityEngine;

public enum ProjectionProxyKind
{
    Solid,
    Interactable
}

public class ProjectionPhysicsProxy : MonoBehaviour
{
    public GameObject sourceObject;
    public ProjectionWalkable sourceWalkable;
    public ProjectionSolid sourceSolid;
    public ProjectionInteractable sourceInteractable;
    public ProjectionProxyKind kind;
    public float sourceDepth;

    public void Initialize(
        GameObject source,
        ProjectionWalkable walkable,
        ProjectionSolid solid,
        ProjectionInteractable interactable,
        ProjectionProxyKind proxyKind,
        float depth)
    {
        sourceObject = source;
        sourceWalkable = walkable;
        sourceSolid = solid;
        sourceInteractable = interactable;
        kind = proxyKind;
        sourceDepth = depth;
    }
}
