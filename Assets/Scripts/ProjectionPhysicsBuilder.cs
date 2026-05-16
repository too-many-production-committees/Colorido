using System.Collections.Generic;
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
    public bool buildWalkableProjectionProxies = true;
    public bool require3DContactForPlatformProjection = true;
    public float platformHorizontalContactTolerance = 0.1f;
    public float platformGroundContactHeightTolerance = 0.25f;
    public bool buildInteractableTriggerProxies = true;
    public bool buildInteractablePhysicsProxies = false;
    public bool rebuildWhenPlayerMovesForContactFiltering = true;
    public float playerContactRebuildDistance = 0.05f;
    public float minimumColliderSize = 0.02f;
    public PhysicsMaterial2D zeroFrictionMaterial;
    public bool useTopSurfaceForWalkables = true;
    public bool constrainWalkableEdges = false;
    public float walkableEdgeThickness = 0.08f;
    public float walkableEdgeHeight = 2.5f;
    public float walkableEdgeMergeTolerance = 0.03f;

    readonly HashSet<GameObject> builtCollisionSources = new HashSet<GameObject>();
    Transform cachedPlayer;
    CharacterController cachedPlayerController;
    Vector3 lastPlayerFeetPosition;
    bool hasLastPlayerFeetPosition;
    bool isRebuilding;

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

    void Update()
    {
        if (!Application.isPlaying ||
            !rebuildWhenPlayerMovesForContactFiltering ||
            !require3DContactForPlatformProjection ||
            isRebuilding)
            return;

        Vector3 feet = GetPlayerFeetWorldPosition();
        if (!hasLastPlayerFeetPosition)
        {
            lastPlayerFeetPosition = feet;
            hasLastPlayerFeetPosition = true;
            return;
        }

        float rebuildDistance = Mathf.Max(0.001f, playerContactRebuildDistance);
        if ((feet - lastPlayerFeetPosition).sqrMagnitude < rebuildDistance * rebuildDistance)
            return;

        Rebuild();
    }

    public void Rebuild()
    {
        if (isRebuilding)
            return;

        isRebuilding = true;
        ResolveReferences();

        if (projectionManager == null)
        {
            Debug.LogWarning("ProjectionPhysicsBuilder needs a ProjectionManager.");
            isRebuilding = false;
            return;
        }

        projectionManager.UpdateProjectionAxes();
        EnsureRoot();
        ClearRoot();
        builtCollisionSources.Clear();
        CachePlayerReferences();
        lastPlayerFeetPosition = GetPlayerFeetWorldPosition();
        hasLastPlayerFeetPosition = true;
        BuildRoleMarkedCollisionObjects();
        BuildWalkables();
        BuildSolids();
        BuildInteractables();
        isRebuilding = false;
    }

    void ResolveReferences()
    {
        if (projectionManager == null)
            projectionManager = FindFirstObjectByType<ProjectionManager>();

        CachePlayerReferences();
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

    void BuildRoleMarkedCollisionObjects()
    {
        ProjectionObjectRoleMarker[] markers = FindObjectsByType<ProjectionObjectRoleMarker>(
            includeInactiveMarkers ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < markers.Length; i++)
        {
            ProjectionObjectRoleMarker marker = markers[i];
            if (marker == null || !RoleHasCollision(marker.role))
                continue;

            GameObject source = marker.gameObject;
            if (builtCollisionSources.Contains(source))
                continue;

            ProjectionSolid solid = source.GetComponent<ProjectionSolid>();
            bool isGround = marker.role == ProjectionObjectRole.Ground;
            if (!isGround)
            {
                if (solid != null && !solid.CanProjectInView(GetCurrentView()))
                    continue;

                if (!IsAreaActiveForCurrentView(source))
                    continue;
            }

            Bounds bounds = solid != null
                ? solid.GetProjectionBounds()
                : GetProjectionBounds(source, true);

            if (!ShouldBuildCollisionProxy(marker.role, solid, bounds))
                continue;

            CreateProxy(
                source,
                null,
                solid,
                null,
                ProjectionProxyKind.Solid,
                bounds,
                solid != null ? solid.padding : Vector2.zero,
                false,
                isGround || marker.role == ProjectionObjectRole.Platform);
            builtCollisionSources.Add(source);
        }
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

    void CachePlayerReferences()
    {
        if (projectionManager != null && projectionManager.player != null)
            cachedPlayer = projectionManager.player;

        if (cachedPlayer == null)
        {
            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player != null)
                cachedPlayer = player.transform;
        }

        if (cachedPlayer == null)
        {
            cachedPlayerController = null;
            return;
        }

        if (cachedPlayerController == null ||
            cachedPlayerController.transform != cachedPlayer)
            cachedPlayerController = cachedPlayer.GetComponent<CharacterController>();
    }

    ProjectionObjectRole GetObjectRole(GameObject source)
    {
        if (source == null)
            return ProjectionObjectRole.None;

        ProjectionObjectRoleMarker marker = source.GetComponent<ProjectionObjectRoleMarker>();
        return marker != null ? marker.role : ProjectionObjectRole.None;
    }

    bool RoleHasCollision(ProjectionObjectRole role)
    {
        return role == ProjectionObjectRole.Collision ||
            role == ProjectionObjectRole.CollisionAndInteractable ||
            role == ProjectionObjectRole.Ground ||
            role == ProjectionObjectRole.Platform ||
            role == ProjectionObjectRole.Obstacle;
    }

    bool ShouldSkipInteractableCollision(GameObject source, ProjectionObjectRole role)
    {
        if (buildInteractablePhysicsProxies)
            return false;

        if (role == ProjectionObjectRole.CollisionAndInteractable)
            return false;

        if (role != ProjectionObjectRole.None &&
            role != ProjectionObjectRole.Interactable)
            return false;

        return source != null && source.GetComponent<ProjectionInteractable>() != null;
    }

    Bounds GetProjectionBounds(GameObject source, bool includeChildren)
    {
        if (ProjectionBoundsUtility.TryGetBounds(source, includeChildren, out Bounds bounds))
            return bounds;

        return new Bounds(source.transform.position, Vector3.one);
    }

    bool ShouldBuildCollisionProxy(
        ProjectionObjectRole role,
        ProjectionSolid solid,
        Bounds bounds)
    {
        if (role == ProjectionObjectRole.Ground)
            return true;

        if (!require3DContactForPlatformProjection)
            return true;

        if (solid != null &&
            solid.alwaysProjected &&
            role != ProjectionObjectRole.Platform &&
            role != ProjectionObjectRole.Obstacle)
            return true;

        if (role == ProjectionObjectRole.Platform ||
            (role == ProjectionObjectRole.None &&
            solid != null &&
            solid.colliderMode == ProjectionSolidColliderMode.TopSurface))
            return IsPlayerFeetNearPlatformTop(bounds);

        return IsPlayerNearBounds3D(bounds);
    }

    bool ShouldBuildWalkableProxy(
        ProjectionObjectRole role,
        ProjectionWalkable walkable,
        Bounds bounds)
    {
        if (role == ProjectionObjectRole.Ground)
            return true;

        if (!require3DContactForPlatformProjection)
            return true;

        if (walkable != null &&
            walkable.alwaysProjected &&
            role != ProjectionObjectRole.Platform &&
            role != ProjectionObjectRole.Obstacle)
            return true;

        if (role == ProjectionObjectRole.Platform)
            return IsPlayerFeetNearPlatformTop(bounds);

        return IsPlayerNearBounds3D(bounds);
    }

    bool CanBuildInteractablePhysicsProxy(GameObject source)
    {
        ProjectionObjectRole role = GetObjectRole(source);
        return role != ProjectionObjectRole.Interactable;
    }

    bool IsPlayerFeetNearPlatformTop(Bounds bounds)
    {
        Vector3 feet = GetPlayerFeetWorldPosition();
        if (cachedPlayer == null)
            return false;

        float horizontalTolerance = Mathf.Max(0f, platformHorizontalContactTolerance);
        float heightTolerance = Mathf.Max(0f, platformGroundContactHeightTolerance);

        bool horizontalInside =
            feet.x >= bounds.min.x - horizontalTolerance &&
            feet.x <= bounds.max.x + horizontalTolerance &&
            feet.z >= bounds.min.z - horizontalTolerance &&
            feet.z <= bounds.max.z + horizontalTolerance;

        bool nearTop =
            Mathf.Abs(feet.y - bounds.max.y) <= heightTolerance ||
            feet.y >= bounds.max.y - heightTolerance;

        return horizontalInside && nearTop;
    }

    bool IsPlayerNearBounds3D(Bounds bounds)
    {
        Bounds playerBounds = GetPlayerWorldBounds();
        if (cachedPlayer == null)
            return false;

        float horizontalTolerance = Mathf.Max(0f, platformHorizontalContactTolerance);
        float heightTolerance = Mathf.Max(0f, platformGroundContactHeightTolerance);
        bounds.Expand(new Vector3(
            horizontalTolerance * 2f,
            heightTolerance * 2f,
            horizontalTolerance * 2f));

        return bounds.Intersects(playerBounds);
    }

    Vector3 GetPlayerFeetWorldPosition()
    {
        CachePlayerReferences();

        if (cachedPlayerController != null)
        {
            Bounds bounds = cachedPlayerController.bounds;
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        if (cachedPlayer == null)
            return Vector3.zero;

        return cachedPlayer.position;
    }

    Bounds GetPlayerWorldBounds()
    {
        CachePlayerReferences();

        if (cachedPlayerController != null)
            return cachedPlayerController.bounds;

        if (cachedPlayer == null)
            return new Bounds(Vector3.zero, Vector3.zero);

        Collider playerCollider = cachedPlayer.GetComponent<Collider>();
        if (playerCollider != null)
            return playerCollider.bounds;

        return new Bounds(cachedPlayer.position, Vector3.one * 0.5f);
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
            ProjectionObjectRole role = GetObjectRole(walkable != null ? walkable.gameObject : null);
            bool isGround = role == ProjectionObjectRole.Ground;
            if (walkable == null ||
                ShouldSkipInteractableCollision(walkable.gameObject, role) ||
                builtCollisionSources.Contains(walkable.gameObject) ||
                (!isGround && !buildWalkableProjectionProxies) ||
                (!isGround && !walkable.CanProjectInView(GetCurrentView())) ||
                (!isGround && !IsAreaActiveForCurrentView(walkable.gameObject)))
                continue;

            Bounds bounds = walkable.GetProjectionBounds();
            if (!ShouldBuildWalkableProxy(role, walkable, bounds))
                continue;

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
                false,
                isGround);
            builtCollisionSources.Add(walkable.gameObject);
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
            ProjectionObjectRole role = GetObjectRole(solid != null ? solid.gameObject : null);
            bool isGround = role == ProjectionObjectRole.Ground;
            if (solid == null ||
                ShouldSkipInteractableCollision(solid.gameObject, role) ||
                builtCollisionSources.Contains(solid.gameObject) ||
                (!isGround && !solid.CanProjectInView(GetCurrentView())) ||
                (!isGround && !IsAreaActiveForCurrentView(solid.gameObject)))
                continue;

            Bounds bounds = solid.GetProjectionBounds();
            if (!ShouldBuildCollisionProxy(role, solid, bounds))
                continue;

            CreateProxy(
                solid.gameObject,
                null,
                solid,
                null,
                ProjectionProxyKind.Solid,
                bounds,
                solid.padding,
                false,
                isGround);
            builtCollisionSources.Add(solid.gameObject);
        }
    }

    void BuildInteractables()
    {
        if (!buildInteractableTriggerProxies)
            return;

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

            Bounds bounds = interactable.GetProjectionBounds();

            if (buildInteractableTriggerProxies)
            {
                CreateProxy(
                    interactable.gameObject,
                    null,
                    null,
                    interactable,
                    ProjectionProxyKind.Interactable,
                    bounds,
                    interactable.padding,
                    true);
            }

            if (buildInteractablePhysicsProxies && CanBuildInteractablePhysicsProxy(interactable.gameObject))
            {
                CreateProxy(
                    interactable.gameObject,
                    null,
                    null,
                    interactable,
                    ProjectionProxyKind.Solid,
                    bounds,
                    interactable.padding,
                    false);
            }
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
        bool isTrigger,
        bool forceTopSurface = false)
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
            isTrigger,
            forceTopSurface);
    }

    void CreateProxyFromRect(
        GameObject source,
        ProjectionWalkable walkable,
        ProjectionSolid solid,
        ProjectionInteractable interactable,
        ProjectionProxyKind kind,
        Bounds bounds,
        Rect projectionRect,
        bool isTrigger,
        bool forceTopSurface = false)
    {
        GameObject proxy = new GameObject($"{source.name}_{kind}_ProjectionProxy");
        proxy.transform.SetParent(projectionPhysicsRoot, false);
        proxy.transform.position = projectionRect.center;
        proxy.layer = GetProxyLayer(source);

        ProjectionObjectRole role = GetObjectRole(source);
        bool useTopSurface = !isTrigger &&
            role != ProjectionObjectRole.Obstacle &&
            (forceTopSurface ||
            ((walkable != null && useTopSurfaceForWalkables) ||
            (solid != null && GetEffectiveSolidColliderMode(solid) == ProjectionSolidColliderMode.TopSurface)));
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

    ProjectionSolidColliderMode GetEffectiveSolidColliderMode(ProjectionSolid solid)
    {
        if (solid == null)
            return ProjectionSolidColliderMode.Auto;

        ProjectionObjectRole role = GetObjectRole(solid.gameObject);
        switch (role)
        {
            case ProjectionObjectRole.Ground:
            case ProjectionObjectRole.Platform:
                return ProjectionSolidColliderMode.TopSurface;

            case ProjectionObjectRole.Obstacle:
                return ProjectionSolidColliderMode.Box;

            default:
                return solid.colliderMode;
        }
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
        edge.isTrigger = false;
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
