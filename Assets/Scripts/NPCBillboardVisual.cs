using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class NPCBillboardVisual : MonoBehaviour
{
    public Texture2D texture;
    public Camera targetCamera;
    public Transform targetCameraTransform;
    public Vector2 size = new Vector2(1.1f, 1.8f);
    public Vector3 localOffset = new Vector3(0f, 0.9f, 0f);
    public Vector3 rotationCorrectionEuler = new Vector3(0f, 180f, 0f);
    public Color tint = Color.white;
    public bool hideOriginalMesh = true;
    public bool autoApplyPositionAndScale = true;
    public bool autoFaceCamera = true;
    public bool manualTransformMode = false;

    private const string VisualName = "npc_billboard_visual";

    private Transform visual;
    private MeshRenderer visualRenderer;
    private NPCBillboard billboard;
    private Material material;

    void OnEnable()
    {
        EnsureVisual();
        ApplyMaterialOnly();

        if (!manualTransformMode)
            ApplySettings();
    }

    void OnValidate()
    {
        if (visual == null)
            return;

        ApplyMaterialOnly();
        UpdateBillboardState();

        if (!manualTransformMode)
            ApplySettings();
    }

    void LateUpdate()
    {
        EnsureVisual();
        ApplyMaterialOnly();

        if (!manualTransformMode)
        {
            ApplySettings();

            if (autoFaceCamera)
                FaceCamera();
        }
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

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RegisterCreatedObjectUndo(visualObject, "Create NPC Billboard Visual");
#endif

            visualObject.name = VisualName;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.SetTransformParent(visualObject.transform, transform, "Parent NPC Billboard Visual");
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

        visualRenderer = visual.GetComponent<MeshRenderer>();
        if (visualRenderer != null && material == null)
        {
            Shader shader = Shader.Find("Custom/Unlit Transparent Double Sided");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            material = new Material(shader);
            material.name = "NPCBillboardMaterial";
            visualRenderer.sharedMaterial = material;
        }

        billboard = visual.GetComponent<NPCBillboard>();
        if (billboard == null)
            billboard = visual.gameObject.AddComponent<NPCBillboard>();

        UpdateBillboardState();
    }

    void ApplySettings()
    {
        if (visual == null)
            return;

        if (autoApplyPositionAndScale)
        {
            visual.localPosition = localOffset;
            visual.localScale = new Vector3(size.x, size.y, 1f);
        }

        ApplyMaterialOnly();

        UpdateBillboardState();

        if (hideOriginalMesh)
        {
            MeshRenderer originalRenderer = GetComponent<MeshRenderer>();
            if (originalRenderer != null)
                originalRenderer.enabled = false;
        }
    }

    void ApplyMaterialOnly()
    {
        if (material == null)
            return;

        material.mainTexture = texture;
        material.color = tint;
    }

    void FaceCamera()
    {
        if (billboard == null || visual == null)
            return;

        Transform cameraTransform = ResolveCameraTransform();

        billboard.targetCameraTransform = cameraTransform;
        billboard.rotationCorrectionEuler = rotationCorrectionEuler;

        if (cameraTransform != null)
            visual.rotation = cameraTransform.rotation * Quaternion.Euler(rotationCorrectionEuler);
    }

    Transform ResolveCameraTransform()
    {
        if (targetCameraTransform != null)
            return targetCameraTransform;

        if (targetCamera == null)
            targetCamera = Camera.main;

        return targetCamera != null ? targetCamera.transform : null;
    }

    void UpdateBillboardState()
    {
        if (billboard != null)
            billboard.enabled = autoFaceCamera && !manualTransformMode;
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
