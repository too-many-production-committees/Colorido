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

    private const string VisualName = "player_billboard_visual";

    private Transform visual;
    private MeshRenderer visualRenderer;
    private Material material;

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
    }

    void ApplySettings()
    {
        if (visual == null)
            return;

        visual.localPosition = localOffset;
        visual.localScale = new Vector3(size.x, size.y, 1f);

        if (material != null)
        {
            material.mainTexture = texture;
            material.color = tint;
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
    }

    void DestroyObject(Object target)
    {
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
