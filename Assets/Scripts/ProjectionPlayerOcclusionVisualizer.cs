using UnityEngine;

[DisallowMultipleComponent]
public class ProjectionPlayerOcclusionVisualizer : MonoBehaviour
{
    [Header("References")]
    public ProjectionManager projectionManager;
    public Transform player;
    public Renderer playerRenderer;
    public GameObject projectionVisualPrefab;

    [Header("Visibility")]
    public float showThreshold = 0.05f;
    public float hideThreshold = 0.01f;
    public float projectionSurfaceOffset = 0.02f;

    [Header("Debug")]
    public bool logDebug;

    GameObject projectionVisual;
    bool isVisible;

    void Awake()
    {
        ResolveReferences();
        EnsureProjectionVisual();
        SetProjectionVisible(false);
    }

    void OnEnable()
    {
        ResolveReferences();
        EnsureProjectionVisual();
        SetProjectionVisible(false);
    }

    void OnDisable()
    {
        SetProjectionVisible(false);
    }

    void OnDestroy()
    {
        if (projectionVisual != null)
            DestroyGeneratedObject(projectionVisual);
    }

    void LateUpdate()
    {
        ResolveReferences();

        if (projectionManager == null || player == null)
        {
            SetProjectionVisible(false);
            return;
        }

        projectionManager.UpdateProjectionAxes();

        if (!TryEvaluateOcclusion(out OcclusionResult result))
        {
            SetProjectionVisible(false);
            return;
        }

        float threshold = isVisible ? hideThreshold : showThreshold;
        bool shouldShow = result.occlusionRatio > threshold;
        SetProjectionVisible(shouldShow);

        if (!shouldShow)
            return;

        EnsureProjectionVisual();
        if (projectionVisual == null)
            return;

        projectionVisual.transform.position = result.visualPosition;
        projectionVisual.transform.rotation = Quaternion.LookRotation(
            projectionManager.viewForward,
            projectionManager.viewUp);
        projectionVisual.transform.localScale = new Vector3(
            Mathf.Max(0.01f, result.playerProjectionRect.width),
            Mathf.Max(0.01f, result.playerProjectionRect.height),
            1f);

        if (logDebug)
            Debug.Log($"[ProjectionPlayerOcclusionVisualizer] Showing projection. Ratio: {result.occlusionRatio:0.00}. Occluder: {result.occluder.name}.", this);
    }

    void ResolveReferences()
    {
        if (projectionManager == null)
            projectionManager = FindFirstObjectByType<ProjectionManager>();

        if (player == null && projectionManager != null)
            player = projectionManager.player;

        if (playerRenderer == null && player != null)
        {
            Renderer[] renderers = player.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled)
                {
                    playerRenderer = renderers[i];
                    break;
                }
            }
        }
    }

    bool TryEvaluateOcclusion(out OcclusionResult bestResult)
    {
        bestResult = default;

        if (!TryGetPlayerBounds(out Bounds playerBounds))
            return false;

        Rect playerRect = GetProjectedBoundsRect(playerBounds);
        float playerArea = playerRect.width * playerRect.height;
        if (playerArea <= 0.0001f)
            return false;

        Camera projectionCamera = GetProjectionCamera();
        if (projectionCamera == null)
            return false;

        float playerDistance = GetDistanceFromCameraAlongView(playerBounds.center, projectionCamera.transform);
        ProjectionOccluder[] occluders = FindObjectsByType<ProjectionOccluder>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        bool found = false;
        for (int i = 0; i < occluders.Length; i++)
        {
            ProjectionOccluder occluder = occluders[i];
            if (!IsValidOccluder(occluder))
                continue;

            if (!occluder.TryGetBounds(out Bounds occluderBounds))
                continue;

            float occluderDistance = GetDistanceFromCameraAlongView(occluderBounds.center, projectionCamera.transform);
            if (occluderDistance >= playerDistance)
                continue;

            Rect occluderRect = GetProjectedBoundsRect(occluderBounds);
            if (!TryGetIntersection(playerRect, occluderRect, out Rect overlap))
                continue;

            float ratio = Mathf.Clamp01((overlap.width * overlap.height) / playerArea);
            if (ratio <= 0f)
                continue;

            float visualDepth = GetNearestDepthToCamera(occluderBounds) - Mathf.Max(0f, projectionSurfaceOffset);
            Vector2 playerProjectionPosition = projectionManager.WorldToProjection2D(playerBounds.center);
            Vector3 visualPosition = projectionManager.Projection2DToWorld(playerProjectionPosition, visualDepth);

            if (!found ||
                ratio > bestResult.occlusionRatio ||
                (Mathf.Approximately(ratio, bestResult.occlusionRatio) &&
                occluderDistance < bestResult.occluderDistance))
            {
                found = true;
                bestResult = new OcclusionResult
                {
                    occluder = occluder,
                    occlusionRatio = ratio,
                    occluderDistance = occluderDistance,
                    playerProjectionRect = playerRect,
                    visualPosition = visualPosition
                };
            }
        }

        return found;
    }

    bool IsValidOccluder(ProjectionOccluder occluder)
    {
        if (occluder == null || !occluder.isActiveAndEnabled || !occluder.participateInOcclusion)
            return false;

        if (player == null)
            return false;

        Transform occluderTransform = occluder.transform;
        return occluderTransform != player && !occluderTransform.IsChildOf(player);
    }

    bool TryGetPlayerBounds(out Bounds bounds)
    {
        bounds = default;

        if (player == null)
            return false;

        if (playerRenderer != null && playerRenderer.enabled)
        {
            bounds = playerRenderer.bounds;
            return true;
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            bounds = controller.bounds;
            return true;
        }

        Collider playerCollider = player.GetComponent<Collider>();
        if (playerCollider != null)
        {
            bounds = playerCollider.bounds;
            return true;
        }

        Renderer renderer = player.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            bounds = renderer.bounds;
            return true;
        }

        return false;
    }

    Rect GetProjectedBoundsRect(Bounds bounds)
    {
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

        Vector2 first = projectionManager.WorldToProjection2D(corners[0]);
        float minX = first.x;
        float maxX = first.x;
        float minY = first.y;
        float maxY = first.y;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 projected = projectionManager.WorldToProjection2D(corners[i]);
            minX = Mathf.Min(minX, projected.x);
            maxX = Mathf.Max(maxX, projected.x);
            minY = Mathf.Min(minY, projected.y);
            maxY = Mathf.Max(maxY, projected.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    bool TryGetIntersection(Rect a, Rect b, out Rect intersection)
    {
        float minX = Mathf.Max(a.xMin, b.xMin);
        float maxX = Mathf.Min(a.xMax, b.xMax);
        float minY = Mathf.Max(a.yMin, b.yMin);
        float maxY = Mathf.Min(a.yMax, b.yMax);

        if (minX >= maxX || minY >= maxY)
        {
            intersection = default;
            return false;
        }

        intersection = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    float GetNearestDepthToCamera(Bounds bounds)
    {
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

        float nearestDepth = projectionManager.WorldToProjectionDepth(corners[0]);
        for (int i = 1; i < corners.Length; i++)
            nearestDepth = Mathf.Min(nearestDepth, projectionManager.WorldToProjectionDepth(corners[i]));

        return nearestDepth;
    }

    float GetDistanceFromCameraAlongView(Vector3 worldPosition, Transform cameraTransform)
    {
        return Vector3.Dot(
            worldPosition - cameraTransform.position,
            projectionManager.viewForward.normalized);
    }

    Camera GetProjectionCamera()
    {
        if (projectionManager != null && projectionManager.projectionCamera != null)
            return projectionManager.projectionCamera;

        return Camera.main;
    }

    void EnsureProjectionVisual()
    {
        if (projectionVisual != null)
            return;

        if (projectionVisualPrefab != null)
        {
            projectionVisual = Instantiate(projectionVisualPrefab, transform);
            projectionVisual.name = $"{projectionVisualPrefab.name}_OcclusionProjectionVisual";
        }
        else
        {
            projectionVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            projectionVisual.name = "PlayerOcclusionProjectionVisual";
            projectionVisual.transform.SetParent(transform, false);
            Collider visualCollider = projectionVisual.GetComponent<Collider>();
            if (visualCollider != null)
                DestroyGeneratedObject(visualCollider);

            MeshRenderer meshRenderer = projectionVisual.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                Material material = new Material(Shader.Find("Sprites/Default"));
                material.color = new Color(0.2f, 0.8f, 1f, 0.35f);
                meshRenderer.sharedMaterial = material;
            }
        }

        Collider[] visualColliders = projectionVisual.GetComponentsInChildren<Collider>();
        for (int i = 0; i < visualColliders.Length; i++)
            DestroyGeneratedObject(visualColliders[i]);

        Collider2D[] visualColliders2D = projectionVisual.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < visualColliders2D.Length; i++)
            DestroyGeneratedObject(visualColliders2D[i]);

        projectionVisual.hideFlags = HideFlags.DontSave;
    }

    void SetProjectionVisible(bool visible)
    {
        isVisible = visible;

        if (projectionVisual == null)
            return;

        projectionVisual.SetActive(visible);
    }

    void DestroyGeneratedObject(Object target)
    {
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    struct OcclusionResult
    {
        public ProjectionOccluder occluder;
        public float occlusionRatio;
        public float occluderDistance;
        public Rect playerProjectionRect;
        public Vector3 visualPosition;
    }
}
