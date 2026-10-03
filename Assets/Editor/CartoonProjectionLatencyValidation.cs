using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CartoonProjection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using CapturedProjectionView = CartoonProjection.ProjectionView;

// Native Unity A/B smoke test, NOT a sealed hardware benchmark or presentation-latency test.
// Same editor process, room, render size and trajectory. Run without -quit.
public static class CartoonProjectionLatencyValidation
{
    const string Output = "Assets/CartoonRenderer/Generated/LowLatencyValidation";
    static readonly StringBuilder report = new StringBuilder();
    static readonly List<ProjectedShapePass.Timing>[] samples = { new(), new() };
    static readonly List<double>[] completions = { new(), new() };
    static CartoonRendererFeature feature;
    static string savedSettings;
    static CartoonRoomAcceptanceController room;
    static Camera camera;
    static RenderTexture target;
    static Stopwatch phaseClock;
    static int phase;
    static double nextRender;
    static bool settledDetail;
    static Color32[] baselineStable;
    static bool previousAsyncShaderCompilation;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch editor.");
        Directory.CreateDirectory(Output);
        previousAsyncShaderCompilation = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset");
            feature = renderer.rendererFeatures.OfType<CartoonRendererFeature>().Single();
            savedSettings = JsonUtility.ToJson(feature.settings);
            CheckPolicy();
            CheckAlignmentRaster();
            EditorSceneManager.OpenScene("Assets/Scenes/CartoonRoomAcceptance.unity", OpenSceneMode.Single);
            room = Object.FindFirstObjectByType<CartoonRoomAcceptanceController>();
            camera = room.viewCamera;
            target = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            ProjectedShapePass.UseSynchronousReadback = false;
            ProjectedShapePass.ProjectionCompleted += Collect;
            BeginPhase();
            EditorApplication.update += Tick;
        }
        catch (Exception exception) { Finish(exception); }
    }

    static void CheckPolicy()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Policy test").AddComponent<Camera>();
        probe.orthographic = true;
        var settings = new CartoonRenderSettings();
        var policy = new ProjectionFramePolicy();
        policy.Observe(probe, 1, settings, false);
        Require(policy.Width == 320 && policy.Moving, "Cold camera must start at interaction resolution.");
        policy.Observe(probe, 1.25, settings, false);
        Require(policy.Width == 640 && !policy.Moving, "Settled camera must regain full detail.");
        probe.transform.position += Vector3.right;
        policy.Observe(probe, 1.3, settings, false);
        Require(policy.Width == 320 && policy.UpdatesPerSecond == 30, "Moving policy incorrect.");
        policy.Observe(probe, 1.31, settings, true);
        Require(policy.Width == 640 && !policy.Moving, "Synchronous tools must keep fixed full resolution.");
        settings.projectionLowLatency = false;
        policy.Observe(probe, 1.32, settings, false);
        Require(policy.Width == 640 && policy.UpdatesPerSecond == 8, "Legacy mode changed.");
        var before = CapturedProjectionView.Capture(probe);
        probe.transform.rotation = Quaternion.Euler(0, 90, 0);
        Require(before.IsCameraCut(CapturedProjectionView.Capture(probe)), "90 degree cut must invalidate old geometry.");
        CheckIdlePolicy(probe);
        Record("PASS: cold start, motion tier, settle detail, fixed tooling, legacy mode, camera cut policy.");
    }

    static void CheckIdlePolicy(Camera probe)
    {
        var settings = new CartoonRenderSettings { projectionIdleUpdatesPerSecond = .5f };
        var policy = new ProjectionFramePolicy();
        policy.Observe(probe, 2, settings, false);
        Require(!policy.Idle && policy.Interacting, "Cold start must not be idle-throttled.");
        policy.Observe(probe, 2.3, settings, false);
        Require(policy.Idle && policy.CaptureInterval == 2, "Stationary capture must use the 0.5 Hz idle cadence.");
        settings.projectionIdleUpdatesPerSecond = 0;
        policy.Observe(probe, 2.4, settings, false);
        Require(double.IsPositiveInfinity(policy.CaptureInterval), "Zero idle rate must hold the cached result.");
        probe.transform.Rotate(0, 2, 0);
        policy.Observe(probe, 2.5, settings, false);
        Require(policy.Interacting && !policy.Idle && policy.UpdatesPerSecond == 30,
            "Rotation must immediately restore the interaction cadence.");
        settings.projectionLowLatency = false;
        policy.Observe(probe, 2.8, settings, false);
        Require(policy.Idle, "Idle reduction must work independently of motion-resolution mode.");
        policy.Observe(probe, 2.9, settings, true);
        Require(!policy.Idle && !double.IsInfinity(policy.CaptureInterval), "Fixed capture tools must bypass idle throttling.");
        settings.projectionAdaptiveRefresh = false;
        policy.Observe(probe, 3, settings, false);
        Require(!policy.Idle && policy.UpdatesPerSecond == 8 && policy.CaptureInterval == 1.0 / 8,
            "Disabling adaptive refresh must restore the original cadence.");
        Record("PASS: idle 0.5 Hz, zero-rate hold, rotation wakeup, independent low-latency switch, tooling bypass, legacy cadence.");
    }

    // This entry point delegates to the asynchronous idle acceptance harness below.
    public static void RunIdle() => CartoonProjectionIdleValidation.Run();

    static void CheckAlignmentRaster()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Alignment camera").AddComponent<Camera>();
        probe.transform.position = new Vector3(0, 0, -3);
        probe.orthographic = true;
        probe.orthographicSize = 1;
        probe.clearFlags = CameraClearFlags.SolidColor;
        probe.backgroundColor = Color.black;
        probe.allowHDR = probe.allowMSAA = false;
        probe.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var rt = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.Create();
        probe.targetTexture = rt;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(.5f, .25f, .125f));
        quad.GetComponent<Renderer>().sharedMaterial = material;
        feature.settings.enabled = feature.settings.projectedShapes = feature.settings.projectionLowLatency = true;
        feature.settings.projectionWidth = 128;
        feature.settings.projectionShowContours = false;
        try
        {
            foreach (var delta in new[] { Vector3.right * .25f, Vector3.up * .25f, Vector3.zero })
            {
                probe.transform.position = new Vector3(0, 0, -3);
                probe.orthographicSize = 1;
                feature.Create();
                ProjectedShapePass.UseSynchronousReadback = true;
                probe.Render();
                Require(ProjectedShapePass.ResolveSynchronousCapture(), "Reference capture failed.");
                probe.Render();
                // Hold the in-flight slot to prove the alignment uses OLD polygons without rebuilding.
                var pass = typeof(CartoonRendererFeature).GetField("projectedPass", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feature);
                var states = (IDictionary)typeof(ProjectedShapePass).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pass);
                var state = states[probe.GetInstanceID()];
                state.GetType().GetField("busy").SetValue(state, true);
                int serial = (int)state.GetType().GetField("serial").GetValue(state);
                ProjectedShapePass.UseSynchronousReadback = false;
                probe.transform.position += delta;
                if (delta == Vector3.zero) probe.orthographicSize = .8f;
                probe.Render();
                Require(serial == (int)state.GetType().GetField("serial").GetValue(state), "Alignment secretly rebuilt.");
                var aligned = Read(rt);
                string label = delta.x != 0 ? "PanX" : delta.y != 0 ? "PanY" : "Zoom";
                Save(rt, label + "_cached");
                feature.Create();
                ProjectedShapePass.UseSynchronousReadback = true;
                probe.Render();
                Require(ProjectedShapePass.ResolveSynchronousCapture(), "Fresh comparison capture failed.");
                probe.Render();
                var fresh = Read(rt);
                Save(rt, label + "_fresh");
                Record("Alignment pixels cached=" + aligned[64 * 128 + 48] + "; fresh=" + fresh[64 * 128 + 48]);
                int changed = aligned.Zip(fresh, (a,b) => Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(d => d > 6);
                Require(changed <= 16, "Cached pan differs from fresh redraw: " + changed + " pixels, delta=" + delta);
                Record("PASS: cached orthographic " + label + " matches fresh polygons; differing pixels=" + changed);
            }
        }
        finally
        {
            probe.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(material);
            ProjectedShapePass.UseSynchronousReadback = false;
        }
    }

    static void BeginPhase()
    {
        JsonUtility.FromJsonOverwrite(savedSettings, feature.settings);
        feature.settings.enabled = feature.settings.projectedShapes = true;
        feature.settings.projectionLowLatency = phase == 1;
        feature.settings.projectionWidth = 640;
        feature.settings.projectionUpdatesPerSecond = 8;
        feature.settings.projectionMotionWidth = 320;
        feature.settings.projectionMotionUpdatesPerSecond = 30;
        feature.settings.projectionShowContours = false;
        room.yaw = -22;
        room.ApplyView();
        feature.Create();
        nextRender = 0;
        settledDetail = false;
        phaseClock = Stopwatch.StartNew();
    }

    static void Tick()
    {
        try
        {
            double elapsed = phaseClock.Elapsed.TotalSeconds;
            if (elapsed < nextRender) return;
            nextRender = elapsed + 1.0 / 60;
            float motion = Mathf.Clamp((float)elapsed - .35f, 0, 3);
            room.yaw = -22 + (elapsed < 3.35 ? 28 * Mathf.Sin(motion * Mathf.PI * 2 / 3) : 0);
            room.ApplyView();
            camera.Render();
            if (elapsed < 4.5) return;
            Require(samples[phase].Count >= 8, "Too few actual async samples.");
            Save(target, phase == 0 ? "Baseline_settled" : "LowLatency_settled");
            if (phase == 0)
            {
                baselineStable = Read(target);
                phase = 1;
                BeginPhase();
                return;
            }
            Require(samples[1].Any(value => value.width == 320), "Motion never used the lower resolution.");
            Require(settledDetail, "Full 640px detail did not return after settling.");
            int changed = baselineStable.Zip(Read(target), (a,b) => Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(d => d > 6);
            Require(changed < 640 * 480 / 100, "Settled style changed in more than 1% of pixels: " + changed);
            Record("PASS: motion 320px, settled 640px; settled A/B changed pixels=" + changed);
            foreach (int i in new[] { 0, 1 })
            {
                Record((i == 0 ? "BASELINE" : "LOW_LATENCY") + ": samples=" + samples[i].Count +
                    "; capture->redraw-submit p50=" + Percentile(samples[i].Select(s => s.captureToRedrawMilliseconds), .5).ToString("F1") +
                    "ms p95=" + Percentile(samples[i].Select(s => s.captureToRedrawMilliseconds), .95).ToString("F1") +
                    "ms; CPU build p50=" + Percentile(samples[i].Select(s => s.buildMilliseconds), .5).ToString("F1") +
                    "ms; completed-update interval p50=" + Percentile(completions[i].Skip(1).Select((value,index) => (value-completions[i][index])*1000), .5).ToString("F1") + "ms");
            }
            Record("NOTE: submission timings exclude monitor presentation; native batch samples are not a sealed hardware benchmark.");
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    static void Collect(ProjectedShapePass.Timing timing)
    {
        if (!camera || timing.cameraId != camera.GetInstanceID()) return;
        double elapsed = phaseClock.Elapsed.TotalSeconds;
        if (elapsed >= .6 && elapsed < 3.35) { samples[phase].Add(timing); completions[phase].Add(elapsed); }
        if (phase == 1 && elapsed > 3.6 && timing.width == 640) settledDetail = true;
    }

    static double Percentile(IEnumerable<double> source, double p)
    {
        var values = source.OrderBy(value => value).ToArray();
        return values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * p) - 1)];
    }

    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        ProjectedShapePass.ProjectionCompleted -= Collect;
        if (camera) camera.targetTexture = null;
        if (target) Object.DestroyImmediate(target);
        ProjectedShapePass.UseSynchronousReadback = false;
        ShaderUtil.allowAsyncCompilation = previousAsyncShaderCompilation;
        if (feature && savedSettings != null)
        {
            JsonUtility.FromJsonOverwrite(savedSettings, feature.settings);
            feature.Create();
        }
        if (error != null) { Record("FAIL: " + error); Debug.LogException(error); }
        File.WriteAllText(Output + "/Report.txt", report.ToString());
        using (var csv = new StreamWriter(Output + "/Samples.csv"))
        {
            csv.WriteLine("mode,width,moving,readback_ms,cpu_build_ms,capture_to_redraw_submit_ms,phase_elapsed_s");
            for (int i = 0; i < samples.Length; i++) for (int j = 0; j < samples[i].Count; j++)
            {
                var value = samples[i][j];
                csv.WriteLine(FormattableString.Invariant($"{i},{value.width},{value.moving},{value.readbackMilliseconds:F3},{value.buildMilliseconds:F3},{value.captureToRedrawMilliseconds:F3},{completions[i][j]:F3}"));
            }
        }
        EditorApplication.Exit(error == null ? 0 : 1);
    }

    static Color32[] Read(RenderTexture texture)
    {
        var previous = RenderTexture.active;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
        try { RenderTexture.active = texture; copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); copy.Apply(); return copy.GetPixels32(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(copy); }
    }
    static void Save(RenderTexture texture, string name)
    {
        var copy = new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
        try { copy.SetPixels32(Read(texture)); copy.Apply(); File.WriteAllBytes(Output + "/" + name + ".png",copy.EncodeToPNG()); }
        finally { Object.DestroyImmediate(copy); }
    }
    static void Require(bool condition,string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Record(string message) { report.AppendLine(message); Debug.Log("[ProjectionLatency] " + message); }
}

// Native async functional checks. No frame-time or monitor-latency benchmark claims.
internal static class CartoonProjectionIdleValidation
{
    const string Output = "Assets/CartoonRenderer/Generated/LowLatencyValidation/IdleReport.txt";
    static readonly StringBuilder report = new();
    static readonly Camera[] cameras = new Camera[2];
    static readonly RenderTexture[] targets = new RenderTexture[2];
    static readonly int[] completed = new int[2], baseline = new int[2];
    static readonly List<double> idleCompletions = new();
    static CartoonRendererFeature feature;
    static string savedSettings;
    static Material material;
    static int stage;
    static double started, nextRender;
    static bool previousAsyncCompilation, secondHint;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch editor.");
        try
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset");
            feature = data.rendererFeatures.OfType<CartoonRendererFeature>().Single();
            savedSettings = JsonUtility.ToJson(feature.settings);
            previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            typeof(CartoonProjectionLatencyValidation).GetMethod("CheckPolicy", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(.5f, .25f, .125f));
            cube.GetComponent<Renderer>().sharedMaterial = material;
            for (int i = 0; i < 2; i++)
            {
                cameras[i] = new GameObject("Idle acceptance camera " + i).AddComponent<Camera>();
                cameras[i].transform.position = new Vector3(0, 0, -4);
                cameras[i].orthographic = true;
                cameras[i].orthographicSize = 1.5f;
                cameras[i].clearFlags = CameraClearFlags.SolidColor;
                cameras[i].allowHDR = cameras[i].allowMSAA = false;
                cameras[i].GetUniversalAdditionalCameraData().renderPostProcessing = false;
                targets[i] = new RenderTexture(256, 256, 24);
                targets[i].Create();
                cameras[i].targetTexture = targets[i];
            }
            feature.settings.enabled = feature.settings.sceneChannelEnabled = feature.settings.projectedShapes = true;
            feature.settings.character.enabled = false;
            feature.settings.projectionLowLatency = feature.settings.projectionAdaptiveRefresh = true;
            feature.settings.projectionWidth = feature.settings.projectionMotionWidth = 128;
            feature.settings.projectionMaximumResultAge = 2;
            feature.settings.projectionSettleSeconds = .2f;
            feature.settings.projectionIdleUpdatesPerSecond = 0;
            feature.Create();
            ProjectedShapePass.UseSynchronousReadback = false;
            ProjectedShapePass.ProjectionCompleted += Collect;
            Begin(0);
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Finish(e); }
    }

    static void Begin(int value)
    {
        stage = value;
        started = EditorApplication.timeSinceStartup;
        Array.Copy(completed, baseline, 2);
    }

    static object State(int i)
    {
        var states = (IDictionary)typeof(ProjectedShapePass).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(feature.SceneChannel);
        return states[cameras[i].GetInstanceID()];
    }
    static T Field<T>(int i, string name) => (T)State(i).GetType().GetField(name).GetValue(State(i));
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Record(string message) { report.AppendLine(message); Debug.Log("[ProjectionIdle] " + message); }

    static void Tick()
    {
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextRender) return;
            nextRender = now + 1.0 / 30;
            double elapsed = now - started;
            if (stage == 0 && elapsed < .75)
                foreach (var camera in cameras) camera.transform.rotation = Quaternion.Euler(0, (float)elapsed * 12, 0);
            foreach (var camera in cameras) camera.Render();
            switch (stage)
            {
                case 0 when elapsed > 2:
                    for (int i = 0; i < 2; i++)
                    {
                        Require(completed[i] > 1, "Cold/moving capture did not complete.");
                        Require(!Field<bool>(i, "meshWasInteracting"), "Equal-width tiers never produced a final settled capture.");
                        Require(!Field<CapturedProjectionView>(i, "meshView").Changed(CapturedProjectionView.Capture(cameras[i])),
                            "Settled result is not from the final camera pose.");
                    }
                    Record("PASS: cold start and rotation complete; equal-width tiers refine to the final settled pose.");
                    Begin(1);
                    break;
                case 1 when elapsed > 2:
                    Require(completed.SequenceEqual(baseline), "Zero idle rate rebuilt an unchanged view.");
                    Record("PASS: two frozen cameras perform zero rebuilds during a 2-second stationary window.");
                    Begin(2);
                    ProjectedShapePass.HintSceneContentChanged();
                    break;
                case 2:
                    if (!secondHint && Field<bool>(0, "busy") && Field<bool>(1, "busy"))
                    {
                        secondHint = true;
                        ProjectedShapePass.HintSceneContentChanged();
                    }
                    if (completed[0] >= baseline[0] + 2 && completed[1] >= baseline[1] + 2)
                    {
                        Require(secondHint, "In-flight notification scenario was not exercised.");
                        Record("PASS: content notifications reach both cameras, including notifications during in-flight jobs.");
                        Begin(3);
                        feature.settings.projectionContourTolerance += .25f;
                    }
                    else Require(elapsed < 5, "Content notification was lost while a camera was busy.");
                    break;
                case 3:
                    if (completed[0] > baseline[0] && completed[1] > baseline[1])
                    {
                        Record("PASS: style changes wake both frozen cameras.");
                        feature.settings.projectionIdleUpdatesPerSecond = .5f;
                        feature.SceneChannel.InvalidateSceneResults();
                        Begin(4);
                    }
                    else Require(elapsed < 5, "Style change did not wake idle capture.");
                    break;
                case 4 when elapsed > 4.5:
                    Require(idleCompletions.Count >= 2 && idleCompletions.Count <= 4, "Idle capture count did not match 0.5 Hz.");
                    for (int i = 1; i < idleCompletions.Count; i++)
                        Require(idleCompletions[i] - idleCompletions[i - 1] > 1.8, "Stationary captures still used the active cadence.");
                    Record("PASS: 0.5 Hz stationary cadence, " + idleCompletions.Count + " completions over 4.5 seconds.");
                    Begin(5);
                    foreach (var camera in cameras) camera.transform.Rotate(0, 3, 0);
                    break;
                case 5:
                    if (completed[0] > baseline[0] && completed[1] > baseline[1])
                    {
                        Require(elapsed < 1.5, "Rotation waited for the 2-second idle deadline.");
                        Record("PASS: rotation wakes both cameras without waiting for the idle deadline.");
                        Finish(null);
                    }
                    else Require(elapsed < 5, "Rotation did not restart capture.");
                    break;
            }
        }
        catch (Exception e) { Finish(e); }
    }
    static void Collect(ProjectedShapePass.Timing timing)
    {
        for (int i = 0; i < 2; i++)
            if (cameras[i] && timing.cameraId == cameras[i].GetInstanceID())
            {
                completed[i]++;
                if (stage == 4 && i == 0) idleCompletions.Add(EditorApplication.timeSinceStartup);
            }
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        ProjectedShapePass.ProjectionCompleted -= Collect;
        ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
        if (feature && savedSettings != null) { JsonUtility.FromJsonOverwrite(savedSettings, feature.settings); feature.Create(); }
        for (int i = 0; i < 2; i++)
        {
            if (cameras[i]) cameras[i].targetTexture = null;
            if (targets[i]) Object.DestroyImmediate(targets[i]);
        }
        if (material) Object.DestroyImmediate(material);
        if (error != null) { Record("FAIL: " + error); Debug.LogException(error); }
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        File.WriteAllText(Output, report.ToString());
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
