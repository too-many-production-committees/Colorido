using UnityEngine;

public class ProjectionPhysicsBuilder : MonoBehaviour
{
    struct WalkableProjection
    {
        public GameObject source;
        public ProjectionWalkable walkable;
        public Bounds bounds;
        public Rect rect;
        public Vector2 padding;
    }

    public ProjectionManager projectionManager;
    public Transform projectionPhysicsRoot;
    public string rootName = "ProjectionPhysicsRoot";
    public LayerMask proxyLayer;
    public bool rebuildOnStart = true;
    public bool rebuildOnCameraRotateFinished = false;
    public bool includeInactiveMarkers = false;
    public float minimumColliderSize = 0.02f;
    public PhysicsMaterial2D zeroFrictionMaterial;
    public bool useTopSurfaceForWalkables = true;
    public bool constrainWalkableEdges = false;
    public float walkableEdgeThickness = 0.08f;
    public float walkableEdgeHeight = 2.5f;
    public float walkableEdgeMergeTolerance = 0.03f;

    void Awake()
    {
        ResolveReferences();
        EnsureRoot();
    }

    void OnEnable()
    {
        ResolveReferences();

        if (rebuildOnCameraRotateFinished &&
            projectionManager != null &&
            projectionManager.cameraController != null)
            projectionManager.cameraController.OnCameraRotateFinished += Rebuild;
    }

    void OnDisable()
    {
        if (projectionManager != null &&
            projectionManager.cameraController != null)
            projectionManager.cameraController.OnCameraRotateFinished -= Rebuild;
    }

    void Start()
    {
        if (rebuildOnStart)
            Rebuild();
    }

    public void Rebuild()
    {
        ResolveReferences();

        if (projectionManager == null)
        {
            Debug.LogWarning("ProjectionPhysicsBuilder needs a ProjectionManager.");
            return;
        }

        projectionManager.UpdateProjectionAxes();
        EnsureRoot();
        ClearRoot();
        BuildWalkables();
        BuildSolids();
        BuildInteractables();
    }

    void ResolveReferences()
    {
        if (projectionManager == null)
            projectionManager = FindFirstObjectByType<ProjectionManager>();
    }

    ProjectionView GetCurrentView()
    {
        if (projectionManager == null)
            return ProjectionView.Front;

        return projectionManager.CurrentView;
    }

    bool IsAreaActiveForCurrentView(GameObject source)
    {
        ProjectionArea area = source.GetComponent<ProjectionArea>();
        if (area == null)
            return true;

        return area.IsActiveForView(ProjectionViewUtility.ToMask(GetCurrentView()));
    }

    void EnsureRoot()
    {
        if (projectionPhysicsRoot != null)
            return;

        GameObject existingRoot = GameObject.Find(rootName);
        if (existingRoot != null)
        {
            projectionPhysicsRoot = existingRoot.transform;
            return;
        }

        GameObject root = new GameObject(rootName);
        projectionPhysicsRoot = root.transform;
    }

    void ClearRoot()
    {
        for (int i = projectionPhysicsRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = projectionPhysicsRoot.GetChild(i);
            if (Application.isPlaying)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    void BuildWalkables()
    {
        ProjectionWalkable[] walkables = FindObjectsByType<ProjectionWalkable>(
            includeInactiveMarkers ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        WalkableProjection[] activeWalkables = new WalkableProjection[walkables.Length];
        int activeCount = 0;

        for (int i = 0; i < walkables.Length; i++)
        {
            ProjectionWalkable walkable = walkables[i];
            if (walkable == null ||
                !walkable.CanProjectInView(GetCurrentView()) ||
                !IsAreaActiveForCurrentView(walkable.gameObject))
                continue;

            Bounds bounds = walkable.GetProjectionBounds();
            Rect rect = GetProjectedBoundsRect(bounds);
            rect.xMin -= walkable.padding.x;
            rect.xMax += walkable.padding.x;
            rect.yMin -= walkable.padding.y;
            rect.yMax += walkable.padding.y;

            activeWalkables[activeCount++] = new WalkableProjection
            {
                source = walkable.gameObject,
                walkable = walkable,
                bounds = bounds,
                rect = rect,
                padding = walkable.padding
            };

            CreateProxyFromRect(
                walkable.gameObject,
                walkable,
                null,
                null,
                ProjectionProxyKind.Solid,
                bounds,
                rect,
                false);
        }

        if (constrainWalkableEdges && !useTopSurfaceForWalkables)
            BuildWalkableEdgeConstraints(activeWalkables, activeCount);
    }

    void BuildSolids()
    {
        ProjectionSolid[] solids = FindObjectsByType<ProjectionSolid>(
            includeInactiveMarkers ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < solids.Length; i++)
        {
            ProjectionSolid solid = solids[i];
            if (solid == null ||
                !solid.CanProjectInView(GetCurrentView()) ||
                !IsAreaActiveForCurrentView(solid.gameObject))
                continue;

            CreateProxy(
                solid.gameObject,
                null,
                solid,
                null,
                ProjectionProxyKind.Solid,
                solid.GetProjectionBounds(),
                solid.padding,
                false);
        }
    }

    void BuildInteractables()
    {
        ProjectionInteractable[] interactables = FindObjectsByType<ProjectionInteractable>(
            includeInactiveMarkers ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < interactables.Length; i++)
        {
            ProjectionInteractable interactable = interactables[i];
            if (interactable == null ||
                !interactable.CanProjectInView(GetCurrentView()) ||
                !IsAreaActiveForCurrentView(interactable.gameObject))
                continue;

            CreateProxy(
                interactable.gameObject,
                null,
                null,
                interactable,
                ProjectionProxyKind.Interactable,
                interactable.GetProjectionBounds(),
                interactable.padding,
                true);
        }
    }

    void CreateProxy(
        GameObject source,
        ProjectionWalkable walkable,
        ProjectionSolid solid,
        ProjectionInteractable interactable,
        ProjectionProxyKind kind,
        Bounds bounds,
        Vector2 padding,
        bool isTrigger)
    {
        Rect projectionRect = GetProjectedBoundsRect(bounds);
        projectionRect.xMin -= padding.x;
        projectionRect.xMax += padding.x;
        projectionRect.yMin -= padding.y;
        projectionRect.yMax += padding.y;

        CreateProxyFromRect(
            source,
            walkable,
            solid,
            interactable,
            kind,
            bounds,
            projectionRect,
            isTrigger);
    }

    void CreateProxyFromRect(
        GameObject source,
        ProjectionWalkable walkable,
        ProjectionSolid solid,
        ProjectionInteractable interactable,
        ProjectionProxyKind kind,
        Bounds bounds,
        Rect projectionRect,
        bool isTrigger)
    {
        GameObject proxy = new GameObject($"{source.name}_{kind}_ProjectionProxy");
        proxy.transform.SetParent(projectionPhysicsRoot, false);
        proxy.transform.position = projectionRect.center;
        proxy.layer = GetProxyLayer(source);

        bool useTopSurface = !isTrigger &&
            ((walkable != null && useTopSurfaceForWalkables) ||
            (solid != null && solid.colliderMode == ProjectionSolidColliderMode.TopSurface));
        if (useTopSurface)
            CreateTopSurfaceCollider(proxy, projectionRect);
        else
            CreateBoxCollider(proxy, projectionRect, isTrigger);

        ProjectionPhysicsProxy proxyLink = proxy.AddComponent<ProjectionPhysicsProxy>();
        proxyLink.Initialize(
            source,
            walkable,
            solid,
            interactable,
            kind,
            projectionManager.WorldToProjectionDepth(bounds.center));
    }

    void BuildWalkableEdgeConstraints(WalkableProjection[] walkables, int count)
    {
        for (int i = 0; i < count; i++)
        {
            WalkableProjection current = walkables[i];

            if (!HasWalkableNeighborAtEdge(walkables, count, i, false))
                CreateWalkableEdgeProxy(current, false);

            if (!HasWalkableNeighborAtEdge(walkables, count, i, true))
                CreateWalkableEdgeProxy(current, true);
        }
    }

    bool HasWalkableNeighborAtEdge(WalkableProjection[] walkables, int count, int currentIndex, bool rightEdge)
    {
        Rect current = walkables[currentIndex].rect;
        float edgeX = rightEdge ? current.xMax : current.xMin;
        float tolerance = Mathf.Max(0.001f, walkableEdgeMergeTolerance);

        for (int i = 0; i < count; i++)
        {
            if (i == currentIndex)
                continue;

            Rect other = walkables[i].rect;
            float neighborEdgeX = rightEdge ? other.xMin : other.xMax;
            if (Mathf.Abs(neighborEdgeX - edgeX) > tolerance)
                continue;

            if (other.yMin < current.yMax - tolerance &&
                other.yMax > current.yMin + tolerance)
                return true;
        }

        return false;
    }

    void CreateWalkableEdgeProxy(WalkableProjection walkable, bool rightEdge)
    {
        float thickness = Mathf.Max(minimumColliderSize, walkableEdgeThickness);
        float height = Mathf.Max(minimumColliderSize, walkableEdgeHeight);
        Rect sourceRect = walkable.rect;
        float edgeX = rightEdge ? sourceRect.xMax : sourceRect.xMin;
        Rect edgeRect = Rect.MinMaxRect(
            edgeX - thickness * 0.5f,
            sourceRect.yMax,
            edgeX + thickness * 0.5f,
            sourceRect.yMax + height);

        GameObject proxy = new GameObject($"{walkable.source.name}_{(rightEdge ? "Right" : "Left")}_WalkableEdgeProxy");
        proxy.transform.SetParent(projectionPhysicsRoot, false);
        proxy.transform.position = edgeRect.center;
        proxy.layer = GetProxyLayer(walkable.source);
        CreateBoxCollider(proxy, edgeRect, false);

        ProjectionPhysicsProxy proxyLink = proxy.AddComponent<ProjectionPhysicsProxy>();
        proxyLink.Initialize(
            walkable.source,
            walkable.walkable,
            null,
            null,
            ProjectionProxyKind.Solid,
            projectionManager.WorldToProjectionDepth(walkable.bounds.center));
    }

    void CreateTopSurfaceCollider(GameObject proxy, Rect projectionRect)
    {
        EdgeCollider2D edge = proxy.AddComponent<EdgeCollider2D>();
        edge.points = new[]
        {
            new Vector2(projectionRect.xMin - projectionRect.center.x, projectionRect.yMax - projectionRect.center.y),
            new Vector2(projectionRect.xMax - projectionRect.center.x, projectionRect.yMax - projectionRect.center.y)
        };
        edge.sharedMaterial = GetZeroFrictionMaterial();
    }

    void CreateBoxCollider(GameObject proxy, Rect projectionRect, bool isTrigger)
    {
        BoxCollider2D box = proxy.AddComponent<BoxCollider2D>();
        box.size = new Vector2(
            Mathf.Max(minimumColliderSize, projectionRect.width),
            Mathf.Max(minimumColliderSize, projectionRect.height));
        box.isTrigger = isTrigger;
        box.sharedMaterial = GetZeroFrictionMaterial();
    }

    PhysicsMaterial2D GetZeroFrictionMaterial()
    {
        if (zeroFrictionMaterial != null)
            return zeroFrictionMaterial;

        zeroFrictionMaterial = new PhysicsMaterial2D("Projection Zero Friction")
        {
            friction = 0f,
            bounciness = 0f
        };

        return zeroFrictionMaterial;
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

        Vector2 rectMin = Vector2.one * float.PositiveInfinity;
        Vector2 rectMax = Vector2.one * float.NegativeInfinity;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 point = projectionManager.WorldToProjection2D(corners[i]);
            rectMin = Vector2.Min(rectMin, point);
            rectMax = Vector2.Max(rectMax, point);
        }

        return Rect.MinMaxRect(rectMin.x, rectMin.y, rectMax.x, rectMax.y);
    }

    int GetProxyLayer(GameObject source)
    {
        int mask = proxyLayer.value;
        if (mask == 0)
            return source.layer;

        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) != 0)
                return layer;
        }

        return source.layer;
    }
}
