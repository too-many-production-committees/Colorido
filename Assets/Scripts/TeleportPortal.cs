using UnityEngine;

public class TeleportPortal : MonoBehaviour
{
    public PlayerController player;
    public Transform exitPortal;
    public KeyCode interactKey = KeyCode.R;
    public float interactRadius = 1.6f;
    public bool useProjectedInteraction = true;
    public Camera projectionCamera;
    public Vector3 exitOffset = Vector3.zero;
    public float cooldown = 0.25f;

    private CharacterController playerController;
    private float cooldownTimer;
    private static float globalCooldownTimer;

    void Awake()
    {
        ResolvePlayer();
    }

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (globalCooldownTimer > 0f)
            globalCooldownTimer -= Time.deltaTime;

        ResolvePlayer();

        if (player == null || exitPortal == null)
            return;

        if (cooldownTimer > 0f || globalCooldownTimer > 0f || !IsPlayerInRange())
            return;

        if (Input.GetKeyDown(interactKey))
            TeleportPlayer();
    }

    void ResolvePlayer()
    {
        if (player == null)
            player = FindObjectOfType<PlayerController>();

        if (player != null && playerController == null)
            playerController = player.GetComponent<CharacterController>();
    }

    bool IsPlayerInRange()
    {
        Vector3 portalPoint = GetPortalCenter(transform);
        Transform playerTransform = GetPlayerTransform();
        if (playerTransform == null)
            return false;

        if (useProjectedInteraction &&
            TryGetPlayerBounds(playerTransform, out Bounds playerBounds) &&
            TryGetPortalProjectedRect(out Rect portalRect) &&
            SurfaceInteractionMarker.TryGetProjectedRect(
                playerBounds,
                GetProjectionCamera(),
                out Rect playerRect))
        {
            return portalRect.Overlaps(playerRect);
        }

        return Vector3.Distance(playerTransform.position, portalPoint) <= interactRadius;
    }

    bool TryGetPortalProjectedRect(out Rect portalRect)
    {
        if (!SurfaceInteractionMarker.TryGetProjectedRect(
            GetPortalBounds(transform),
            GetProjectionCamera(),
            out portalRect))
            return false;

        SurfaceInteractionMarker marker = GetComponent<SurfaceInteractionMarker>();
        if (marker != null)
        {
            portalRect.xMin -= marker.projectionPadding;
            portalRect.xMax += marker.projectionPadding;
            portalRect.yMin -= marker.projectionPadding;
            portalRect.yMax += marker.projectionPadding;
        }

        return true;
    }

    void TeleportPlayer()
    {
        Vector3 destination = GetExitPosition();

        if (player != null)
            player.TeleportTo(destination);

        cooldownTimer = cooldown;
        globalCooldownTimer = cooldown;
    }

    Vector3 GetExitPosition()
    {
        Bounds exitBounds = GetPortalBounds(exitPortal);
        float playerHalfHeight = playerController != null ? playerController.height * 0.5f : 0.9f;
        Vector3 destination = exitBounds.center;
        destination.y = exitBounds.max.y + playerHalfHeight + 0.02f;
        return destination + exitOffset;
    }

    Vector3 GetPortalCenter(Transform portal)
    {
        return GetPortalBounds(portal).center;
    }

    Bounds GetPortalBounds(Transform portal)
    {
        Collider portalCollider = portal.GetComponent<Collider>();
        if (portalCollider != null)
            return portalCollider.bounds;

        Renderer portalRenderer = portal.GetComponent<Renderer>();
        if (portalRenderer != null)
            return portalRenderer.bounds;

        return new Bounds(portal.position, Vector3.one);
    }

    bool TryGetPlayerBounds(Transform playerTransform, out Bounds bounds)
    {
        Renderer[] renderers = playerTransform.GetComponentsInChildren<Renderer>();
        bool hasBounds = false;
        bounds = new Bounds(playerTransform.position, Vector3.one);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer playerRenderer = renderers[i];
            if (playerRenderer == null || !playerRenderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = playerRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(playerRenderer.bounds);
            }
        }

        if (hasBounds)
            return true;

        CharacterController characterController = playerTransform.GetComponent<CharacterController>();
        if (characterController != null)
        {
            bounds = characterController.bounds;
            return true;
        }

        return false;
    }

    Camera GetProjectionCamera()
    {
        if (projectionCamera == null)
            projectionCamera = Camera.main;

        return projectionCamera;
    }

    Transform GetPlayerTransform()
    {
        return player != null ? player.transform : null;
    }
}
