using UnityEngine;

public class ProjectionManager : MonoBehaviour
{
    public FezCameraController cameraController;
    public Camera projectionCamera;
    public ProjectionViewReferenceMode currentViewReferenceMode = ProjectionViewReferenceMode.CameraIndex;
    public Transform player;
    public PlatformMarker[] platforms;
    public bool enablePlatformTransfer = false;
    public bool keepWorldUpAsProjectionUp = true;
    public float overlapTolerance = 0.05f;
    public float snapHeightTolerance = 1.5f;
    public float standingHeightTolerance = 0.25f;

    public Vector3 viewRight { get; private set; } = Vector3.right;
    public Vector3 viewUp { get; private set; } = Vector3.up;
    public Vector3 viewForward { get; private set; } = Vector3.forward;
    public ProjectionView CurrentView { get; private set; } = ProjectionView.Front;

    CharacterController controller;

    Transform cachedPlatform;

    void Awake()
    {
        if (player != null)
            controller = player.GetComponent<CharacterController>();

        ResolveReferences();
        UpdateProjectionAxes();
    }

    void OnEnable()
    {
        ResolveReferences();

        if (cameraController != null)
            cameraController.OnCameraRotateFinished += HandleCameraRotateFinished;
    }

    void OnDisable()
    {
        if (cameraController != null)
            cameraController.OnCameraRotateFinished -= HandleCameraRotateFinished;
    }

    void ResolveReferences()
    {
        if (cameraController == null)
            cameraController = FindFirstObjectByType<FezCameraController>();

        if (projectionCamera == null)
        {
            if (cameraController != null && cameraController.controlledCamera != null)
                projectionCamera = cameraController.controlledCamera;
            else
                projectionCamera = Camera.main;
        }
    }

    void HandleCameraRotateFinished()
    {
        UpdateProjectionAxes();
    }

    public void UpdateProjectionAxes()
    {
        ResolveReferences();
        UpdateCurrentView();

        Transform source = projectionCamera != null
            ? projectionCamera.transform
            : cameraController != null
                ? cameraController.transform
                : null;

        if (source == null)
        {
            viewRight = Vector3.right;
            viewUp = Vector3.up;
            viewForward = Vector3.forward;
            return;
        }

        viewForward = source.forward.normalized;
        viewUp = keepWorldUpAsProjectionUp ? Vector3.up : source.up.normalized;
        viewRight = Vector3.Cross(viewUp, viewForward).normalized;

        if (viewRight.sqrMagnitude < 0.0001f)
            viewRight = source.right.normalized;

        viewForward = Vector3.Cross(viewRight, viewUp).normalized;
    }

    void UpdateCurrentView()
    {
        if (currentViewReferenceMode == ProjectionViewReferenceMode.UnityWorldAxis)
        {
            CurrentView = GetCurrentViewFromUnityWorldAxis();
            return;
        }

        if (cameraController == null)
        {
            CurrentView = ProjectionView.Front;
            return;
        }

        switch (cameraController.CurrentIndex())
        {
            case 1:
                CurrentView = ProjectionView.Right;
                break;
            case 2:
                CurrentView = ProjectionView.Back;
                break;
            case 3:
                CurrentView = ProjectionView.Left;
                break;
            default:
                CurrentView = ProjectionView.Front;
                break;
        }
    }

    ProjectionView GetCurrentViewFromUnityWorldAxis()
    {
        Vector3 direction = Vector3.forward;

        if (cameraController != null)
            direction = cameraController.GetForward();
        else if (projectionCamera != null)
            direction = projectionCamera.transform.forward;

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return ProjectionView.Front;

        direction.Normalize();
        float absX = Mathf.Abs(direction.x);
        float absZ = Mathf.Abs(direction.z);

        if (absX > absZ)
            return direction.x >= 0f ? ProjectionView.Right : ProjectionView.Left;

        return direction.z >= 0f ? ProjectionView.Front : ProjectionView.Back;
    }

    public Vector2 WorldToProjection2D(Vector3 worldPos)
    {
        return new Vector2(
            Vector3.Dot(worldPos, viewRight),
            Vector3.Dot(worldPos, viewUp));
    }

    public float WorldToProjectionDepth(Vector3 worldPos)
    {
        return Vector3.Dot(worldPos, viewForward);
    }

    public Vector3 Projection2DToWorld(Vector2 pos2D, float depth)
    {
        return viewRight * pos2D.x + viewUp * pos2D.y + viewForward * depth;
    }

    public void CacheBeforeRotate()
    {
        if (!enablePlatformTransfer)
        {
            cachedPlatform = null;
            return;
        }

        if (platforms == null || platforms.Length < 2)
            return;

        if (cameraController == null || player == null)
            return;

        cachedPlatform = GetCurrentPlatform();
    }

    public void TrySnapPlayer()
    {
        if (!enablePlatformTransfer)
        {
            cachedPlatform = null;
            return;
        }

        if (platforms == null || platforms.Length < 2)
            return;

        if (cameraController == null || player == null)
            return;

        if (cachedPlatform == null)
            return;

        if (controller == null)
            controller = player.GetComponent<CharacterController>();

        int direction = cameraController.CurrentIndex();
        float playerHorizontal = ProjectHorizontal(player.position, direction);

        Transform targetPlatform = FindOverlappingPlatform(cachedPlatform, playerHorizontal);
        if (targetPlatform == null)
        {
            cachedPlatform = null;
            return;
        }

        Vector3 p = player.position;

        if (direction == 0 || direction == 2)
        {
            p.x = playerHorizontal;
            p.z = targetPlatform.position.z;
        }
        else
        {
            p.z = playerHorizontal;
            p.x = targetPlatform.position.x;
        }

        if (controller != null)
            controller.enabled = false;

        player.position = p;

        if (controller != null)
            controller.enabled = true;

        cachedPlatform = null;
    }

    Transform GetCurrentPlatform()
    {
        for (int i = 0; i < platforms.Length; i++)
        {
            Transform platform = platforms[i].transform;

            if (IsStandingOn(platform))
                return platform;
        }

        return null;
    }

    bool IsStandingOn(Transform platform)
    {
        Vector3 p = player.position;
        Bounds bounds = GetPlatformBounds(platform);

        bool insideFootprint =
            p.x >= bounds.min.x &&
            p.x <= bounds.max.x &&
            p.z >= bounds.min.z &&
            p.z <= bounds.max.z;

        float top = bounds.max.y;
        float feet = GetPlayerFeetY();

        bool nearTop = Mathf.Abs(feet - top) < standingHeightTolerance;

        return insideFootprint && nearTop;
    }

    Transform FindOverlappingPlatform(Transform sourcePlatform, float playerHorizontal)
    {
        int direction = cameraController.CurrentIndex();
        Rect sourceRect = GetProjectedRect(sourcePlatform, direction);

        for (int i = 0; i < platforms.Length; i++)
        {
            Transform candidate = platforms[i].transform;
            if (candidate == sourcePlatform)
                continue;

            Rect candidateRect = GetProjectedRect(candidate, direction);
            if (Overlaps(sourceRect, candidateRect) &&
                ContainsHorizontal(candidateRect, playerHorizontal))
                return candidate;
        }

        return null;
    }

    Rect GetProjectedRect(Transform platform, int direction)
    {
        Vector3 center = platform.position;
        Vector3 size = platform.localScale;

        float horizontal = ProjectHorizontal(center, direction);
        float width = direction == 0 || direction == 2 ? size.x : size.z;

        return new Rect(
            horizontal - width * 0.5f,
            center.y - size.y * 0.5f,
            width,
            size.y);
    }

    bool Overlaps(Rect a, Rect b)
    {
        return a.xMin <= b.xMax + overlapTolerance &&
            a.xMax >= b.xMin - overlapTolerance &&
            a.yMin <= b.yMax + overlapTolerance &&
            a.yMax >= b.yMin - overlapTolerance;
    }

    bool ContainsHorizontal(Rect rect, float horizontal)
    {
        return horizontal >= rect.xMin - overlapTolerance &&
            horizontal <= rect.xMax + overlapTolerance;
    }

    Bounds GetPlatformBounds(Transform platform)
    {
        Collider platformCollider = platform.GetComponent<Collider>();
        if (platformCollider != null)
            return platformCollider.bounds;

        Renderer platformRenderer = platform.GetComponent<Renderer>();
        if (platformRenderer != null)
            return platformRenderer.bounds;

        return new Bounds(platform.position, platform.localScale);
    }

    float GetPlayerFeetY()
    {
        if (controller == null)
            controller = player.GetComponent<CharacterController>();

        if (controller != null)
            return controller.bounds.min.y;

        return player.position.y;
    }

    float ProjectHorizontal(Vector3 world, int direction)
    {
        switch (direction)
        {
            case 0:
            case 2:
                return world.x;

            case 1:
            case 3:
                return world.z;
        }

        return 0f;
    }
}
