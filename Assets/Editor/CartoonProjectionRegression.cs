using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using CartoonProjection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Batch regression entry point. Returns to the editor loop for async GPU callbacks;
// run without -quit, since this method exits explicitly after completion/failure.
public static class CartoonProjectionRegression
{
    const string RendererPath = "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset";
    const string PipelinePath = "Assets/CartoonRenderer/Settings/CartoonUniversalRenderPipeline.asset";
    const string ReportPath = "Assets/CartoonRenderer/Generated/RegressionReport.txt";
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly StringBuilder report = new StringBuilder();
    static readonly Color32 expected = new Color32(128, 64, 32, 255);
    static CartoonRendererFeature feature;
    static Camera camera;
    static RenderTexture cameraTarget;
    static Material probeMaterial;
    static Stopwatch timeout;
    static int frames;
    static bool finished;

    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run this regression in a separate Unity batch process.");
        try
        {
            report.AppendLine($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceType}; {QualitySettings.activeColorSpace}");
            EditorSceneManager.OpenScene("Assets/Scenes/CartoonMigrationPreview.unity", OpenSceneMode.Single);
            CheckLoadedScenes();
            CheckCaptureFormats();
            CreateColorProbe();

            ProjectedShapePass.UseSynchronousReadback = true;
            camera.Render();
            Require(ProjectedShapePass.ResolveSynchronousCapture(), "Synchronous capture must resolve.");
            int width = ProjectedShapePass.LastCaptureWidth;
            int height = ProjectedShapePass.LastCaptureHeight;
            Color32 captured = ProjectedShapePass.LastCapturePixels[(height / 2) * width + width / 2];
            CheckColor(captured, "Synchronous albedo capture");

            object targets = GetTargets(128, 128);
            var ids = (RenderTexture)targets.GetType().GetField("ids").GetValue(targets);
            Color32 identity = ReadCenter(ids);
            Require(identity.r == 1 && identity.g == 0 && identity.b == 0 && identity.a == 255,
                $"Identity bytes must be exact, got {identity}.");
            Record("PASS: identity bytes (1, 0, 0, 255), no sRGB conversion");

            camera.Render();
            CheckColor(ReadCenter(cameraTarget), "Synchronous polygon redraw");

            // Dispose the completed synchronous mesh, so a stale result cannot pass
            // the async test. Only the normal callbacks can populate the new state.
            feature.Create();
            ProjectedShapePass.UseSynchronousReadback = false;
            timeout = Stopwatch.StartNew();
            EditorApplication.update += PollAsync;
        }
        catch (Exception e) { Finish(e); }
    }

    static void CheckLoadedScenes()
    {
        Scene first = default, second = default, empty = default;
        var temporaryPaths = new List<string>();
        Scene previousActive = SceneManager.GetActiveScene();
        try
        {
            first = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SaveTemporaryScene(first, "First", temporaryPaths);
            var firstObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(firstObject, first);
            var generated = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(generated, first);
            generated.transform.SetParent(firstObject.transform);
            generated.hideFlags = HideFlags.DontSave;

            second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SaveTemporaryScene(second, "Second", temporaryPaths);
            var secondObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(secondObject, second);
            empty = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var renderers = new HashSet<Renderer>(CollectRenderers());
            Require(renderers.Contains(firstObject.GetComponent<Renderer>()), "First additive scene was omitted.");
            Require(renderers.Contains(secondObject.GetComponent<Renderer>()), "Second additive scene was omitted.");
            Require(renderers.Contains(generated.GetComponent<Renderer>()), "DontSave child was omitted.");
            Record("PASS: both additive scenes and DontSave child collected, with an empty last scene");

            var unloaded = secondObject.GetComponent<Renderer>();
            EditorSceneManager.CloseScene(second, true);
            second = default;
            Require(!new HashSet<Renderer>(CollectRenderers()).Contains(unloaded), "Unloaded scene retained a renderer.");
            Record("PASS: unloaded scene removed from collection");
        }
        finally
        {
            if (empty.IsValid()) EditorSceneManager.CloseScene(empty, true);
            if (second.IsValid()) EditorSceneManager.CloseScene(second, true);
            if (first.IsValid()) EditorSceneManager.CloseScene(first, true);
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            foreach (string path in temporaryPaths) AssetDatabase.DeleteAsset(path);
        }
    }

    static void SaveTemporaryScene(Scene scene, string name, List<string> paths)
    {
        // Unity disallows another additive NewScene while an untitled scene is open.
        // Save only our temporary test scenes, then remove their assets in finally.
        string path = AssetDatabase.GenerateUniqueAssetPath($"Assets/CartoonRenderer/Generated/Regression{name}.unity");
        Require(EditorSceneManager.SaveScene(scene, path), "Cannot save temporary regression scene.");
        paths.Add(path);
    }

    static Renderer[] CollectRenderers() => (Renderer[])typeof(ProjectedShapePass)
        .GetMethod("CollectRenderers", PrivateStatic).Invoke(null, null);

    static object GetTargets(int width, int height) => typeof(ProjectedShapePass)
        .GetMethod("EnsureSynchronousTargets", PrivateStatic).Invoke(null, new object[] { width, height });

    static void CheckCaptureFormats()
    {
        object targets = GetTargets(16, 16);
        foreach (string name in new[] { "color", "ids" })
        {
            var texture = (RenderTexture)targets.GetType().GetField(name).GetValue(targets);
            Require(texture.graphicsFormat == GraphicsFormat.R8G8B8A8_UNorm && !texture.sRGB,
                $"{name} must be explicit UNorm without sRGB conversion.");
            Record($"PASS: {name} format={texture.graphicsFormat}, sRGB={texture.sRGB}");
        }
    }

    static void CreateColorProbe()
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        Require(pipeline && renderer, "Migration URP assets missing.");
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        foreach (var candidate in renderer.rendererFeatures)
            if (candidate is CartoonRendererFeature cartoon) feature = cartoon;
        Require(feature, "Migration feature missing.");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Regression camera");
        camera = go.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -2);
        camera.orthographic = true;
        camera.orthographicSize = 1;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        cameraTarget = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cameraTarget.Create();
        camera.targetTexture = cameraTarget;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        probeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        probeMaterial.SetColor("_BaseColor", new Color(0.5f, 0.25f, 0.125f, 1));
        quad.GetComponent<Renderer>().sharedMaterial = probeMaterial;
        feature.settings.enabled = true;
        feature.settings.projectedShapes = true;
        feature.settings.projectionWidth = 128;
        feature.settings.projectionUpdatesPerSecond = 30;
        feature.settings.projectionShowContours = false;
        feature.Create();
    }

    static void PollAsync()
    {
        try
        {
            if (timeout.Elapsed.TotalSeconds > 30)
                throw new TimeoutException("Normal async readback did not produce a polygon mesh within 30 seconds.");
            frames++;
            camera.Render();
            var pass = typeof(CartoonRendererFeature).GetField("projectedPass", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(feature);
            var states = (System.Collections.IDictionary)typeof(ProjectedShapePass)
                .GetField("states", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pass);
            object state = states[camera.GetInstanceID()];
            if (state == null) return;
            var mesh = (Mesh)state.GetType().GetField("mesh").GetValue(state);
            if (!mesh || mesh.vertexCount == 0) return;

            CheckColor(ReadCenter(cameraTarget), "Normal asynchronous polygon redraw");
            Record($"PASS: async callbacks and worker completed after {frames} editor updates; {ProjectedShapePass.LastStatistics}");
            EditorApplication.update -= PollAsync;
            CleanupProbe();

            // Regenerate the same four migration fixtures only after the targeted
            // regressions pass. Keep the interactive async check separate from them.
            CartoonMigrationTools.RunAll();
            Require(ProjectedShapePass.Regions > 0, "Migration fixture produced no regions.");
            string migrationReport = File.ReadAllText("Assets/CartoonRenderer/Generated/MigrationReport.txt");
            Require(!migrationReport.Contains("FATAL:") && !migrationReport.Contains("FAILED:"),
                "Migration fixture generation reported a failure.");
            Record("PASS: four migration comparison fixtures regenerated");
            Finish(null);
        }
        catch (Exception e) { Finish(e); }
    }

    static Color32 ReadCenter(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active;
        var copy = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(target.width / 2, target.height / 2, 1, 1), 0, 0, false);
            copy.Apply(false, false);
            return copy.GetPixels32()[0];
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    static void CheckColor(Color32 actual, string label)
    {
        Require(Math.Abs(actual.r - expected.r) <= 2 && Math.Abs(actual.g - expected.g) <= 2 && Math.Abs(actual.b - expected.b) <= 2,
            $"{label}: expected {expected}, got {actual}.");
        Record($"PASS: {label} RGB=({actual.r}, {actual.g}, {actual.b}), expected (128, 64, 32), tolerance 2");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Record(string message)
    {
        report.AppendLine(message);
        UnityEngine.Debug.Log("[CartoonRegression] " + message);
    }

    static void CleanupProbe()
    {
        if (camera) camera.targetTexture = null;
        if (cameraTarget) UnityEngine.Object.DestroyImmediate(cameraTarget);
        if (probeMaterial) UnityEngine.Object.DestroyImmediate(probeMaterial);
        cameraTarget = null;
        probeMaterial = null;
    }

    static void Finish(Exception error)
    {
        if (finished) return;
        finished = true;
        EditorApplication.update -= PollAsync;
        ProjectedShapePass.UseSynchronousReadback = false;
        CleanupProbe();
        if (error != null)
        {
            report.AppendLine("FAIL: " + error);
            UnityEngine.Debug.LogException(error);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
