using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using CartoonProjection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Migration helper for moving the projected-2D-shape renderer from the Built-in pipeline
// into Colorido's URP setup. Everything it touches is either regenerated on demand or
// scoped to the migration preview scene; the original scenes are only repaired where a
// Built-in-only material would otherwise render magenta under URP.
public static class CartoonMigrationTools
{
    private const string PipelinePath = "Assets/CartoonRenderer/Settings/CartoonUniversalRenderPipeline.asset";
    private const string RendererPath = "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset";
    private const string FixMaterialPath = "Assets/Materials/URP_Migration_Gray.mat";
    private const string PreviewScenePath = "Assets/Scenes/CartoonMigrationPreview.unity";
    private const string TestModelName = "cartoon_projection_test_model";
    private const string TestTexturePath = "Assets/CartoonRenderer/Generated/ProjectionTestTex.asset";
    private const string CaptureDir = "Assets/CartoonRenderer/Generated/Captures";
    private const string ReportPath = "Assets/CartoonRenderer/Generated/MigrationReport.txt";

    [MenuItem("Cartoon Migration/1. Fix Built-in Materials For URP")]
    public static void FixMaterialsMenu()
    {
        var log = new StringBuilder();
        FixBuiltInMaterials(log);
        Debug.Log(log.ToString());
    }

    [MenuItem("Cartoon Migration/2. Create Preview Scene")]
    public static void CreatePreviewSceneMenu()
    {
        var log = new StringBuilder();
        CreatePreviewScene(log);
        Debug.Log(log.ToString());
    }

    [MenuItem("Cartoon Migration/3. Capture Comparison")]
    public static void CaptureComparisonMenu()
    {
        var log = new StringBuilder();
        CaptureComparison(log, true);
        Debug.Log(log.ToString());
    }

    [MenuItem("Cartoon Migration/0. Run All")]
    public static void RunAllMenu()
    {
        var log = new StringBuilder();
        FixBuiltInMaterials(log);
        CreatePreviewScene(log);
        var report = CaptureComparison(log, false);
        Debug.Log(log.ToString());
        Debug.Log(report);
    }

    // Batch entry point: -executeMethod CartoonMigrationTools.RunAll
    public static void RunAll()
    {
        var log = new StringBuilder();
        var report = new StringBuilder();
        try
        {
            FixBuiltInMaterials(log);
            CreatePreviewScene(log);
            report.Append(CaptureComparison(log, false));
        }
        catch (Exception e)
        {
            log.AppendLine("FATAL: " + e);
            log.AppendLine(e.StackTrace ?? string.Empty);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? ".");
        File.WriteAllText(ReportPath, log.AppendLine().Append(report).ToString());
        Debug.Log(log.ToString());
        Debug.Log(report.ToString());
    }


    // Renders the camera into a temporary target and writes it to disk.
    private static void SaveCameraTo(Camera camera, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        int width = Mathf.Max(64, camera.pixelWidth);
        int height = Mathf.Max(64, camera.pixelHeight);
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D shot = null;
        try
        {
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previousTarget;

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            shot = new Texture2D(width, height, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply(false, false);
            RenderTexture.active = previousActive;

            File.WriteAllBytes(path, shot.EncodeToPNG());
        }
        finally
        {
            RenderTexture.ReleaseTemporary(rt);
            if (shot != null)
                UnityEngine.Object.DestroyImmediate(shot);
        }
    }


    // The background box builds its mesh from Update(), which never runs during a blocking
    // batch call. Driving its public yaw entry point forces the build so captures see the
    // same room the editor shows.
    private static void EnsureBackgroundBox(StringBuilder log)
    {
        var backgroundBox = UnityEngine.Object.FindFirstObjectByType<BackgroundBox>();
        if (backgroundBox == null)
        {
            log?.AppendLine("background box: component missing from preview scene");
            return;
        }

        Transform generated = backgroundBox.transform.Find("generated_background_box");
        if (generated == null)
        {
            backgroundBox.SetCameraYaw(0f, 0f);
            generated = backgroundBox.transform.Find("generated_background_box");
        }

        var bgRenderer = generated != null ? generated.GetComponent<MeshRenderer>() : null;
        Log(log, $"background box: enabled={backgroundBox.enabled} generated={(generated != null)} " +
                 $"renderer={(bgRenderer != null)} " +
                 $"materials={(bgRenderer != null && bgRenderer.sharedMaterials != null ? bgRenderer.sharedMaterials.Length : 0)}");
    }

    // Renders one frame and, in synchronous tooling mode, rebuilds the mesh from the
    // capture the frame just produced. The blocking GPU read has to happen after
    // Camera.Render returns, because the render graph is recorded before it executes.
    private static bool RenderAndResolve(Camera camera)
    {
        camera.Render();
        return ProjectedShapePass.ResolveSynchronousCapture();
    }



    private static void Log(StringBuilder log, string message)
    {
        log?.AppendLine(message);
        Debug.Log("[CartoonMigration] " + message);
    }

    // ---------------------------------------------------------------- materials

    private static void FixBuiltInMaterials(StringBuilder log)
    {
        var fixMaterial = LoadOrCreateFixMaterial(log);
        int replaced = 0;
        var scenes = new List<string> { "Assets/Scenes/SampleScene.unity" };

        foreach (string scenePath in scenes)
        {
            if (!File.Exists(scenePath))
                continue;

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            bool dirty = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                dirty |= ReplaceMissingMaterials(root, fixMaterial, log, ref replaced);

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Log(log, $"Scene repaired and saved: {scenePath}");
            }
            else
            {
                Log(log, $"Scene needed no material repair: {scenePath}");
            }
        }

        Log(log, $"Built-in material repair finished; {replaced} renderer slot(s) replaced.");
    }

    private static bool ReplaceMissingMaterials(GameObject go, Material fixMaterial, StringBuilder log, ref int replaced)
    {
        bool dirty = false;
        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!IsBuiltInOnlyMaterial(materials[i]))
                    continue;
                log.AppendLine($"    URP-incompatible shader '{materials[i].shader.name}' on " +
                               $"{GetPath(go)}[{i}] -> {fixMaterial.shader.name}");
                materials[i] = fixMaterial;
                replaced++;
                dirty = true;
            }
            if (dirty)
                renderer.sharedMaterials = materials;
        }

        foreach (Transform child in go.transform)
            dirty |= ReplaceMissingMaterials(child.gameObject, fixMaterial, log, ref replaced);

        return dirty;
    }

    private static bool IsBuiltInOnlyMaterial(Material material)
    {
        if (material == null || material.shader == null)
            return false;

        string name = material.shader.name;
        if (name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
            return false;
        if (name.StartsWith("Custom/", StringComparison.Ordinal) ||
            name.StartsWith("Hidden/", StringComparison.Ordinal))
            return false;

        // Built-in Standard/Default-Material and legacy Unlit* render magenta under URP.
        return name == "Standard" ||
               name.StartsWith("Standard (", StringComparison.Ordinal) ||
               name.StartsWith("Legacy Shaders/", StringComparison.Ordinal);
    }

    private static Material LoadOrCreateFixMaterial(StringBuilder log)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(FixMaterialPath);
        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("URP Lit shader not found; is the URP package installed?");

        Directory.CreateDirectory(Path.GetDirectoryName(FixMaterialPath) ?? ".");
        material = new Material(shader) { name = "URP_Migration_Gray" };
        material.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.5f, 1f));
        material.SetFloat("_Smoothness", 0.25f);
        AssetDatabase.CreateAsset(material, FixMaterialPath);
        AssetDatabase.SaveAssets();
        Log(log, $"Created neutral URP material: {FixMaterialPath}");
        return material;
    }

    private static string GetPath(GameObject go)
    {
        var path = go.name;
        Transform t = go.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }

    // ------------------------------------------------------------ preview scene

    private static void CreatePreviewScene(StringBuilder log)
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
            throw new InvalidOperationException($"URP pipeline asset not found: {PipelinePath}");

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
            throw new InvalidOperationException($"Universal Renderer asset not found: {RendererPath}");

        var source = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

        var fixMaterial = AssetDatabase.LoadAssetAtPath<Material>(FixMaterialPath);
        int replaced = 0;
        if (fixMaterial != null)
        {
            foreach (GameObject root in source.GetRootGameObjects())
                ReplaceMissingMaterials(root, fixMaterial, log, ref replaced);
        }

        RemoveGeneratedBackground(source);

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;

        var feature = FindFeature(rendererData);
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<CartoonRendererFeature>();
            feature.name = "Cartoon Projection Renderer";
            rendererData.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            Log(log, "Added Cartoon Projection Renderer feature to the Universal Renderer.");
        }

        ApplyRecommendedSettings(feature.settings);
        feature.settings.enabled = true;
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);

        EnsureTestModel(source, log);

        EditorSceneManager.MarkSceneDirty(source);
        Directory.CreateDirectory(Path.GetDirectoryName(PreviewScenePath) ?? ".");
        EditorSceneManager.SaveScene(source, PreviewScenePath);
        Log(log, $"Saved preview scene: {PreviewScenePath} (renderer slots repaired: {replaced})");
        AssetDatabase.SaveAssets();
    }

    private static void RemoveGeneratedBackground(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var box = root.GetComponentInChildren<BackgroundBox>(true);
            if (box == null)
                continue;
            Transform generated = box.transform.Find("generated_background_box");
            if (generated != null)
                UnityEngine.Object.DestroyImmediate(generated.gameObject);
        }
    }

    private static CartoonRendererFeature FindFeature(UniversalRendererData data)
    {
        if (data.rendererFeatures == null)
            return null;

        foreach (ScriptableRendererFeature feature in data.rendererFeatures)
        {
            if (feature is CartoonRendererFeature cartoon)
                return cartoon;
        }
        return null;
    }

    // Verified parameter set from the Unity 6000.3.25f1 / URP 17.3 test project.
    private static void ApplyRecommendedSettings(CartoonRenderSettings settings)
    {
        settings.enabled = true;
        settings.projectedShapes = true;
        settings.projectionWidth = 640;
        settings.projectionUpdatesPerSecond = 8;
        settings.projectionColorStep = 40;
        settings.projectionMinimumArea = 4;
        settings.projectionContourTolerance = 1.5f;
        settings.projectionShowContours = false;
        settings.backgroundMode = CartoonBackgroundMode.Camera;
    }

    private static void EnsureTestModel(Scene scene, StringBuilder log)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == TestModelName)
                return;
        }

        Texture2D texture = LoadOrCreateTestTexture();
        Shader pixelated = Shader.Find("Custom/Pixelated Model");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (pixelated == null || lit == null)
        {
            Log(log, "Test model skipped: required shaders are unavailable.");
            return;
        }

        var model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        model.name = TestModelName;
        model.transform.position = new Vector3(-4.2f, 0.75f, 0.4f);
        model.transform.localScale = Vector3.one * 1.5f;

        var pixelatedMaterial = new Material(pixelated) { name = "ProjectionTestMaterial" };
        pixelatedMaterial.SetTexture("_MainTex", texture);
        pixelatedMaterial.SetFloat("_TexturePixels", 32f);
        pixelatedMaterial.SetFloat("_ColorSteps", 6f);

        var litMaterial = new Material(lit) { name = "ProjectionTestMaterialLit" };
        litMaterial.SetTexture("_BaseMap", texture);
        litMaterial.SetColor("_BaseColor", Color.white);

        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "cartoon_projection_test_block";
        block.transform.position = new Vector3(4.4f, 0.6f, 0.4f);
        block.transform.localScale = new Vector3(1.6f, 1.2f, 1.6f);
        block.GetComponent<MeshRenderer>().sharedMaterial = litMaterial;

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "cartoon_projection_test_cube";
        cube.transform.position = new Vector3(-4.2f, 1.6f, 0.4f);
        cube.transform.localScale = Vector3.one * 0.9f;
        cube.GetComponent<MeshRenderer>().sharedMaterial = pixelatedMaterial;

        log.AppendLine($"    Test model added at {model.transform.position} (shader {pixelated.name}, texture {texture.name})");
    }

    private static Texture2D LoadOrCreateTestTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TestTexturePath);
        if (existing != null)
            return existing;

        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ProjectionTestTex",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int cellX = x * 4 / size;
                int cellY = y * 4 / size;
                float hue = (cellX + cellY * 4) / 16f;
                Color color = Color.HSVToRGB(hue, 0.65f, 0.95f);
                if (cellX == 3 && cellY == 3)
                    color = new Color(0.15f, 0.15f, 0.18f);
                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(TestTexturePath) ?? ".");
        AssetDatabase.CreateAsset(texture, TestTexturePath);
        AssetDatabase.SaveAssets();
        return texture;
    }

    // --------------------------------------------------------------- capturing

    private static string CaptureComparison(StringBuilder log, bool interactivate)
    {
        var report = new StringBuilder();
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (pipeline == null || rendererData == null)
            throw new InvalidOperationException("URP assets missing; run 'Create Preview Scene' first.");

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;

        var feature = FindFeature(rendererData);
        if (feature == null)
            throw new InvalidOperationException("CartoonRendererFeature not present on the Universal Renderer.");

        CartoonRenderSettings settings = feature.settings;
        ApplyRecommendedSettings(settings);
        EditorUtility.SetDirty(feature);

        EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);

        var camera = Camera.main;
        if (camera == null)
            throw new InvalidOperationException("Preview scene has no MainCamera.");

        Directory.CreateDirectory(CaptureDir);
        var originalCameraPos = camera.transform.position;
        var originalCameraRot = camera.transform.rotation;
        var controller = UnityEngine.Object.FindFirstObjectByType<FezCameraController>();
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controller != null)
            controller.enabled = false;

        // Fixed machine position: the same frame is reused for all three captures.
        camera.transform.position = new Vector3(0f, 3.24f, -10f);
        camera.transform.rotation = Quaternion.identity;
        camera.orthographic = true;
        camera.orthographicSize = 5.6f;

        report.AppendLine("Cartoon projection migration - comparison captures");
        report.AppendLine($"Unity {Application.unityVersion}; pipeline {pipeline.name}; renderer {rendererData.name}");
        report.AppendLine($"Renderer feature: {feature.name}, active={feature.isActive}");
        report.AppendLine($"Camera fixed at {originalCameraPos} -> (0.00, 3.24, -10.00), orthographic size 5.60");
        report.AppendLine();

        try
        {
            settings.projectedShapes = true;
            settings.projectionShowContours = false;
            settings.enabled = false;
            Capture(log, report, camera, "01_effect_off", settings.enabled);

            settings.enabled = true;
            settings.projectionShowContours = false;
            Capture(log, report, camera, "02_effect_on", settings.enabled);


            settings.projectionShowContours = true;
            Capture(log, report, camera, "03_contours_debug", settings.enabled);

            // Camera rotation check: the rebuilt image and the billboard orientation must
            // both follow the camera without the proxies reappearing in frame.
            settings.projectionShowContours = false;
            camera.transform.rotation = Quaternion.Euler(0f, 28f, 0f);
            camera.transform.position = new Vector3(2.5f, 3.24f, -9.6f);
            Capture(log, report, camera, "04_camera_rotated", settings.enabled);
        }
        finally
        {
            if (controller != null)
                controller.enabled = controllerWasEnabled;
            camera.transform.position = originalCameraPos;
            camera.transform.rotation = originalCameraRot;
            EditorUtility.SetDirty(feature);
        }

        Log(log, report.ToString());
        return report.ToString();
    }

    private static void Capture(StringBuilder log, StringBuilder report, Camera camera, string fileName, bool effectEnabled)
    {
        // Blocking readback is only acceptable in an automated run; in the interactive editor
        // the pass keeps its asynchronous path and the frames resolve normally.
        ProjectedShapePass.UseSynchronousReadback = Application.isBatchMode;

        EnsureBackgroundBox(null);
        string before = ProjectedShapePass.LastStatistics;
        RenderAndResolve(camera);

        // Wait for a rebuilt mesh, then confirm it stays stable. Batch mode never resolves
        // the pass's async readback callbacks in this blocking loop, so tooling resolves
        // the capture itself. Returning to EditorApplication.update does dispatch them.
        string previous = before;
        int stableFrames = 0;
        const int maxFrames = 400;
        for (int frame = 0; effectEnabled && frame < maxFrames && stableFrames < 6; frame++)
        {
            Thread.Sleep(30);
            bool resolved = RenderAndResolve(camera);
            string current = ProjectedShapePass.LastStatistics;
            if (resolved && current != null && current == previous && !string.Equals(current, before, StringComparison.Ordinal))
                stableFrames++;
            else if (!resolved)
                stableFrames = 0;
            previous = current;
        }

        string stats = effectEnabled ? ProjectedShapePass.LastStatistics : "Effect disabled (no projection rebuild)";
        string path = Path.Combine(CaptureDir, fileName + ".png");
        try
        {
            SaveCameraTo(camera, path);
            report.AppendLine($"{fileName}.png  {camera.pixelWidth}x{camera.pixelHeight}");
            report.AppendLine($"    statistics: {stats}");
            Log(log, $"Captured {path} :: {stats}");
        }
        catch (Exception e)
        {
            report.AppendLine($"{fileName}.png  FAILED: {e.Message}");
            Log(log, $"Capture failed for {fileName}: {e}");
        }
    }
}
