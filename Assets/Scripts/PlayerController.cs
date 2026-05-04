using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public enum MoveState
    {
        Idle,
        Walking,
        Running
    }

    public FezCameraController cameraController;

    public float walkSpeed = 5f;
    public float runSpeed = 8f;
    public float gravity = -20f;
    public float jumpHeight = 1.5f;
    public KeyCode jumpKey = KeyCode.Space;
    public float risingGravityMultiplier = 0.82f;
    public float apexGravityMultiplier = 0.45f;
    public float fallingGravityMultiplier = 1.45f;
    public float jumpCutGravityMultiplier = 1.8f;
    public float apexVelocityThreshold = 1.2f;

    public float jumpBufferTime = 0.18f;
    public float coyoteTime = 0.16f;
    public LayerMask groundMask = ~0;
    public float groundCheckRadius = 0.22f;
    public float groundCheckDistance = 0.08f;
    public bool requireWalkableSurface = true;
    public float fallRespawnY = -8f;
    public float respawnLookbackTime = 1f;
    public float respawnGroundOffset = 0.05f;
    public bool projected2DMovement = false;
    public PlatformMarker[] platforms;
    public float projectedGroundTolerance = 0.18f;
    public float projectedLandingTolerance = 0.08f;
    public BarrierMarker[] barriers;
    public float projectedBarrierPadding = 0.04f;
    public float projectedBarrierDepthTolerance = 0.35f;
    public bool avoidMidgroundOcclusion = true;
    public MidgroundMarker[] midgroundObjects;
    public float midgroundDepthDistance = 0.65f;
    public float midgroundOverlapPadding = 0.08f;

    private CharacterController controller;
    private Vector3 velocity;
    private readonly Collider[] groundHits = new Collider[8];
    private readonly GroundedPositionSample[] groundedPositionHistory = new GroundedPositionSample[90];

    private float jumpBufferCounter;
    private float coyoteCounter;
    private int groundedPositionIndex;
    private bool hasGroundedPosition;
    private bool wasGroundedLastFrame;
    private bool hasCachedRespawnPosition;
    private Vector3 cachedRespawnPosition;
    private MoveState currentMoveState;

    private struct GroundedPositionSample
    {
        public Vector3 position;
        public float time;
    }

    public MoveState CurrentMoveState => currentMoveState;
    public bool IsWalking => currentMoveState == MoveState.Walking;
    public bool IsRunning => currentMoveState == MoveState.Running;

    public void TeleportTo(Vector3 worldPosition)
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();

        bool controllerWasEnabled = controller != null && controller.enabled;

        if (controllerWasEnabled)
            controller.enabled = false;

        transform.position = worldPosition;
        velocity = Vector3.zero;
        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
        RecordGroundedPosition(worldPosition);
        cachedRespawnPosition = worldPosition;
        hasCachedRespawnPosition = true;

        if (controllerWasEnabled)
            controller.enabled = true;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        ResolvePlatforms();
        ResolveBarriers();
        ResolveMidgroundObjects();
        CorrectInitialProjectedPosition();
    }

    void Update()
    {
        Move();
    }

    void Move()
    {
        UpdateJumpBuffer();
        CheckFallRespawn();

        if (cameraController == null ||
            cameraController.IsRotating ||
            cameraController.IsSwitchingView ||
            cameraController.IsFirstPerson)
        {
            currentMoveState = MoveState.Idle;
            return;
        }

        Vector3 right = cameraController.GetRight();

        float inputX = Input.GetAxisRaw("Horizontal");
        Vector3 move = right * inputX;
        bool hasHorizontalInput = Mathf.Abs(inputX) > 0.01f;
        bool wantsToRun = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float currentSpeed = wantsToRun ? runSpeed : walkSpeed;

        if (!hasHorizontalInput)
            currentMoveState = MoveState.Idle;
        else
            currentMoveState = wantsToRun ? MoveState.Running : MoveState.Walking;

        if (projected2DMovement)
        {
            MoveProjected2D(right, inputX, currentSpeed);
            CheckFallRespawn();
            return;
        }

        controller.Move(move * currentSpeed * Time.deltaTime);

        bool grounded = IsGrounded();

        if (grounded)
        {
            coyoteCounter = coyoteTime;
            RecordGroundedPosition(transform.position);
            hasCachedRespawnPosition = false;

            if (velocity.y < 0)
                velocity.y = -2f;
        }
        else
        {
            if (wasGroundedLastFrame)
                CacheFallRespawnPosition();

            coyoteCounter -= Time.deltaTime;
        }

        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
        }

        velocity.y += GetJumpGravity() * Time.deltaTime;

        CollisionFlags flags = controller.Move(velocity * Time.deltaTime);

        if ((flags & CollisionFlags.Below) != 0 && IsGrounded())
        {
            coyoteCounter = coyoteTime;
            RecordGroundedPosition(transform.position);
            hasCachedRespawnPosition = false;
            grounded = true;
        }

        wasGroundedLastFrame = grounded;
        CheckFallRespawn();
    }

    void MoveProjected2D(Vector3 right, float inputX, float currentSpeed)
    {
        ResolvePlatforms();
        ResolveBarriers();

        Vector3 position = transform.position;
        float previousFeetY = GetFeetY(position);
        Vector3 horizontalTarget = position + right * inputX * currentSpeed * Time.deltaTime;

        if (!WouldHitProjectedBarrier(horizontalTarget))
            position = horizontalTarget;

        bool grounded = TryGetProjectedGround(position, out Bounds groundBounds);

        if (grounded)
        {
            coyoteCounter = coyoteTime;
            RecordGroundedPosition(position);
            hasCachedRespawnPosition = false;

            if (velocity.y < 0f)
                velocity.y = -2f;
        }
        else
        {
            if (wasGroundedLastFrame)
                CacheFallRespawnPosition();

            coyoteCounter -= Time.deltaTime;
        }

        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
            grounded = false;
        }

        velocity.y += GetJumpGravity() * Time.deltaTime;
        position.y += velocity.y * Time.deltaTime;

        if (velocity.y <= 0f &&
            TryGetProjectedGround(position, out groundBounds) &&
            previousFeetY >= groundBounds.max.y - projectedLandingTolerance)
        {
            position.y = groundBounds.max.y + GetControllerHalfHeight() + respawnGroundOffset;
            velocity.y = -2f;
            coyoteCounter = coyoteTime;
            grounded = true;
            hasCachedRespawnPosition = false;
            AlignDepthToPlatform(ref position, groundBounds);
            RecordGroundedPosition(position);
        }

        bool shouldCorrectMidground = Mathf.Abs(inputX) > 0.01f || !grounded;
        if (shouldCorrectMidground)
            ApplyMidgroundDepthOffset(ref position);

        SetControllerPosition(position);
        wasGroundedLastFrame = grounded;
    }

    void CorrectInitialProjectedPosition()
    {
        if (!projected2DMovement || cameraController == null)
            return;

        Vector3 position = transform.position;

        ApplyMidgroundDepthOffset(ref position);
        if ((position - transform.position).sqrMagnitude > 0.000001f)
            SetControllerPosition(position);

        RecordGroundedPosition(position);
    }

    float GetJumpGravity()
    {
        if (velocity.y > 0f && !Input.GetKey(jumpKey) && !Input.GetButton("Jump"))
            return gravity * jumpCutGravityMultiplier;

        if (Mathf.Abs(velocity.y) < apexVelocityThreshold)
            return gravity * apexGravityMultiplier;

        if (velocity.y > 0f)
            return gravity * risingGravityMultiplier;

        return gravity * fallingGravityMultiplier;
    }

    void UpdateJumpBuffer()
    {
        bool jumpPressed = Input.GetKeyDown(jumpKey) || Input.GetButtonDown("Jump");

        if (jumpPressed)
            jumpBufferCounter = jumpBufferTime;
        else
            jumpBufferCounter -= Time.deltaTime;
    }

    bool IsGrounded()
    {
        if (controller != null && controller.isGrounded && !requireWalkableSurface)
            return true;

        Vector3 center = transform.TransformPoint(controller.center);
        float bottomOffset = (controller.height * 0.5f) - groundCheckRadius;
        Vector3 checkCenter = center + Vector3.down * (bottomOffset + groundCheckDistance);
        int hitCount = Physics.OverlapSphereNonAlloc(
            checkCenter,
            groundCheckRadius,
            groundHits,
            groundMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = groundHits[i];
            groundHits[i] = null;

            if (hit == null)
                continue;

            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;

            if (requireWalkableSurface && hit.GetComponentInParent<WalkableSurface>() == null)
                continue;

            return true;
        }

        return false;
    }

    void ResolvePlatforms()
    {
        if (platforms == null || platforms.Length == 0)
            platforms = FindObjectsOfType<PlatformMarker>();
    }

    void ResolveMidgroundObjects()
    {
        if (midgroundObjects == null || midgroundObjects.Length == 0)
            midgroundObjects = FindObjectsOfType<MidgroundMarker>();
    }

    void ResolveBarriers()
    {
        if (barriers == null || barriers.Length == 0)
            barriers = FindObjectsOfType<BarrierMarker>();
    }

    bool WouldHitProjectedBarrier(Vector3 position)
    {
        if (barriers == null || barriers.Length == 0 || cameraController == null)
            return false;

        int direction = cameraController.CurrentIndex();
        Rect playerRect = GetProjectedPlayerRect(position, direction);

        for (int i = 0; i < barriers.Length; i++)
        {
            BarrierMarker barrier = barriers[i];
            if (barrier == null || !barrier.isActiveAndEnabled)
                continue;

            Bounds bounds = GetBounds(barrier.transform);
            if (!ContainsProjectedDepth(bounds, position, direction))
                continue;

            Rect barrierRect = GetProjectedRect(bounds, direction);
            if (ProjectedRectsOverlap(playerRect, barrierRect, projectedBarrierPadding))
                return true;
        }

        return false;
    }

    bool ContainsProjectedDepth(Bounds bounds, Vector3 position, int direction)
    {
        if (direction == 0 || direction == 2)
            return position.z >= bounds.min.z - projectedBarrierDepthTolerance &&
                position.z <= bounds.max.z + projectedBarrierDepthTolerance;

        return position.x >= bounds.min.x - projectedBarrierDepthTolerance &&
            position.x <= bounds.max.x + projectedBarrierDepthTolerance;
    }

    bool TryGetProjectedGround(Vector3 position, out Bounds groundBounds)
    {
        groundBounds = default;

        if (platforms == null || platforms.Length == 0 || cameraController == null)
            return false;

        int direction = cameraController.CurrentIndex();
        float playerHorizontal = ProjectHorizontal(position, direction);
        float feetY = GetFeetY(position);
        float bestTop = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < platforms.Length; i++)
        {
            PlatformMarker platform = platforms[i];
            if (platform == null)
                continue;

            if (requireWalkableSurface && platform.GetComponent<WalkableSurface>() == null)
                continue;

            Bounds bounds = GetBounds(platform.transform);
            if (!ContainsProjectedHorizontal(bounds, playerHorizontal, direction))
                continue;

            float top = bounds.max.y;
            bool closeToFeet = feetY >= top - projectedLandingTolerance &&
                feetY <= top + projectedGroundTolerance;

            if (!closeToFeet || top < bestTop)
                continue;

            bestTop = top;
            groundBounds = bounds;
            found = true;
        }

        return found;
    }

    bool ContainsProjectedHorizontal(Bounds bounds, float horizontal, int direction)
    {
        if (direction == 0 || direction == 2)
            return horizontal >= bounds.min.x - groundCheckRadius &&
                horizontal <= bounds.max.x + groundCheckRadius;

        return horizontal >= bounds.min.z - groundCheckRadius &&
            horizontal <= bounds.max.z + groundCheckRadius;
    }

    float ProjectHorizontal(Vector3 worldPosition, int direction)
    {
        return direction == 0 || direction == 2
            ? worldPosition.x
            : worldPosition.z;
    }

    Bounds GetBounds(Transform target)
    {
        Collider targetCollider = target.GetComponent<Collider>();
        if (targetCollider != null)
            return targetCollider.bounds;

        Renderer targetRenderer = target.GetComponent<Renderer>();
        if (targetRenderer != null)
            return targetRenderer.bounds;

        return new Bounds(target.position, target.localScale);
    }

    float GetFeetY(Vector3 position)
    {
        return position.y - GetControllerHalfHeight();
    }

    float GetControllerHalfHeight()
    {
        if (controller != null)
            return controller.height * 0.5f;

        return 0.9f;
    }

    void AlignDepthToPlatform(ref Vector3 position, Bounds platformBounds)
    {
        int direction = cameraController.CurrentIndex();

        if (direction == 0 || direction == 2)
            position.z = platformBounds.center.z;
        else
            position.x = platformBounds.center.x;
    }

    void ApplyMidgroundDepthOffset(ref Vector3 position)
    {
        if (!avoidMidgroundOcclusion || cameraController == null)
            return;

        ResolveMidgroundObjects();

        if (midgroundObjects == null || midgroundObjects.Length == 0)
            return;

        int direction = cameraController.CurrentIndex();
        Rect playerRect = GetProjectedPlayerRect(position, direction);
        Vector3 viewDirection = GetCameraViewDirection();
        float playerDepth = Vector3.Dot(position, viewDirection);
        float targetDepth = playerDepth;
        bool needsDepthShift = false;

        for (int i = 0; i < midgroundObjects.Length; i++)
        {
            MidgroundMarker midground = midgroundObjects[i];
            if (midground == null)
                continue;

            Bounds bounds = GetBounds(midground.transform);
            Rect midgroundRect = GetProjectedRect(bounds, direction);
            if (!ProjectedRectsOverlap(playerRect, midgroundRect, midgroundOverlapPadding))
                continue;

            float midgroundFrontDepth = GetBoundsFrontDepth(bounds, viewDirection);
            float depthInFrontOfMidground = midgroundFrontDepth - midgroundDepthDistance;

            if (playerDepth <= depthInFrontOfMidground)
                continue;

            targetDepth = needsDepthShift
                ? Mathf.Min(targetDepth, depthInFrontOfMidground)
                : depthInFrontOfMidground;
            needsDepthShift = true;
        }

        if (!needsDepthShift)
            return;

        position += viewDirection * (targetDepth - playerDepth);
    }

    float GetBoundsFrontDepth(Bounds bounds, Vector3 viewDirection)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        float frontDepth = float.PositiveInfinity;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z));
                    frontDepth = Mathf.Min(frontDepth, Vector3.Dot(corner, viewDirection));
                }
            }
        }

        return frontDepth;
    }

    Vector3 GetCameraViewDirection()
    {
        Camera targetCamera = Camera.main;
        Vector3 direction = targetCamera != null
            ? targetCamera.transform.forward
            : cameraController.GetForward();

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            direction = cameraController.GetForward();

        return direction.normalized;
    }

    Rect GetProjectedPlayerRect(Vector3 position, int direction)
    {
        float horizontal = ProjectHorizontal(position, direction);
        float halfWidth = controller != null ? controller.radius : 0.35f;
        float halfHeight = GetControllerHalfHeight();

        return new Rect(
            horizontal - halfWidth,
            position.y - halfHeight,
            halfWidth * 2f,
            halfHeight * 2f);
    }

    Rect GetProjectedRect(Bounds bounds, int direction)
    {
        float horizontal = direction == 0 || direction == 2 ? bounds.center.x : bounds.center.z;
        float width = direction == 0 || direction == 2 ? bounds.size.x : bounds.size.z;

        return new Rect(
            horizontal - width * 0.5f,
            bounds.min.y,
            width,
            bounds.size.y);
    }

    bool ProjectedRectsOverlap(Rect a, Rect b, float padding)
    {
        return a.xMin <= b.xMax + padding &&
            a.xMax >= b.xMin - padding &&
            a.yMin <= b.yMax + padding &&
            a.yMax >= b.yMin - padding;
    }

    void SetControllerPosition(Vector3 position)
    {
        if (controller == null)
        {
            transform.position = position;
            return;
        }

        bool controllerWasEnabled = controller.enabled;
        if (controllerWasEnabled)
            controller.enabled = false;

        transform.position = position;

        if (controllerWasEnabled)
            controller.enabled = true;
    }

    void RecordGroundedPosition(Vector3 position)
    {
        groundedPositionHistory[groundedPositionIndex] = new GroundedPositionSample
        {
            position = position,
            time = Time.time
        };

        groundedPositionIndex = (groundedPositionIndex + 1) % groundedPositionHistory.Length;
        hasGroundedPosition = true;
    }

    void CheckFallRespawn()
    {
        if (transform.position.y > fallRespawnY)
            return;

        TeleportTo(GetRespawnPosition());
    }

    Vector3 GetRespawnPosition()
    {
        if (hasCachedRespawnPosition)
            return cachedRespawnPosition;

        return GetGroundedPositionAtTime(Time.time - respawnLookbackTime);
    }

    void CacheFallRespawnPosition()
    {
        cachedRespawnPosition = GetGroundedPositionAtTime(Time.time - respawnLookbackTime);
        hasCachedRespawnPosition = true;
    }

    Vector3 GetGroundedPositionAtTime(float targetTime)
    {
        if (!hasGroundedPosition)
            return transform.position;

        GroundedPositionSample bestSample = default;
        float bestAge = float.PositiveInfinity;
        bool foundOlderSample = false;

        for (int i = 0; i < groundedPositionHistory.Length; i++)
        {
            GroundedPositionSample sample = groundedPositionHistory[i];
            if (sample.time <= 0f)
                continue;

            if (sample.time <= targetTime)
            {
                float age = targetTime - sample.time;
                if (age < bestAge)
                {
                    bestAge = age;
                    bestSample = sample;
                    foundOlderSample = true;
                }
            }
        }

        if (!foundOlderSample)
            bestSample = GetNewestGroundedPosition();

        Vector3 position = bestSample.position;
        position.y += respawnGroundOffset;
        return position;
    }

    GroundedPositionSample GetNewestGroundedPosition()
    {
        GroundedPositionSample newestSample = default;
        float newestTime = float.NegativeInfinity;

        for (int i = 0; i < groundedPositionHistory.Length; i++)
        {
            GroundedPositionSample sample = groundedPositionHistory[i];
            if (sample.time > newestTime)
            {
                newestTime = sample.time;
                newestSample = sample;
            }
        }

        return newestSample;
    }
}
