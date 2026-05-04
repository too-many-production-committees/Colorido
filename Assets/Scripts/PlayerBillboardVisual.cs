using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(PlayerController))]
public class PlayerBillboardVisual : MonoBehaviour
{
    public Texture2D texture;
    public Camera targetCamera;
    public Vector2 size = new Vector2(1.1f, 1.8f);
    public Vector3 localOffset = new Vector3(0f, 0.85f, 0f);
    public Color tint = Color.white;
    public bool hideOriginalMesh = true;
    public bool showOccludedShadow = true;
    public Color occludedShadowColor = new Color(0.05f, 0.08f, 0.14f, 0.55f);
    public float occludedShadowScale = 1.08f;
    public float occludedShadowAlphaCutoff = 0.1f;

    private const string VisualName = "player_billboard_visual";
    private const string ShadowName = "player_occluded_shadow";

    private Transform visual;
    private Transform shadowVisual;
    private MeshRenderer visualRenderer;
    private MeshRenderer shadowRenderer;
    private Material material;
    private Material shadowMaterial;

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

    void EnsureVisual()
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
            visualObject.name = VisualName;
            visualObject.transform.SetParent(transform, false);

            Collider visualCollider = visualObject.GetComponent<Collider>();
            if (visualCollider != null)
                DestroyObject(visualCollider);

            visual = visualObject.transform;
        }

        visualRenderer = visual.GetComponent<MeshRenderer>();
        if (visualRenderer != null && material == null)
        {
            Shader shader = Shader.Find("Custom/Billboard Image");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            material = new Material(shader);
            material.name = "PlayerBillboardMaterial";
            visualRenderer.sharedMaterial = material;
        }

        EnsureShadowVisual();
    }

    void ApplySettings()
    {
        if (visual == null)
            return;

        visual.localPosition = localOffset;
        visual.localScale = new Vector3(size.x, size.y, 1f);

        if (shadowVisual != null)
        {
            shadowVisual.gameObject.SetActive(showOccludedShadow);
            shadowVisual.localPosition = localOffset;
            shadowVisual.localScale = new Vector3(
                size.x * occludedShadowScale,
                size.y * occludedShadowScale,
                1f);
        }

        if (material != null)
        {
            material.mainTexture = texture;
            material.color = tint;
        }

        if (shadowMaterial != null)
        {
            shadowMaterial.mainTexture = texture;
            shadowMaterial.color = occludedShadowColor;
            shadowMaterial.SetFloat("_AlphaCutoff", occludedShadowAlphaCutoff);
        }

        if (hideOriginalMesh)
        {
            MeshRenderer originalRenderer = GetComponent<MeshRenderer>();
            if (originalRenderer != null)
                originalRenderer.enabled = false;
        }
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
        if (shadowVisual != null)
            shadowVisual.rotation = visual.rotation;
    }

    void EnsureShadowVisual()
    {
        if (shadowVisual == null)
        {
            Transform existing = transform.Find(ShadowName);
            if (existing != null)
                shadowVisual = existing;
        }

        if (shadowVisual == null)
        {
            GameObject shadowObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadowObject.name = ShadowName;
            shadowObject.transform.SetParent(transform, false);

            Collider shadowCollider = shadowObject.GetComponent<Collider>();
            if (shadowCollider != null)
                DestroyObject(shadowCollider);

            shadowVisual = shadowObject.transform;
        }

        shadowRenderer = shadowVisual.GetComponent<MeshRenderer>();
        if (shadowRenderer != null && shadowMaterial == null)
        {
            Shader shader = Shader.Find("Custom/Billboard Occluded Shadow");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            shadowMaterial = new Material(shader);
            shadowMaterial.name = "PlayerOccludedShadowMaterial";
            shadowRenderer.sharedMaterial = shadowMaterial;
        }
    }

    void DestroyObject(Object target)
    {
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
