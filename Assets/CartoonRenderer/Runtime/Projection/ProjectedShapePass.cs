using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CartoonProjection
{
    // The camera image is replaced by CPU-generated 2D polygon geometry.
    // Original textures are only used by the capture stage, never the redraw stage.
    public sealed class ProjectedShapePass : ScriptableRenderPass, IDisposable
    {
        // Capture shader encodes albedo explicitly; identity channels are raw bytes.
        // Neither attachment may apply an additional hardware sRGB conversion.
        const GraphicsFormat CaptureFormat = GraphicsFormat.R8G8B8A8_UNorm;

        sealed class State
        {
            public Camera camera;
            public Mesh mesh, lines;
            public bool busy, disposed;
            public double next;
            public Task<ProjectedShapeBuilder.Result> task;
            public Texture2D coverage;
            public byte[] coverageBuffer;
            public int captureWidth, captureHeight;
            public Color32[] captureIdentities;
            public readonly ProjectionFramePolicy policy = new();
            public ProjectionView capturedView, meshView;
            public double captureMilliseconds, readbackMilliseconds, meshCaptureMilliseconds;
            public int serial, meshSerial, publishedSerial;
            public bool meshWasMoving;
            public bool captureWasMoving;
            public bool captureWasInteracting, meshWasInteracting;
            public int contentVersion;
            public double captureSeconds;
            public readonly MaterialPropertyBlock redrawProperties = new();
            public int meshCaptureWidth;
            public double meshReadbackMilliseconds, meshBuildMilliseconds;
        }
        struct Draw { public Renderer renderer; public Material material; public int submesh; }
        sealed class CaptureData { public List<Draw> draws; public int width, height; public bool clear; public Color clearColor; }
        sealed class ReadData { public TextureHandle color, ids; public State state; public int width,height,step,area; public float epsilon; }
        sealed class RedrawData { public Mesh mesh, lines; public Material material; public MaterialPropertyBlock properties; public State state; }
        readonly Dictionary<int, State> states = new();
        readonly Dictionary<long, Material> materials = new();
        readonly Dictionary<long, Renderer> materialOwners = new();
        readonly Material redraw;
        readonly Shader captureShader;
        CartoonRenderSettings settings;
        Renderer[] renderers = Array.Empty<Renderer>();
        double refresh;
        int rendererSceneCount = -1;
        public static string LastStatistics { get; private set; } = "Waiting for projection";

        public static int BuildFailures { get; private set; }

        // Most recent completed rebuild, exposed so tooling can report the real numbers
        // instead of re-deriving them from the pass internals.
        public static int Regions { get; private set; }
        public static int SourceEdges { get; private set; }
        public static int SimplifiedEdges { get; private set; }
        public static int Triangles { get; private set; }
        public static double BuildMilliseconds { get; private set; }
        public static int LastMeshVertexCount { get; private set; }
        public static int LastProjectionWidth { get; private set; }
        public static double LastCaptureToRedrawMilliseconds { get; private set; }
        public static int DiscardedResults { get; private set; }
        public static int ContentHintRefreshes { get; private set; }

        /// <summary>How many renderers were collected and how many draws the capture kept.</summary>
        public static string LastCaptureSetSummary { get; private set; } = "(none)";

        public readonly struct Timing
        {
            public readonly int cameraId, serial, width;
            public readonly bool moving;
            public readonly double readbackMilliseconds, buildMilliseconds, captureToRedrawMilliseconds;
            public Timing(int cameraId, int serial, int width, bool moving, double readback, double build, double total)
            {
                this.cameraId = cameraId; this.serial = serial; this.width = width; this.moving = moving;
                readbackMilliseconds = readback; buildMilliseconds = build; captureToRedrawMilliseconds = total;
            }
        }
        // Timings end at redraw command submission, not monitor presentation/GPU completion.
        public static event Action<Timing> ProjectionCompleted;
        static double ClockMilliseconds => System.Diagnostics.Stopwatch.GetTimestamp() * (1000.0 / System.Diagnostics.Stopwatch.Frequency);
#if UNITY_EDITOR
        // Blocking capture tools do not return to the editor update loop to dispatch async
        // callbacks. They can opt into persistent targets and read them after Camera.Render.
        // Normal runtime frames and non-blocking editor tests keep the asynchronous path.
        public static bool UseSynchronousReadback;

        // Raw capture output of the most recent synchronous readback, for tooling checks.
        public static Color32[] LastCapturePixels { get; private set; }
        public static int LastCaptureWidth { get; private set; }
        public static int LastCaptureHeight { get; private set; }

        sealed class SynchronousTargets { public RenderTexture color, ids; public RTHandle colorHandle, idsHandle; }
        static SynchronousTargets synchronousTargets;
        static bool pendingCapture;
        static Camera pendingCamera;
        static int pendingWidth, pendingHeight, pendingStep, pendingArea;
        static float pendingEpsilon;
        static ProjectedShapePass instance;

        // The pass instance owning the camera states, used by the static tooling hook below.
        internal static ProjectedShapePass Instance
        {
            get => instance;
            private set => instance = value;
        }

        public static bool HasSynchronousCapture => pendingCapture;

        static SynchronousTargets EnsureSynchronousTargets(int width, int height)
        {
            if (synchronousTargets != null &&
                synchronousTargets.color && synchronousTargets.color.width == width &&
                synchronousTargets.color.height == height)
                return synchronousTargets;

            ReleaseSynchronousTargets();
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = CaptureFormat,
                depthBufferBits = 0,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            synchronousTargets = new SynchronousTargets
            {
                color = new RenderTexture(descriptor)
                {
                    name = "Projection capture (tooling)",
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave
                },
                ids = new RenderTexture(descriptor)
                {
                    name = "Projection identities (tooling)",
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave
                }
            };
            synchronousTargets.color.Create();
            synchronousTargets.ids.Create();
            synchronousTargets.colorHandle = RTHandles.Alloc(synchronousTargets.color);
            synchronousTargets.idsHandle = RTHandles.Alloc(synchronousTargets.ids);
            return synchronousTargets;
        }

        static void ReleaseSynchronousTargets()
        {
            if (synchronousTargets == null)
                return;
            if (synchronousTargets.colorHandle != null)
                RTHandles.Release(synchronousTargets.colorHandle);
            if (synchronousTargets.idsHandle != null)
                RTHandles.Release(synchronousTargets.idsHandle);
            CoreUtils.Destroy(synchronousTargets.color);
            CoreUtils.Destroy(synchronousTargets.ids);
            synchronousTargets = null;
        }

        /// <summary>
        /// Reads back the capture written by the last recorded render graph and rebuilds the
        /// mesh synchronously. Editor tooling calls this after Camera.Render() returns, when
        /// the GPU has actually executed the pass.
        /// </summary>
        public static bool ResolveSynchronousCapture()
        {
            if (!pendingCapture || synchronousTargets == null)
                return false;

            pendingCapture = false;
            Camera camera = pendingCamera;
            if (camera == null)
                return false;

            Color32[] colors = ReadTexture(synchronousTargets.color, pendingWidth, pendingHeight);
            Color32[] identities = ReadTexture(synchronousTargets.ids, pendingWidth, pendingHeight);
            if (colors == null || identities == null)
            {
                return false;
            }

            LastCapturePixels = colors;
            LastCaptureWidth = pendingWidth;
            LastCaptureHeight = pendingHeight;

            ProjectedShapeBuilder.Result result;
            try
            {
                result = ProjectedShapeBuilder.Build(colors, identities, pendingWidth, pendingHeight,
                    pendingStep, pendingArea, pendingEpsilon);
            }
            catch (Exception e)
            {
                BuildFailures++;
                Debug.LogException(e);
                return false;
            }

            Instance.ApplyCaptureFeedback(camera, result, identities);
            return true;
        }

        static Color32[] ReadTexture(RenderTexture texture, int width, int height)
        {
            if (!texture)
                return null;

            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                RenderTexture.active = texture;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                copy.Apply(false, false);
                return copy.GetPixels32();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Cartoon projection synchronous readback failed: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (copy) CoreUtils.Destroy(copy);
            }
        }
#endif

        public ProjectedShapePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            requiresIntermediateTexture = true;
            captureShader = Shader.Find("Hidden/CartoonProjection/ProjectedCapture");
            var shader = Shader.Find("Hidden/CartoonProjection/ProjectedRedraw");
            if (shader) redraw = CoreUtils.CreateEngineMaterial(shader);
#if UNITY_EDITOR
            Instance = this;
#endif
        }
        public void Setup(CartoonRenderSettings value) => settings = value;

        // Object.FindObjectsByType skips objects created with HideFlags.DontSave, which is how
        // the project's generated background box (and the projection proxies) are marked. Walking
        // the loaded scenes keeps those generated renderers in the capture set.
        static Renderer[] CollectRenderers()
        {
            var found = new List<Renderer>();
            var roots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                // GetRootGameObjects clears roots. Consume each scene before reusing it.
                scene.GetRootGameObjects(roots);
                foreach (var root in roots)
                    if (root) found.AddRange(root.GetComponentsInChildren<Renderer>(true));
            }
            return found.ToArray();
        }

        /// <summary>
        /// Scene style changed: drop the deadline so every camera reshapes with the new settings.
        /// The last good image stays on screen until the replacement arrives.
        /// </summary>
        public void InvalidateSceneResults()
        {
            foreach (var state in states.Values)
                state.next = 0;
        }

        /// <summary>
        /// Something in the captured content moved (for example a character walked). The next
        /// capture is pulled forward to the configured cadence so stale silhouettes do not
        /// linger, without bypassing the update rate entirely.
        /// </summary>
        public static void HintSceneContentChanged() { unchecked { sceneContentVersion++; } }

        // A version (rather than a consumed bool) delivers notifications to every camera,
        // including cameras that still have a readback/build in flight.
        static int sceneContentVersion;

        /// <summary>Scene channel counters, separate from the character channel's.</summary>
        public readonly struct ChannelStatistics
        {
            public readonly int cameraStates;
            public readonly int buildFailures;
            public readonly int discardedResults;
            public readonly int regions, sourceEdges, simplifiedEdges, triangles, lastWidth;
            public readonly double lastBuildMilliseconds;
            public readonly string lastStatistics;

            public ChannelStatistics(int cameraStates, int failures, int discarded, int regions, int sourceEdges,
                int simplifiedEdges, int triangles, int lastWidth, double build, string statistics)
            {
                this.cameraStates = cameraStates;
                buildFailures = failures;
                discardedResults = discarded;
                this.regions = regions; this.sourceEdges = sourceEdges; this.simplifiedEdges = simplifiedEdges;
                this.triangles = triangles; this.lastWidth = lastWidth;
                lastBuildMilliseconds = build; lastStatistics = statistics;
            }
        }

        public ChannelStatistics SceneStatistics => new ChannelStatistics(
            states.Count, BuildFailures, DiscardedResults,
            Regions, SourceEdges, SimplifiedEdges, Triangles, LastProjectionWidth,
            BuildMilliseconds, LastStatistics);

        static void ReleaseState(State state)
        {
            state.disposed = true;
            CoreUtils.Destroy(state.mesh);
            CoreUtils.Destroy(state.lines);
            CoreUtils.Destroy(state.coverage);
        }

        void ApplyCompletedTask(State state)
        {
            if (state.task.IsCompletedSuccessfully)
            {
                ApplyResult(state.camera, state.task.Result, state);
            }
            else
            {
                BuildFailures++;
                Debug.LogException(state.task.Exception);
            }
            state.task = null;
            state.busy = false;
        }


        // Shared by the async and synchronous capture paths.
        void ApplyCaptureFeedback(Camera camera, ProjectedShapeBuilder.Result result, Color32[] identities)
        {
            if (!states.TryGetValue(camera.GetInstanceID(), out var state))
                return;
            state.captureIdentities = identities;
            ApplyResult(camera, result, state);
        }

        void ApplyResult(Camera camera, ProjectedShapeBuilder.Result r, State state = null)
        {
            if (state == null && !states.TryGetValue(camera.GetInstanceID(), out state))
                return;
            if (state == null)
                return;

            state.task = null;
            state.busy = false;

            bool lowLatency = settings.projectionLowLatency;
#if UNITY_EDITOR
            lowLatency &= !UseSynchronousReadback;
#endif
            if (lowLatency && (ClockMilliseconds - state.captureMilliseconds > settings.projectionMaximumResultAge * 1000 ||
                state.capturedView.IsCameraCut(ProjectionView.Capture(camera))))
            {
                DiscardedResults++;
                state.next = 0;
                return;
            }
            state.meshView = state.capturedView;
            state.meshCaptureMilliseconds = state.captureMilliseconds;
            state.meshReadbackMilliseconds = Math.Max(0, state.readbackMilliseconds - state.captureMilliseconds);
            state.meshBuildMilliseconds = r.milliseconds;
            state.meshCaptureWidth = state.captureWidth;
            state.meshWasMoving = state.captureWasMoving;
            state.meshWasInteracting = state.captureWasInteracting;
            state.meshSerial = state.serial;

            if (!state.mesh) state.mesh = new Mesh { name = "Projected 2D filled polygons", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            state.mesh.Clear(); state.mesh.vertices = r.vertices;
            // Values arrive display-referred from the capture; the redraw shader decodes
            // them for the linear target, so they are uploaded unchanged.
            state.mesh.colors32 = r.colors;
            state.mesh.triangles = r.triangles;
            if (!state.lines) state.lines = new Mesh { name = "Simplified shared contours", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            state.lines.Clear(); state.lines.vertices = r.lines;
            var indices = new int[r.lines.Length]; var colors = new Color32[r.lines.Length];
            for (int i=0;i<indices.Length;i++) { indices[i]=i; colors[i]=new Color32(25,25,30,255); }
            state.lines.colors32 = colors; state.lines.SetIndices(indices, MeshTopology.Lines, 0);

            Regions = r.regions; SourceEdges = r.sourceEdges; SimplifiedEdges = r.simplifiedEdges;
            Triangles = r.triangles.Length/3; BuildMilliseconds = r.milliseconds;
            LastMeshVertexCount = state.mesh.vertexCount;
            LastProjectionWidth = state.captureWidth;
            UpdateCoverage(state, r.regionPixels);
            LastStatistics = $"{r.regions} regions; {r.sourceEdges} → {r.simplifiedEdges} contour edges; {r.triangles.Length/3} triangles; CPU {r.milliseconds:F1} ms";
        }

        // Region pixels keep alpha 255 exactly where an object was projected. That mask is what
        // lets the redraw leave the captured background (and the frame's sky) untouched rather
        // than painting the empty projection buffer over the whole viewport.
        void UpdateCoverage(State state, Color32[] regionPixels)
        {
            int width = state.captureWidth, height = state.captureHeight;
            // regionPixels does not separate background from geometry (the palette is opaque),
            // so coverage comes from the identity buffer, whose alpha is 1 only where drawn.
            Color32[] identities = state.captureIdentities;
            bool useIdentities = identities != null && identities.Length == width * height;
            if (regionPixels == null || width <= 0 || height <= 0 || regionPixels.Length != width * height)
                return;

            int count = regionPixels.Length;
            if (state.coverageBuffer == null || state.coverageBuffer.Length != count * 4)
                state.coverageBuffer = new byte[count * 4];

            byte[] buffer = state.coverageBuffer;
            for (int i = 0; i < count; i++)
            {
                int o = i * 4;
                buffer[o] = 0;
                buffer[o + 1] = 0;
                buffer[o + 2] = 0;
                buffer[o + 3] = (byte)((useIdentities ? identities[i].a : regionPixels[i].a) > 0 ? 255 : 0);
            }

            if (state.coverage == null || state.coverage.width != width || state.coverage.height != height)
            {
                CoreUtils.Destroy(state.coverage);
                state.coverage = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Projected coverage mask",
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            state.coverage.LoadRawTextureData(buffer);
            state.coverage.Apply(false, false);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            if (!captureShader || !redraw) return;
            var camera = frame.Get<UniversalCameraData>().camera;
            var resources = frame.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            // Instance ids are recycled when a scene unloads, so the id alone is not the key:
            // a different camera that reused the id must not inherit the old capture state
            // (which could still be busy and would silently stop all further captures).
            int cameraId = camera.GetInstanceID();
            if (!states.TryGetValue(cameraId, out var state) || !ReferenceEquals(state.camera, camera))
            {
                if (state != null)
                    ReleaseState(state);
                states[cameraId] = state = new State { camera = camera };
            }
            double now = Time.realtimeSinceStartupAsDouble;
            bool fixedTooling = false;
#if UNITY_EDITOR
            fixedTooling = UseSynchronousReadback;
#endif
            state.policy.Observe(camera, now, settings, fixedTooling);
            bool contentHint = state.contentVersion != sceneContentVersion;
            if (contentHint)
            {
                // Captured content moved: pull the next capture forward to the configured cadence
                // so the previous silhouette does not linger, without ignoring the update rate.
                state.next = Math.Min(state.next, now + 1.0 / Mathf.Max(1, state.policy.UpdatesPerSecond));
            }
            if (state.task != null && state.task.IsCompleted)
            {
                ApplyCompletedTask(state);
            }
            // Camera movement and the settled detail pass must not wait for the old 8 Hz deadline.
            // Keep one in-flight job; do not accumulate a queue of stale captures.
            if (!fixedTooling && (settings.projectionLowLatency || settings.projectionAdaptiveRefresh) && !state.busy)
            {
                // Always finish the interaction with one capture from the final camera pose,
                // even when motion/detail widths are equal or the idle rate is zero.
                bool needsSettledCapture = state.policy.Idle && state.mesh &&
                    (state.meshWasInteracting || state.meshView.Changed(state.policy.Current));
                if (state.mesh && (needsSettledCapture || state.meshCaptureWidth != state.policy.Width || state.meshView.IsCameraCut(state.policy.Current)))
                    state.next = 0;
                else if (state.policy.Changed)
                    state.next = Math.Min(state.next, state.captureSeconds + 1.0 / state.policy.UpdatesPerSecond);
            }
            if (!state.busy && now >= state.next)
            {
                int width = state.policy.Width;
                int height = Mathf.Max(32, Mathf.RoundToInt(width * camera.pixelHeight / (float)Mathf.Max(1,camera.pixelWidth)));
                if(height>640) { width=Mathf.Max(32,Mathf.RoundToInt(width*640f/height)); height=640; }
                state.captureWidth = width; state.captureHeight = height;
                state.capturedView = state.policy.Current;
                state.captureMilliseconds = ClockMilliseconds;
                state.readbackMilliseconds = state.captureMilliseconds;
                state.captureSeconds = now;
                state.captureWasMoving = state.policy.Moving;
                state.captureWasInteracting = state.policy.Interacting;
                state.contentVersion = sceneContentVersion;
                if (contentHint) ContentHintRefreshes++;
                state.serial++;
                Color background = settings.backgroundMode == CartoonBackgroundMode.SolidColor ? settings.backgroundColor : camera.backgroundColor;
                var desc = new TextureDesc(width,height) { colorFormat=CaptureFormat, clearBuffer=true, clearColor=background, filterMode=FilterMode.Point, name="Original material albedo projection" };
                TextureHandle color, ids, depth;
                bool useImport=false;
#if UNITY_EDITOR
                if (UseSynchronousReadback)
                {
                    useImport=true;
                    // Editor tooling reads these outside the render graph, so they must outlive it.
                    var sync = EnsureSynchronousTargets(width, height);
                    color = graph.ImportTexture(sync.colorHandle);
                    ids = graph.ImportTexture(sync.idsHandle);
                    depth = graph.CreateTexture(new TextureDesc(width,height) { depthBufferBits=DepthBits.Depth32,clearBuffer=true,name="Projection visibility depth" });
                }
                else
                {
#endif
                color = graph.CreateTexture(desc);
                desc.name="Projected source identities"; desc.clearColor=Color.clear;
                ids=graph.CreateTexture(desc);
                depth=graph.CreateTexture(new TextureDesc(width,height) { depthBufferBits=DepthBits.Depth32,clearBuffer=true,name="Projection visibility depth" });
#if UNITY_EDITOR
                }
#endif
                // The one-second cache is only a cost saving. It must not outlive the scene it was
                // built from, or a capture right after a scene swap would draw a list of destroyed
                // renderers and silently produce an empty projection.
                int sceneCount = SceneManager.sceneCount;
                bool rendererListStale = sceneCount != rendererSceneCount;
                if (!rendererListStale)
                {
                    for (int i = 0; i < renderers.Length; i++)
                        if (!renderers[i]) { rendererListStale = true; break; }
                }
                if (now >= refresh || rendererListStale)
                {
                    rendererSceneCount = sceneCount;
                    renderers=CollectRenderers(); refresh=now+1;
                    var dead=new List<int>();
                    foreach(var entry in states)
                    {
                        if (entry.Value.camera) continue;
                        ReleaseState(entry.Value); dead.Add(entry.Key);
                    }
                    foreach(int key in dead) states.Remove(key);
                    // Capture materials are pooled per renderer; drop the ones whose source is gone.
                    var staleMaterials = new List<long>();
                    foreach (var entry in materials)
                        if (!materialOwners.TryGetValue(entry.Key, out var owner) || !owner) staleMaterials.Add(entry.Key);
                    foreach (long key in staleMaterials)
                    {
                        CoreUtils.Destroy(materials[key]);
                        materials.Remove(key);
                        materialOwners.Remove(key);
                    }
                }
                var draws = new List<Draw>();
                var planes = GeometryUtility.CalculateFrustumPlanes(camera);
                foreach(var renderer in renderers)
                {
                    if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff ||
                        (camera.cullingMask & (1 << renderer.gameObject.layer))==0 || !GeometryUtility.TestPlanesAABB(planes,renderer.bounds)) continue;
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    // Channel ownership: character and excluded objects are never baked into the
                    // scene image, so the scene channel cannot paint over art another channel owns.
                    if (CartoonRenderRegistry.Resolve(renderer).channel != CartoonRenderChannel.Scene) continue;
                    var sources=renderer.sharedMaterials;
                    for(int i=0;i<sources.Length;i++)
                    {
                        var source=sources[i]; if(!source || source.renderQueue>=3000) continue;
                        long key=((long)renderer.GetInstanceID()<<32)|(uint)i;
                        if(!materials.TryGetValue(key,out var mat)) { mat=CoreUtils.CreateEngineMaterial(captureShader); materials.Add(key,mat); materialOwners[key]=renderer; }
                        string map=source.HasProperty("_BaseMap")?"_BaseMap":source.HasProperty("_MainTex")?"_MainTex":null;
                        Texture texture=map!=null?source.GetTexture(map):null;
                        Color tint=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;
                        mat.SetTexture("_ProjectionMap",texture?texture:Texture2D.whiteTexture);
                        var scale=map!=null?source.GetTextureScale(map):Vector2.one; var offset=map!=null?source.GetTextureOffset(map):Vector2.zero;
                        mat.SetVector("_ProjectionST",new Vector4(scale.x,scale.y,offset.x,offset.y)); mat.SetColor("_ProjectionTint",tint.linear);
                        bool cut=source.IsKeywordEnabled("_ALPHATEST_ON") || source.renderQueue>=2450;
                        mat.SetFloat("_ProjectionCutoff",cut && source.HasProperty("_Cutoff")?source.GetFloat("_Cutoff"):0.001f);
                        int id=materials.Count;
                        // Stable across frames and independent of draw-list ordering.
                        if(mat.GetVector("_ProjectionId")==Vector4.zero) mat.SetVector("_ProjectionId",new Vector4((id&255)/255f,((id>>8)&255)/255f,((id>>16)&255)/255f,1));
                        draws.Add(new Draw { renderer=renderer,material=mat,submesh=i });
                    }
                }
                int sceneChannel = 0, excluded = 0, character = 0;
                foreach (var candidate in renderers)
                {
                    if (!candidate) continue;
                    var channel = CartoonRenderRegistry.Resolve(candidate).channel;
                    if (channel == CartoonRenderChannel.Scene) sceneChannel++;
                    else if (channel == CartoonRenderChannel.Character) character++;
                    else excluded++;
                }
                string summary = $"{renderers.Length} renderers (scene={sceneChannel} excluded={excluded} " +
                                 $"character={character}) -> {draws.Count} draws at {width}x{height}";
                if (summary != LastCaptureSetSummary)
                {
                    LastCaptureSetSummary = summary;
                    Debug.Log($"[CartoonProjection] capture set: {summary}");
                }
                using(var builder=graph.AddRasterRenderPass<CaptureData>("Project original material colors",out var data))
                {
                    data.draws=draws; data.width=width; data.height=height;
                    data.clear=useImport; data.clearColor=useImport?new Color(0,0,0,0):default;
                    builder.SetRenderAttachment(color,0); builder.SetRenderAttachment(ids,1); builder.SetRenderAttachmentDepth(depth,AccessFlags.Write);
                    builder.SetRenderFunc(static(CaptureData d,RasterGraphContext ctx)=> {
                        ctx.cmd.SetViewport(new Rect(0,0,d.width,d.height));
                        // Imported targets do not receive the descriptor's clear, so the
                        // tooling path clears explicitly to match the graph-created buffers.
                        if(d.clear) { ctx.cmd.ClearRenderTarget(RTClearFlags.All, d.clearColor, 1.0f, 0); }
                        foreach(var draw in d.draws) if(draw.renderer) ctx.cmd.DrawRenderer(draw.renderer,draw.material,draw.submesh,0);
                    });
                }
                state.busy=true; state.next=now+state.policy.CaptureInterval;
#if UNITY_EDITOR
                if (UseSynchronousReadback)
                {
                    // The graph is recorded before the GPU runs, so the readback happens after
                    // Camera.Render returns; see ResolveSynchronousCapture.
                    pendingCamera = camera;
                    pendingWidth = width;
                    pendingHeight = height;
                    pendingStep = settings.projectionColorStep;
                    pendingArea = settings.projectionMinimumArea;
                    pendingEpsilon = settings.projectionContourTolerance;
                    pendingCapture = true;
                }
                else
#endif
                using(var builder=graph.AddUnsafePass<ReadData>("Read projection for contour reconstruction",out var data))
                {
                    data.color=color; data.ids=ids; data.state=state; data.width=width; data.height=height;
                    float resolutionScale = width / (float)Mathf.Clamp(settings.projectionWidth, 128, 640);
                    data.step=settings.projectionColorStep;
                    data.area=Mathf.Max(1, Mathf.RoundToInt(settings.projectionMinimumArea * resolutionScale * resolutionScale));
                    data.epsilon=settings.projectionContourTolerance * resolutionScale;
                    builder.UseTexture(color,AccessFlags.Read); builder.UseTexture(ids,AccessFlags.Read); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static(ReadData d,UnsafeGraphContext ctx)=> {
                        // RenderGraph recycles pass data immediately after execution.
                        // Never retain d in asynchronous callbacks.
                        var state=d.state; int width=d.width,height=d.height,step=d.step,area=d.area;
                        float epsilon=d.epsilon;
                        Color32[] colors=null, identities=null; bool failed=false; int remaining=2;
                        void Complete() {
                            if(--remaining!=0 || state.disposed) return;
                            if(failed || colors.Length!=width*height || identities.Length!=width*height) { state.busy=false; return; }
                            state.captureIdentities=identities;
                            state.readbackMilliseconds=ClockMilliseconds;
                            state.task=Task.Run(()=>ProjectedShapeBuilder.Build(colors,identities,width,height,step,area,epsilon));
                        }
                        RTHandle c=d.color, id=d.ids;
                        ctx.cmd.RequestAsyncReadback(c.rt,0,TextureFormat.RGBA32,r=> { if(r.hasError) failed=true; else colors=r.GetData<Color32>().ToArray(); Complete(); });
                        ctx.cmd.RequestAsyncReadback(id.rt,0,TextureFormat.RGBA32,r=> { if(r.hasError) failed=true; else identities=r.GetData<Color32>().ToArray(); Complete(); });
                    });
                }
            }
            // Keep original camera image until the first complete result (never blank it).
            bool validView = fixedTooling || !settings.projectionLowLatency || !state.meshView.IsCameraCut(state.policy.Current);
            if(validView && state.mesh && state.mesh.vertexCount>0)
            using(var builder=graph.AddRasterRenderPass<RedrawData>("Redraw simplified 2D polygons",out var data))
            {
                data.mesh=state.mesh; data.lines=settings.projectionShowContours?state.lines:null; data.material=redraw;
                data.state=state;
                data.properties = state.redrawProperties;
                data.properties.SetTexture("_ProjectionPickMask", state.coverage ? state.coverage : Texture2D.whiteTexture);
                data.properties.SetMatrix("_ProjectionUvTransform", settings.projectionLowLatency && !fixedTooling ?
                    state.meshView.AlignmentTo(state.policy.Current) : Matrix4x4.identity);
                builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.Write);
                builder.SetRenderFunc(static(RedrawData d,RasterGraphContext ctx)=> {
                    ctx.cmd.DrawMesh(d.mesh,Matrix4x4.identity,d.material,0,0,d.properties);
                    if(d.lines) ctx.cmd.DrawMesh(d.lines,Matrix4x4.identity,d.material,0,0,d.properties);
                    var state = d.state;
                    if (state.publishedSerial == state.meshSerial) return;
                    state.publishedSerial = state.meshSerial;
                    LastCaptureToRedrawMilliseconds = ClockMilliseconds - state.meshCaptureMilliseconds;
                    ProjectionCompleted?.Invoke(new Timing(state.camera.GetInstanceID(), state.meshSerial, state.meshCaptureWidth,
                        state.meshWasMoving, state.meshReadbackMilliseconds, state.meshBuildMilliseconds, LastCaptureToRedrawMilliseconds));
                });
            }
        }
        public void Dispose()
        {
            foreach(var state in states.Values) { state.disposed=true; CoreUtils.Destroy(state.mesh); CoreUtils.Destroy(state.lines); CoreUtils.Destroy(state.coverage); }
            foreach(var mat in materials.Values) CoreUtils.Destroy(mat);
            states.Clear(); materials.Clear(); materialOwners.Clear(); CoreUtils.Destroy(redraw);
#if UNITY_EDITOR
            ReleaseSynchronousTargets();
            pendingCapture=false; pendingCamera=null;
#endif
        }
    }
}
