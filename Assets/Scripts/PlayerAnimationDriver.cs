using UnityEngine;

/// <summary>
/// 玩家帧动画驱动器。
/// 读取 PlayerController 暴露的只读运动信息，写入 Animator 参数，
/// 处理待机段的随机选择与左右翻转（SpriteRenderer.flipX）。
/// 地面移动统一使用 Move 状态：走动与奔跑共用同一 Clip，
/// 只在 MoveSpeed 参数上区分播放速度（不动 Animator.speed，跳跃与待机不受影响）。
/// 待机不再用计时器：进入地面静止时按 idleVariantChance 随机选 Idle 或 IdleVariant，
/// 当前段完整播放一轮后再重新随机（同一段被再次选中即从头重播）。
/// 不读取键盘、不通过世界坐标差计算速度。
/// 执行顺序（见 .meta）：PlayerController(-50) → PlayerAnimationDriver(-40) → PlayerBillboardVisual(-30)。
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationDriver : MonoBehaviour
{
    public PlayerController playerController;
    public PlayerBillboardVisual billboardVisual;

    [Tooltip("水平速度低于该值视为静止")]
    public float moveSpeedThreshold = 0.05f;

    [Tooltip("进入地面静止后，需要连续多少秒没有任何操作，才开始随机切换待机段；在此之前固定播放 Idle。")]
    public float idleStartDelay = 4f;

    [Tooltip("地面静止时选中 IdleVariant 的概率（默认 50%）")]
    [Range(0f, 1f)]
    public float idleVariantChance = 0.5f;

    [Tooltip("步伐节奏曲线：横轴 = Move 动画的归一化周期，纵轴 = 水平速度倍率。\n" +
             "默认 0.9~1.1 的轻微起伏，一个周期平均为 1（不改变平均速度）。\n" +
             "峰值位置对应蹬地推进阶段，可在曲线上自由调整；一个循环可以有多步，本脚本不做单步假设。")]
    public AnimationCurve moveStepCurve = new AnimationCurve(
        new Keyframe(0f, 0.9f),
        new Keyframe(0.5f, 1.1f),
        new Keyframe(1f, 0.9f));

    [Tooltip("步伐节奏强度：0 = 匀速移动（曲线不生效），1 = 完全按曲线起伏")]
    [Range(0f, 1f)]
    public float moveStepStrength = 1f;

    [Tooltip("Move 状态在走动时的播放速度倍率")]
    public float moveWalkPlaybackSpeed = 1f;

    [Tooltip("Move 状态在奔跑（按住 Shift）时的播放速度倍率")]
    public float moveRunPlaybackSpeed = 1.5f;

    [Tooltip("素材默认朝右时勾选；素材默认朝左时取消勾选")]
    public bool artFacesRight = true;

    static readonly int SpeedParam = Animator.StringToHash("Speed");
    static readonly int VerticalSpeedParam = Animator.StringToHash("VerticalSpeed");
    static readonly int GroundedParam = Animator.StringToHash("Grounded");
    static readonly int MoveSpeedParam = Animator.StringToHash("MoveSpeed");
    static readonly int IdleVariantSelectedParam = Animator.StringToHash("IdleVariantSelected");
    static readonly int IdleSelectTrigger = Animator.StringToHash("IdleSelect");
    static readonly int JumpStartedTrigger = Animator.StringToHash("JumpStarted");
    static readonly int IdleStateHash = Animator.StringToHash("Idle");
    static readonly int IdleVariantStateHash = Animator.StringToHash("IdleVariant");
    static readonly int MoveStateHash = Animator.StringToHash("Move");

    /// <summary>
    /// 当前水平速度倍率，来自 moveStepCurve 在 Move 动画归一化周期上的取值。
    /// 只在地面主动移动（Move 状态）时按曲线起伏，其余情况恒为 1。
    /// 由 PlayerController 读取后乘到移动速度上；曲线相位取自动画本身，
    /// 因此奔跑的 1.5 倍播放速度会自动让节奏同步加快。
    /// </summary>
    public float MoveRhythmMultiplier { get; private set; } = 1f;

    Animator animator;
    SpriteRenderer spriteRenderer;
    int lastSeenJumpCount;
    bool jumpQueued;

    // 控制器参数校验：避免用旧版 Player.controller 时每帧刷 "Parameter does not exist" 告警。
    RuntimeAnimatorController verifiedController;
    bool parametersVerified;
    bool hasMoveSpeedParam;
    bool hasJumpStartedParam;
    bool hasIdleSelectParam;
    bool hasIdleVariantSelectedParam;

    // 待机段追踪：记录当前段的播放进度，用于判断「完整播放一轮」。
    int idleCycleHash;
    bool idleCycleStarted;
    float idleCycleNormalizedTime;
    bool idleSelectionPending;
    float idleNoInputTimer;
    int groundedFrameStreak;

    // 连续贴地这么多帧才算真正站住，避免下落途中擦到地面一帧就播待机。
    const int RequiredGroundedFrames = 2;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();

        if (playerController != null)
            playerController.Jumped += HandleJumped;
    }

    void OnDisable()
    {
        if (playerController != null)
            playerController.Jumped -= HandleJumped;

        RestoreAnimatorSpeed();
    }

    void Update()
    {
        ResolveReferences();

        // 未配置动画控制器时保持旧的静态显示，驱动器不做任何事。
        if (playerController == null || billboardVisual == null || !billboardVisual.IsAnimatedModeActive)
        {
            MoveRhythmMultiplier = 1f;
            return;
        }

        if (animator == null || spriteRenderer == null)
        {
            MoveRhythmMultiplier = 1f;
            return;
        }

        // 镜头旋转、视角切换、第一人称期间：暂停播放、保留姿势与朝向，暂停第二待机计时。
        if (playerController.IsInputPaused)
        {
            if (animator.speed != 0f)
                animator.speed = 0f;
            MoveRhythmMultiplier = 1f;
            return;
        }

        if (animator.speed != 1f)
            animator.speed = 1f;

        VerifyAnimatorParameters();

        bool jumpStarted = jumpQueued || playerController.JumpCount != lastSeenJumpCount;
        lastSeenJumpCount = playerController.JumpCount;
        jumpQueued = false;

        float horizontalSpeed = playerController.HorizontalSpeed;
        float verticalSpeed = playerController.VerticalSpeed;
        bool grounded = playerController.IsGrounded && !jumpStarted;
        bool running = playerController.IsRunning;

        animator.SetFloat(SpeedParam, horizontalSpeed);
        animator.SetFloat(VerticalSpeedParam, verticalSpeed);
        animator.SetBool(GroundedParam, grounded);
        // 播放速度只看基础移动速度（走/跑各自的倍率），不读步伐节奏后的速度，避免互相反馈。
        if (hasMoveSpeedParam)
            animator.SetFloat(MoveSpeedParam, running ? moveRunPlaybackSpeed : moveWalkPlaybackSpeed);

        // 成功起跳优先级最高：当帧进入 Jump 并从头播放（触发器转换自带重播）。
        if (jumpStarted)
        {
            if (hasJumpStartedParam)
                animator.SetTrigger(JumpStartedTrigger);

            InterruptIdle();
        }
        else
        {
            UpdateIdlePlayback(grounded, horizontalSpeed);
        }

        UpdateMoveRhythm(grounded);
        ApplyFacing();
    }

    // 校验当前控制器是否具备本驱动需要的参数；旧版 Player.controller 缺少待机随机参数时
    // 只提示一次并停用待机随机，避免每帧抛出 "Parameter does not exist"。
    void VerifyAnimatorParameters()
    {
        RuntimeAnimatorController current = animator.runtimeAnimatorController;
        if (parametersVerified && verifiedController == current)
            return;

        parametersVerified = true;
        verifiedController = current;
        hasMoveSpeedParam = false;
        hasJumpStartedParam = false;
        hasIdleSelectParam = false;
        hasIdleVariantSelectedParam = false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            int hash = parameters[i].nameHash;
            if (hash == MoveSpeedParam)
                hasMoveSpeedParam = true;
            else if (hash == JumpStartedTrigger)
                hasJumpStartedParam = true;
            else if (hash == IdleSelectTrigger)
                hasIdleSelectParam = true;
            else if (hash == IdleVariantSelectedParam)
                hasIdleVariantSelectedParam = true;
        }

        if (!hasIdleSelectParam || !hasIdleVariantSelectedParam)
        {
            Debug.LogWarning(
                "[PlayerAnimationDriver] 当前 Player.controller 与驱动不匹配（缺少 IdleSelect / IdleVariantSelected），" +
                "待机会退回到 Controller 自身的旧逻辑。请运行 Tools → Player Animation Setup 重新生成 Controller。",
                this);
        }
    }

    // 步伐节奏：只在地面主动移动（Move 状态）时按动画归一化周期起伏，其余情况倍率为 1。
    // 相位直接取 Move 动画自身的归一化时间，所以播放越快节奏越快，无需单独积分。
    void UpdateMoveRhythm(bool grounded)
    {
        if (!grounded || moveStepStrength <= 0f)
        {
            MoveRhythmMultiplier = 1f;
            return;
        }

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != MoveStateHash)
        {
            MoveRhythmMultiplier = 1f;
            return;
        }

        float curveValue = moveStepCurve != null && moveStepCurve.length > 0
            ? moveStepCurve.Evaluate(Mathf.Repeat(info.normalizedTime, 1f))
            : 1f;

        MoveRhythmMultiplier = Mathf.Lerp(1f, curveValue, Mathf.Clamp01(moveStepStrength));
    }

    // 地面静止的处理分两步：
    // 1. 刚站住就先固定播放 Idle（不放任掉帧，也不立刻抽到 IdleVariant）；
    // 2. 连续无操作达到 idleStartDelay 秒后，才开始「一段播完一轮再随机」的循环。
    // 只依据 Animator 的实际播放进度判断一轮结束，两段长度可以不同。
    void UpdateIdlePlayback(bool grounded, float horizontalSpeed)
    {
        // 控制器缺参数时（旧版 Controller）不做随机选择，交给 Controller 自身逻辑。
        if (!hasIdleSelectParam || !hasIdleVariantSelectedParam)
            return;

        bool idleCandidate = grounded && horizontalSpeed <= moveSpeedThreshold;

        if (!idleCandidate)
        {
            // 有操作、离地或下落：不触发待机，无操作计时清零，再次站住时重新计时。
            InterruptIdle();
            return;
        }

        groundedFrameStreak++;
        if (groundedFrameStreak < RequiredGroundedFrames)
            return;

        if (animator.IsInTransition(0))
            return;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = info.shortNameHash;
        bool inIdleSegment = stateHash == IdleStateHash || stateHash == IdleVariantStateHash;

        if (!inIdleSegment)
        {
            // 刚站住：默认先固定播 Idle，无操作计时从零开始；
            // 把 idleStartDelay 设为 0 则视为不需要缓冲，立刻随机选段。
            idleNoInputTimer = 0f;

            if (!idleSelectionPending)
                SelectIdleSegment(useRandom: idleStartDelay <= 0f);

            ResetIdleCycleTracking();
            return;
        }

        // 已经在待机段内：累计无操作时间（暂停时不会执行到这里，计时自然冻结）。
        idleNoInputTimer += Time.deltaTime;

        float normalizedTime = info.normalizedTime;

        if (idleSelectionPending)
        {
            // 等待本次选择生效：换段看 stateHash 变化，同段重播看归一化时间回落到起点。
            bool segmentChanged = stateHash != idleCycleHash;
            bool restarted = normalizedTime <= 0.0001f || normalizedTime < idleCycleNormalizedTime - 0.0001f;
            if (!segmentChanged && !restarted)
            {
                // 仍停在上一次的末尾（非循环段），继续等待 Animator 应用新的选择。
                idleCycleNormalizedTime = normalizedTime;
                return;
            }

            idleSelectionPending = false;
            idleCycleHash = stateHash;
            idleCycleStarted = false;
            idleCycleNormalizedTime = normalizedTime;
            return;
        }

        if (stateHash != idleCycleHash)
        {
            ResetIdleCycleTracking();
            idleCycleHash = stateHash;
            idleCycleNormalizedTime = normalizedTime;
            return;
        }

        // 一轮完整播放结束：循环段回绕，非循环段停在末尾。
        bool atEnd = normalizedTime >= 1f - 0.0001f;
        bool wrapped = normalizedTime < idleCycleNormalizedTime - 0.0001f;

        if (idleCycleStarted && (atEnd || wrapped))
        {
            // 无操作时间还不够：保持当前段，不切换。
            if (idleNoInputTimer < idleStartDelay)
            {
                // 循环段继续转下一轮；非循环段停在末尾，保留标记以便计时到点时立刻切换。
                idleCycleStarted = atEnd;
            }
            else
            {
                SelectIdleSegment(useRandom: true);
                return;
            }
        }

        if (normalizedTime > idleCycleNormalizedTime)
            idleCycleStarted = true;

        idleCycleNormalizedTime = normalizedTime;
    }

    // useRandom = false：站住时固定播 Idle；true：无操作够久后的随机切换。
    void SelectIdleSegment(bool useRandom)
    {
        bool useVariant = useRandom && Random.value < idleVariantChance;
        animator.SetBool(IdleVariantSelectedParam, useVariant);
        animator.ResetTrigger(IdleSelectTrigger);
        animator.SetTrigger(IdleSelectTrigger);
        idleSelectionPending = true;
        idleCycleStarted = false;
    }

    void InterruptIdle()
    {
        idleSelectionPending = false;
        idleNoInputTimer = 0f;
        groundedFrameStreak = 0;
        if (hasIdleSelectParam)
            animator.ResetTrigger(IdleSelectTrigger);
        ResetIdleCycleTracking();
    }

    void ResetIdleCycleTracking()
    {
        idleCycleHash = 0;
        idleCycleStarted = false;
        idleCycleNormalizedTime = 0f;
    }

    void ApplyFacing()
    {
        // 朝向只跟随最近一次有效水平输入，镜头旋转与平台吸附不会改变它。
        bool facesLeft = playerController.FacingSign < 0f;
        bool flip = artFacesRight ? facesLeft : !facesLeft;

        if (spriteRenderer.flipX != flip)
            spriteRenderer.flipX = flip;
    }

    void HandleJumped()
    {
        // 起跳事件在 PlayerController.Update 内触发，先记下，本帧 Update 中消费。
        jumpQueued = true;
    }

    void RestoreAnimatorSpeed()
    {
        if (animator != null && animator.speed != 1f)
            animator.speed = 1f;
    }

    void ResolveReferences()
    {
        if (playerController == null)
            playerController = GetComponent<PlayerController>();

        if (billboardVisual == null)
            billboardVisual = GetComponent<PlayerBillboardVisual>();

        if (billboardVisual != null && animator == null)
            animator = billboardVisual.AnimatedAnimator;

        if (billboardVisual != null && spriteRenderer == null)
            spriteRenderer = billboardVisual.AnimatedSpriteRenderer;
    }
}
