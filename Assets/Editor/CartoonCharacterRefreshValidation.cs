using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CartoonProjection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Fixed-workload animation comparison; no hardware frame-time or presentation claims.
// Run in an isolated batch editor without -quit or -nographics.
public static class CartoonCharacterRefreshValidation
{
    const string Output = "Assets/CartoonRenderer/Generated/CharacterRefresh/Report.txt";
    static CartoonRendererFeature feature;
    static CartoonCharacterRenderController controller;
    static SpriteRenderer sprite;
    static Camera camera;
    static RenderTexture target;
    static Sprite[] frames;
    static string savedSettings;
    static double started, lastRender;
    static int phase, samples, projectedSamples;
    static int warmStartedBuilds;
    static int[] builds = new int[2];
    static double[] coverage = new double[2];
    static HashSet<Sprite> projectedFrames = new();
    static readonly FieldInfo pumpFrame = typeof(CartoonRendererFeature).GetField(
        "pumpedFrame", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch editor.");
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        File.WriteAllText(Output, "24 fps / 40 sprite frames / 8 seconds per cold-cache phase\n");
        try
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset");
            feature = data.rendererFeatures.OfType<CartoonRendererFeature>().Single();
            savedSettings = JsonUtility.ToJson(feature.settings);
            EditorSceneManager.OpenScene("Assets/Scenes/CartoonChannelAcceptance.unity", OpenSceneMode.Single);
            controller = UnityEngine.Object.FindFirstObjectByType<CartoonCharacterRenderController>();
            sprite = controller.GetComponentInChildren<SpriteRenderer>();
            camera = Camera.main;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Anime/Player/Clips/Move.anim");
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).First(value =>
                value.type == typeof(SpriteRenderer) && value.propertyName == "m_Sprite");
            frames = AnimationUtility.GetObjectReferenceCurve(clip, binding)
                .Select(value => value.value as Sprite).Where(value => value).Distinct().ToArray();
            Require(frames.Length == 40, "Expected the real 40-frame Move animation.");
            target = new RenderTexture(640, 480, 24);
            target.Create();
            camera.targetTexture = target;
            ShaderUtil.allowAsyncCompilation = false;
            ProjectedShapePass.UseSynchronousReadback = false;
            CartoonCharacterSystem.AllowVisualChangesOverride = true;
            phase = 0;
            Begin();
            EditorApplication.update += Tick;
        }
        catch (Exception exception) { Finish(exception); }
    }

    static void Begin()
    {
        feature.ResetChannelState();
        feature.settings.enabled = true;
        feature.settings.sceneChannelEnabled = true;
        feature.settings.character.enabled = true;
        feature.settings.character.minimumRebuildInterval = phase == 0 ? 0.221f : 0f;
        samples = projectedSamples = 0;
        projectedFrames.Clear();
        // Allocate camera targets and compile shaders before measuring either phase.
        camera.Render();
        feature.CharacterChannel.ResetStatistics();
        started = EditorApplication.timeSinceStartup;
        lastRender = started;
        Record("START interval=" + feature.settings.character.minimumRebuildInterval);
    }

    static void Tick()
    {
        try
        {
            double now = EditorApplication.timeSinceStartup;
            // Camera.Render tooling does not advance frameCount. Rendering many times
            // between animation ticks would probe the intentional duplicate-camera guard,
            // not animation refresh. Keep renders at the same 24 Hz as this workload.
            if (now - lastRender < 1.0 / 24.0) return;
            lastRender = now;
            double elapsed = now - started;
            sprite.sprite = frames[(int)(elapsed * 24) % frames.Length];
            // Editor Scene/Game views can render immediately before this callback while
            // batch frameCount remains unchanged. Model a NEW animation frame explicitly;
            // otherwise the once-per-frame camera guard would replay the previous Sprite.
            pumpFrame.SetValue(feature, int.MinValue);
            camera.Render();
            samples++;
            var projection = feature.CharacterChannel.FindProjection(controller);
            if (projection && projection.IsShowingProjected)
            {
                Require(!sprite.enabled, "Original and projected sprites overlap.");
                Require(projection.CurrentShape.sprite == sprite.sprite, "A stale animation frame is displayed.");
                projectedSamples++;
                projectedFrames.Add(sprite.sprite);
            }
            if (elapsed < (phase == 2 ? 4 : 8)) return;
            var stats = feature.CharacterChannel.Stats;
            Require(stats.preparesFailed == 0, "Character preparation failed.");
            if (phase == 2)
            {
                Record($"WARM replay: samples={samples} matchingProjected={projectedSamples} " +
                    $"coverage={projectedSamples / (double)samples:P1} " +
                    $"newBuilds={stats.preparesStarted - warmStartedBuilds}");
                Require(stats.preparesStarted == warmStartedBuilds, "Cached playback started new builds.");
                Require(projectedSamples == samples, "Cached animation fell back to its original sprite.");
                Record("PASS: no stale animation frames or overlapping original; identical style and scene settings.");
                Record("NOTE: coverage counts CPU render checks, not display presentation; this is not a sealed benchmark.");
                Finish(null);
                return;
            }
            builds[phase] = stats.preparesCompleted;
            coverage[phase] = projectedSamples / (double)samples;
            Record($"interval={feature.settings.character.minimumRebuildInterval:F3}s " +
                $"samples={samples} matchingProjected={projectedSamples} coverage={coverage[phase]:P1} " +
                $"distinctProjected={projectedFrames.Count}/40 completed={builds[phase]} " +
                $"cache={stats.cacheEntries} cpuLast={stats.lastBuildMilliseconds:F1}ms");
            if (phase++ == 0) { Begin(); return; }
            Require(builds[1] >= builds[0], "Removing the throttle reduced completed frame builds.");
            warmStartedBuilds = stats.preparesStarted;
            samples = projectedSamples = 0;
            started = lastRender = EditorApplication.timeSinceStartup;
            Record("START warm replay (same cache, no reset)");
        }
        catch (Exception exception) { Finish(exception); }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Record(string message)
    {
        File.AppendAllText(Output, message + "\n");
        Debug.Log("[CharacterRefresh] " + message);
    }

    static void Finish(Exception exception)
    {
        EditorApplication.update -= Tick;
        if (camera) camera.targetTexture = null;
        if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        CartoonCharacterSystem.AllowVisualChangesOverride = false;
        if (feature && savedSettings != null) JsonUtility.FromJsonOverwrite(savedSettings, feature.settings);
        Record(exception == null ? "RESULT: PASS" : "RESULT: FAIL " + exception);
        EditorApplication.Exit(exception == null ? 0 : 1);
    }
}
