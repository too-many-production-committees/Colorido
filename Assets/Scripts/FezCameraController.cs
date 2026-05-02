using UnityEngine;
using System.Collections;

public class FezCameraController : MonoBehaviour
{
    public float rotateDuration = 0.45f;
    public ProjectionManager projectionManager;
    public BackgroundBox backgroundBox;
    public Camera controlledCamera;
    public Transform player;
    public KeyCode viewToggleKey = KeyCode.F;
    public float viewTransitionDuration = 0.45f;
    public float firstPersonEyeHeight = 1.2f;
    public float firstPersonFieldOfView = 65f;
    public bool showDebugViewButton = true;
    public Vector3 platformFollowOffset = Vector3.zero;
    public float platformFollowStrength = 0.35f;
    public float platformFollowSmoothTime = 0.18f;
    public bool platformFollowVertical = false;
    public float viewArcHeight = 1.2f;
    public float viewPullBackDistance = 1.5f;
    public float rotationCameraPush = 1.5f;
    public float rotationFovBoost = 10f;
    public float rotationElasticOvershoot = 0.16f;
    public float rotationElasticStart = 0.68f;
    public float rotationElasticFrequency = 1.35f;
    public float rotationElasticDamping = 2.4f;
    public float firstPersonPreSwitchOrthographicSize = 1.2f;
    public float projectionBlendPower = 1.4f;
    public AnimationCurve platformRotationEase = new AnimationCurve(
        new Keyframe(0f, 0f, 3.8f, 3.8f),
        new Keyframe(0.58f, 0.88f, 0.8f, 0.8f),
        new Keyframe(1f, 1f, 0f, 0f));
    public AnimationCurve platformCameraPulseEase = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.45f, 1f),
        new Keyframe(1f, 0f));

    private int currentIndex = 0;
    private bool rotating = false;
    private bool firstPerson = false;
    private bool switchingView = false;

    private Vector3 platformCameraLocalPosition;
    private Quaternion platformCameraLocalRotation;
    private Transform platformCameraParent;
    private bool platformCameraOrthographic;
    private float platformCameraOrthographicSize;
    private float platformCameraFieldOfView;
    private Vector3 platformRigBasePosition;
    private Vector3 platformFollowVelocity;

    public bool IsRotating => rotating;
    public bool IsFirstPerson => firstPerson;
    public bool IsSwitchingView => switchingView;

    void Awake()
    {
        ResolveReferences();
        platformRigBasePosition = transform.position;

        if (controlledCamera != null)
        {
            platformCameraParent = controlledCamera.transform.parent;
            platformCameraLocalPosition = controlledCamera.transform.localPosition;
            platformCameraLocalRotation = controlledCamera.transform.localRotation;
            platformCameraOrthographic = controlledCamera.orthographic;
            platformCameraOrthographicSize = controlledCamera.orthographicSize;
            platformCameraFieldOfView = controlledCamera.fieldOfView;
        }
    }

    void Start()
    {
        Debug.Log($"FezCameraController ready. Camera: {controlledCamera?.name ?? "missing"}, Player: {player?.name ?? "missing"}");
    }

    void Update()
    {
        UpdatePlatformFollow();

        if (Input.GetKeyDown(viewToggleKey) || Input.GetKeyDown(KeyCode.V))
            ToggleViewMode();

        if (firstPerson || switchingView)
            return;

        if (Input.GetKeyDown(KeyCode.E))
            RotateRight();

        if (Input.GetKeyDown(KeyCode.Q))
            RotateLeft();
    }

    public void RotateRight()
    {
        if (firstPerson || switchingView) return;
        if (rotating) return;

        if (projectionManager != null)
            projectionManager.CacheBeforeRotate();

        currentIndex = (currentIndex + 1) % 4;
        StartCoroutine(RotateTo(currentIndex * 90f));
    }

    void UpdatePlatformFollow()
    {
        if (player == null || firstPerson || switchingView)
            return;

        Vector3 playerOffset = player.position - platformRigBasePosition;
        if (!platformFollowVertical)
            playerOffset.y = 0f;

        Vector3 target = platformRigBasePosition + platformFollowOffset + playerOffset * platformFollowStrength;
        transform.position = Vector3.SmoothDamp(
            transform.position,
            target,
            ref platformFollowVelocity,
            Mathf.Max(0.01f, platformFollowSmoothTime));
    }

    public void RotateLeft()
    {
        if (firstPerson || switchingView) return;
        if (rotating) return;

        if (projectionManager != null)
            projectionManager.CacheBeforeRotate();

        currentIndex = (currentIndex + 3) % 4;
        StartCoroutine(RotateTo(currentIndex * 90f));
    }

    IEnumerator RotateTo(float targetY)
    {
        rotating = true;

        Quaternion start = transform.rotation;
        Quaternion end = Quaternion.Euler(0f, targetY, 0f);
        Vector3 cameraStartLocalPosition = platformCameraLocalPosition;
        float cameraStartFieldOfView = platformCameraFieldOfView;

        float t = 0f;

        float duration = Mathf.Max(0.01f, rotateDuration);
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float progress = Mathf.Clamp01(t);
            float eased = EvaluatePlatformRotation(progress);
            float pulse = EvaluateCurve(platformCameraPulseEase, progress);

            transform.rotation = Quaternion.SlerpUnclamped(start, end, eased);
            ApplyPlatformCameraMotion(cameraStartLocalPosition, cameraStartFieldOfView, pulse);
            ApplyBackgroundMotion(pulse);
            yield return null;
        }

        transform.rotation = end;
        ApplyPlatformCameraMotion(platformCameraLocalPosition, platformCameraFieldOfView, 0f);
        ApplyBackgroundMotion(0f);
        rotating = false;

        if (projectionManager != null)
            projectionManager.TrySnapPlayer();
    }

    public void ToggleViewMode()
    {
        ResolveReferences();

        if (controlledCamera == null || player == null)
        {
            Debug.LogWarning("Cannot switch view. Assign a Main Camera and player, or keep Camera tagged MainCamera and player with PlayerController.");
            return;
        }

        if (rotating || switchingView)
            return;

        Debug.Log(firstPerson ? "Switching to platform view" : "Switching to first-person view");
        StartCoroutine(SwitchViewMode(!firstPerson));
    }

    void OnGUI()
    {
        if (!showDebugViewButton)
            return;

        string label = firstPerson ? "Platform View (F)" : "First Person (F)";
        if (GUI.Button(new Rect(12f, 12f, 150f, 34f), label))
            ToggleViewMode();
    }

    void ResolveReferences()
    {
        if (controlledCamera == null)
            controlledCamera = Camera.main;

        if (backgroundBox == null)
            backgroundBox = FindObjectOfType<BackgroundBox>();

        if (player == null)
        {
            PlayerController playerController = FindObjectOfType<PlayerController>();
            if (playerController != null)
                player = playerController.transform;
        }
    }

    IEnumerator SwitchViewMode(bool enterFirstPerson)
    {
        switchingView = true;

        Transform cameraTransform = controlledCamera.transform;
        Vector3 startPosition = cameraTransform.position;
        Quaternion startRotation = cameraTransform.rotation;
        bool startOrthographic = controlledCamera.orthographic;
        float startOrthographicSize = controlledCamera.orthographicSize;
        float startFieldOfView = controlledCamera.fieldOfView;

        Vector3 targetPosition;
        Quaternion targetRotation;
        bool targetOrthographic;
        float targetOrthographicSize;
        float targetFieldOfView;

        if (enterFirstPerson)
        {
            cameraTransform.SetParent(null, true);
            targetPosition = player.position + Vector3.up * firstPersonEyeHeight;
            targetRotation = Quaternion.LookRotation(GetForward(), Vector3.up);
            targetOrthographic = false;
            targetOrthographicSize = platformCameraOrthographicSize;
            targetFieldOfView = firstPersonFieldOfView;
        }
        else
        {
            targetPosition = transform.TransformPoint(platformCameraLocalPosition);
            targetRotation = transform.rotation * platformCameraLocalRotation;
            targetOrthographic = platformCameraOrthographic;
            targetOrthographicSize = platformCameraOrthographicSize;
            targetFieldOfView = platformCameraFieldOfView;
        }

        firstPerson = enterFirstPerson;
        controlledCamera.orthographic = false;

        float t = 0f;
        float duration = Mathf.Max(0.01f, viewTransitionDuration);
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float eased = EaseInOutCubic(Mathf.Clamp01(t));
            float orthographicSize = GetTransitionOrthographicSize(
                enterFirstPerson,
                eased,
                startOrthographicSize,
                targetOrthographicSize);
            float fieldOfView = Mathf.Lerp(startFieldOfView, targetFieldOfView, eased);

            cameraTransform.position = GetArcPosition(startPosition, targetPosition, eased, enterFirstPerson);
            cameraTransform.rotation = Quaternion.Slerp(startRotation, targetRotation, eased);
            controlledCamera.orthographicSize = orthographicSize;
            controlledCamera.fieldOfView = fieldOfView;
            ApplyProjectionBlend(
                enterFirstPerson,
                eased,
                orthographicSize,
                fieldOfView);

            yield return null;
        }

        cameraTransform.position = targetPosition;
        cameraTransform.rotation = targetRotation;
        controlledCamera.ResetProjectionMatrix();
        controlledCamera.orthographic = targetOrthographic;
        controlledCamera.orthographicSize = targetOrthographicSize;
        controlledCamera.fieldOfView = targetFieldOfView;

        if (!enterFirstPerson)
        {
            cameraTransform.SetParent(platformCameraParent, true);
            cameraTransform.localPosition = platformCameraLocalPosition;
            cameraTransform.localRotation = platformCameraLocalRotation;
        }

        switchingView = false;
    }

    float GetTransitionOrthographicSize(
        bool enteringFirstPerson,
        float progress,
        float startOrthographicSize,
        float targetOrthographicSize)
    {
        if (enteringFirstPerson)
            return Mathf.Lerp(startOrthographicSize, firstPersonPreSwitchOrthographicSize, progress);

        return Mathf.Lerp(firstPersonPreSwitchOrthographicSize, targetOrthographicSize, progress);
    }

    void ApplyProjectionBlend(
        bool enteringFirstPerson,
        float progress,
        float orthographicSize,
        float fieldOfView)
    {
        float blend = enteringFirstPerson ? progress : 1f - progress;
        blend = Mathf.Pow(Mathf.Clamp01(blend), Mathf.Max(0.01f, projectionBlendPower));

        float halfHeight = Mathf.Max(0.01f, orthographicSize);
        float halfWidth = halfHeight * controlledCamera.aspect;
        Matrix4x4 orthographicMatrix = Matrix4x4.Ortho(
            -halfWidth,
            halfWidth,
            -halfHeight,
            halfHeight,
            controlledCamera.nearClipPlane,
            controlledCamera.farClipPlane);
        Matrix4x4 perspectiveMatrix = Matrix4x4.Perspective(
            fieldOfView,
            controlledCamera.aspect,
            controlledCamera.nearClipPlane,
            controlledCamera.farClipPlane);

        controlledCamera.projectionMatrix = LerpMatrix(orthographicMatrix, perspectiveMatrix, blend);
    }

    Matrix4x4 LerpMatrix(Matrix4x4 from, Matrix4x4 to, float t)
    {
        t = Mathf.Clamp01(t);

        Matrix4x4 result = new Matrix4x4();
        for (int i = 0; i < 16; i++)
            result[i] = Mathf.Lerp(from[i], to[i], t);

        return result;
    }

    void ApplyPlatformCameraMotion(Vector3 baseLocalPosition, float baseFieldOfView, float pulse)
    {
        if (controlledCamera == null || firstPerson || switchingView)
            return;

        Transform cameraTransform = controlledCamera.transform;
        cameraTransform.localPosition = baseLocalPosition + Vector3.back * rotationCameraPush * pulse;
        controlledCamera.fieldOfView = baseFieldOfView + rotationFovBoost * pulse;
    }

    void ApplyBackgroundMotion(float pulse)
    {
        if (backgroundBox == null)
            return;

        backgroundBox.SetCameraYaw(transform.eulerAngles.y, pulse);
    }

    Vector3 GetArcPosition(Vector3 start, Vector3 end, float t, bool enteringFirstPerson)
    {
        Vector3 position = Vector3.Lerp(start, end, t);
        float arc = Mathf.Sin(t * Mathf.PI) * viewArcHeight;
        Vector3 pullDirection = enteringFirstPerson ? -GetForward() : GetForward();
        float pull = Mathf.Sin(t * Mathf.PI) * viewPullBackDistance;

        return position + Vector3.up * arc + pullDirection * pull;
    }

    float EaseInOutCubic(float t)
    {
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    float EvaluateCurve(AnimationCurve curve, float progress)
    {
        if (curve == null || curve.length == 0)
            return EaseInOutCubic(progress);

        return curve.Evaluate(progress);
    }

    float EvaluatePlatformRotation(float progress)
    {
        float baseEase = EvaluateCurve(platformRotationEase, progress);

        if (progress < rotationElasticStart)
            return baseEase;

        float elasticProgress = Mathf.InverseLerp(rotationElasticStart, 1f, progress);
        float settle = EaseOutBack(elasticProgress, rotationElasticOvershoot);
        float tail = GetSpringTail(elasticProgress);

        return Mathf.Lerp(baseEase, 1f + tail, settle);
    }

    float GetSpringTail(float progress)
    {
        float envelope = Mathf.Pow(1f - progress, rotationElasticDamping);
        float wave = Mathf.Cos(progress * Mathf.PI * 2f * rotationElasticFrequency);

        return wave * envelope * rotationElasticOvershoot;
    }

    float EaseOutBack(float t, float overshoot)
    {
        float c1 = 1.70158f + overshoot * 3f;
        float c3 = c1 + 1f;

        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    public Vector3 GetRight()
    {
        Vector3 r = transform.right;
        r.y = 0f;
        return r.normalized;
    }

    public Vector3 GetForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.normalized;
    }

    public int CurrentIndex()
    {
        return currentIndex;
    }
}
