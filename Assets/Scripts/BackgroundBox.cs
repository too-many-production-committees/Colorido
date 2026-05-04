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

        CreateCube(container);
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

    void CreateCube(Transform parent)
    {
        GameObject cube = new GameObject("background_cube");
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = Vector3.zero;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = Vector3.one;
        cube.hideFlags = HideFlags.DontSave;

        MeshFilter meshFilter = cube.AddComponent<MeshFilter>();
        MeshRenderer renderer = cube.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = CreateInsideCubeMesh(cubeSize);

        if (renderer != null)
        {
            renderer.sharedMaterials = new[]
            {
                CreateMaterial(backBrightness),
                CreateMaterial(frontBrightness),
                CreateMaterial(leftBrightness),
                CreateMaterial(rightBrightness),
                CreateMaterial(ceilingBrightness),
                CreateMaterial(floorBrightness)
            };
        }
    }

    Mesh CreateInsideCubeMesh(float size)
    {
        float h = size * 0.5f;
        Mesh mesh = new Mesh();
        mesh.name = "BackgroundBox_InsideCube";

        Vector3[] vertices =
        {
            new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h),
            new Vector3(h, -h, -h), new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(h, h, -h),
            new Vector3(-h, -h, -h), new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h),
            new Vector3(h, -h, h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h),
            new Vector3(-h, h, h), new Vector3(h, h, h), new Vector3(h, h, -h), new Vector3(-h, h, -h),
            new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h)
        };

        int[][] triangles =
        {
            new[] { 0, 2, 1, 0, 3, 2 },
            new[] { 4, 6, 5, 4, 7, 6 },
            new[] { 8, 10, 9, 8, 11, 10 },
            new[] { 12, 14, 13, 12, 15, 14 },
            new[] { 16, 18, 17, 16, 19, 18 },
            new[] { 20, 22, 21, 20, 23, 22 }
        };

        mesh.vertices = vertices;
        mesh.subMeshCount = triangles.Length;

        for (int i = 0; i < triangles.Length; i++)
            mesh.SetTriangles(triangles[i], i);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
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
