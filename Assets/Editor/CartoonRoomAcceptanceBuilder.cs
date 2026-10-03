using System;
using System.IO;
using System.Text;
using CartoonProjection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Generated, project-local acceptance fixtures. Never modifies SampleScene or game controls.
public static class CartoonRoomAcceptanceBuilder
{
    const string Root = "Assets/CartoonRenderer/Acceptance";
    const string ScenePath = "Assets/Scenes/CartoonRoomAcceptance.unity";
    const string RendererPath = "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset";
    static readonly Color[] bookColors = {
        new Color(.72f, .29f, .23f), new Color(.22f, .49f, .53f),
        new Color(.86f, .65f, .24f), new Color(.66f, .72f, .54f) };

    [MenuItem("Cartoon Migration/4. Build Room Acceptance Scene")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void BuildAndVerify()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch process for verification.");
        try
        {
            Build();
            CaptureRoom();
            // Also verify the normal asynchronous path in this main checkout.
            CartoonProjectionRegression.Run();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void Build()
    {
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Textures");
        Directory.CreateDirectory(Root + "/Captures");
        AssetDatabase.Refresh();
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (!data) throw new InvalidOperationException("Cartoon Universal Renderer is missing.");
        CartoonRendererFeature feature = null;
        foreach (var candidate in data.rendererFeatures)
            if (candidate is CartoonRendererFeature cartoon) feature = cartoon;
        if (!feature) throw new InvalidOperationException("Cartoon feature is missing.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.42f, .45f, .48f);
        RenderSettings.fog = false;

        var woodTexture = Texture("WoodBoards", (x, y) => {
            int row = y / 32;
            int board = ((x + (row % 2) * 16) / 32 + row) % 3;
            Color c = board == 0 ? new Color(.68f, .49f, .32f) : board == 1 ? new Color(.57f, .38f, .24f) : new Color(.76f, .58f, .4f);
            return y % 32 < 2 || (x + (row % 2) * 16) % 32 < 2 ? c * .8f : c;
        });
        var posterTexture = Texture("AbstractPrint", (x, y) => {
            if (x < 8 || x > 119 || y < 8 || y > 119) return new Color(.93f, .87f, .73f);
            if ((x - 68) * (x - 68) + (y - 83) * (y - 83) < 500) return bookColors[2];
            return y < 32 + x / 3 ? bookColors[1] : y < 63 + x / 4 ? bookColors[0] : new Color(.83f, .79f, .64f);
        });
        Material wood = Mat("Wood", Color.white, woodTexture);
        wood.SetTextureScale("_BaseMap", new Vector2(2, 1));
        Material darkWood = Mat("Walnut", new Color(.31f, .21f, .17f));
        Material cream = Mat("Cream", new Color(.83f, .76f, .64f));
        Material mint = Mat("MintWall", new Color(.57f, .7f, .65f));
        Material blue = Mat("BlueFabric", new Color(.29f, .46f, .53f));
        Material green = Mat("GreenFabric", new Color(.24f, .43f, .37f));
        Material greenLight = Mat("GreenCushion", new Color(.42f, .59f, .48f));
        Material terracotta = Mat("Terracotta", new Color(.71f, .36f, .26f));
        Material trim = Mat("IvoryTrim", new Color(.93f, .87f, .73f));
        Material ink = Mat("Ink", new Color(.16f, .2f, .23f));
        Material sky = Mat("WindowBlue", new Color(.46f, .72f, .8f));
        var room = new GameObject("Acceptance Room - original albedo materials").transform;
        Cube("Wood floor", room, new Vector3(0, -.12f, 0), new Vector3(12, .24f, 8), wood);
        Cube("Terracotta rug", room, new Vector3(-1.3f, .018f, -.7f), new Vector3(4.6f, .035f, 3.2f), terracotta);
        Cube("Rug inset", room, new Vector3(-1.3f, .039f, -.7f), new Vector3(4.25f, .008f, 2.85f), Mat("RugWarm", new Color(.82f, .55f, .36f)));

        var back = Group("Cutaway back wall", room, new Vector3(0, 0, 4));
        Cube("Back left", back, new Vector3(-2.4f, 2, 4), new Vector3(7.2f, 4, .18f), cream);
        Cube("Back right", back, new Vector3(5.05f, 2, 4), new Vector3(1.9f, 4, .18f), cream);
        Cube("Below window", back, new Vector3(2.65f, 1, 4), new Vector3(2.9f, 2, .18f), cream);
        Cube("Above window", back, new Vector3(2.65f, 3.7f, 4), new Vector3(2.9f, .6f, .18f), cream);
        Cube("Back skirting", back, new Vector3(0, .13f, 3.88f), new Vector3(12, .24f, .1f), trim);
        Cube("Window view", back, new Vector3(2.65f, 2.7f, 4.1f), new Vector3(2.8f, 1.3f, .04f), sky);
        foreach (float x in new[] { 1.2f, 2.65f, 4.1f })
            Cube("Window vertical frame", back, new Vector3(x, 2.7f, 3.86f), new Vector3(.09f, 1.5f, .16f), trim);
        foreach (float y in new[] { 2f, 3.4f })
            Cube("Window horizontal frame", back, new Vector3(2.65f, y, 3.86f), new Vector3(3f, .09f, .16f), trim);
        var left = Group("Cutaway mint wall", room, new Vector3(-6, 0, 0));
        Cube("Mint wall", left, new Vector3(-6, 2, 0), new Vector3(.18f, 4, 8), mint);
        Cube("Left skirting", left, new Vector3(-5.88f, .13f, 0), new Vector3(.1f, .24f, 8), trim);
        Cube("Picture frame", left, new Vector3(-5.84f, 2.4f, .15f), new Vector3(.08f, 1.65f, 1.45f), darkWood);
        var picture = Primitive("Abstract wall print", PrimitiveType.Quad, left,
            new Vector3(-5.79f, 2.4f, .15f), new Vector3(1.3f, 1.5f, 1), Mat("Print", Color.white, posterTexture));
        picture.transform.rotation = Quaternion.Euler(0, -90, 0);
        var right = Group("Cutaway blue wall", room, new Vector3(6, 0, 0));
        Cube("Blue wall", right, new Vector3(6, 2, 0), new Vector3(.18f, 4, 8), Mat("BlueWall", new Color(.64f, .73f, .76f)));

        var sofa = Group("Sofa", room, Vector3.zero);
        Cube("Sofa base", sofa, new Vector3(-3.6f, .52f, 1.75f), new Vector3(3.5f, .6f, 1.35f), green);
        Cube("Sofa back", sofa, new Vector3(-3.6f, 1.08f, 2.3f), new Vector3(3.5f, 1.15f, .25f), green);
        foreach (float x in new[] { -5.28f, -1.92f })
            Cube("Sofa arm", sofa, new Vector3(x, .93f, 1.75f), new Vector3(.24f, .68f, 1.35f), greenLight);
        for (int i = 0; i < 3; i++)
            Cube("Separate seat cushion", sofa, new Vector3(-4.67f + i * 1.06f, .87f, 1.7f), new Vector3(1.02f, .22f, 1.08f), greenLight);
        Cube("Ochre pillow", sofa, new Vector3(-4.55f, 1.24f, 2.04f), new Vector3(.56f, .57f, .2f), Mat("PillowOchre", bookColors[2])).transform.rotation = Quaternion.Euler(0, 0, -12);
        Table("Coffee table / occlusion probe", room, new Vector3(-1.9f, 0, -.5f), new Vector2(2.15f, 1.35f), .86f, wood, darkWood);
        Primitive("Ceramic cup", PrimitiveType.Cylinder, room, new Vector3(-1.45f, 1.05f, -.5f), new Vector3(.2f, .13f, .2f), trim);
        Cube("Table book", room, new Vector3(-2.3f, .98f, -.6f), new Vector3(.56f, .09f, .38f), blue);

        var shelf = Group("Bookcase", room, Vector3.zero);
        foreach (float x in new[] { -5.4f, -3.25f })
            Cube("Bookcase side", shelf, new Vector3(x, 1.45f, 3.4f), new Vector3(.13f, 2.9f, .56f), darkWood);
        for (int row = 0; row < 3; row++)
        {
            float y = .25f + row * .92f;
            Cube("Shelf", shelf, new Vector3(-4.33f, y, 3.4f), new Vector3(2.28f, .12f, .56f), wood);
            for (int i = 0; i < 7; i++)
            {
                float height = .42f + (i % 3) * .07f;
                Cube("Colored book", shelf, new Vector3(-5.1f + i * .24f, y + .06f + height / 2, 3.32f),
                    new Vector3(.18f, height, .36f), Mat("Book" + i % 4, bookColors[i % 4]));
            }
        }
        Table("Desk", room, new Vector3(2.65f, 0, 2.55f), new Vector2(2.6f, 1.1f), 1f, wood, darkWood);
        Cube("Chair seat", room, new Vector3(2.65f, .62f, 1.25f), new Vector3(.84f, .15f, .8f), blue);
        Cube("Chair back", room, new Vector3(2.65f, 1.03f, .9f), new Vector3(.84f, .72f, .14f), blue);
        Legs("Chair leg", room, new Vector3(2.65f, 0, 1.25f), .66f, .62f, .55f, darkWood);
        Cube("Monitor stand", room, new Vector3(2.65f, 1.21f, 2.7f), new Vector3(.12f, .36f, .16f), ink);
        Cube("Monitor frame", room, new Vector3(2.65f, 1.6f, 2.7f), new Vector3(1.04f, .7f, .09f), ink);
        Cube("Monitor painted display", room, new Vector3(2.65f, 1.6f, 2.642f), new Vector3(.92f, .58f, .025f), sky);
        Cube("Monitor painted detail", room, new Vector3(2.45f, 1.54f, 2.623f), new Vector3(.3f, .06f, .012f), trim);
        Primitive("Plant pot", PrimitiveType.Cylinder, room, new Vector3(4.75f, .3f, -.3f), new Vector3(.64f, .3f, .64f), terracotta);
        Cube("Plant stem", room, new Vector3(4.75f, 1.1f, -.3f), new Vector3(.06f, 1.5f, .06f), darkWood);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            var leaf = Primitive("Leaf silhouette", PrimitiveType.Sphere, room,
                new Vector3(4.75f + Mathf.Cos(a) * .3f, 1.1f + i % 3 * .25f, -.3f + Mathf.Sin(a) * .3f),
                new Vector3(.24f, .72f, .15f), i % 2 == 0 ? green : greenLight);
            leaf.transform.rotation = Quaternion.Euler(35, i * 45, 35);
        }

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5.6f;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 60;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.94f, .91f, .84f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var sun = new GameObject("Warm room sunlight", typeof(Light)).GetComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(48, -30, 0);
        sun.intensity = 1.2f;
        sun.color = new Color(1, .97f, .9f);
        sun.shadows = LightShadows.Soft;

        var actor = Group("2D character - movable occlusion probe", room, Vector3.zero);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/madoka.png");
        if (!texture) throw new InvalidOperationException("Existing character texture missing.");
        Material characterMaterial = Mat("CharacterOriginal", Color.white, texture, "Custom/Billboard Image");
        var visual = Primitive("Original 2D character", PrimitiveType.Quad, actor, new Vector3(0, 1.05f, 0),
            new Vector3(2.1f * texture.width / texture.height, 2.1f, 1), characterMaterial);
        var controls = new GameObject("Room Acceptance Controls").AddComponent<CartoonRoomAcceptanceController>();
        controls.viewCamera = camera;
        controls.feature = feature;
        controls.character = actor;
        controls.characterVisual = visual.transform;
        controls.cutawayWalls = new[] { back.gameObject, left.gameObject, right.gameObject };
        controls.outwardNormals = new[] { Vector3.forward, Vector3.left, Vector3.right };
        controls.ResetView();
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Cannot save room scene.");
        Debug.Log("[RoomAcceptance] Saved " + ScenePath);
    }

    static Transform Group(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        return go.transform;
    }

    static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material) =>
        Primitive(name, PrimitiveType.Cube, parent, position, scale, material);

    static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static void Table(string name, Transform parent, Vector3 position, Vector2 size, float height, Material top, Material legs)
    {
        var group = Group(name, parent, Vector3.zero);
        Cube("Table top", group, position + new Vector3(0, height, 0), new Vector3(size.x, .14f, size.y), top);
        Legs("Table leg", group, position, size.x - .25f, size.y - .25f, height - .07f, legs);
    }

    static void Legs(string name, Transform parent, Vector3 position, float width, float depth, float height, Material material)
    {
        foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            Cube(name, parent, position + new Vector3(x * width / 2, height / 2, z * depth / 2), new Vector3(.12f, height, .12f), material);
    }

    static Material Mat(string name, Color color, Texture texture = null, string shaderName = "Universal Render Pipeline/Lit")
    {
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(shaderName);
        if (!shader) throw new InvalidOperationException("Missing shader " + shaderName);
        if (!material) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
        material.shader = shader;
        material.SetColor(shaderName == "Custom/Billboard Image" ? "_Color" : "_BaseColor", color);
        if (texture) material.SetTexture(shaderName == "Custom/Billboard Image" ? "_MainTex" : "_BaseMap", texture);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .15f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D Texture(string name, Func<int, int, Color> sample)
    {
        string path = Root + "/Textures/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing) return existing;
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point };
        var pixels = new Color32[128 * 128];
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) pixels[y * 128 + x] = sample(x, y);
        texture.SetPixels32(pixels);
        texture.Apply();
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    static void CaptureRoom()
    {
        var controls = UnityEngine.Object.FindFirstObjectByType<CartoonRoomAcceptanceController>();
        var feature = controls.feature;
        var camera = controls.viewCamera;
        var settings = feature.settings;
        bool enabled = settings.enabled, contours = settings.projectionShowContours, projected = settings.projectedShapes;
        var previousTarget = camera.targetTexture;
        var target = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        camera.targetTexture = target;
        ProjectedShapePass.UseSynchronousReadback = true;
        var report = new StringBuilder($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceType}; room 640x480\n");
        try
        {
            settings.projectedShapes = true;
            settings.enabled = false;
            camera.Render();
            Save(target, "01_room_original");
            settings.enabled = true;
            settings.projectionShowContours = false;
            Rebuild(feature, camera);
            Save(target, "02_room_redrawn");
            report.AppendLine("Overview: " + ProjectedShapePass.LastStatistics);
            if (ProjectedShapePass.Regions < 20) throw new InvalidOperationException("Room projection missing material regions.");
            settings.projectionShowContours = true;
            camera.Render();
            Save(target, "03_room_contours");
            settings.projectionShowContours = false;
            controls.yaw = 28;
            controls.ApplyView();
            Rebuild(feature, camera);
            Save(target, "04_room_rotated");
            report.AppendLine("Rotated: " + ProjectedShapePass.LastStatistics);

            controls.SetOcclusionPose(false);
            Rebuild(feature, camera);
            int front = CountCharacterPixels(controls, target);
            Save(target, "05_character_front");
            controls.SetOcclusionPose(true);
            camera.Render();
            int behind = CountCharacterPixels(controls, target);
            Save(target, "06_character_behind");
            report.AppendLine($"Character visible pixels: front={front}, behind={behind} (difference threshold 6/255)");
            if (front < 50 || behind < 10 || behind >= front * .95f)
                throw new InvalidOperationException("Character front/behind occlusion comparison failed.");
            report.AppendLine("PASS: room redraw, contours, rotated view, 2D character front/behind depth occlusion");
            Debug.Log("[RoomAcceptance] " + report);
        }
        finally
        {
            File.WriteAllText(Root + "/RoomValidation.txt", report.ToString());
            settings.enabled = enabled;
            settings.projectionShowContours = contours;
            settings.projectedShapes = projected;
            ProjectedShapePass.UseSynchronousReadback = false;
            camera.targetTexture = previousTarget;
            UnityEngine.Object.DestroyImmediate(target);
            controls.ResetView();
        }
    }

    static void Rebuild(CartoonRendererFeature feature, Camera camera)
    {
        feature.Create();
        camera.Render();
        if (!ProjectedShapePass.ResolveSynchronousCapture()) throw new InvalidOperationException("Room capture did not resolve.");
        camera.Render();
    }

    static int CountCharacterPixels(CartoonRoomAcceptanceController controls, RenderTexture target)
    {
        var renderer = controls.characterVisual.GetComponent<Renderer>();
        renderer.enabled = false;
        controls.viewCamera.Render();
        Color32[] without = Read(target);
        renderer.enabled = true;
        controls.viewCamera.Render();
        Color32[] with = Read(target);
        int count = 0;
        for (int i = 0; i < with.Length; i++)
            if (Math.Abs(with[i].r - without[i].r) > 6 || Math.Abs(with[i].g - without[i].g) > 6 || Math.Abs(with[i].b - without[i].b) > 6) count++;
        return count;
    }

    static Color32[] Read(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            copy.Apply();
            return copy.GetPixels32();
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }

    static void Save(RenderTexture target, string name)
    {
        var copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try { copy.SetPixels32(Read(target)); copy.Apply(); File.WriteAllBytes(Root + "/Captures/" + name + ".png", copy.EncodeToPNG()); }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }
}
