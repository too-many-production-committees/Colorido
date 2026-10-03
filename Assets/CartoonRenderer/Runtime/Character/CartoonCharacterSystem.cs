using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CartoonProjection
{
    /// <summary>
    /// Owns the character channel's shared state: the sprite shape cache, the per-frame prepare
    /// budget and the channel statistics. One system is created by the renderer feature and can
    /// be rebuilt without touching the scene channel.
    /// </summary>
    public sealed class CartoonCharacterSystem
    {
        public sealed class Statistics
        {
            public int trackedCharacters;
            public int projectedCharacters;
            public int preparesStarted;
            public int preparesCompleted;
            public int preparesFailed;
            public long cacheHits;
            public long cacheMisses;
            public long cacheEvictions;
            public int cacheEntries;
            public int cachePixels;
            public double hitRate;
            public double lastBuildMilliseconds;
            public double totalBuildMilliseconds;
            public int regions;
            public int sourceEdges;
            public int simplifiedEdges;
            public int triangles;
            public string lastError;
            public string lastControllerRefresh;

            public void Reset()
            {
                preparesStarted = preparesCompleted = preparesFailed = 0;
                lastBuildMilliseconds = totalBuildMilliseconds = 0;
                regions = sourceEdges = simplifiedEdges = triangles = 0;
                lastError = null;
                cacheHits = cacheMisses = cacheEvictions = 0;
            }

            public override string ToString() =>
                $"characters={projectedCharacters}/{trackedCharacters} prepares={preparesCompleted}/{preparesStarted} " +
                $"failed={preparesFailed} cache={cacheEntries} entries/{cachePixels} px hitRate={hitRate:P1} " +
                $"evictions={cacheEvictions} buildMs={lastBuildMilliseconds:F1}";
        }

        readonly Dictionary<int, CartoonCharacterProjection> projections = new();
        readonly List<int> deadProjections = new();
        readonly List<CartoonCharacterRenderController> controllers = new();
        readonly List<int> deadPose = new();

        readonly Dictionary<int, int> lastPose = new();

        int preparesThisFrame;
        int budgetFrame = -1;
        int activePrepares;
        int lastControllersVersion = -1;
        int lastSceneCount = -1;
        double lastControllerSweep = -1;
        double budgetWindowStart = -1;

        /// <summary>Real-time stand-in for a frame while nothing advances Time.frameCount.</summary>
        const double BudgetWindowSeconds = 0.016;

        public bool logActivity;

        public CartoonSpriteShapeCache Cache { get; } = new CartoonSpriteShapeCache();

        /// <summary>
        /// Runtime driver for a controller. The driver component is marked DontSave, so a scene
        /// scan cannot find it; this dictionary lookup is the reliable accessor.
        /// </summary>
        public CartoonCharacterProjection FindProjection(CartoonCharacterRenderController controller)
        {
            if (controller == null)
                return null;
            return projections.TryGetValue(controller.GetInstanceID(), out var projection) ? projection : null;
        }

        public CartoonSpriteShape FindShape(CartoonCharacterRenderController controller) =>
            FindProjection(controller)?.CurrentShape;

        /// <summary>Sum of every projection's own counters, for reporting.</summary>
        public int ProjectedCharacterCount
        {
            get
            {
                int count = 0;
                foreach (var pair in projections)
                    if (pair.Value && pair.Value.IsShowingProjected)
                        count++;
                return count;
            }
        }
        public Statistics Stats { get; } = new Statistics();

        /// <summary>Follows ProjectedShapePass's tooling switch so batch tests sample synchronously too.</summary>
        public bool SynchronousSampling { get; set; }

        /// <summary>Inherit means "projected" while the channel is on; per-character Original still opts out.</summary>
        public bool InheritMeansProjected { get; set; } = true;

        /// <summary>
        /// The character channel swaps a live SpriteRenderer for its rebuilt shape. That is only
        /// allowed while the game is running or when a tool explicitly asks for it, so opening a
        /// scene in the editor never hides a character or marks it dirty.
        /// </summary>
        public bool AllowVisualChanges => AllowVisualChangesOverride || Application.isPlaying;

        /// <summary>Set by acceptance and validation tooling that drives rendering outside play mode.</summary>
        public static bool AllowVisualChangesOverride;

        public int PrepareCountThisFrame => preparesThisFrame;
        public int ActivePrepares => activePrepares;

        internal bool TryBeginPrepare()
        {
            if (preparesThisFrame >= MaxPreparesPerFrame)
                return false;
            preparesThisFrame++;
            return true;
        }

        internal int MaxPreparesPerFrame { get; set; } = 2;

        internal void NotePrepareStarted()
        {
            Stats.preparesStarted++;
            activePrepares++;
        }

        internal void EndPrepare()
        {
            if (activePrepares > 0)
                activePrepares--;
        }

        internal void AbandonPrepare(bool started)
        {
            if (!started)
                return;
            // The task keeps running; only the slot is released so the channel never stalls.
            if (activePrepares > 0)
                activePrepares--;
        }

        internal void NotePrepareCompleted(CartoonSpriteShape shape)
        {
            Stats.preparesCompleted++;
            if (shape == null)
                return;
            Stats.lastBuildMilliseconds = shape.buildMilliseconds;
            Stats.totalBuildMilliseconds += shape.buildMilliseconds;
            Stats.regions = shape.regions;
            Stats.sourceEdges = shape.sourceEdges;
            Stats.simplifiedEdges = shape.simplifiedEdges;
            Stats.triangles = shape.TriangleCount;
        }

        internal void NotePrepareFailed(string error)
        {
            Stats.preparesFailed++;
            Stats.lastError = error;
            if (activePrepares > 0)
                activePrepares--;
        }

        internal void NoteCacheHit() => Stats.cacheHits++;
        internal void NoteCacheMiss() => Stats.cacheMisses++;

        internal void PumpPrepare(CartoonCharacterProjection projection, double now)
        {
            projection.PumpPrepare(this, now);
        }

        /// <summary>
        /// Adds or removes the runtime driver for every registered controller. Controllers come
        /// from the registry's live list, so scene loads, unloads and pools are handled without
        /// a periodic full scan.
        /// </summary>
        public void SyncControllers(CartoonRenderSettings settings)
        {
            // The registry is the fast, event-driven path. It is merged with a scene sweep so a
            // missed OnEnable (domain-reload timing, an object activated in the same frame, a
            // DontSave hierarchy) cannot silently leave a character untracked.
            int sceneCount = SceneManager.sceneCount;
            // The sweep is the safety net, not the primary path: it also runs on a short timer so
            // a registration missed by OnEnable timing cannot strand a character indefinitely.
            if (lastControllersVersion != CartoonRenderRegistry.ControllersVersion ||
                lastSceneCount != sceneCount || controllers.Count == 0 ||
                Time.realtimeSinceStartupAsDouble - lastControllerSweep > 0.5)
            {
                lastControllerSweep = Time.realtimeSinceStartupAsDouble;
                lastControllersVersion = CartoonRenderRegistry.ControllersVersion;
                lastSceneCount = sceneCount;
                RefreshControllerList();
            }

            for (int i = controllers.Count - 1; i >= 0; i--)
            {
                var controller = controllers[i];
                if (!controller)
                {
                    controllers.RemoveAt(i);
                    continue;
                }

                int id = controller.GetInstanceID();
                if (!projections.TryGetValue(id, out var projection) || !projection)
                {
                    projection = controller.gameObject.GetComponent<CartoonCharacterProjection>();
                    if (projection == null)
                        projection = controller.gameObject.AddComponent<CartoonCharacterProjection>();
                    projection.Initialize(controller);
                    projections[id] = projection;
                }
            }

            deadProjections.Clear();
            foreach (var pair in projections)
            {
                if (pair.Value)
                    continue;
                deadProjections.Add(pair.Key);
            }
            foreach (int id in deadProjections)
                projections.Remove(id);

            Stats.trackedCharacters = projections.Count;

            if (settings?.character != null)
                MaxPreparesPerFrame = Mathf.Clamp(settings.character.preparesPerFrame, 1, 4);
        }

        void RefreshControllerList()
        {
            // Scene loads recycle instance ids and leave dead renderers behind; the same periodic
            // refresh that finds controllers keeps the classification cache bounded.
            CartoonRenderRegistry.PruneDestroyed();
            controllers.Clear();
            controllers.AddRange(CartoonRenderRegistry.Controllers);

            // Sweep loaded scenes (including inactive objects) and add anything the registry
            // missed. Only runs when the registry, the scene set or the list actually changed.
            var found = UnityEngine.Object.FindObjectsByType<CartoonCharacterRenderController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var controller in found)
            {
                if (controller && !controllers.Contains(controller))
                    controllers.Add(controller);
            }

            if (logActivity || controllers.Count != found.Length)
                Stats.lastControllerRefresh = $"registry={CartoonRenderRegistry.Controllers.Count} " +
                                              $"scene={found.Length} merged={controllers.Count}";
        }

        public void Tick(CartoonRenderSettings settings, double now)
        {
            if (settings == null)
                return;

            SyncControllers(settings);

            // The prepare budget is per frame. Batch tooling drives Camera.Render directly and
            // never advances frameCount, so the budget is also released after a frame's worth of
            // real time; without that window the budget would never reset outside play mode.
            int frame = Time.frameCount;
            bool newFrame = frame != budgetFrame;
            bool newWindow = budgetWindowStart < 0 || now - budgetWindowStart >= BudgetWindowSeconds;
            if (newFrame || newWindow)
            {
                budgetFrame = frame;
                budgetWindowStart = now;
                preparesThisFrame = 0;
            }

            int projected = 0;
            bool poseChanged = false;
            foreach (var pair in projections)
            {
                var projection = pair.Value;
                if (!projection)
                    continue;
                projection.Tick(settings, this, now);
                if (!projection.IsShowingProjected)
                    continue;
                projected++;

                // The scene capture excludes character objects, so a character that moves leaves
                // an un-rebuilt region where it used to be. Pulling the next scene capture forward
                // heals it at the configured cadence instead of waiting for the idle refresh.
                int pose = PoseHash(projection.transform);
                if (lastPose.TryGetValue(pair.Key, out int previous) && previous != pose)
                    poseChanged = true;
                lastPose[pair.Key] = pose;
            }
            if (poseChanged)
                ProjectedShapePass.HintSceneContentChanged();
            if (lastPose.Count > projections.Count)
            {
                deadPose.Clear();
                foreach (var key in lastPose.Keys)
                    if (!projections.ContainsKey(key))
                        deadPose.Add(key);
                foreach (int key in deadPose)
                    lastPose.Remove(key);
            }
            Stats.projectedCharacters = projected;
            Stats.cacheEntries = Cache.Count;
            Stats.cachePixels = Cache.Pixels;
            Stats.cacheHits = Cache.Hits;
            Stats.cacheMisses = Cache.Misses;
            Stats.cacheEvictions = Cache.Evictions;
            Stats.hitRate = Cache.HitRate;
        }

        /// <summary>
        /// Drops every cached shape. Called when character style changes, never when the scene
        /// channel changes.
        /// </summary>
        public void InvalidateShapes()
        {
            Cache.Clear();
        }

        public void Dispose()
        {
            foreach (var pair in projections)
            {
                if (pair.Value)
                    CoreUtils.Destroy(pair.Value);
            }
            projections.Clear();
            Cache.Clear();
        }

        static int PoseHash(Transform transform)
        {
            unchecked
            {
                Vector3 position = transform.position;
                Vector3 lossy = transform.lossyScale;
                return position.GetHashCode() * 397 ^ lossy.GetHashCode();
            }
        }

        public void ResetStatistics()
        {
            Stats.Reset();
            Cache.ResetStatistics();
        }
    }
}
