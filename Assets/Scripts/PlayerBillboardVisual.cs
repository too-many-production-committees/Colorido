using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(PlayerController))]
public class PlayerBillboardVisual : MonoBehaviour
{
    [Header("Static Quad Mode")]
    public Texture2D texture;
    public Camera targetCamera;
    public Vector2 size = new Vector2(1.1f, 1.8f);
    public Vector3 localOffset = new Vector3(0f, 0.85f, 0f);
    public Color tint = Color.white;
    public bool hideOriginalMesh = true;

    [Header("Animated Sprite Mode")]
    [Tooltip("不为空时改用 AnimatedSprite 子节点播放帧动画；为空时保持旧的静态 Quad 显示。")]
    public RuntimeAnimatorController animationController;

    [Tooltip("动画角色 1 倍缩放下的显示基准高度（世界单位）。size.y 显示高度按它换算统一缩放。0 表示用首个精灵自动推导一次并缓存。")]
    public float animatedSpriteWorldHeight = 0f;

    private const string VisualName = "player_billboard_visual";
    private const string AnimatedSpriteName = "AnimatedSprite";

    private Transform visual;
    private MeshRenderer visualRenderer;
    private Material material;

    private Transform animatedSprite;
    private SpriteRenderer animatedSpriteRenderer;
    private Animator animatedAnimator;

    /// <summary>动画模式是否生效：配置了动画控制器即为动画模式。</summary>
    public bool IsAnimatedModeActive => animationController != null;

    public SpriteRenderer AnimatedSpriteRenderer => animatedSpriteRenderer;

    public Animator AnimatedAnimator => animatedAnimator;

    void OnEnable()
    {
        EnsureVisual();
        ApplySettings();
    }

    void OnValidate()
    {
        if (visual != null)
            ApplySettings();
    }

    void LateUpdate()
    {
        EnsureVisual();
        ApplySettings();
        FaceCamera();
    }

    void OnDestroy()
    {
        DestroyRuntimeMaterial();
    }

    public void EnsureVisual()
    {
        if (visual == null)
        {
            Transform existing = transform.Find(VisualName);
            if (existing != null)
                visual = existing;
        }

        if (visual == null)
        {
            GameObject visualObject = GameObject.CreatePrimitive(PrimitiveType.Quad);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RegisterCreatedObjectUndo(visualObject, "Create Player Billboard Visual");
#endif

            visualObject.name = VisualName;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.SetTransformParent(visualObject.transform, transform, "Parent Player Billboard Visual");
            else
                visualObject.transform.SetParent(transform, false);
#else
            visualObject.transform.SetParent(transform, false);
#endif

            Collider visualCollider = visualObject.GetComponent<Collider>();
            if (visualCollider != null)
                DestroyGeneratedObject(visualCollider);

            visual = visualObject.transform;
        }

        if (IsAnimatedModeActive)
        {
            EnsureAnimatedSprite();
            DisableQuadRenderer();
        }
        else
        {
            EnsureQuadRenderer();
            HideAnimatedSprite();
        }
    }

    public void ApplySettings()
    {
        if (visual == null)
            return;

        visual.localPosition = localOffset;

        if (IsAnimatedModeActive)
        {
            // 动画模式下 billboard 节点不缩放，统一缩放在 AnimatedSprite 上按基准高度换算。
            visual.localScale = Vector3.one;

            if (animatedSpriteRenderer != null)
                animatedSpriteRenderer.color = tint;

            ApplyAnimatedSpriteScale();
        }
        else
        {
            visual.localScale = new Vector3(size.x, size.y, 1f);

            if (material != null)
            {
                material.mainTexture = texture;
                material.color = tint;
            }
        }

        if (hideOriginalMesh)
        {
            MeshRenderer originalRenderer = GetComponent<MeshRenderer>();
            if (originalRenderer != null)
                originalRenderer.enabled = false;
        }
    }

    void EnsureQuadRenderer()
    {
        if (visualRenderer == null)
            visualRenderer = visual.GetComponent<MeshRenderer>();

        if (visualRenderer == null && visual.GetComponent<MeshFilter>() == null)
        {
            // 兼容空节点情况：补一个默认 Quad 网格。
            visual.gameObject.AddComponent<MeshFilter>().sharedMesh =
                Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            visualRenderer = visual.gameObject.AddComponent<MeshRenderer>();
        }

        if (visualRenderer != null)
            visualRenderer.enabled = true;

        if (visualRenderer != null && material == null)
        {
            Shader shader = Shader.Find("Custom/Billboard Image");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            material = new Material(shader);
            material.name = "PlayerBillboardMaterial";
            visualRenderer.sharedMaterial = material;
        }
    }

    void DisableQuadRenderer()
    {
        if (visualRenderer == null)
            visualRenderer = visual.GetComponent<MeshRenderer>();

        if (visualRenderer != null)
            visualRenderer.enabled = false;

        // 动画模式不使用 Quad 临时材质，及时销毁避免泄漏。
        DestroyRuntimeMaterial();
    }

    void EnsureAnimatedSprite()
    {
        if (animatedSprite == null)
        {
            Transform existing = visual.Find(AnimatedSpriteName);
            if (existing != null)
            {
                animatedSprite = existing;
                animatedSpriteRenderer = null;
                animatedAnimator = null;
            }
        }

        if (animatedSprite == null)
        {
            GameObject spriteObject = new GameObject(AnimatedSpriteName);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RegisterCreatedObjectUndo(spriteObject, "Create Player Animated Sprite");
#endif

            spriteObject.transform.SetParent(visual, false);
            animatedSprite = spriteObject.transform;
        }

        if (animatedSpriteRenderer == null)
            animatedSpriteRenderer = animatedSprite.GetComponent<SpriteRenderer>();

        if (animatedSpriteRenderer == null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                animatedSpriteRenderer = Undo.AddComponent<SpriteRenderer>(animatedSprite.gameObject);
            else
                animatedSpriteRenderer = animatedSprite.gameObject.AddComponent<SpriteRenderer>();
#else
            animatedSpriteRenderer = animatedSprite.gameObject.AddComponent<SpriteRenderer>();
#endif
        }

        // 工程为内置渲染管线：SpriteRenderer 默认材质即 Sprites/Default（透明、无光照）。
        // 若被清空保持 null，Unity 会回退到同一默认材质，无需额外创建。

        if (animatedAnimator == null)
            animatedAnimator = animatedSprite.GetComponent<Animator>();

        if (animatedAnimator == null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                animatedAnimator = Undo.AddComponent<Animator>(animatedSprite.gameObject);
            else
                animatedAnimator = animatedSprite.gameObject.AddComponent<Animator>();
#else
            animatedAnimator = animatedSprite.gameObject.AddComponent<Animator>();
#endif
        }

        if (animatedAnimator.runtimeAnimatorController != animationController)
            animatedAnimator.runtimeAnimatorController = animationController;

        // 只驱动 SpriteRenderer.sprite，不回写节点位置、旋转与缩放。
        if (animatedAnimator.applyRootMotion)
            animatedAnimator.applyRootMotion = false;

        if (animatedAnimator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            animatedAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (!animatedSprite.gameObject.activeSelf)
            animatedSprite.gameObject.SetActive(true);
    }

    void HideAnimatedSprite()
    {
        // 静态模式下隐藏动画节点，避免两种显示同时出现重复角色。
        if (animatedSprite != null && animatedSprite.gameObject.activeSelf)
            animatedSprite.gameObject.SetActive(false);
    }

    void ApplyAnimatedSpriteScale()
    {
        if (animatedSprite == null)
            return;

        if (animatedSpriteWorldHeight <= 0f &&
            animatedSpriteRenderer != null &&
            animatedSpriteRenderer.sprite != null)
        {
            // 只在首次拿到精灵时推导一次并缓存；
            // 不逐帧按当前帧重新缩放，避免动作切换时人物呼吸或变形。
            animatedSpriteWorldHeight = animatedSpriteRenderer.sprite.bounds.size.y;
        }

        float scale = animatedSpriteWorldHeight > 0.0001f
            ? size.y / animatedSpriteWorldHeight
            : 1f;

        animatedSprite.localScale = new Vector3(scale, scale, 1f);
    }

    void FaceCamera()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null || visual == null)
            return;

        Vector3 forward = targetCamera.transform.position - visual.position;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = -targetCamera.transform.forward;

        visual.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    void DestroyRuntimeMaterial()
    {
        if (material == null)
            return;

        if (visualRenderer != null && visualRenderer.sharedMaterial == material)
            visualRenderer.sharedMaterial = null;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(material, true);
        else
            Destroy(material);
#else
        Destroy(material);
#endif

        material = null;
    }

    void DestroyGeneratedObject(Object target)
    {
        if (target == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(target);
            return;
        }
#endif

        Destroy(target);
    }
}
