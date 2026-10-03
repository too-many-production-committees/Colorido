using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace CartoonProjection
{
    /// <summary>
    /// Drives one character's 2D projection. Created at runtime by
    /// <see cref="CartoonCharacterSystem"/> for every registered controller and never saved
    /// into a scene.
    ///
    /// The character's own sprite pixels are partitioned in local space and cached per frame,
    /// so replaying animation frames, moving, rotating or scaling the character and moving the
    /// camera all reuse the cached geometry instead of re-running a full-screen rebuild.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CartoonCharacterProjection : MonoBehaviour
    {
        const string FillName = "cartoon_character_projected";
        const string ContourName = "cartoon_character_projected_contours";

        sealed class Target
        {
            public SpriteRenderer source;
            public MeshRenderer fill;
            public MeshFilter fillFilter;
            public Mesh fillMesh;
            public MeshRenderer contours;
            public MeshFilter contourFilter;
            public Mesh contourMesh;
            public readonly MaterialPropertyBlock properties = new();
            public CartoonSpriteShape contourShape;
            public bool originalEnabled = true;
            public bool originalStateCaptured;
            public Sprite appliedSprite;
            public int appliedShapeHash;
            public int appliedColorHash = -1;
            public bool appliedContours;
            public CartoonSpriteShape shape;
        }

        sealed class Pending
        {
            public Target target;
            public Sprite sprite;
            public CartoonSpriteSampler.Geometry geometry;
            public int shapeHash;
            public Task<CartoonSpriteShape> task;
            public bool started;
        }

        readonly List<Target> targets = new();
        readonly List<SpriteRenderer> scratch = new();
        Material material;

        CartoonCharacterRenderController controller;
        CartoonCharacterSettings style = new();
        CartoonCharacterSettings sharedShape = new();
        int scannedRegistryVersion = -1;
        double nextPrepareTime;
        Pending pending;
        bool reportedError;
        bool reportedThreeDimensional;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public CartoonCharacterRenderController Controller => controller;
        public int TargetCount => targets.Count;

        /// <summary>Shape currently drawn for the first target, for tooling and tests.</summary>
        public CartoonSpriteShape CurrentShape => targets.Count > 0 ? targets[0].shape : null;

        public bool IsShowingProjected
        {
            get
            {
                foreach (var target in targets)
                    if (target.fill && target.fill.enabled)
                        return true;
                return false;
            }
        }

        internal void Initialize(CartoonCharacterRenderController owner)
        {
            controller = owner;
            // The driver is runtime-only. DontSave belongs on this component, never on the
            // character's own GameObject, which must keep saving normally.
            hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
        }

        void OnDestroy()
        {
            foreach (var target in targets)
                ReleaseTarget(target);
            targets.Clear();
            pending = null;
            if (material)
                CoreUtils.Destroy(material);
            material = null;
        }

        /// <summary>
        /// Channel switch wins over the per-character setting, so no single character can
        /// re-enable a disabled channel. Opting a character out stays possible while the
        /// channel is on.
        /// </summary>
        internal static CartoonCharacterRenderController.Mode ResolveMode(
            CartoonCharacterRenderController.Mode perCharacter, bool channelActive, bool inheritMeansProjected)
        {
            if (!channelActive)
                return CartoonCharacterRenderController.Mode.Original;
            return perCharacter switch
            {
                CartoonCharacterRenderController.Mode.Original => CartoonCharacterRenderController.Mode.Original,
                CartoonCharacterRenderController.Mode.ProjectedShapes => CartoonCharacterRenderController.Mode.ProjectedShapes,
                _ => inheritMeansProjected
                    ? CartoonCharacterRenderController.Mode.ProjectedShapes
                    : CartoonCharacterRenderController.Mode.Original
            };
        }

        internal void Tick(CartoonRenderSettings settings, CartoonCharacterSystem system, double now)
        {
            if (controller == null || !controller)
                return;

            // Editing a scene must never be mutated by a render effect: hiding a SpriteRenderer
            // would dirty the scene and could be saved. Outside play mode (and outside an
            // explicit tooling run) the character keeps its original art untouched.
            if (!system.AllowVisualChanges)
            {
                foreach (var target in targets)
                    ShowOriginal(target);
                return;
            }

            ResolveStyle(settings);
            RefreshTargets();

            var mode = ResolveMode(controller.mode, settings.CharacterChannelActive, system.InheritMeansProjected);
            if (mode != CartoonCharacterRenderController.Mode.ProjectedShapes)
            {
                if (pending != null)
                    system.AbandonPrepare(pending.started);
                pending = null;
                foreach (var target in targets)
                    ShowOriginal(target);
                return;
            }

            system.PumpPrepare(this, now);

            for (int i = 0; i < targets.Count; i++)
                TickTarget(targets[i], system, now);
        }

        void ResolveStyle(CartoonRenderSettings settings)
        {
            var shared = settings.character;
            if (shared != null)
                sharedShape.CopyShapeFrom(shared);

            style.CopyShapeFrom(sharedShape);
            if (shared != null)
            {
                style.paletteSaturation = shared.paletteSaturation;
                style.paletteBrightness = shared.paletteBrightness;
                style.showContours = shared.showContours;
                style.minimumRebuildInterval = shared.minimumRebuildInterval;
                style.cacheCapacity = shared.cacheCapacity;
                style.cachePixelBudget = shared.cachePixelBudget;
            }

            var overrideAsset = controller ? controller.styleOverride : null;
            if (overrideAsset != null)
                overrideAsset.ApplyTo(style);
        }

        /// <summary>
        /// Re-scans only when the registry says something changed, so runtime-spawned children,
        /// swapped costumes and pooled characters are picked up without a periodic scan.
        /// </summary>
        void RefreshTargets()
        {
            bool needsScan = scannedRegistryVersion != CartoonRenderRegistry.Version;
            if (!needsScan)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!targets[i].source)
                    {
                        needsScan = true;
                        break;
                    }
                }
            }
            if (!needsScan)
                return;

            scannedRegistryVersion = CartoonRenderRegistry.Version;
            scratch.Clear();
            GetComponentsInChildren(true, scratch);

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (!targets[i].source || !scratch.Contains(targets[i].source))
                {
                    ReleaseTarget(targets[i]);
                    targets.RemoveAt(i);
                }
            }

            foreach (var source in scratch)
            {
                if (!source)
                    continue;
                // Only this character's own renderers: a nested character keeps its own driver.
                if (CartoonRenderRegistry.Resolve(source).controller != controller)
                    continue;
                if (FindTarget(source) != null)
                    continue;
                targets.Add(new Target { source = source });
            }

            ReportUnsupportedRenderers();
            targets.Sort((a, b) =>
            {
                int layer = a.source.sortingLayerID.CompareTo(b.source.sortingLayerID);
                return layer != 0 ? layer : a.source.sortingOrder.CompareTo(b.source.sortingOrder);
            });
        }

        Target FindTarget(SpriteRenderer source)
        {
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].source == source)
                    return targets[i];
            return null;
        }

        void TickTarget(Target target, CartoonCharacterSystem system, double now)
        {
            var source = target.source;
            if (!source)
                return;

            var sprite = source.sprite;
            // An empty frame is genuinely empty: hide rather than leave a stale silhouette.
            if (sprite == null)
            {
                HideProjected(target);
                RestoreOriginal(target);
                return;
            }

            int shapeHash = style.ShapeHash();

            if (target.shape != null && target.appliedSprite == sprite && target.appliedShapeHash == shapeHash)
            {
                ShowProjected(target);
                RefreshDrawState(target);
                return;
            }

            if (system.Cache.TryGet(sprite, shapeHash, now, out var cached))
            {
                Adopt(target, cached, sprite, shapeHash);
                ShowProjected(target);
                RefreshDrawState(target);
                return;
            }

            // Cache miss: keep original art on screen until the background rebuild lands.
            ShowOriginal(target);
            RequestPrepare(target, sprite, shapeHash, system, now);
        }

        void Adopt(Target target, CartoonSpriteShape shape, Sprite sprite, int shapeHash)
        {
            EnsureRenderers(target);
            target.fillMesh = ToMesh(target.fillMesh, shape, false);
            // Contours are optional. Do not allocate and upload their mesh on every
            // animation frame while the feature is off; prepare lazily when enabled.
            if (style.showContours)
                UpdateContourMesh(target, shape);
            if (target.fillFilter)
                target.fillFilter.sharedMesh = target.fillMesh;
            if (target.contourFilter)
                target.contourFilter.sharedMesh = target.contourMesh;
            target.shape = shape;
            target.appliedSprite = sprite;
            target.appliedShapeHash = shapeHash;
            target.appliedColorHash = -1;
        }

        void UpdateContourMesh(Target target, CartoonSpriteShape shape)
        {
            if (target.contourShape == shape && target.contourMesh)
                return;
            target.contourMesh = ToMesh(target.contourMesh, shape, true);
            target.contourShape = shape;
            if (target.contourFilter)
                target.contourFilter.sharedMesh = target.contourMesh;
        }

        Mesh ToMesh(Mesh mesh, CartoonSpriteShape shape, bool contours)
        {
            if (mesh == null)
                mesh = new Mesh
                {
                    name = contours ? "Character contours" : "Character shape",
                    hideFlags = HideFlags.HideAndDontSave
                };

            mesh.Clear();
            if (contours)
            {
                var vertices = shape.contourVertices;
                mesh.vertices = vertices;
                var colors = new Color32[vertices.Length];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = new Color32(25, 25, 30, 255);
                mesh.colors32 = colors;
                var indices = new int[vertices.Length];
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = i;
                mesh.SetIndices(indices, MeshTopology.Lines, 0);
            }
            else
            {
                mesh.vertices = shape.vertices;
                mesh.uv = shape.uv;
                mesh.colors32 = StyledColors(shape);
                mesh.triangles = shape.triangles;
            }
            return mesh;
        }

        /// <summary>
        /// Colour-only restyle. Saturation/brightness never move geometry, so these redraw the
        /// vertex colours of the cached mesh instead of forcing a rebuild.
        /// </summary>
        Color32[] StyledColors(CartoonSpriteShape shape)
        {
            var colors = new Color32[shape.vertexLabel.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                Color32 c = shape.palette != null && shape.palette.Length > 0 &&
                            shape.vertexLabel[i] >= 0 && shape.vertexLabel[i] < shape.palette.Length
                    ? shape.palette[shape.vertexLabel[i]]
                    : shape.colors[i];
                colors[i] = AdjustColor(c, style.paletteSaturation, style.paletteBrightness);
            }
            return colors;
        }

        static Color32 AdjustColor(Color32 color, float saturation, float brightness)
        {
            if (Mathf.Approximately(saturation, 1f) && Mathf.Approximately(brightness, 1f))
                return color;

            Color.RGBToHSV(new Color(color.r / 255f, color.g / 255f, color.b / 255f), out float h, out float s, out float v);
            Color styled = Color.HSVToRGB(h, Mathf.Clamp01(s * saturation), Mathf.Clamp01(v * brightness));
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(styled.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(styled.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(styled.b * 255f), 0, 255),
                color.a);
        }

        void RefreshDrawState(Target target)
        {
            if (target.shape == null)
                return;
            EnsureRenderers(target);
            if (style.showContours)
                UpdateContourMesh(target, target.shape);
            if (target.fillMesh != null)
            {
                int colorHash = ColorHash();
                if (target.appliedColorHash != colorHash)
                {
                    target.fillMesh.colors32 = StyledColors(target.shape);
                    target.appliedColorHash = colorHash;
                }
            }
            if (target.contours && target.appliedContours != style.showContours)
            {
                target.contours.enabled = style.showContours;
                target.appliedContours = style.showContours;
            }
        }

        int ColorHash()
        {
            unchecked
            {
                return (style.paletteSaturation.GetHashCode() * 397) ^ style.paletteBrightness.GetHashCode();
            }
        }

        // ----------------------------------------------------------- preparing

        void RequestPrepare(Target target, Sprite sprite, int shapeHash, CartoonCharacterSystem system, double now)
        {
            if (pending != null || now < nextPrepareTime)
                return;
            if (!system.TryBeginPrepare())
                return;

            nextPrepareTime = now + Mathf.Max(0f, style.minimumRebuildInterval);
            var request = new Pending
            {
                target = target,
                sprite = sprite,
                shapeHash = shapeHash,
                geometry = CartoonSpriteSampler.ReadGeometry(sprite)
            };

            bool needGpuRead = !system.SynchronousSampling && sprite.texture && !sprite.texture.isReadable;
            if (!needGpuRead)
            {
                var sample = CartoonSpriteSampler.Read(sprite, style.maximumSampleSize);
                if (!sample.success)
                {
                    system.NotePrepareFailed(sample.error);
                    ReportOnce(sample.error);
                    system.EndPrepare();
                    return;
                }
                pending = request;
                request.task = StartBuild(sprite, CartoonSpriteSampler.ReadGeometry(sprite), sample);
                request.started = true;
                system.NotePrepareStarted();
                return;
            }

            // Non-blocking GPU read for textures that are not CPU readable.
            pending = request;
            CartoonSpriteSampler.Request(sprite, style.maximumSampleSize, result =>
            {
                if (pending != request)
                {
                    // The request was abandoned while the GPU was reading; drop the result.
                    system.EndPrepare();
                    return;
                }
                if (!result.success)
                {
                    system.NotePrepareFailed(result.error);
                    ReportOnce(result.error);
                    system.EndPrepare();
                    pending = null;
                    return;
                }
                request.task = StartBuild(sprite, request.geometry, result);
                request.started = true;
                system.NotePrepareStarted();
            });
        }

        Task<CartoonSpriteShape> StartBuild(Sprite sprite, CartoonSpriteSampler.Geometry geometry,
            CartoonSpriteSampler.Sample sample)
        {
            var localStyle = new CartoonCharacterSettings();
            localStyle.CopyShapeFrom(style);
            // The partition/contour algorithm is the expensive part; keep it off the main thread
            // exactly like the scene channel does.
            return Task.Run(() => CartoonSpriteShapeFactory.Build(sprite, geometry, sample, localStyle));
        }

        internal void PumpPrepare(CartoonCharacterSystem system, double now)
        {
            var request = pending;
            if (request == null || request.task == null || !request.task.IsCompleted)
                return;

            pending = null;
            system.EndPrepare();

            if (request.task.IsFaulted)
            {
                system.NotePrepareFailed(request.task.Exception?.GetBaseException().Message);
                Debug.LogException(request.task.Exception);
                return;
            }

            var shape = request.task.Result;
            if (shape == null)
                return;

            system.Cache.Store(request.sprite, request.shapeHash, shape, now,
                style.cacheCapacity, style.cachePixelBudget);
            system.Cache.Trim(now, style.cacheCapacity, style.cachePixelBudget);
            system.NotePrepareCompleted(shape);
        }

        // ------------------------------------------------------- visual switch

        void EnsureRenderers(Target target)
        {
            if (!target.source)
                return;

            if (target.fillFilter == null)
            {
                var node = new GameObject(FillName) { hideFlags = HideFlags.DontSave };
                node.transform.SetParent(target.source.transform, false);
                node.layer = target.source.gameObject.layer;
                // The generated renderer must never be captured by the scene channel again.
                node.AddComponent<CartoonRenderClassifier>().mode = CartoonRenderClassifier.Mode.Exclude;
                target.fillFilter = node.AddComponent<MeshFilter>();
                target.fill = node.AddComponent<MeshRenderer>();
                target.fill.sharedMaterial = Material();
                target.fill.enabled = false;
            }

            SyncRenderState(target.fill, target.source, target.properties);

            if (style.showContours && target.contourFilter == null)
            {
                var node = new GameObject(ContourName) { hideFlags = HideFlags.DontSave };
                node.transform.SetParent(target.source.transform, false);
                node.layer = target.source.gameObject.layer;
                node.AddComponent<CartoonRenderClassifier>().mode = CartoonRenderClassifier.Mode.Exclude;
                target.contourFilter = node.AddComponent<MeshFilter>();
                target.contours = node.AddComponent<MeshRenderer>();
                target.contours.sharedMaterial = Material();
                target.contours.enabled = false;
            }

            if (target.contours)
                SyncRenderState(target.contours, target.source, target.properties);
        }

        void SyncRenderState(MeshRenderer renderer, SpriteRenderer source, MaterialPropertyBlock block)
        {
            if (!renderer || !source)
                return;

            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;

            // Flip is a SpriteRenderer property, not a transform change, so the projected node
            // mirrors it explicitly to keep the silhouette aligned with the original.
            var flip = new Vector3(source.flipX ? -1f : 1f, source.flipY ? -1f : 1f, 1f);
            if (renderer.transform.localScale != flip)
                renderer.transform.localScale = flip;

            block.SetTexture(MainTexId, source.sprite ? source.sprite.texture : Texture2D.whiteTexture);
            block.SetColor(ColorId, source.color);
            renderer.SetPropertyBlock(block);
        }

        void ShowProjected(Target target)
        {
            CaptureOriginalState(target);
            if (target.source.enabled)
                target.source.enabled = false;
            if (target.fill)
                target.fill.enabled = true;
            if (target.contours)
                target.contours.enabled = style.showContours;
            target.appliedContours = style.showContours;
        }

        void ShowOriginal(Target target)
        {
            RestoreOriginal(target);
            HideProjected(target);
        }

        void HideProjected(Target target)
        {
            if (target.fill)
                target.fill.enabled = false;
            if (target.contours)
                target.contours.enabled = false;
        }

        void CaptureOriginalState(Target target)
        {
            if (target.originalStateCaptured || !target.source)
                return;
            target.originalEnabled = target.source.enabled;
            target.originalStateCaptured = true;
        }

        /// <summary>
        /// Restores the renderer's own enabled flag exactly as it was. The Animator and gameplay
        /// components are never touched, so animation keeps driving the sprite while hidden.
        /// </summary>
        void RestoreOriginal(Target target)
        {
            if (target.source && target.originalStateCaptured)
                target.source.enabled = target.originalEnabled;
        }

        void ReleaseTarget(Target target)
        {
            RestoreOriginal(target);
            if (target.fill)
                CoreUtils.Destroy(target.fill.gameObject);
            if (target.contours)
                CoreUtils.Destroy(target.contours.gameObject);
            if (target.fillMesh)
                CoreUtils.Destroy(target.fillMesh);
            if (target.contourMesh)
                CoreUtils.Destroy(target.contourMesh);
            target.shape = null;
            target.fill = null;
            target.fillFilter = null;
            target.contours = null;
            target.contourFilter = null;
            target.fillMesh = null;
            target.contourMesh = null;
        }

        Material Material()
        {
            if (material)
                return material;
            var shader = Shader.Find("Hidden/CartoonProjection/ProjectedCharacter");
            if (shader == null)
            {
                Debug.LogWarning("Cartoon Projection: Hidden/CartoonProjection/ProjectedCharacter shader missing.");
                return null;
            }
            material = CoreUtils.CreateEngineMaterial(shader);
            material.name = "Cartoon character projection";
            return material;
        }

        /// <summary>
        /// 3D character meshes are not rebuilt in this phase. They keep their original material
        /// and are reported once rather than silently claimed as supported.
        /// </summary>
        void ReportUnsupportedRenderers()
        {
            if (reportedThreeDimensional)
                return;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SpriteRenderer || !renderer)
                    continue;
                if (renderer is MeshRenderer && (renderer.gameObject.name == FillName || renderer.gameObject.name == ContourName))
                    continue;
                if (CartoonRenderRegistry.Resolve(renderer).controller != controller)
                    continue;
                if (renderer is SkinnedMeshRenderer || renderer is MeshRenderer)
                {
                    reportedThreeDimensional = true;
                    Debug.Log($"Cartoon Projection: character '{name}' has 3D renderers. The character " +
                              "channel rebuilds 2D sprites in this phase, so those meshes keep their " +
                              "original material instead of being rebuilt.");
                    return;
                }
            }
        }

        void ReportOnce(string error)
        {
            if (reportedError || string.IsNullOrEmpty(error))
                return;
            reportedError = true;
            Debug.LogWarning($"Cartoon Projection: character '{name}' cannot be projected ({error}); keeping original art.");
        }
    }
}
