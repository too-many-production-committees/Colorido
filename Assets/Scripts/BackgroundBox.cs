using UnityEngine;

[ExecuteAlways]
public class BackgroundBox : MonoBehaviour
{
    public Vector3 center = new Vector3(0f, 4f, 0f);
    public float cubeSize = 28f;
    public float wallThickness = 0.2f;
    public Color color = new Color(0.22f, 0.28f, 0.4f, 1f);
    public float backBrightness = 1f;
    public float frontBrightness = 0.72f;
    public float leftBrightness = 0.86f;
    public float rightBrightness = 1.14f;
    public float ceilingBrightness = 1.28f;
    public float floorBrightness = 0.62f;
    public bool includeFloor = true;
    public bool rebuild;
    public float rotationLag = 0.18f;
    public float rotationOvershoot = 4f;

    private const string ContainerName = "generated_background_box";
    private Transform container;
    private float currentYaw;
    private float yawVelocity;
    private bool rebuildQueued;

    void OnEnable()
    {
        Build();
    }

    void Start()
    {
        Build();
    }

    void OnValidate()
    {
        QueueRebuild();
        rebuild = false;
    }

    void Update()
    {
        if (!rebuildQueued)
            return;

        rebuildQueued = false;
        Build();
    }

    void QueueRebuild()
    {
        rebuildQueued = true;
    }

    void Build()
    {
        if (!enabled)
            return;

        Transform existing = transform.Find(ContainerName);
        if (existing != null)
            DestroyObject(existing.gameObject);

        GameObject containerObject = new GameObject(ContainerName);
        container = containerObject.transform;
        container.SetParent(transform, true);
        container.position = center;
        container.rotation = Quaternion.identity;
        containerObject.hideFlags = HideFlags.DontSave;

        Vector3 size = Vector3.one * cubeSize;

        CreateWall(container, "back", new Vector3(0f, 0f, size.z * 0.5f), new Vector3(size.x, size.y, wallThickness), CreateMaterial(backBrightness));
        CreateWall(container, "front", new Vector3(0f, 0f, -size.z * 0.5f), new Vector3(size.x, size.y, wallThickness), CreateMaterial(frontBrightness));
        CreateWall(container, "left", new Vector3(-size.x * 0.5f, 0f, 0f), new Vector3(wallThickness, size.y, size.z), CreateMaterial(leftBrightness));
        CreateWall(container, "right", new Vector3(size.x * 0.5f, 0f, 0f), new Vector3(wallThickness, size.y, size.z), CreateMaterial(rightBrightness));
        CreateWall(container, "ceiling", new Vector3(0f, size.y * 0.5f, 0f), new Vector3(size.x, wallThickness, size.z), CreateMaterial(ceilingBrightness));

        if (includeFloor)
            CreateWall(container, "floor", new Vector3(0f, -size.y * 0.5f, 0f), new Vector3(size.x, wallThickness, size.z), CreateMaterial(floorBrightness));
    }

    Material CreateMaterial(float brightness)
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.color = color * Mathf.Max(0f, brightness);
        material.color = new Color(material.color.r, material.color.g, material.color.b, color.a);
        return material;
    }

    void CreateWall(Transform parent, string wallName, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = wallName;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = localPosition;
        wall.transform.localScale = scale;
        wall.hideFlags = HideFlags.DontSave;

        Collider wallCollider = wall.GetComponent<Collider>();
        if (wallCollider != null)
            DestroyObject(wallCollider);

        Renderer renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
    }

    void DestroyObject(Object target)
    {
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    public void SetCameraYaw(float cameraYaw, float rotationPulse)
    {
        if (container == null)
        {
            Transform existing = transform.Find(ContainerName);
            if (existing == null)
                Build();
            else
                container = existing;
        }

        float targetYaw = cameraYaw;
        currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, Mathf.Max(0.01f, rotationLag));
        float overshoot = Mathf.Sin(rotationPulse * Mathf.PI) * rotationOvershoot;
        container.localRotation = Quaternion.Euler(0f, currentYaw + overshoot, 0f);
    }
}
