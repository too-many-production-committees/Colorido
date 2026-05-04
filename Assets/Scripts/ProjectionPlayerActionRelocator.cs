using System;
using UnityEngine;

[Flags]
public enum PlayerProjectionAreaState
{
    None = 0,
    NearArea = 1 << 0,
    FarArea = 1 << 1,
    SideArea = 1 << 2,
    CommonArea = 1 << 3,
    ExclusiveArea = 1 << 4
}

public class ProjectionPlayerActionRelocator : MonoBehaviour
{
    private const float ProjectionContainmentTolerance = 0.05f;

    [Header("References")]
    public ProjectionManager projectionManager;
    public Transform player;

    [Header("Occlusion")]
    [Range(0f, 1f)]
    public float occlusionThreshold = 0.5f;

    [Header("Debug")]
    public bool logStateChanges = true;

    public PlayerProjectionAreaState CurrentAreaState { get; private set; }
    public bool PendingNearTransfer => pendingNearTransfer;
    public float CurrentOcclusionRatio => currentOcclusionRatio;

    private bool pendingNearTransfer;
    private float currentOcclusionRatio;
    private ProjectionView recordedView;

    void Awake()
    {
        ResolveReferences();
    }

    void OnValidate()
    {
        occlusionThreshold = Mathf.Clamp01(occlusionThreshold);
    }

    void Update()
    {
        UpdatePendingNearTransfer();
    }

    public void OnCameraRotationComplete()
    {
        ResolveReferences();

        recordedView = projectionManager != null
            ? projectionManager.CurrentView
            : ProjectionView.Front;
        CurrentAreaState = EvaluatePlayerAreaState();
        pendingNearTransfer = false;

        Log($"Camera rotation complete. Recorded view: {recordedView}. Area state: {CurrentAreaState}. Occlusion: {currentOcclusionRatio:0.00}.");
    }

    public void OnPlayerActionInput()
    {
        ResolveReferences();

        CurrentAreaState = EvaluatePlayerAreaState();
        Log($"Player action input received. Area state: {CurrentAreaState}. Occlusion: {currentOcclusionRatio:0.00}.");

        if ((CurrentAreaState & PlayerProjectionAreaState.CommonArea) != 0 &&
            (CurrentAreaState & PlayerProjectionAreaState.NearArea) != 0)
        {
            pendingNearTransfer = false;
            Log("Player is already in a near common area. Relocation skipped.");
            return;
        }

        if (IsPureSideArea(CurrentAreaState))
        {
            TryRelocateFromSideArea();
            return;
        }

        if ((CurrentAreaState & PlayerProjectionAreaState.FarArea) != 0)
            TryRelocateFromFarArea();
    }

    public PlayerProjectionAreaState EvaluatePlayerAreaState()
    {
        ResolveReferences();

        currentOcclusionRatio = EvaluateOcclusionRatio();

        if (projectionManager == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] TODO: Cannot evaluate area state because ProjectionManager is missing.", this);
            return PlayerProjectionAreaState.None;
        }

        if (player == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] TODO: Cannot evaluate area state because Player Transform is missing.", this);
            return PlayerProjectionAreaState.None;
        }

        ProjectionView currentView = projectionManager.CurrentView;
        ProjectionViewMask currentViewMask = ProjectionViewUtility.ToMask(currentView);
        Vector3 playerWorldPosition = player.position;
        Vector2 playerProjectionPosition = projectionManager.WorldToProjection2D(playerWorldPosition);

        ProjectionWalkable[] walkables = FindObjectsByType<ProjectionWalkable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        bool foundActiveWalkable = false;
        bool foundContainingWalkable = false;
        float nearestLayerDistance = float.PositiveInfinity;
        float farthestLayerDistance = float.NegativeInfinity;
        float containingLayerDistance = 0f;
        ProjectionViewMask containingViews = ProjectionViewMask.None;
        ProjectionWalkable containingWalkable = null;

        for (int i = 0; i < walkables.Length; i++)
        {
            ProjectionWalkable walkable = walkables[i];
            if (walkable == null ||
                !walkable.isActiveAndEnabled ||
                !walkable.CanProjectInView(currentView))
                continue;

            ProjectionViewMask activeViews = GetWalkableActiveViews(walkable);
            if ((activeViews & currentViewMask) == 0 && !walkable.alwaysProjected)
                continue;

            Bounds bounds = walkable.GetProjectionBounds();
            Rect projectionRect = GetProjectedBoundsRect(bounds);
            float layerDistance = GetDistanceFromCameraAlongView(bounds.center);

            foundActiveWalkable = true;
            nearestLayerDistance = Mathf.Min(nearestLayerDistance, layerDistance);
            farthestLayerDistance = Mathf.Max(farthestLayerDistance, layerDistance);

            if (!ContainsProjectionPoint(projectionRect, playerProjectionPosition))
                continue;

            if (!foundContainingWalkable ||
                Mathf.Abs(layerDistance - GetDistanceFromCameraAlongView(playerWorldPosition)) <
                Mathf.Abs(containingLayerDistance - GetDistanceFromCameraAlongView(playerWorldPosition)))
            {
                foundContainingWalkable = true;
                containingLayerDistance = layerDistance;
                containingViews = activeViews;
                containingWalkable = walkable;
            }
        }

        PlayerProjectionAreaState state = PlayerProjectionAreaState.None;

        if (!foundActiveWalkable)
        {
            Debug.LogWarning($"[ProjectionPlayerActionRelocator] TODO: No active ProjectionWalkable areas were found for current view {currentView}. Player is treated as SideArea.", this);
            return PlayerProjectionAreaState.SideArea;
        }

        if (!foundContainingWalkable)
        {
            state |= PlayerProjectionAreaState.SideArea;
            Log($"Player projection position {playerProjectionPosition} is outside all active walkable projection rects for {currentView}.");
            return state;
        }

        if (IsCommonViewMask(containingViews))
            state |= PlayerProjectionAreaState.CommonArea;

        if (IsExclusiveToCurrentView(containingViews, currentViewMask))
            state |= PlayerProjectionAreaState.ExclusiveArea;

        if (Mathf.Approximately(containingLayerDistance, nearestLayerDistance))
            state |= PlayerProjectionAreaState.NearArea;

        if (Mathf.Approximately(containingLayerDistance, farthestLayerDistance))
            state |= PlayerProjectionAreaState.FarArea;

        if ((state & (PlayerProjectionAreaState.NearArea | PlayerProjectionAreaState.FarArea)) == 0)
            Debug.LogWarning($"[ProjectionPlayerActionRelocator] TODO: Player is on an intermediate depth layer ({containingWalkable.name}). Multi-layer near/far policy is not defined yet.", this);

        Log($"Evaluated area state: {state}. Walkable: {containingWalkable.name}. Views: {containingViews}. Layer distance: {containingLayerDistance:0.00}. Near: {nearestLayerDistance:0.00}. Far: {farthestLayerDistance:0.00}.");
        return state;
    }

    public bool TryRelocateFromSideArea()
    {
        ResolveReferences();

        if (!IsPureSideArea(CurrentAreaState))
        {
            Log($"SideArea relocation skipped. Current state is not pure SideArea: {CurrentAreaState}.");
            return false;
        }

        if (projectionManager == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot relocate from SideArea because ProjectionManager is missing.", this);
            return false;
        }

        if (player == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot relocate from SideArea because Player Transform is missing.", this);
            return false;
        }

        Vector3 playerAnchorWorldPosition = GetPlayerProjectionAnchorWorldPosition();
        Vector2 playerProjectionPosition = projectionManager.WorldToProjection2D(playerAnchorWorldPosition);

        if (!TryFindNearestLevelTargetDepth(playerProjectionPosition, out float targetDepth, out ProjectionWalkable targetWalkable))
        {
            Debug.LogWarning($"[ProjectionPlayerActionRelocator] Cannot relocate from SideArea. No current-view level area contains projected player anchor {playerProjectionPosition}.", this);
            return false;
        }

        Log($"Relocating from SideArea to '{targetWalkable.name}' at depth {targetDepth:0.00}.");
        return ApplyDepthOnlyRelocation(targetDepth);
    }

    public bool TryRelocateFromFarArea()
    {
        ResolveReferences();

        if ((CurrentAreaState & PlayerProjectionAreaState.FarArea) == 0)
        {
            Log($"FarArea relocation skipped. Current state is not FarArea: {CurrentAreaState}.");
            return false;
        }

        if ((CurrentAreaState & PlayerProjectionAreaState.NearArea) != 0)
        {
            pendingNearTransfer = false;
            Log($"FarArea relocation skipped because player is already on the nearest level. Current state: {CurrentAreaState}.");
            return false;
        }

        if (projectionManager == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot relocate from FarArea because ProjectionManager is missing.", this);
            return false;
        }

        if (player == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot relocate from FarArea because Player Transform is missing.", this);
            return false;
        }

        currentOcclusionRatio = EvaluateOcclusionRatio();

        if (currentOcclusionRatio >= occlusionThreshold)
        {
            pendingNearTransfer = true;
            Log($"Far area relocation delayed. Occlusion {currentOcclusionRatio:0.00} >= threshold {occlusionThreshold:0.00}.");
            return false;
        }

        Vector3 playerAnchorWorldPosition = GetPlayerProjectionAnchorWorldPosition();
        Vector2 playerProjectionPosition = projectionManager.WorldToProjection2D(playerAnchorWorldPosition);

        if (!TryFindNearestLevelTargetDepth(playerProjectionPosition, out float targetDepth, out ProjectionWalkable targetWalkable))
        {
            Debug.LogWarning($"[ProjectionPlayerActionRelocator] Cannot relocate from FarArea. No nearest level area contains projected player anchor {playerProjectionPosition}.", this);
            return false;
        }

        Log($"Relocating from FarArea to nearest level '{targetWalkable.name}' at depth {targetDepth:0.00}. Occlusion {currentOcclusionRatio:0.00} < threshold {occlusionThreshold:0.00}.");
        bool relocated = ApplyDepthOnlyRelocation(targetDepth);
        if (relocated)
            pendingNearTransfer = false;

        return relocated;
    }

    public bool ApplyDepthOnlyRelocation(float targetDepth)
    {
        ResolveReferences();

        if (projectionManager == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot apply depth-only relocation because ProjectionManager is missing.", this);
            return false;
        }

        if (player == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot apply depth-only relocation because Player Transform is missing.", this);
            return false;
        }

        Vector3 currentAnchorWorldPosition = GetPlayerProjectionAnchorWorldPosition();
        Vector2 currentProjectionPosition = projectionManager.WorldToProjection2D(currentAnchorWorldPosition);
        Vector3 targetAnchorWorldPosition = projectionManager.Projection2DToWorld(currentProjectionPosition, targetDepth);

        MovePlayerByWorldDelta(targetAnchorWorldPosition - currentAnchorWorldPosition);

        Log($"Applied depth-only relocation. Anchor projection 2D: {currentProjectionPosition}. Target depth: {targetDepth:0.00}. Anchor from: {currentAnchorWorldPosition}. Anchor to: {targetAnchorWorldPosition}.");
        return true;
    }

    public void UpdatePendingNearTransfer()
    {
        if (!pendingNearTransfer)
            return;

        currentOcclusionRatio = EvaluateOcclusionRatio();
        if (currentOcclusionRatio >= occlusionThreshold)
            return;

        ResolveReferences();

        if (projectionManager == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot complete pending near transfer because ProjectionManager is missing.", this);
            return;
        }

        if (player == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot complete pending near transfer because Player Transform is missing.", this);
            return;
        }

        Vector3 playerAnchorWorldPosition = GetPlayerProjectionAnchorWorldPosition();
        Vector2 playerProjectionPosition = projectionManager.WorldToProjection2D(playerAnchorWorldPosition);

        if (!TryFindNearestLevelTargetDepth(playerProjectionPosition, out float targetDepth, out ProjectionWalkable targetWalkable))
        {
            Debug.LogWarning($"[ProjectionPlayerActionRelocator] Cannot complete pending near transfer. No nearest level area contains projected player anchor {playerProjectionPosition}.", this);
            return;
        }

        Log($"Completing pending near transfer to '{targetWalkable.name}' at depth {targetDepth:0.00}. Occlusion {currentOcclusionRatio:0.00} < threshold {occlusionThreshold:0.00}.");
        if (ApplyDepthOnlyRelocation(targetDepth))
            pendingNearTransfer = false;
    }

    private void ResolveReferences()
    {
        if (projectionManager == null)
            projectionManager = FindFirstObjectByType<ProjectionManager>();

        if (player == null && projectionManager != null)
            player = projectionManager.player;

        if (player == null)
        {
            PlayerController playerController = FindFirstObjectByType<PlayerController>();
            if (playerController != null)
                player = playerController.transform;
        }
    }

    private float EvaluateOcclusionRatio()
    {
        return 0f;
    }

    private bool IsPureSideArea(PlayerProjectionAreaState state)
    {
        if ((state & PlayerProjectionAreaState.SideArea) == 0)
            return false;

        PlayerProjectionAreaState disallowedStates =
            PlayerProjectionAreaState.FarArea |
            PlayerProjectionAreaState.NearArea |
            PlayerProjectionAreaState.CommonArea |
            PlayerProjectionAreaState.ExclusiveArea;

        return (state & disallowedStates) == 0;
    }

    private bool TryFindNearestLevelTargetDepth(
        Vector2 playerProjectionPosition,
        out float targetDepth,
        out ProjectionWalkable targetWalkable)
    {
        targetDepth = 0f;
        targetWalkable = null;

        ProjectionView currentView = projectionManager.CurrentView;
        ProjectionViewMask currentViewMask = ProjectionViewUtility.ToMask(currentView);
        ProjectionWalkable[] walkables = FindObjectsByType<ProjectionWalkable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        float bestLayerDistance = float.PositiveInfinity;

        for (int i = 0; i < walkables.Length; i++)
        {
            ProjectionWalkable walkable = walkables[i];
            if (walkable == null ||
                !walkable.isActiveAndEnabled ||
                !walkable.CanProjectInView(currentView))
                continue;

            ProjectionViewMask activeViews = GetWalkableActiveViews(walkable);
            if (!IsCurrentViewLevelArea(activeViews, currentViewMask, walkable.alwaysProjected))
                continue;

            Bounds bounds = walkable.GetProjectionBounds();
            Rect projectionRect = GetProjectedBoundsRect(bounds);
            if (!ContainsProjectionPoint(projectionRect, playerProjectionPosition))
                continue;

            float layerDistance = GetDistanceFromCameraAlongView(bounds.center);
            if (targetWalkable != null && layerDistance >= bestLayerDistance)
                continue;

            targetWalkable = walkable;
            bestLayerDistance = layerDistance;
            targetDepth = projectionManager.WorldToProjectionDepth(bounds.center);
        }

        return targetWalkable != null;
    }

    private bool IsCurrentViewLevelArea(
        ProjectionViewMask activeViews,
        ProjectionViewMask currentViewMask,
        bool alwaysProjected)
    {
        if (alwaysProjected)
            return true;

        if ((activeViews & currentViewMask) == 0)
            return false;

        return IsExclusiveToCurrentView(activeViews, currentViewMask) ||
            IsCommonViewMask(activeViews);
    }

    private Vector3 GetPlayerProjectionAnchorWorldPosition()
    {
        CharacterController characterController = player.GetComponent<CharacterController>();
        if (characterController == null)
            return player.position;

        Vector3 center = player.TransformPoint(characterController.center);
        float halfHeight = Mathf.Max(0f, characterController.height * Mathf.Abs(player.lossyScale.y) * 0.5f);
        return center + Vector3.down * halfHeight;
    }

    private void MovePlayerByWorldDelta(Vector3 worldDelta)
    {
        if (worldDelta.sqrMagnitude < 0.0000001f)
            return;

        CharacterController characterController = player.GetComponent<CharacterController>();
        bool controllerWasEnabled = characterController != null && characterController.enabled;

        if (controllerWasEnabled)
            characterController.enabled = false;

        player.position += worldDelta;

        if (controllerWasEnabled)
            characterController.enabled = true;
    }

    private ProjectionViewMask GetWalkableActiveViews(ProjectionWalkable walkable)
    {
        ProjectionArea area = walkable.GetComponent<ProjectionArea>();
        if (area != null)
            return area.activeViews;

        Debug.LogWarning($"[ProjectionPlayerActionRelocator] TODO: ProjectionWalkable '{walkable.name}' has no ProjectionArea. Falling back to ProjectionWalkable.activeViews.", this);
        return walkable.alwaysProjected
            ? ProjectionViewMask.All
            : walkable.activeViews;
    }

    private float GetDistanceFromCameraAlongView(Vector3 worldPosition)
    {
        Camera projectionCamera = projectionManager.projectionCamera != null
            ? projectionManager.projectionCamera
            : Camera.main;

        if (projectionCamera == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] TODO: Projection camera is missing. Falling back to ProjectionManager depth for near/far evaluation.", this);
            return projectionManager.WorldToProjectionDepth(worldPosition);
        }

        return Vector3.Dot(worldPosition - projectionCamera.transform.position, projectionManager.viewForward);
    }

    private Rect GetProjectedBoundsRect(Bounds bounds)
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

    private bool ContainsProjectionPoint(Rect rect, Vector2 point)
    {
        return point.x >= rect.xMin - ProjectionContainmentTolerance &&
               point.x <= rect.xMax + ProjectionContainmentTolerance &&
               point.y >= rect.yMin - ProjectionContainmentTolerance &&
               point.y <= rect.yMax + ProjectionContainmentTolerance;
    }

    private bool IsCommonViewMask(ProjectionViewMask activeViews)
    {
        return CountViews(activeViews) > 1;
    }

    private bool IsExclusiveToCurrentView(ProjectionViewMask activeViews, ProjectionViewMask currentViewMask)
    {
        return activeViews == currentViewMask;
    }

    private int CountViews(ProjectionViewMask activeViews)
    {
        int count = 0;
        if ((activeViews & ProjectionViewMask.Front) != 0)
            count++;
        if ((activeViews & ProjectionViewMask.Right) != 0)
            count++;
        if ((activeViews & ProjectionViewMask.Back) != 0)
            count++;
        if ((activeViews & ProjectionViewMask.Left) != 0)
            count++;

        return count;
    }

    private void Log(string message)
    {
        if (!logStateChanges)
            return;

        Debug.Log($"[ProjectionPlayerActionRelocator] {message}", this);
    }
}
