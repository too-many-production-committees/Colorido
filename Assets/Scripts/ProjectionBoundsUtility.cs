using UnityEngine;

public static class ProjectionBoundsUtility
{
    public static bool TryGetBounds(GameObject source, bool includeChildren, out Bounds bounds)
    {
        bounds = default;

        if (source == null)
            return false;

        bool hasBounds = false;
        Collider[] colliders = includeChildren
            ? source.GetComponentsInChildren<Collider>()
            : source.GetComponents<Collider>();

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider targetCollider = colliders[i];
            if (targetCollider == null || !targetCollider.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = targetCollider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(targetCollider.bounds);
            }
        }

        if (hasBounds)
            return true;

        Renderer[] renderers = includeChildren
            ? source.GetComponentsInChildren<Renderer>()
            : source.GetComponents<Renderer>();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null || !targetRenderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = targetRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(targetRenderer.bounds);
            }
        }

        return hasBounds;
    }
}
