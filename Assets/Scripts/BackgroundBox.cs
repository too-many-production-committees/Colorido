using UnityEngine;
using System.Collections.Generic;

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
        MeshFilter meshFilter = containerObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = containerObject.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = CreateBackgroundMesh(size);
        meshRenderer.sharedMaterials = CreateMaterials();
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

    Material[] CreateMaterials()
    {
        List<Material> materials = new List<Material>
        {
            CreateMaterial(backBrightness),
            CreateMaterial(frontBrightness),
            CreateMaterial(leftBrightness),
            CreateMaterial(rightBrightness),
            CreateMaterial(ceilingBrightness)
        };

        if (includeFloor)
            materials.Add(CreateMaterial(floorBrightness));

        return materials.ToArray();
    }

    Mesh CreateBackgroundMesh(Vector3 size)
    {
        float halfX = size.x * 0.5f;
        float halfY = size.y * 0.5f;
        float halfZ = size.z * 0.5f;

        List<Vector3> vertices = new List<Vector3>();
        List<int[]> submeshTriangles = new List<int[]>();

        AddQuad(
            vertices,
            submeshTriangles,
            new Vector3(-halfX, -halfY, halfZ),
            new Vector3(-halfX, halfY, halfZ),
            new Vector3(halfX, halfY, halfZ),
            new Vector3(halfX, -halfY, halfZ));
        AddQuad(
            vertices,
            submeshTriangles,
            new Vector3(-halfX, -halfY, -halfZ),
            new Vector3(halfX, -halfY, -halfZ),
            new Vector3(halfX, halfY, -halfZ),
            new Vector3(-halfX, halfY, -halfZ));
        AddQuad(
            vertices,
            submeshTriangles,
            new Vector3(-halfX, -halfY, -halfZ),
            new Vector3(-halfX, halfY, -halfZ),
            new Vector3(-halfX, halfY, halfZ),
            new Vector3(-halfX, -halfY, halfZ));
        AddQuad(
            vertices,
            submeshTriangles,
            new Vector3(halfX, -halfY, -halfZ),
            new Vector3(halfX, -halfY, halfZ),
            new Vector3(halfX, halfY, halfZ),
            new Vector3(halfX, halfY, -halfZ));
        AddQuad(
            vertices,
            submeshTriangles,
            new Vector3(-halfX, halfY, -halfZ),
            new Vector3(halfX, halfY, -halfZ),
            new Vector3(halfX, halfY, halfZ),
            new Vector3(-halfX, halfY, halfZ));

        if (includeFloor)
        {
            AddQuad(
                vertices,
                submeshTriangles,
                new Vector3(-halfX, -halfY, -halfZ),
                new Vector3(-halfX, -halfY, halfZ),
                new Vector3(halfX, -halfY, halfZ),
                new Vector3(halfX, -halfY, -halfZ));
        }

        Mesh mesh = new Mesh
        {
            name = "Combined Background Box Mesh"
        };
        mesh.SetVertices(vertices);
        mesh.subMeshCount = submeshTriangles.Count;

        for (int i = 0; i < submeshTriangles.Count; i++)
            mesh.SetTriangles(submeshTriangles[i], i);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.hideFlags = HideFlags.DontSave;
        return mesh;
    }

    void AddQuad(
        List<Vector3> vertices,
        List<int[]> submeshTriangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        submeshTriangles.Add(new[]
        {
            start,
            start + 1,
            start + 2,
            start,
            start + 2,
            start + 3
        });
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
