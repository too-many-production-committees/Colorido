using UnityEngine;

/// <summary>
/// Marks an object as eligible for projected player occlusion checks.
/// Only objects with this component should be considered by occlusion code.
/// </summary>
public class ProjectionOccluder : MonoBehaviour
{
    public bool participateInOcclusion = true;

    public bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;

        if (!participateInOcclusion)
            return false;

        Collider objectCollider = GetComponent<Collider>();
        if (objectCollider != null)
        {
            bounds = objectCollider.bounds;
            return true;
        }

        Renderer objectRenderer = GetComponent<Renderer>();
        if (objectRenderer != null)
        {
            bounds = objectRenderer.bounds;
            return true;
        }

        return false;
    }
}
