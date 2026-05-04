using UnityEngine;

[ExecuteAlways]
public class PlatformDepthColorizer : MonoBehaviour
{
    public FezCameraController cameraController;
    public PlatformMarker[] platforms;
    public Color baseColor = new Color(0.78f, 0.78f, 0.72f, 1f);
    public Color nearColor = new Color(0.95f, 0.9f, 0.72f, 1f);
    public Color farColor = new Color(0.38f, 0.55f, 0.82f, 1f);
    public float projectionOverlapTolerance = 0.05f;
    public bool colorOnlyOverlappingPlatforms = true;

    private MaterialPropertyBlock propertyBlock;

    void OnEnable()
    {
        RefreshColors();
    }

    void OnValidate()
    {
        RefreshColors();
    }

    void LateUpdate()
    {
        RefreshColors();
    }

    void RefreshColors()
    {
        ResolveReferences();

        if (platforms == null || platforms.Length == 0 || cameraController == null)
            return;

        float minDepth = float.PositiveInfinity;
        float maxDepth = float.NegativeInfinity;

        for (int i = 0; i < platforms.Length; i++)
        {
            if (platforms[i] == null)
                continue;

            if (colorOnlyOverlappingPlatforms && !HasProjectedOverlap(i))
                continue;

            float depth = GetViewDepth(platforms[i].transform.position);
            minDepth = Mathf.Min(minDepth, depth);
            maxDepth = Mathf.Max(maxDepth, depth);
        }

        bool hasDepthRange = maxDepth > minDepth + 0.0001f;

        for (int i = 0; i < platforms.Length; i++)
        {
            PlatformMarker platform = platforms[i];
            if (platform == null)
                continue;

            bool shouldColor = !colorOnlyOverlappingPlatforms || HasProjectedOverlap(i);
            Color color = baseColor;

            if (shouldColor && hasDepthRange)
            {
                float depth = GetViewDepth(platform.transform.position);
                float t = Mathf.InverseLerp(minDepth, maxDepth, depth);
                color = Color.Lerp(nearColor, farColor, t);
            }

            ApplyColor(platform, color);
        }
    }

    void ResolveReferences()
    {
        if (cameraController == null)
            cameraController = FindObjectOfType<FezCameraController>();

        if (platforms == null || platforms.Length == 0)
            platforms = FindObjectsOfType<PlatformMarker>();
    }

    bool HasProjectedOverlap(int sourceIndex)
    {
        PlatformMarker source = platforms[sourceIndex];
        if (source == null)
            return false;

        Rect sourceRect = GetProjectedRect(source.transform);

        for (int i = 0; i < platforms.Length; i++)
        {
            if (i == sourceIndex || platforms[i] == null)
                continue;

            if (Overlaps(sourceRect, GetProjectedRect(platforms[i].transform)))
                return true;
        }

        return false;
    }

    Rect GetProjectedRect(Transform platform)
    {
        Bounds bounds = GetBounds(platform);
        int direction = cameraController.CurrentIndex();

        float horizontal = direction == 0 || direction == 2 ? bounds.center.x : bounds.center.z;
        float width = direction == 0 || direction == 2 ? bounds.size.x : bounds.size.z;

        return new Rect(
            horizontal - width * 0.5f,
            bounds.min.y,
            width,
            bounds.size.y);
    }

    bool Overlaps(Rect a, Rect b)
    {
        return a.xMin <= b.xMax + projectionOverlapTolerance &&
            a.xMax >= b.xMin - projectionOverlapTolerance &&
            a.yMin <= b.yMax + projectionOverlapTolerance &&
            a.yMax >= b.yMin - projectionOverlapTolerance;
    }

    float GetViewDepth(Vector3 worldPosition)
    {
        Vector3 forward = cameraController.GetForward();
        return Vector3.Dot(worldPosition, forward);
    }

    Bounds GetBounds(Transform platform)
    {
        Collider platformCollider = platform.GetComponent<Collider>();
        if (platformCollider != null)
            return platformCollider.bounds;

        Renderer platformRenderer = platform.GetComponent<Renderer>();
        if (platformRenderer != null)
            return platformRenderer.bounds;

        return new Bounds(platform.position, platform.localScale);
    }

    void ApplyColor(PlatformMarker platform, Color color)
    {
        Renderer renderer = platform.GetComponent<Renderer>();
        if (renderer == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_Color", color);
        renderer.SetPropertyBlock(propertyBlock);
    }
}
