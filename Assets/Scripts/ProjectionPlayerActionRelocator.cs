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

        if ((CurrentAreaState & PlayerProjectionAreaState.SideArea) != 0)
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

        Log("EvaluatePlayerAreaState is using placeholder logic. Region classification will be implemented in a later pass.");
        return PlayerProjectionAreaState.None;
    }

    public bool TryRelocateFromSideArea()
    {
        Log("TryRelocateFromSideArea called. Placeholder only; no player position changes are performed yet.");
        return false;
    }

    public bool TryRelocateFromFarArea()
    {
        currentOcclusionRatio = EvaluateOcclusionRatio();

        if (currentOcclusionRatio >= occlusionThreshold)
        {
            pendingNearTransfer = true;
            Log($"Far area relocation delayed. Occlusion {currentOcclusionRatio:0.00} >= threshold {occlusionThreshold:0.00}.");
            return false;
        }

        Log($"TryRelocateFromFarArea called. Occlusion {currentOcclusionRatio:0.00} < threshold {occlusionThreshold:0.00}. Placeholder only; no player position changes are performed yet.");
        return false;
    }

    public void UpdatePendingNearTransfer()
    {
        if (!pendingNearTransfer)
            return;

        currentOcclusionRatio = EvaluateOcclusionRatio();
        if (currentOcclusionRatio >= occlusionThreshold)
            return;

        pendingNearTransfer = false;
        Log($"Pending near transfer condition reached. Occlusion {currentOcclusionRatio:0.00} < threshold {occlusionThreshold:0.00}. Placeholder only; no player position changes are performed yet.");
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

    private void Log(string message)
    {
        if (!logStateChanges)
            return;

        Debug.Log($"[ProjectionPlayerActionRelocator] {message}", this);
    }
}
