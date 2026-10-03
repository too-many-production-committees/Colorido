using System;
using System.IO;
using System.Linq;
using CartoonProjection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Run only in an isolated batch editor, without -quit or -nographics.
// Unlike Camera.Render-only acceptance, this enters actual Play mode with domain reload,
// the real player Animator, shadow-casting scene props and the normal asynchronous path.
[InitializeOnLoad]
public static class CartoonCharacterPlayModeValidation
{
    const string Running = "CartoonCharacterPlayModeValidation.Running";
    const string Cycle = "CartoonCharacterPlayModeValidation.Cycle";
    const string Output = "Assets/CartoonRenderer/Generated/PlayModeValidation/Report.txt";
    static CartoonRendererFeature feature;
    static Camera camera;
    static SpriteRenderer sprite;
    static RenderTexture target;
    static int phase, phaseFrames, renderedFrames, observedProjected;
    static double phaseStarted;
    static bool finishing;

    static CartoonCharacterPlayModeValidation()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        if (SessionState.GetBool(Running, false))
            EditorApplication.delayCall += Resume;
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Use an isolated batch editor, not the user's editor.");
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        File.WriteAllText(Output, "Real Play mode / shadow / character regression\n");
        SessionState.SetBool(Running, true);
        SessionState.SetInt(Cycle, 0);
        Enter();
    }

    static void Enter()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/CartoonMigrationPreview.unity", OpenSceneMode.Single);
            // Also supports a preview scene which has not yet been marked by its user.
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerBillboardVisual>();
            Require(player != null && player.IsAnimatedModeActive, "Animated player missing.");
            if (!player.GetComponent<CartoonCharacterRenderController>())
                player.gameObject.AddComponent<CartoonCharacterRenderController>();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset");
            feature = renderer.rendererFeatures.OfType<CartoonRendererFeature>().Single();
            feature.settings.enabled = true;
            feature.settings.projectedShapes = true;
            feature.settings.sceneChannelEnabled = true;
            feature.settings.character.enabled = true;
            Record("ENTER cycle=" + SessionState.GetInt(Cycle, 0));
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception) { Fail(exception); }
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Running, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall += Resume;
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (finishing) return;
            int cycle = SessionState.GetInt(Cycle, 0) + 1;
            SessionState.SetInt(Cycle, cycle);
            if (cycle < 3) EditorApplication.delayCall += Enter;
            else Finish();
        }
    }

    static void Resume()
    {
        if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || camera) return;
        try
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset");
            feature = renderer.rendererFeatures.OfType<CartoonRendererFeature>().Single();
            feature.settings.enabled = true;
            feature.settings.sceneChannelEnabled = true;
            feature.settings.character.enabled = true;
            ProjectedShapePass.UseSynchronousReadback = false;
            CartoonCharacterSystem.AllowVisualChangesOverride = false;
            ShaderUtil.allowAsyncCompilation = false;
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerBillboardVisual>();
            player.EnsureVisual();
            sprite = player.AnimatedSpriteRenderer;
            camera = Camera.main;
            Require(camera && sprite, "Camera or animated SpriteRenderer missing.");
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            Require(lights.Any(light => light.enabled && light.shadows != LightShadows.None),
                "No shadow-casting light: cannot exercise the crash path.");
            target = new RenderTexture(640, 480, 24) { name = "Play-mode regression capture" };
            target.Create();
            camera.targetTexture = target;
            phase = phaseFrames = renderedFrames = observedProjected = 0;
            phaseStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Record("PLAY cycle=" + SessionState.GetInt(Cycle, 0) + " shadows=" + lights.Length);
        }
        catch (Exception exception) { Fail(exception); }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || !camera) return;
        try
        {
            camera.Render();
            renderedFrames++;
            phaseFrames++;
            bool projected = feature.CharacterChannel.ProjectedCharacterCount > 0;
            if (projected) observedProjected++;
            Require(!projected || !sprite.enabled, "Original sprite and projection overlap.");
            Require(EditorApplication.timeSinceStartup - phaseStarted < 45,
                "Timed out waiting for phase " + phase);
            if (phaseFrames < 20) return;
            switch (phase)
            {
                case 0:
                    if (!projected) return;
                    Record("PASS: both channels on; projection visible; original hidden.");
                    feature.settings.enabled = false;
                    Next();
                    break;
                case 1:
                    Require(!projected && sprite.enabled, "Master off did not restore the sprite.");
                    Record("PASS: Master off restores original sprite.");
                    feature.settings.enabled = true;
                    feature.settings.character.enabled = false;
                    Next();
                    break;
                case 2:
                    Require(!projected && sprite.enabled, "Character off did not restore the sprite.");
                    Record("PASS: Scene only, character original.");
                    feature.settings.character.enabled = true;
                    feature.settings.sceneChannelEnabled = false;
                    Next();
                    break;
                case 3:
                    if (!projected) return;
                    Record("PASS: Character only, scene off, original hidden.");
                    feature.settings.sceneChannelEnabled = true;
                    feature.settings.character.showContours = true;
                    feature.settings.character.colorStep = 24;
                    Next();
                    break;
                case 4:
                    if (!projected) return;
                    var driver = feature.CharacterChannel.FindProjection(
                        sprite.GetComponentInParent<CartoonCharacterRenderController>());
                    var contour = driver.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(filter =>
                        filter.gameObject.name == "cartoon_character_projected_contours");
                    Require(contour && contour.sharedMesh &&
                        contour.sharedMesh.vertexCount == driver.CurrentShape.contourVertices.Length,
                        "Enabling contours did not upload the current cached frame's contour mesh.");
                    Record("PASS: shape/contour change under shadows; frames=" + renderedFrames +
                        " projectedFrames=" + observedProjected + " " + feature.CharacterChannel.Stats);
                    EditorApplication.update -= Tick;
                    camera.targetTexture = null;
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                    target = null;
                    camera = null;
                    EditorApplication.ExitPlaymode();
                    break;
            }
        }
        catch (Exception exception) { Fail(exception); }
    }

    static void Next()
    {
        phase++;
        phaseFrames = 0;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Record(string message)
    {
        File.AppendAllText(Output, message + "\n");
        Debug.Log("[CharacterPlayModeValidation] " + message);
    }

    static void Fail(Exception exception)
    {
        finishing = true;
        SessionState.SetBool(Running, false);
        EditorApplication.update -= Tick;
        Record("RESULT: FAIL " + exception);
        EditorApplication.Exit(1);
    }

    static void Finish()
    {
        finishing = true;
        SessionState.SetBool(Running, false);
        Record("RESULT: PASS (3 real Play/Stop cycles; async sampling; shadows; Master and channel switches)");
        EditorApplication.Exit(0);
    }
}
