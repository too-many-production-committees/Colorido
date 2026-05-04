using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public FezCameraController cameraController;
    public ProjectionManager projectionManager;
    public ProjectionPhysicsBuilder projectionPhysicsBuilder;
    public ProjectionPlayerActionRelocator playerActionRelocator;

    public bool useProjectionPhysics = true;
    public float moveSpeed = 5f;
    public float gravity = -20f;
    public float jumpHeight = 1.5f;
    public int maxJumpCount = 2;
    public float jumpBufferTime = 0.12f;
    public float coyoteTime = 0.12f;

    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode projectionInteractKey = KeyCode.R;
    public LayerMask projectionCollisionMask = ~0;
    public float projectionGroundProbeDistance = 0.06f;
    public float projectionGroundNormalY = 0.5f;
    public Vector2 projectionColliderSize = new Vector2(0.7f, 1.8f);
    public Vector2 projectionColliderOffset = new Vector2(0f, 0.9f);
    public Rigidbody2D projectionBody;
    public Collider2D projectionCollider;
    public PhysicsMaterial2D zeroFrictionMaterial;
    public bool hideProjectionBodyInHierarchy = true;
    public bool snapToNearestWalkableOnProjectionSync = true;
    public float walkableSpawnSearchRadius = 8f;
    public float walkableSpawnHorizontalPadding = 0.05f;
    public bool constrainToWalkableArea = true;
    public float walkableAreaSearchRadius = 8f;
    public float walkableAreaVerticalTolerance = 0.35f;

    private CharacterController controller;
    private Vector3 velocity;
    private ProjectionInteractable currentProjectionInteractable;
    private readonly ContactPoint2D[] projectionContacts = new ContactPoint2D[12];

    private float jumpBufferCounter;
    private float coyoteCounter;
    private int jumpsRemaining;
    private bool jumpPressedThisFrame;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        ResolveProjectionReferences();
        ConfigureProjectionBody();
    }

    void Start()
    {
        if (useProjectionPhysics &&
            projectionPhysicsBuilder != null &&
            projectionPhysicsBuilder.rebuildOnStart)
            projectionPhysicsBuilder.Rebuild();

        SyncWorldToProjectionBody();
    }

    void Update()
    {
        Move();
    }

    void LateUpdate()
    {
        if (useProjectionPhysics)
        {
            ConstrainProjectionBodyToWalkableArea();
            SyncProjectionBodyToWorld();
        }
    }

    public void SyncWorldToProjectionBody(bool snapToWalkable = true)
    {
        ResolveProjectionReferences();

        if (projectionManager == null || projectionBody == null)
            return;

        projectionManager.UpdateProjectionAxes();
        projectionBody.position = projectionManager.WorldToProjection2D(GetFeetWorldPosition());
        projectionBody.velocity = Vector2.zero;
        if (!snapToWalkable)
            return;

        SnapProjectionBodyToNearestWalkableIfNeeded();
        velocity = Vector3.zero;
        jumpsRemaining = Mathf.Max(1, maxJumpCount);
        currentProjectionInteractable = null;
    }

    public void SyncProjectionBodyToWorld()
    {
        ResolveProjectionReferences();

        if (projectionManager == null || projectionBody == null)
            return;

        Vector3 currentFeet = GetFeetWorldPosition();
        float depth = projectionManager.WorldToProjectionDepth(currentFeet);
        Vector3 targetFeet = projectionManager.Projection2DToWorld(projectionBody.position, depth);
        MoveTransformByFeetDelta(targetFeet - currentFeet);
        ConstrainWorldPositionToWalkableArea();
    }

    public void TeleportTo(Vector3 worldPosition)
    {
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
            controller.enabled = false;

        transform.position = worldPosition;
        velocity = Vector3.zero;
        jumpsRemaining = Mathf.Max(1, maxJumpCount);

        if (projectionBody != null)
        {
            projectionBody.position = projectionManager != null
                ? projectionManager.WorldToProjection2D(GetFeetWorldPosition())
                : new Vector2(worldPosition.x, worldPosition.y);
            projectionBody.velocity = Vector2.zero;
            SnapProjectionBodyToNearestWalkableIfNeeded();
        }

        if (controllerWasEnabled)
            controller.enabled = true;
    }

    void Move()
    {
        UpdateJumpBuffer();

        bool inputPaused = cameraController == null ||
            cameraController.IsRotating ||
            cameraController.IsSwitchingView ||
            cameraController.IsFirstPerson;

        if (inputPaused)
        {
            if (projectionBody != null)
                projectionBody.velocity = Vector2.zero;
            return;
        }

        NotifyRelocatorOnActionInput();

        if (useProjectionPhysics)
        {
            MoveProjectionPhysics();
            return;
        }

        MoveCharacterControllerFallback();
    }

    void MoveProjectionPhysics()
    {
        ResolveProjectionReferences();
        if (projectionBody == null)
            return;

        float inputX = Input.GetAxisRaw("Horizontal");
        Vector2 bodyVelocity = projectionBody.velocity;
        bodyVelocity.x = inputX * moveSpeed;

        bool grounded = IsProjectionGrounded();
        if (grounded)
        {
            coyoteCounter = coyoteTime;
            jumpsRemaining = Mathf.Max(1, maxJumpCount);
            if (bodyVelocity.y < 0f)
                bodyVelocity.y = -2f;
        }
        else
        {
            coyoteCounter -= Time.deltaTime;
        }

        if (jumpBufferCounter > 0f && CanJump())
        {
            bodyVelocity.y = GetJumpVelocity();
            ConsumeJump();
        }

        bodyVelocity.y += gravity * Time.deltaTime;
        projectionBody.velocity = bodyVelocity;
        ConstrainProjectionBodyToWalkableArea();
        velocity.y = bodyVelocity.y;

        if (currentProjectionInteractable != null && Input.GetKeyDown(projectionInteractKey))
            currentProjectionInteractable.Interact(this);
    }

    void MoveCharacterControllerFallback()
    {
        if (controller == null || cameraController == null)
            return;

        Vector3 right = cameraController.GetRight();
        float inputX = Input.GetAxisRaw("Horizontal");
        Vector3 move = right * inputX;

        controller.Move(move * moveSpeed * Time.deltaTime);

        if (controller.isGrounded)
        {
            coyoteCounter = coyoteTime;
            jumpsRemaining = Mathf.Max(1, maxJumpCount);
            if (velocity.y < 0)
                velocity.y = -2f;
        }
        else
        {
            coyoteCounter -= Time.deltaTime;
        }

        if (jumpBufferCounter > 0f && CanJump())
        {
            velocity.y = GetJumpVelocity();
            ConsumeJump();
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
        ConstrainWorldPositionToWalkableArea();
    }

    void UpdateJumpBuffer()
    {
        jumpPressedThisFrame = Input.GetKeyDown(jumpKey) || Input.GetButtonDown("Jump");
        if (jumpPressedThisFrame)
            jumpBufferCounter = jumpBufferTime;
        else
            jumpBufferCounter -= Time.deltaTime;
    }

    void NotifyRelocatorOnActionInput()
    {
        ResolveProjectionReferences();

        if (playerActionRelocator == null)
            return;

        bool hasMoveInput = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f ||
            Input.GetKey(KeyCode.A) ||
            Input.GetKey(KeyCode.D) ||
            Input.GetKey(KeyCode.LeftArrow) ||
            Input.GetKey(KeyCode.RightArrow);
        if (!hasMoveInput && !jumpPressedThisFrame)
            return;

        playerActionRelocator.OnPlayerActionInput(hasMoveInput, jumpPressedThisFrame);
    }

    bool CanJump()
    {
        return coyoteCounter > 0f || jumpsRemaining > 0;
    }

    float GetJumpVelocity()
    {
        return Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    void ConsumeJump()
    {
        jumpBufferCounter = 0f;

        if (coyoteCounter > 0f)
            jumpsRemaining = Mathf.Max(0, Mathf.Max(1, maxJumpCount) - 1);
        else
            jumpsRemaining = Mathf.Max(0, jumpsRemaining - 1);

        coyoteCounter = 0f;
    }

    bool IsProjectionGrounded()
    {
        if (projectionCollider == null)
            return false;

        int contactCount = projectionCollider.GetContacts(projectionContacts);
        for (int i = 0; i < contactCount; i++)
        {
            ContactPoint2D contact = projectionContacts[i];
            if (contact.collider == null || contact.collider.isTrigger)
                continue;

            if ((projectionCollisionMask.value & (1 << contact.collider.gameObject.layer)) == 0)
                continue;

            if (contact.normal.y >= projectionGroundNormalY)
                return true;
        }

        Bounds bounds = projectionCollider.bounds;
        Vector2 probeCenter = new Vector2(
            bounds.center.x,
            bounds.min.y - projectionGroundProbeDistance * 0.5f);
        Vector2 probeSize = new Vector2(
            Mathf.Max(0.02f, bounds.size.x * 0.85f),
            Mathf.Max(0.01f, projectionGroundProbeDistance));

        Collider2D[] hits = Physics2D.OverlapBoxAll(probeCenter, probeSize, 0f, projectionCollisionMask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null ||
                hit.isTrigger ||
                hit.transform == projectionBody.transform ||
                hit.transform.IsChildOf(projectionBody.transform))
                continue;

            if (IsStandingOnColliderTop(hit))
                return true;
        }

        return false;
    }

    void SnapProjectionBodyToNearestWalkableIfNeeded()
    {
        if (!snapToNearestWalkableOnProjectionSync ||
            projectionBody == null ||
            projectionCollider == null)
            return;

        if (IsProjectionGrounded())
            return;

        Collider2D nearest = FindNearestWalkableCollider();
        if (nearest == null)
            return;

        Bounds bounds = nearest.bounds;
        float halfWidth = projectionCollider.bounds.extents.x;
        float targetX = Mathf.Clamp(
            projectionBody.position.x,
            bounds.min.x + halfWidth + walkableSpawnHorizontalPadding,
            bounds.max.x - halfWidth - walkableSpawnHorizontalPadding);

        if (bounds.max.x - bounds.min.x < halfWidth * 2f)
            targetX = bounds.center.x;

        projectionBody.position = new Vector2(targetX, bounds.max.y);
        projectionBody.velocity = Vector2.zero;
    }

    Collider2D FindNearestWalkableCollider()
    {
        float radius = Mathf.Max(0.1f, walkableSpawnSearchRadius);
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            projectionBody.position,
            radius,
            projectionCollisionMask);

        Collider2D best = null;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (!IsValidWalkableSpawnCollider(hit))
                continue;

            float horizontalDistance = Mathf.Max(
                0f,
                Mathf.Abs(projectionBody.position.x - hit.bounds.center.x) - hit.bounds.extents.x);
            float verticalDistance = Mathf.Abs(projectionBody.position.y - hit.bounds.max.y);
            float score = horizontalDistance * 2f + verticalDistance;

            if (score < bestScore)
            {
                bestScore = score;
                best = hit;
            }
        }

        return best;
    }

    void ConstrainProjectionBodyToWalkableArea()
    {
        if (!constrainToWalkableArea ||
            projectionBody == null ||
            projectionCollider == null)
            return;

        if (ShouldBypassProjectionWalkableConstraint())
            return;

        if (!TryGetNearbyWalkableHorizontalRange(out float minX, out float maxX))
            return;

        float halfWidth = projectionCollider.bounds.extents.x;
        float paddedMin = minX + halfWidth + walkableSpawnHorizontalPadding;
        float paddedMax = maxX - halfWidth - walkableSpawnHorizontalPadding;

        if (paddedMin > paddedMax)
        {
            float center = (minX + maxX) * 0.5f;
            paddedMin = center;
            paddedMax = center;
        }

        float clampedX = Mathf.Clamp(projectionBody.position.x, paddedMin, paddedMax);
        if (Mathf.Approximately(clampedX, projectionBody.position.x))
            return;

        projectionBody.position = new Vector2(clampedX, projectionBody.position.y);
        Vector2 bodyVelocity = projectionBody.velocity;
        bodyVelocity.x = 0f;
        projectionBody.velocity = bodyVelocity;
    }

    bool TryGetNearbyWalkableHorizontalRange(out float minX, out float maxX)
    {
        minX = 0f;
        maxX = 0f;

        float radius = Mathf.Max(0.1f, walkableAreaSearchRadius);
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            projectionBody.position,
            radius,
            projectionCollisionMask);

        float feetY = projectionCollider.bounds.min.y;
        bool found = false;
        float bestVerticalDistance = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (!IsValidWalkableSpawnCollider(hit))
                continue;

            float verticalDistance = Mathf.Abs(feetY - hit.bounds.max.y);
            if (verticalDistance > walkableAreaVerticalTolerance &&
                verticalDistance > bestVerticalDistance + 0.001f)
                continue;

            if (verticalDistance < bestVerticalDistance - 0.001f)
            {
                bestVerticalDistance = verticalDistance;
                minX = hit.bounds.min.x;
                maxX = hit.bounds.max.x;
                found = true;
                continue;
            }

            if (found && Mathf.Abs(verticalDistance - bestVerticalDistance) <= walkableAreaVerticalTolerance)
            {
                minX = Mathf.Min(minX, hit.bounds.min.x);
                maxX = Mathf.Max(maxX, hit.bounds.max.x);
            }
        }

        return found;
    }

    bool IsValidWalkableSpawnCollider(Collider2D hit)
    {
        if (hit == null ||
            hit.isTrigger ||
            hit.transform == projectionBody.transform ||
            hit.transform.IsChildOf(projectionBody.transform))
            return false;

        ProjectionPhysicsProxy proxy = hit.GetComponent<ProjectionPhysicsProxy>();
        return proxy != null && proxy.sourceWalkable != null;
    }

    bool IsStandingOnColliderTop(Collider2D hit)
    {
        float feetY = projectionCollider.bounds.min.y;
        float topY = hit.bounds.max.y;
        float tolerance = Mathf.Max(0.03f, projectionGroundProbeDistance * 2f);

        return topY <= feetY + tolerance &&
            topY >= feetY - tolerance;
    }

    void ResolveProjectionReferences()
    {
        if (projectionManager == null)
            projectionManager = FindFirstObjectByType<ProjectionManager>();

        if (projectionPhysicsBuilder == null)
            projectionPhysicsBuilder = FindFirstObjectByType<ProjectionPhysicsBuilder>();

        if (cameraController == null && projectionManager != null)
            cameraController = projectionManager.cameraController;

        if (playerActionRelocator == null)
            playerActionRelocator = GetComponent<ProjectionPlayerActionRelocator>();

        if (playerActionRelocator == null)
            playerActionRelocator = FindFirstObjectByType<ProjectionPlayerActionRelocator>();

        if (projectionManager != null && projectionManager.player == null)
            projectionManager.player = transform;
    }

    void ConfigureProjectionBody()
    {
        if (projectionBody == null)
            CreateProjectionBody();

        projectionBody.gravityScale = 0f;
        projectionBody.freezeRotation = true;
        projectionBody.interpolation = RigidbodyInterpolation2D.Interpolate;
        projectionBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (projectionCollider == null)
            projectionCollider = projectionBody.GetComponent<Collider2D>();

        if (projectionCollider == null)
        {
            CapsuleCollider2D capsule = projectionBody.gameObject.AddComponent<CapsuleCollider2D>();
            projectionCollider = capsule;
        }

        CapsuleCollider2D capsuleCollider = projectionCollider as CapsuleCollider2D;
        if (capsuleCollider != null)
        {
            capsuleCollider.size = projectionColliderSize;
            capsuleCollider.offset = projectionColliderOffset;
        }

        projectionCollider.sharedMaterial = GetZeroFrictionMaterial();

        ProjectionPlayerProxy proxy = projectionBody.GetComponent<ProjectionPlayerProxy>();
        if (proxy == null)
            proxy = projectionBody.gameObject.AddComponent<ProjectionPlayerProxy>();
        proxy.owner = this;

        if (hideProjectionBodyInHierarchy)
            projectionBody.gameObject.hideFlags = HideFlags.HideInHierarchy;
    }

    void CreateProjectionBody()
    {
        GameObject bodyObject = new GameObject($"{name}_ProjectionBody2D");
        projectionBody = bodyObject.AddComponent<Rigidbody2D>();
        CapsuleCollider2D capsule = bodyObject.AddComponent<CapsuleCollider2D>();
        capsule.size = projectionColliderSize;
        capsule.offset = projectionColliderOffset;
        projectionCollider = capsule;
    }

    PhysicsMaterial2D GetZeroFrictionMaterial()
    {
        if (zeroFrictionMaterial != null)
            return zeroFrictionMaterial;

        zeroFrictionMaterial = new PhysicsMaterial2D("Player Projection Zero Friction")
        {
            friction = 0f,
            bounciness = 0f
        };

        return zeroFrictionMaterial;
    }

    Vector3 GetFeetWorldPosition()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();

        if (controller == null)
            return transform.position;

        Vector3 center = transform.TransformPoint(controller.center);
        float halfHeight = Mathf.Max(0f, controller.height * Mathf.Abs(transform.lossyScale.y) * 0.5f);
        return center + Vector3.down * halfHeight;
    }

    void MoveTransformByFeetDelta(Vector3 feetDelta)
    {
        if (feetDelta.sqrMagnitude < 0.0000001f)
            return;

        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
            controller.enabled = false;

        transform.position += feetDelta;

        if (controllerWasEnabled)
            controller.enabled = true;
    }

    void ConstrainWorldPositionToWalkableArea()
    {
        if (!constrainToWalkableArea)
            return;

        if (ShouldBypassProjectionWalkableConstraint())
            return;

        Vector3 feetPosition = GetFeetWorldPosition();

        ProjectionWalkable[] walkables = FindObjectsByType<ProjectionWalkable>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        const float verticalTolerance = 0.15f;

        // Check if feet XZ is within any walkable horizontal bounds
        for (int i = 0; i < walkables.Length; i++)
        {
            ProjectionWalkable walkable = walkables[i];
            if (walkable == null || !walkable.isActiveAndEnabled)
                continue;

            Bounds bounds = walkable.GetProjectionBounds();
            bounds.Expand(0.05f);

            if (feetPosition.x >= bounds.min.x && feetPosition.x <= bounds.max.x &&
                feetPosition.z >= bounds.min.z && feetPosition.z <= bounds.max.z &&
                feetPosition.y >= bounds.min.y - verticalTolerance)
                return;
        }

        // Player XZ is outside all walkables — find nearest and clamp horizontally only
        ProjectionWalkable nearest = null;
        float nearestDist = float.PositiveInfinity;
        float clampedX = feetPosition.x;
        float clampedZ = feetPosition.z;

        for (int i = 0; i < walkables.Length; i++)
        {
            ProjectionWalkable walkable = walkables[i];
            if (walkable == null || !walkable.isActiveAndEnabled)
                continue;

            Bounds bounds = walkable.GetProjectionBounds();
            float cx = Mathf.Clamp(feetPosition.x, bounds.min.x, bounds.max.x);
            float cz = Mathf.Clamp(feetPosition.z, bounds.min.z, bounds.max.z);
            float dy = Mathf.Max(0f, bounds.min.y - feetPosition.y, feetPosition.y - bounds.max.y);

            float dist = Vector3.Distance(feetPosition,
                new Vector3(cx, Mathf.Clamp(feetPosition.y, bounds.min.y, bounds.max.y), cz));

            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = walkable;
                clampedX = cx;
                clampedZ = cz;
            }
        }

        if (nearest != null)
        {
            Vector3 targetFeet = feetPosition;
            targetFeet.x = clampedX;
            targetFeet.z = clampedZ;

            MoveTransformByFeetDelta(targetFeet - feetPosition);

            // Sync projection body position, preserve vertical velocity
            if (useProjectionPhysics && projectionBody != null && projectionManager != null)
            {
                projectionBody.position = projectionManager.WorldToProjection2D(GetFeetWorldPosition());
                projectionBody.velocity = new Vector2(0f, projectionBody.velocity.y);
            }
        }
    }

    bool ShouldBypassProjectionWalkableConstraint()
    {
        ResolveProjectionReferences();
        return playerActionRelocator != null &&
            playerActionRelocator.ShouldBypassProjectionWalkableConstraint();
    }

    public void HandleProjectionTriggerEnter(Collider2D other)
    {
        ProjectionPhysicsProxy proxy = other.GetComponent<ProjectionPhysicsProxy>();
        if (proxy == null || proxy.sourceInteractable == null)
            return;

        currentProjectionInteractable = proxy.sourceInteractable;
        currentProjectionInteractable.ProjectionEnter(this);
    }

    public void HandleProjectionTriggerExit(Collider2D other)
    {
        ProjectionPhysicsProxy proxy = other.GetComponent<ProjectionPhysicsProxy>();
        if (proxy == null || proxy.sourceInteractable == null)
            return;

        if (currentProjectionInteractable == proxy.sourceInteractable)
            currentProjectionInteractable = null;

        proxy.sourceInteractable.ProjectionExit(this);
    }
}
