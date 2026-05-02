using UnityEngine;

public class ProjectionManager : MonoBehaviour
{
    public FezCameraController cameraController;
    public Transform player;
    public PlatformMarker[] platforms;
    public float overlapTolerance = 0.05f;
    public float snapHeightTolerance = 1.5f;
    public float standingHeightTolerance = 0.25f;

    CharacterController controller;

    Transform cachedPlatform;

    void Awake()
    {
        if (player != null)
            controller = player.GetComponent<CharacterController>();
    }

    public void CacheBeforeRotate()
    {
        if (platforms == null || platforms.Length < 2)
            return;

        if (cameraController == null || player == null)
            return;

        cachedPlatform = GetCurrentPlatform();
    }

    public void TrySnapPlayer()
    {
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
