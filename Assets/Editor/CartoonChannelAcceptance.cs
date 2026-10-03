using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CartoonProjection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Independent acceptance stage for the scene/character channel split.
///
/// Builds its own scene (never SampleScene) and drives the renderer feature exactly the way a
/// user would: through the renderer asset's settings and the per-character controller. Every
/// check writes its numbers into the report instead of only asserting, so a regression is
/// visible even when the pass/fail gate still succeeds.
/// </summary>
public static class CartoonChannelAcceptance
{
    const string ScenePath = "Assets/Scenes/CartoonChannelAcceptance.unity";
    const string Output = "Assets/CartoonRenderer/Generated/ChannelAcceptance";
    const string MaterialRoot = "Assets/CartoonRenderer/ChannelAcceptance/Materials";
    const string TextureRoot = "Assets/CartoonRenderer/ChannelAcceptance/Textures";
    const string RendererPath = "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset";
    const string PipelinePath = "Assets/CartoonRenderer/Settings/CartoonUniversalRenderPipeline.asset";

    static readonly StringBuilder report = new StringBuilder();
    static readonly List<string> failures = new List<string>();

    // ------------------------------------------------------------------ entries

    [MenuItem("Cartoon Migration/5. Build Channel Acceptance Scene")]
    public static void BuildMenu()
    {
        Build();
        Debug.Log("[ChannelAcceptance] Built " + ScenePath);
    }

    [MenuItem("Cartoon Migration/6. Run Channel Acceptance")]
    public static void RunMenu()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Scene swaps require a batch editor.");
        Run();
    }

    // Batch entry point. No -quit: the harness exits itself when finished.
    public static void BuildAndRun()
    {
        try
        {
            Build();
            Run();
        }
        catch (Exception exception)
        {
            Fail("unhandled: " + exception);
            Flush(1);
        }
    }

    // -------------------------------------------------------------------- scene

    public static void Build()
    {
        Directory.CreateDirectory(MaterialRoot);
        Directory.CreateDirectory(TextureRoot);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "CartoonChannelAcceptance";

        var stage = new GameObject("Cartoon Channel Stage").transform;

        var floor = MakeTexture("FloorTiles", new Color(0.62f, 0.44f, 0.30f), new Color(0.44f, 0.30f, 0.20f));
        var wall = MakeTexture("WallPanels", new Color(0.30f, 0.36f, 0.52f), new Color(0.22f, 0.27f, 0.40f));
        var wood = MakeTexture("PropWood", new Color(0.48f, 0.33f, 0.19f), new Color(0.34f, 0.22f, 0.12f));

        var floorMaterial = MakeMaterial("ChannelFloor", floor);
        var wallMaterial = MakeMaterial("ChannelWall", wall);
        var propMaterial = MakeMaterial("ChannelProp", wood);

        Block("floor", stage, new Vector3(0f, -0.25f, 0f), new Vector3(14f, 0.5f, 14f), floorMaterial);
        Block("back wall", stage, new Vector3(0f, 2.5f, 3.4f), new Vector3(14f, 6f, 0.4f), wallMaterial);
        Block("side wall", stage, new Vector3(-3.6f, 2.0f, 0f), new Vector3(0.4f, 5f, 7f), wallMaterial);
        Block("prop table", stage, new Vector3(0f, 0.175f, 1.0f), new Vector3(2.6f, 0.35f, 0.6f), propMaterial);

        // Character: a SpriteRenderer whose sprite is swapped by the harness, which is exactly
        // the state an Animator drives. The controller marks the whole subtree as the character
        // channel without touching gameplay components.
        var character = new GameObject("character");
        character.transform.SetParent(stage, false);
        character.transform.position = new Vector3(0f, 0f, 0.2f);
        character.AddComponent<CartoonCharacterRenderController>();

        var spriteNode = new GameObject("character sprite");
        spriteNode.transform.SetParent(character.transform, false);
        var spriteRenderer = spriteNode.AddComponent<SpriteRenderer>();
        var firstFrame = LoadFrames().First();
        spriteRenderer.sprite = firstFrame;
        // Size the character from the sprite's own bounds. The frame is a large transparent
        // canvas whose art sits on the pivot, so the frame is sized for legibility and the pivot
        // is left at the root: that is the foot baseline, exactly as the original sprite uses it.
        var bounds = firstFrame.bounds;
        const float worldHeight = 4.0f;
        float scale = worldHeight / Mathf.Max(0.001f, bounds.size.y);
        spriteNode.transform.localScale = Vector3.one * scale;
        spriteNode.transform.localPosition = Vector3.zero;

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 2.2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.10f, 0.12f, 0.16f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 60f;
        cameraObject.transform.position = new Vector3(0f, 1.1f, -6f);
        cameraObject.transform.rotation = Quaternion.identity;
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[ChannelAcceptance] Saved " + ScenePath);
    }

    static Texture2D MakeTexture(string name, Color a, Color b)
    {
        string path = TextureRoot + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing) return existing;

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool checker = ((x / 8) + (y / 8)) % 2 == 0;
                pixels[y * size + x] = checker ? (Color32)a : (Color32)b;
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    static Material MakeMaterial(string name, Texture2D texture)
    {
        string path = MaterialRoot + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("URP Lit shader missing.");
        var material = new Material(shader) { name = name };
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static GameObject Block(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        // The stage is render-only; colliders would only add physics noise.
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// <summary>
    /// Uses the sprites the project's own Move clip actually plays, so the frame set is the real
    /// animation rather than an arbitrary folder listing.
    /// </summary>
    static Sprite[] LoadFrames()
    {
        const string clipPath = "Assets/Anime/Player/Clips/Move.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip)
        {
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.type != typeof(SpriteRenderer) || binding.propertyName != "m_Sprite")
                    continue;
                var sprites = AnimationUtility.GetObjectReferenceCurve(clip, binding)
                    .Select(key => key.value as Sprite)
                    .Where(sprite => sprite)
                    .Distinct()
                    .ToArray();
                if (sprites.Length > 0)
                    return sprites;
            }
        }

        // Fallback: any imported sprite in the frame folders.
        var guids = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/Anime/Player/Frames" });
        var found = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
            .Where(sprite => sprite)
            .OrderBy(sprite => sprite.name)
            .ToArray();
        if (found.Length == 0)
            throw new InvalidOperationException("No animation sprites found for the acceptance stage.");
        return found;
    }

    // ------------------------------------------------------------------- runner

    static CartoonRendererFeature feature;
    static CartoonCharacterSystem characters;
    static Camera camera;
    static CartoonCharacterRenderController controller;
    static SpriteRenderer characterSprite;
    static GameObject prop;
    static Sprite[] frames;
    static RenderTexture target;

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (!rendererData) throw new InvalidOperationException("Renderer asset missing.");
            feature = rendererData.rendererFeatures.OfType<CartoonRendererFeature>().Single();
            if (feature == null) throw new InvalidOperationException("CartoonRendererFeature missing.");
            // Create() preserves channel state by design, so calling it here is safe and makes
            // sure both channels exist before anything is measured.
            feature.Create();
            characters = feature.CharacterChannel;
            if (characters == null) throw new InvalidOperationException("Character channel missing.");

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            camera = Camera.main;
            if (!camera) throw new InvalidOperationException("Stage camera missing.");
            controller = UnityEngine.Object.FindFirstObjectByType<CartoonCharacterRenderController>();
            if (!controller) throw new InvalidOperationException("Character controller missing.");
            characterSprite = controller.GetComponentInChildren<SpriteRenderer>();
            prop = GameObject.Find("prop table");
            frames = LoadFrames();

            // Batch tooling renders without a player loop, so both channels are told to resolve
            // synchronously and the character swap is explicitly allowed outside play mode.
            ProjectedShapePass.UseSynchronousReadback = true;
            CartoonCharacterSystem.AllowVisualChangesOverride = true;

            target = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();

            // Initializing a new URP pipeline can dispose and recreate the feature channel.
            // Keep the live channel reference after that first render.
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            characters = feature.CharacterChannel;

            Record("Cartoon projection channel acceptance");
            Record($"Unity {Application.unityVersion}; frames={frames.Length}; sprite={characterSprite.sprite.name}");
            Record($"sprite bounds center={characterSprite.sprite.bounds.center} size={characterSprite.sprite.bounds.size} " +
                   $"pixelsPerUnit={characterSprite.sprite.pixelsPerUnit} rect={characterSprite.sprite.textureRect} " +
                   $"readable={characterSprite.sprite.texture.isReadable} worldScale={characterSprite.transform.lossyScale}");
            Record($"sprite renderer world size={characterSprite.bounds.size} camera ortho={camera.orthographicSize} " +
                   $"pixelsPerUnitOnScreen={Screen.height / (camera.orthographicSize * 2f):F1}");
            // Where the visible art sits in world space, so prop placement can be checked.
            var artBounds = characterSprite.sprite.textureRect;
            Record($"art rect in frame={artBounds} pivot={characterSprite.sprite.pivot}");
            Record($"prop world top={prop.transform.position.y + prop.transform.lossyScale.y * 0.5f:F3}");
            Record($"registry controllers={CartoonRenderRegistry.Controllers.Count} " +
                   $"component={controller.GetInstanceID()}");
            var sweep = UnityEngine.Object.FindObjectsByType<CartoonCharacterRenderController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Record($"registrations={CartoonRenderRegistry.RegistrationCount} controller sweep found {sweep.Length}");
            foreach (var found in sweep)
                Record($"  sweep: '{found.name}' active={found.gameObject.activeInHierarchy} " +
                       $"enabled={found.enabled} componentHideFlags={found.hideFlags} " +
                       $"objectHideFlags={found.gameObject.hideFlags} mode={found.mode}");

            RunSwitches();
            RunParameterIsolation();
            RunAnimationUnderLoad();
            RunFrameCacheReplay();
            RunShapeQuality();
            RunOcclusion();
            RunCameraMoves();
            RunFlipAndEdges();
            RunChurnAndLifetime();
            RunChannelCounters();
        }
        catch (Exception exception)
        {
            Fail("unhandled: " + exception);
        }
        finally
        {
            Cleanup();
            Flush(failures.Count == 0 ? 0 : 1);
        }
    }

    // ------------------------------------------------------------ 1. switches

    static void RunSwitches()
    {
        Section("1. Four switch combinations");
        characters.ResetStatistics();
        var images = new Dictionary<string, Color32[]>();

        foreach (var (scene, character, name) in new[]
                 {
                     (false, false, "10_both_off"),
                     (true, false, "11_scene_only"),
                     (false, true, "12_character_only"),
                     (true, true, "13_both_on")
                 })
        {
            feature.settings.sceneChannelEnabled = scene;
            feature.settings.character.enabled = character;
            // The rebuild runs on a worker thread; give it frames before judging the combination.
            Pump(25);
            long bytes = RenderAndSave(name);
            images[name] = ReadPixels();

            bool projected = characters.Stats.projectedCharacters > 0;
            bool originalVisible = characterSprite.enabled;
            Record($"  {name}: scene={(scene ? "on" : "off")} character={(character ? "on" : "off")} " +
                   $"characterProjected={projected} originalSpriteEnabled={originalVisible} " +
                   $"sceneRegions={ProjectedShapePass.Regions} png={bytes}B");

            if (character && !projected) Fail($"{name}: character channel on but nothing projected");
            if (!character && projected) Fail($"{name}: character channel off but a character is still projected");
            if (character && originalVisible) Fail($"{name}: original sprite and rebuilt shape would overlap");
            if (!character && !characterSprite.enabled) Fail($"{name}: character channel off but the original sprite is hidden");
        }

        // Each switch must actually change pixels, otherwise the channel is only a flag.
        int sceneEffect = Difference(images["10_both_off"], images["11_scene_only"]);
        int characterEffect = Difference(images["10_both_off"], images["12_character_only"]);
        int characterWithScene = Difference(images["11_scene_only"], images["13_both_on"]);
        int sceneWithCharacter = Difference(images["12_character_only"], images["13_both_on"]);
        Record($"  changed pixels - scene switch: {sceneEffect}, character switch: {characterEffect}, " +
               $"scene switch while character on: {sceneWithCharacter}, character switch while scene on: {characterWithScene}");
        Require(sceneEffect > 500, "the scene channel barely changed the image");
        Require(characterEffect > 150, "the character channel barely changed the image");
        Require(characterWithScene > 150, "the character channel did nothing while the scene was on");
        Require(sceneWithCharacter > 500, "the scene channel did nothing while the character was on");

        feature.settings.sceneChannelEnabled = true;
        feature.settings.character.enabled = true;
        Pump(12);
        Record("  PASS: all four combinations render, each switch changes pixels, and the two channels never overlap");
    }

    // --------------------------------------------------- 2. parameter isolation

    static void RunParameterIsolation()
    {
        Section("2. Scene and character parameters are independent");

        feature.settings.sceneChannelEnabled = true;
        feature.settings.character.enabled = true;
        Pump(20);

        int sceneRegions = ProjectedShapePass.Regions;
        int sceneEdges = ProjectedShapePass.SimplifiedEdges;
        int charPreparations = characters.Stats.preparesStarted;
        int cacheEntries = characters.Cache.Count;

        // Character style change must not disturb the scene channel.
        int previousStep = feature.settings.character.colorStep;
        feature.settings.character.colorStep = previousStep == 40 ? 24 : 40;
        Pump(30);
        Record($"  character colorStep {previousStep} -> {feature.settings.character.colorStep}: " +
               $"sceneRegions={ProjectedShapePass.Regions} (was {sceneRegions}), " +
               $"sceneEdges={ProjectedShapePass.SimplifiedEdges} (was {sceneEdges}), " +
               $"characterPreparations={characters.Stats.preparesStarted - charPreparations}, " +
               $"cache={characters.Cache.Count} (was {cacheEntries})");
        Require(ProjectedShapePass.Regions == sceneRegions,
            "changing a character parameter rebuilt the scene channel");
        Require(characters.Stats.preparesStarted > charPreparations,
            "changing a character shape parameter did not invalidate the character cache");

        // Scene style change must not disturb the character channel.
        int charPreparations2 = characters.Stats.preparesStarted;
        int charRegions = characters.Stats.regions;
        int sceneEdgesBefore = ProjectedShapePass.SimplifiedEdges;
        int previousTolerance = Mathf.RoundToInt(feature.settings.projectionContourTolerance * 10f);
        feature.settings.projectionContourTolerance = previousTolerance == 15 ? 2.5f : 1.5f;
        Pump(30);
        Record($"  scene contourTolerance -> {feature.settings.projectionContourTolerance:F1}: " +
               $"characterPreparations={characters.Stats.preparesStarted - charPreparations2}, " +
               $"characterRegions={characters.Stats.regions} (was {charRegions}), " +
               $"sceneEdges={ProjectedShapePass.SimplifiedEdges} (was {sceneEdgesBefore})");
        Require(characters.Stats.preparesStarted == charPreparations2,
            "changing a scene parameter invalidated the character cache");
        Require(ProjectedShapePass.SimplifiedEdges != sceneEdgesBefore,
            "changing a scene parameter did not rebuild the scene channel");

        // Character palette-only changes must reuse cached geometry.
        int charPreparations3 = characters.Stats.preparesStarted;
        feature.settings.character.paletteSaturation = 1.25f;
        Pump(20);
        Record($"  palette-only change: characterPreparations={characters.Stats.preparesStarted - charPreparations3} (expected 0)");
        Require(characters.Stats.preparesStarted == charPreparations3,
            "a colour-only change forced a geometry rebuild");
        feature.settings.character.paletteSaturation = 1f;

        // Turning the character channel off must not clear or rebuild the scene channel.
        int sceneRegions2 = ProjectedShapePass.Regions;
        feature.settings.character.enabled = false;
        Pump(20);
        Record($"  character channel off: sceneRegions={ProjectedShapePass.Regions}, " +
               $"characterCache={characters.Cache.Count}");
        Require(ProjectedShapePass.Regions == sceneRegions2, "disabling the character channel rebuilt the scene");
        Require(characters.Cache.Count > 0, "disabling the character channel cleared the character cache");
        feature.settings.character.enabled = true;
        Pump(12);
        Record("  PASS: both channels' parameters, caches and results are independent");
    }

    // ------------------------------------------------ 3. animation under load

    static void RunAnimationUnderLoad()
    {
        Section("3. Character animation continues while the scene is busy");
        feature.settings.character.enabled = true;
        feature.settings.sceneChannelEnabled = true;
        Pump(20);

        int sceneCapturesBefore = ProjectedShapePass.Regions;
        var seen = new HashSet<string>();

        // Keep the scene rebuilding by moving the camera while stepping sprite frames.
        for (int step = 0; step < 8; step++)
        {
            camera.transform.position = new Vector3(Mathf.Sin(step * 0.7f) * 1.2f, 1.1f, -6f);
            characterSprite.sprite = frames[step % frames.Length];
            Pump(10);
            var shape = characters.FindShape(controller);
            if (shape != null && characterSprite.enabled == false)
                seen.Add(shape.sprite.name);
        }

        Record($"  while the scene was rebuilding, the character showed {seen.Count} distinct frame(s): " +
               $"{string.Join(", ", seen.OrderBy(x => x))}");
        Require(seen.Count >= 2, "character animation did not advance while the scene channel was busy");
        Record($"  scene regions after the churn: {ProjectedShapePass.Regions} (before {sceneCapturesBefore})");
    }

    // ------------------------------------------------------- 4. frame caching

    static void RunFrameCacheReplay()
    {
        Section("4. Replaying cached frames does not rebuild");
        feature.settings.character.enabled = true;
        Pump(20);

        // Warm every frame first.
        foreach (var frame in frames)
        {
            characterSprite.sprite = frame;
            Pump(10);
        }
        int warmPreparations = characters.Stats.preparesStarted;
        int warmHits = (int)characters.Cache.Hits;

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var frame in frames)
            {
                characterSprite.sprite = frame;
                Pump(4);
            }
        }

        int newPreparations = characters.Stats.preparesStarted - warmPreparations;
        int newHits = (int)characters.Cache.Hits - warmHits;
        Record($"  replaying {frames.Length} frames twice: newPreparations={newPreparations} " +
               $"cacheHits={newHits} entries={characters.Cache.Count} pixels={characters.Cache.Pixels} " +
               $"hitRate={characters.Cache.HitRate:P1}");
        Require(newPreparations == 0, "a replayed animation frame was rebuilt instead of cached");
        Require(newHits >= frames.Length, "replayed frames did not register as cache hits");
        Require(characters.Cache.Count <= feature.settings.character.cacheCapacity,
            "sprite cache exceeded its capacity budget");
        Require(characters.Cache.Pixels <= feature.settings.character.cachePixelBudget,
            "sprite cache exceeded its pixel budget");
        Record("  PASS: cached frames replay without rebuilding and stay inside both budgets");
    }

    // ----------------------------------------------------------- 5. shape quality

    static void RunShapeQuality()
    {
        Section("5. ProjectedShapes produces real simplified polygons with original colours");
        feature.settings.character.enabled = true;
        characterSprite.sprite = frames[0];
        Pump(20);

        var projection = FindProjection();
        var shape = projection?.CurrentShape;
        Require(shape != null, "no character shape was produced");
        if (shape == null) return;

        var distinct = new HashSet<int>();
        foreach (var color in shape.palette)
            distinct.Add((color.r << 16) | (color.g << 8) | color.b);
        int nearWhite = shape.palette.Count(c => c.r > 245 && c.g > 245 && c.b > 245);
        float nearWhiteShare = shape.palette.Length == 0 ? 1f : nearWhite / (float)shape.palette.Length;

        Record($"  sample={shape.sampleWidth}x{shape.sampleHeight} regions={shape.regions} " +
               $"edges={shape.sourceEdges}->{shape.simplifiedEdges} triangles={shape.TriangleCount} " +
               $"vertices={shape.VertexCount} buildMs={shape.buildMilliseconds:F1} " +
               $"palette={shape.palette.Length} distinct={distinct.Count} nearWhite={nearWhiteShare:P1}");
        Record($"  cache: entries={characters.Cache.Count} pixels={characters.Cache.Pixels} " +
               $"hits={characters.Cache.Hits} misses={characters.Cache.Misses} " +
               $"hitRate={characters.Cache.HitRate:P1} evictions={characters.Cache.Evictions}");

        Require(shape.regions > 1, "the character shape has no colour regions");
        Require(shape.TriangleCount > 0, "the character shape has no polygons");
        Require(shape.simplifiedEdges < shape.sourceEdges,
            "contours were not simplified (expected fewer edges after simplification)");
        Require(distinct.Count > 1, "the character shape collapsed into a single flat colour");
        // A sprite may contain genuine white pixels; a white model would be almost entirely white.
        Require(nearWhiteShare < 0.9f, "the character shape collapsed into a white model");
        Require(shape.sampleWidth <= feature.settings.character.maximumSampleSize,
            "the sprite was upscaled beyond its own resolution");

        // Original mode must put the untouched sprite back.
        controller.mode = CartoonCharacterRenderController.Mode.Original;
        Pump(10);
        bool stillProjected = projection != null && projection.IsShowingProjected;
        Record($"  Original mode: originalSpriteEnabled={characterSprite.enabled} projected={stillProjected}");
        Require(characterSprite.enabled, "Original mode did not restore the original sprite");
        Require(!stillProjected, "Original mode still shows projected geometry");

        controller.mode = CartoonCharacterRenderController.Mode.ProjectedShapes;
        Pump(10);
        Record("  PASS: real simplified polygons, original palette, and Original mode restores the sprite");
    }

    // -------------------------------------------------------------- 6. occlusion

    static void RunOcclusion()
    {
        Section("6. Occlusion in front of and behind scene props");
        feature.settings.sceneChannelEnabled = true;
        feature.settings.character.enabled = true;
        characterSprite.sprite = frames[0];

        var characterRoot = controller.transform;
        characterRoot.position = new Vector3(0f, 0f, 0.2f);
        Pump(25);
        long frontPixels = RenderAndSave("20_character_front");

        // Behind the prop: the table must cover the legs, exactly like the original sprite.
        characterRoot.position = new Vector3(0f, 0f, 1.8f);
        Pump(25);
        long behindPixels = RenderAndSave("21_character_behind");

        // Same two positions with the character channel off, to compare against the original art.
        feature.settings.character.enabled = false;
        Pump(20);
        long beforeOff = RenderAndSave("22_character_behind_original");
        characterRoot.position = new Vector3(0f, 0f, 0.2f);
        Pump(20);
        long frontOff = RenderAndSave("23_character_front_original");
        feature.settings.character.enabled = true;

        Record($"  png evidence: front={frontPixels}B off={frontOff}B; behind={behindPixels}B off={beforeOff}B");

        // Measure the projected silhouette in both positions with the prop present.
        characterRoot.position = new Vector3(0f, 0f, 0.2f);
        Pump(25);
        int frontVisible = MeasureCharacterPixels();
        characterRoot.position = new Vector3(0f, 0f, 1.8f);
        Pump(25);
        int behindVisible = MeasureCharacterPixels();

        // Then the same two positions with the prop hidden: behind it the character must gain
        // pixels, which is what proves the prop is really in front of it.
        var propRenderer = prop.GetComponent<MeshRenderer>();
        propRenderer.enabled = false;
        Pump(25);
        int behindUnobstructed = MeasureCharacterPixels();
        characterRoot.position = new Vector3(0f, 0f, 0.2f);
        Pump(25);
        int frontUnobstructed = MeasureCharacterPixels();
        propRenderer.enabled = true;

        Record($"  projected silhouette pixels: front={frontVisible} behind={behindVisible} " +
               $"| prop disabled: front={frontUnobstructed} behind={behindUnobstructed}");
        Record($"  pixels hidden by the prop behind it: {behindUnobstructed - behindVisible}");
        Require(frontVisible > 200, "the projected character is not visible in front of the prop");
        Require(behindUnobstructed > behindVisible + 50,
            "the prop does not occlude the rebuilt character standing behind it");
        Require(frontVisible > frontUnobstructed - 50,
            "the prop wrongly hides the character standing in front of it");

        characterRoot.position = new Vector3(0f, 0f, 0.2f);
        Pump(20);
        Record("  PASS: depth-tested compositing keeps prop occlusion for the rebuilt character");
    }

    // ----------------------------------------------------------- 7. camera moves

    static void RunCameraMoves()
    {
        Section("7. Camera pan, zoom and rotation");
        feature.settings.sceneChannelEnabled = true;
        feature.settings.character.enabled = true;
        characterSprite.sprite = frames[0];
        controller.transform.position = new Vector3(0f, 0f, 0.2f);
        Pump(25);

        var start = camera.transform.position;
        float startSize = camera.orthographicSize;

        // Pan.
        camera.transform.position = start + new Vector3(0.8f, 0.15f, 0f);
        Pump(20);
        int panned = MeasureCharacterPixels();
        RenderAndSave("30_camera_panned");

        // Zoom.
        camera.orthographicSize = startSize * 0.7f;
        Pump(20);
        int zoomed = MeasureCharacterPixels();
        RenderAndSave("31_camera_zoomed");

        // Rotate.
        camera.transform.position = start;
        camera.transform.rotation = Quaternion.Euler(0f, 18f, 0f);
        camera.orthographicSize = startSize;
        Pump(20);
        int rotated = MeasureCharacterPixels();
        RenderAndSave("32_camera_rotated");

        int silhouettes = CountCharacterSilhouettes();
        Record($"  visible character pixels: panned={panned} zoomed={zoomed} rotated={rotated}; " +
               $"projected silhouette nodes={silhouettes}");
        Require(panned > 100 && zoomed > 100 && rotated > 100,
            "the character disappeared during a camera move");
        Require(silhouettes == 1, "the character was drawn more than once after a camera move");

        camera.transform.rotation = Quaternion.identity;
        Pump(20);
        Record("  PASS: no duplicated character and no lost character across pan/zoom/rotation");
    }

    // ------------------------------------------------------- 8. flip and edges

    static void RunFlipAndEdges()
    {
        Section("8. Flip, transparent edges and foot baseline");
        feature.settings.character.enabled = true;
        characterSprite.sprite = frames[0];
        characterSprite.flipX = false;
        Pump(20);
        var normal = ReadPixels();
        RenderAndSave("40_flip_off");

        characterSprite.flipX = true;
        Pump(20);
        var flipped = ReadPixels();
        RenderAndSave("41_flip_on");

        int difference = normal.Zip(flipped, (a, b) =>
            Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b)).Count(d => d > 30);
        Record($"  flipX changed {difference} pixels (expected a mirrored silhouette)");
        Require(difference > 80, "flipX did not mirror the rebuilt character");

        var projection = FindProjection();
        var shape = projection?.CurrentShape;
        if (shape != null)
        {
            float lowest = shape.vertices.Length == 0 ? 0 : shape.vertices.Min(v => v.y);
            float highest = shape.vertices.Length == 0 ? 0 : shape.vertices.Max(v => v.y);
            float left = shape.vertices.Length == 0 ? 0 : shape.vertices.Min(v => v.x);
            float right = shape.vertices.Length == 0 ? 0 : shape.vertices.Max(v => v.x);
            var bounds = characterSprite.sprite.bounds;
            Record($"  shape local rect x[{left:F3},{right:F3}] y[{lowest:F3},{highest:F3}] " +
                   $"sprite bounds center={bounds.center} size={bounds.size}");
            Record($"  sprite local rect x[{bounds.min.x:F3},{bounds.max.x:F3}] y[{bounds.min.y:F3},{bounds.max.y:F3}]");
            Require(Mathf.Abs(left - bounds.min.x) < 0.02f && Mathf.Abs(highest - bounds.max.y) < 0.02f,
                "the rebuilt shape does not line up with the sprite's own rect (pivot/foot baseline)");
        }

        characterSprite.flipX = false;
        Pump(10);

        // Silhouette fidelity: the rebuilt shape must cover roughly the same pixels as the
        // original sprite. A filled rectangle would be an order of magnitude larger, and a
        // broken alpha path would make it vanish.
        controller.transform.position = new Vector3(0f, 0f, 0.2f);
        feature.settings.character.enabled = true;
        Pump(20);
        int projectedPixels = MeasureCharacterPixels();
        feature.settings.character.enabled = false;
        Pump(20);
        int originalPixels = MeasureCharacterPixels();
        feature.settings.character.enabled = true;
        Pump(20);

        float ratio = originalPixels == 0 ? 0f : projectedPixels / (float)originalPixels;
        Record($"  silhouette pixels: rebuilt={projectedPixels} original={originalPixels} ratio={ratio:F2}");
        Require(projectedPixels > 150, "the rebuilt character is not visible");
        Require(originalPixels > 150, "the original sprite is not visible for comparison");
        Require(ratio > 0.5f && ratio < 1.8f,
            "the rebuilt character does not cover the same silhouette as the original sprite");
        Record("  PASS: rebuilt silhouette matches the original sprite, so edges stay transparent");
    }

    // ------------------------------------------------- 9. churn and lifetime

    static void RunChurnAndLifetime()
    {
        Section("9. Toggling, respawning and scene unloading stay bounded");
        feature.settings.character.enabled = true;

        int nodesBefore = CountGeneratedNodes();
        for (int i = 0; i < 12; i++)
        {
            feature.settings.character.enabled = i % 2 == 0;
            Pump(3);
        }
        feature.settings.character.enabled = true;
        Pump(12);
        int nodesAfter = CountGeneratedNodes();
        Record($"  after 12 toggles: generated nodes {nodesBefore} -> {nodesAfter}, " +
               $"cache={characters.Cache.Count} entries/{characters.Cache.Pixels} px");
        Require(nodesAfter <= nodesBefore + 1,
            "toggling the channel leaked generated renderers");

        // Respawn: destroy the character and add a new one. The registry must notice.
        var respawned = new GameObject("respawned character");
        respawned.transform.position = new Vector3(0.6f, 0f, 0.2f);
        respawned.AddComponent<CartoonCharacterRenderController>();
        var respawnSprite = new GameObject("sprite");
        respawnSprite.transform.SetParent(respawned.transform, false);
        var respawnRenderer = respawnSprite.AddComponent<SpriteRenderer>();
        respawnRenderer.sprite = frames[1];
        var respawnBounds = frames[1].bounds;
        float respawnScale = 4.0f / Mathf.Max(0.001f, respawnBounds.size.y);
        respawnSprite.transform.localScale = Vector3.one * respawnScale;
        respawnSprite.transform.localPosition = Vector3.zero;
        Pump(25);
        Record($"  after respawn: tracked={characters.Stats.trackedCharacters} projected={characters.Stats.projectedCharacters}");
        Require(characters.Stats.trackedCharacters >= 2, "a runtime-spawned character was not tracked");
        Require(characters.Stats.projectedCharacters >= 2, "the runtime-spawned character was not projected");

        UnityEngine.Object.DestroyImmediate(respawned);
        Pump(20);
        Record($"  after destroy: tracked={characters.Stats.trackedCharacters} cache={characters.Cache.Count}");

        // Unloading the active scene must not throw and must drop the projections.
        int cacheBeforeUnload = characters.Cache.Count;
        // The swap destroys this scene's camera, so nothing may render after it.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Record($"  after scene swap: liveProjections={characters.ProjectedCharacterCount} " +
               $"cache={characters.Cache.Count} (was {cacheBeforeUnload})");
        Require(characters.ProjectedCharacterCount == 0, "projections survived a scene unload");
        Record("  PASS: toggles, respawn and scene unload stay bounded and tracked");
    }

    static void RunChannelCounters()
    {
        Section("10. Per-channel counters");
        var scene = feature.SceneChannel == null ? default : feature.SceneChannel.SceneStatistics;
        Record($"  capture set: {ProjectedShapePass.LastCaptureSetSummary}");
        Record($"  scene channel: " + (feature.SceneChannel == null
            ? "absent"
            : $"cameras={scene.cameraStates} regions={scene.regions} " +
              $"edges={scene.sourceEdges}->{scene.simplifiedEdges} triangles={scene.triangles} " +
              $"width={scene.lastWidth} buildMs={scene.lastBuildMilliseconds:F1} " +
              $"failures={scene.buildFailures} discarded={scene.discardedResults}"));
        Record($"  character channel: {characters.Stats}");
        Record($"  controller discovery: registrations={CartoonRenderRegistry.RegistrationCount} " +
               $"{characters.Stats.lastControllerRefresh}");
        Record($"  cache: entries={characters.Cache.Count} pixels={characters.Cache.Pixels} " +
               $"hits={characters.Cache.Hits} misses={characters.Cache.Misses} " +
               $"evictions={characters.Cache.Evictions} hitRate={characters.Cache.HitRate:P1}");
    }

    // ------------------------------------------------------------- utilities

    static CartoonCharacterProjection FindProjection() => characters.FindProjection(controller);

    static int CountGeneratedNodes()
    {
        int count = 0;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == "cartoon_character_projected" ||
                    transform.name == "cartoon_character_projected_contours")
                    count++;
        return count;
    }

    static int Difference(Color32[] a, Color32[] b)
    {
        int count = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 24)
                count++;
        }
        return count;
    }

    static Color32[] ReadPixels()
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
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    static long RenderAndSave(string name)
    {
        RenderAndResolve();
        var pixels = ReadPixels();
        var copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try
        {
            copy.SetPixels32(pixels);
            copy.Apply();
            var bytes = copy.EncodeToPNG();
            File.WriteAllBytes(Output + "/" + name + ".png", bytes);
            return bytes.Length;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    /// <summary>
    /// One frame plus a synchronous resolve for both channels. Batch mode never dispatches the
    /// asynchronous readback callbacks, so the scene capture is resolved explicitly here.
    /// </summary>
    static void RenderAndResolve()
    {
        var previous = camera.targetTexture;
        camera.targetTexture = target;
        try
        {
            camera.Render();
            ProjectedShapePass.ResolveSynchronousCapture();
            camera.Render();
        }
        finally
        {
            camera.targetTexture = previous;
        }
    }

    /// <summary>Advances real time and renders, so queued background builds can complete.</summary>
    static void Pump(int frames)
    {
        var previous = camera.targetTexture;
        camera.targetTexture = target;
        try
        {
            for (int i = 0; i < frames; i++)
            {
                Thread.Sleep(12);
                camera.Render();
                ProjectedShapePass.ResolveSynchronousCapture();
            }
        }
        finally
        {
            camera.targetTexture = previous;
        }
    }

    /// <summary>
    /// Character silhouette size, measured as the pixels that change when the whole character is
    /// hidden. Counting "non-black" pixels would include the entire stage, which would make the
    /// occlusion checks compare scene content instead of the character.
    /// </summary>
    static int MeasureCharacterPixels()
    {
        var root = controller.transform;
        Vector3 parked = root.position;
        RenderAndResolve();
        var withCharacter = ReadPixels();

        // Parking the character far off screen is non-destructive, unlike toggling the root:
        // activation changes would invalidate the channel's registration mid-measurement.
        root.position = parked + new Vector3(0f, 0f, 400f);
        try
        {
            RenderAndResolve();
            var without = ReadPixels();
            return withCharacter.Zip(without, (a, b) =>
                Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b)).Count(d => d > 24);
        }
        finally
        {
            root.position = parked;
            RenderAndResolve();
        }
    }

    static int CountCharacterSilhouettes() => characters.ProjectedCharacterCount;

    static void Cleanup()
    {
        ProjectedShapePass.UseSynchronousReadback = false;
        CartoonCharacterSystem.AllowVisualChangesOverride = false;
        // Section 9 swaps the scene, so every scene object may already be destroyed here.
        try
        {
            if (camera) camera.targetTexture = null;
        }
        catch (MissingReferenceException)
        {
            // Already torn down with the scene; nothing to release.
        }
        camera = null;
        controller = null;
        characterSprite = null;
        prop = null;
        if (target)
        {
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = null;
        }
    }

    static void Section(string title)
    {
        report.AppendLine();
        report.AppendLine(title);
        Debug.Log("[ChannelAcceptance] " + title);
    }

    static void Require(bool condition, string message)
    {
        if (condition)
            return;
        Fail(message);
    }

    static void Fail(string message)
    {
        failures.Add(message);
        report.AppendLine("  FAIL: " + message);
        Debug.LogError("[ChannelAcceptance] FAIL: " + message);
    }

    static void Record(string message)
    {
        report.AppendLine(message);
        Debug.Log("[ChannelAcceptance] " + message);
    }

    static void Flush(int exitCode)
    {
        Directory.CreateDirectory(Output);
        report.AppendLine();
        report.AppendLine(failures.Count == 0
            ? "RESULT: PASS"
            : $"RESULT: FAIL ({failures.Count})");
        foreach (string failure in failures)
            report.AppendLine("  - " + failure);
        File.WriteAllText(Output + "/Report.txt", report.ToString());
        AssetDatabase.Refresh();
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }
}
