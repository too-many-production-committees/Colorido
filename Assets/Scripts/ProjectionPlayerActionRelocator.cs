using System;
using System.Collections.Generic;
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
    [Range(1, 64)]
    public int occlusionSampleResolution = 16;
    public float occlusionDepthTolerance = 0.01f;

    [Header("Debug")]
    public bool logStateChanges = true;

    public PlayerProjectionAreaState CurrentAreaState { get; private set; }
    public bool PendingNearTransfer => pendingNearTransfer;
    public float CurrentOcclusionRatio => currentOcclusionRatio;

    private bool pendingNearTransfer;
    private float currentOcclusionRatio;
    private ProjectionView recordedView;
    private int bypassConstraintUntilFrame = -1;
    private bool actionRelocationAttemptedForCurrentArea;
    private PlayerProjectionAreaState actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;
    private PlayerProjectionAreaState areaStateAtRotationComplete = PlayerProjectionAreaState.None;
    private bool relocationAllowedForRecordedPostRotationState;

    void Awake()
    {
        ResolveReferences();
    }

    void OnValidate()
    {
        occlusionThreshold = Mathf.Clamp01(occlusionThreshold);
        occlusionSampleResolution = Mathf.Max(1, occlusionSampleResolution);
        occlusionDepthTolerance = Mathf.Max(0f, occlusionDepthTolerance);
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
        areaStateAtRotationComplete = CurrentAreaState;
        relocationAllowedForRecordedPostRotationState =
            (areaStateAtRotationComplete & PlayerProjectionAreaState.SideArea) != 0 ||
            (areaStateAtRotationComplete & PlayerProjectionAreaState.FarArea) != 0;
        pendingNearTransfer = false;
        actionRelocationAttemptedForCurrentArea = false;
        actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;

        Log($"Camera rotation complete. Recorded view: {recordedView}. Area state: {CurrentAreaState}. Relocation allowed: {relocationAllowedForRecordedPostRotationState}. Occlusion: {currentOcclusionRatio:0.00}.");
    }

    public void OnPlayerActionInput()
    {
        OnPlayerActionInput(false, false);
    }

    public void OnPlayerActionInput(bool moveInput, bool jumpInput)
    {
        ResolveReferences();

        CurrentAreaState = EvaluatePlayerAreaState();
        bool shouldRequestBypass =
            (CurrentAreaState & PlayerProjectionAreaState.SideArea) != 0 ||
            (CurrentAreaState & PlayerProjectionAreaState.FarArea) != 0;

        if (actionRelocationAttemptedForCurrentArea &&
            CurrentAreaState != actionRelocationAttemptedAreaState)
        {
            actionRelocationAttemptedForCurrentArea = false;
            actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;
        }

        if ((CurrentAreaState & PlayerProjectionAreaState.NearArea) != 0)
        {
            relocationAllowedForRecordedPostRotationState = false;
            actionRelocationAttemptedForCurrentArea = false;
            actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;
        }

        Debug.LogWarning($"[ProjectionPlayerActionRelocator] Player action input received. Source: {GetActionInputSource(moveInput, jumpInput)}. Area state: {CurrentAreaState}. Recorded rotation state: {areaStateAtRotationComplete}. Occlusion: {currentOcclusionRatio:0.00}. Needs relocation: {shouldRequestBypass}. Relocation allowed: {relocationAllowedForRecordedPostRotationState}. Already attempted: {actionRelocationAttemptedForCurrentArea}.", this);

        if ((CurrentAreaState & PlayerProjectionAreaState.CommonArea) != 0 &&
            (CurrentAreaState & PlayerProjectionAreaState.NearArea) != 0)
        {
            pendingNearTransfer = false;
            relocationAllowedForRecordedPostRotationState = false;
            actionRelocationAttemptedForCurrentArea = false;
            actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;
            Log("Player is already in a near common area. Relocation skipped.");
            return;
        }

        if (shouldRequestBypass)
        {
            if (!relocationAllowedForRecordedPostRotationState)
            {
                Debug.LogWarning($"[ProjectionPlayerActionRelocator] Action relocation skipped. Current {CurrentAreaState} was not recorded as a post-rotation SideArea/FarArea state, so this is treated as player-driven movement off the level.", this);
                return;
            }

            if (actionRelocationAttemptedForCurrentArea)
            {
                Debug.LogWarning($"[ProjectionPlayerActionRelocator] Action relocation already attempted for current area state {CurrentAreaState}; skipping repeat bypass and relocation.", this);
                return;
            }

            actionRelocationAttemptedForCurrentArea = true;
            actionRelocationAttemptedAreaState = CurrentAreaState;
            relocationAllowedForRecordedPostRotationState = false;
            RequestConstraintBypassThisFrame();
        }

        if (IsPureSideArea(CurrentAreaState))
        {
            TryRelocateFromSideArea();
            return;
        }

        if ((CurrentAreaState & PlayerProjectionAreaState.FarArea) != 0)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Trying FarArea relocation after player action input.", this);
            TryRelocateFromFarArea();
        }
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
        Vector3 playerWorldPosition = GetPlayerProjectionAnchorWorldPosition();
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

        RequestConstraintBypassThisFrame();
        if (!TryFindNearestLevelCenterDepth(out float targetDepth, out ProjectionWalkable targetWalkable))
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot relocate from SideArea. No current-view nearest level depth was found.", this);
            return false;
        }

        Log($"Relocating from SideArea to nearest level '{targetWalkable.name}' at depth {targetDepth:0.00}.");
        bool relocated = ApplyDepthOnlyRelocation(targetDepth);
        Log($"SideArea ApplyDepthOnlyRelocation success: {relocated}.");
        if (relocated)
        {
            RequestConstraintBypassThisFrame();
            relocationAllowedForRecordedPostRotationState = false;
            actionRelocationAttemptedForCurrentArea = false;
            actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;
            CurrentAreaState = EvaluatePlayerAreaState();
        }

        return relocated;
    }

    public bool TryRelocateFromFarArea()
    {
        ResolveReferences();

        if ((CurrentAreaState & PlayerProjectionAreaState.FarArea) == 0)
        {
            Debug.LogWarning($"[ProjectionPlayerActionRelocator] FarArea relocation skipped. Current state is not FarArea: {CurrentAreaState}.", this);
            return false;
        }

        if ((CurrentAreaState & PlayerProjectionAreaState.NearArea) != 0)
        {
            pendingNearTransfer = false;
            relocationAllowedForRecordedPostRotationState = false;
            actionRelocationAttemptedForCurrentArea = false;
            actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;
            Debug.LogWarning($"[ProjectionPlayerActionRelocator] FarArea relocation skipped because player is already on the nearest level. Current state: {CurrentAreaState}.", this);
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
            Debug.LogWarning($"[ProjectionPlayerActionRelocator] FarArea relocation delayed. Occlusion {currentOcclusionRatio:0.00} >= threshold {occlusionThreshold:0.00}.", this);
            return false;
        }

        if (!TryFindNearestLevelCenterDepth(out float targetDepth, out ProjectionWalkable targetWalkable))
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot relocate from FarArea. No current-view nearest level depth was found.", this);
            return false;
        }

        Debug.LogWarning($"[ProjectionPlayerActionRelocator] FarArea target selected: '{targetWalkable.name}', targetDepth: {targetDepth:0.00}. Applying depth-only relocation. Occlusion {currentOcclusionRatio:0.00} < threshold {occlusionThreshold:0.00}.", this);
        bool relocated = ApplyDepthOnlyRelocation(targetDepth);
        Debug.LogWarning($"[ProjectionPlayerActionRelocator] FarArea ApplyDepthOnlyRelocation success: {relocated}.", this);
        if (relocated)
        {
            pendingNearTransfer = false;
            RequestConstraintBypassThisFrame();
            relocationAllowedForRecordedPostRotationState = false;
            actionRelocationAttemptedForCurrentArea = false;
            actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;
            CurrentAreaState = EvaluatePlayerAreaState();
        }

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
        SyncProjectionBodyToRelocatedPlayer();

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

        if (!TryFindNearestLevelCenterDepth(out float targetDepth, out ProjectionWalkable targetWalkable))
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot complete pending near transfer. No current-view nearest level depth was found.", this);
            return;
        }

        Log($"Completing pending near transfer to '{targetWalkable.name}' at depth {targetDepth:0.00}. Occlusion {currentOcclusionRatio:0.00} < threshold {occlusionThreshold:0.00}.");
        if (ApplyDepthOnlyRelocation(targetDepth))
        {
            pendingNearTransfer = false;
            RequestConstraintBypassThisFrame();
            relocationAllowedForRecordedPostRotationState = false;
            actionRelocationAttemptedForCurrentArea = false;
            actionRelocationAttemptedAreaState = PlayerProjectionAreaState.None;
            CurrentAreaState = EvaluatePlayerAreaState();
        }
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

    public bool ShouldBypassProjectionWalkableConstraint()
    {
        return pendingNearTransfer ||
            Time.frameCount <= bypassConstraintUntilFrame;
    }

    private void RequestConstraintBypassThisFrame()
    {
        bypassConstraintUntilFrame = Time.frameCount + 1;
    }

    private float EvaluateOcclusionRatio()
    {
        ResolveReferences();

        if (projectionManager == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot evaluate occlusion because ProjectionManager is missing.", this);
            return 0f;
        }

        if (player == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot evaluate occlusion because Player Transform is missing.", this);
            return 0f;
        }

        if (!TryGetProjectionCamera(out Camera projectionCamera))
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot evaluate occlusion because projection camera is missing.", this);
            return 0f;
        }

        if (!TryGetPlayerBounds(out Bounds playerBounds))
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Cannot evaluate occlusion because Player has no CharacterController, Collider, or Renderer bounds.", this);
            return 0f;
        }

        Rect playerProjectionRect = GetProjectedBoundsRect(playerBounds);
        if (playerProjectionRect.width <= 0f || playerProjectionRect.height <= 0f)
            return 0f;

        float playerDistance = GetDistanceFromCameraAlongView(
            playerBounds.center,
            projectionCamera.transform);
        List<Rect> occluderProjectionRects = CollectOccluderProjectionRects(
            playerProjectionRect,
            playerDistance,
            projectionCamera.transform);

        if (occluderProjectionRects.Count == 0)
            return 0f;

        int resolution = Mathf.Max(1, occlusionSampleResolution);
        int totalSamples = resolution * resolution;
        int occludedSamples = 0;
        Vector2 sampleStep = new Vector2(
            playerProjectionRect.width / resolution,
            playerProjectionRect.height / resolution);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector2 samplePoint = new Vector2(
                    playerProjectionRect.xMin + sampleStep.x * (x + 0.5f),
                    playerProjectionRect.yMin + sampleStep.y * (y + 0.5f));

                if (IsSampleOccluded(samplePoint, occluderProjectionRects))
                    occludedSamples++;
            }
        }

        if (totalSamples <= 0)
            return 0f;

        return Mathf.Clamp01((float)occludedSamples / totalSamples);
    }

    private bool TryGetPlayerBounds(out Bounds bounds)
    {
        bounds = default;

        CharacterController characterController = player.GetComponent<CharacterController>();
        if (characterController != null)
        {
            bounds = characterController.bounds;
            return true;
        }

        Collider playerCollider = player.GetComponent<Collider>();
        if (playerCollider != null)
        {
            bounds = playerCollider.bounds;
            return true;
        }

        Renderer playerRenderer = player.GetComponent<Renderer>();
        if (playerRenderer != null)
        {
            bounds = playerRenderer.bounds;
            return true;
        }

        return false;
    }

    private bool TryGetProjectionCamera(out Camera projectionCamera)
    {
        projectionCamera = null;

        if (projectionManager != null && projectionManager.projectionCamera != null)
            projectionCamera = projectionManager.projectionCamera;
        else
            projectionCamera = Camera.main;

        return projectionCamera != null;
    }

    private List<Rect> CollectOccluderProjectionRects(
        Rect playerProjectionRect,
        float playerDistance,
        Transform projectionCameraTransform)
    {
        List<Rect> occluderProjectionRects = new List<Rect>();
        ProjectionOccluder[] occluders = FindObjectsByType<ProjectionOccluder>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < occluders.Length; i++)
        {
            ProjectionOccluder occluder = occluders[i];
            if (occluder == null ||
                !occluder.participateInOcclusion ||
                IsPlayerOrPlayerChild(occluder.transform))
                continue;

            if (!occluder.TryGetBounds(out Bounds occluderBounds))
                continue;

            float occluderDistance = GetDistanceFromCameraAlongView(
                occluderBounds.center,
                projectionCameraTransform);
            if (occluderDistance < -occlusionDepthTolerance ||
                occluderDistance >= playerDistance - occlusionDepthTolerance)
                continue;

            Rect occluderProjectionRect = GetProjectedBoundsRect(occluderBounds);
            if (!ProjectionRectsOverlap(playerProjectionRect, occluderProjectionRect))
                continue;

            occluderProjectionRects.Add(occluderProjectionRect);
        }

        return occluderProjectionRects;
    }

    private bool IsPlayerOrPlayerChild(Transform target)
    {
        return target != null && player != null &&
            (target == player || target.IsChildOf(player));
    }

    private float GetDistanceFromCameraAlongView(
        Vector3 worldPosition,
        Transform projectionCameraTransform)
    {
        return Vector3.Dot(
            worldPosition - projectionCameraTransform.position,
            projectionManager.viewForward);
    }

    private bool ProjectionRectsOverlap(Rect a, Rect b)
    {
        return a.xMin < b.xMax &&
            a.xMax > b.xMin &&
            a.yMin < b.yMax &&
            a.yMax > b.yMin;
    }

    private bool IsSampleOccluded(Vector2 samplePoint, List<Rect> occluderProjectionRects)
    {
        for (int i = 0; i < occluderProjectionRects.Count; i++)
        {
            if (occluderProjectionRects[i].Contains(samplePoint))
                return true;
        }

        return false;
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

    private bool TryFindNearestLevelTargetDepthInsideActivityRange(
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

    private bool TryFindNearestLevelCenterDepth(
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
            float layerDistance = GetDistanceFromCameraAlongView(bounds.center);
            if (targetWalkable != null && layerDistance >= bestLayerDistance)
                continue;

            targetWalkable = walkable;
            bestLayerDistance = layerDistance;
            targetDepth = projectionManager.WorldToProjectionDepth(bounds.center);
        }

        return targetWalkable != null;
    }

    private string GetActionInputSource(bool moveInput, bool jumpInput)
    {
        if (moveInput && jumpInput)
            return "Move+Jump";

        if (moveInput)
            return "Move";

        if (jumpInput)
            return "Jump";

        return "Unknown";
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

    private void SyncProjectionBodyToRelocatedPlayer()
    {
        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null)
            playerController = FindFirstObjectByType<PlayerController>();

        if (playerController == null)
        {
            Debug.LogWarning("[ProjectionPlayerActionRelocator] Depth-only relocation succeeded, but PlayerController was not found for projection body sync.", this);
            return;
        }

        playerController.SyncWorldToProjectionBody(false);
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
