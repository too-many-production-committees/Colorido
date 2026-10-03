using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    [DisallowMultipleRendererFeature("Cartoon Projection Renderer")]
    public sealed class CartoonRendererFeature : ScriptableRendererFeature
    {
        public CartoonRenderSettings settings = new();

        private Material captureMaterial;
        private Material paintHistoryMaterial;
        private Material simplifyMaterial;
        private Material lightingMaterial;
        private Material outlineMaterial;
        private Material compositeMaterial;
        private CartoonCapturePass capturePass;
        private CartoonPaintHistoryPass paintHistoryPass;
        private CartoonSimplifyPass simplifyPass;
        private CartoonLightingPass lightingPass;
        private CartoonOutlinePass outlinePass;
        private CartoonCompositePass compositePass;
        private CartoonPaintHistory paintHistory;
        private ProjectedShapePass projectedPass;
        private CartoonCharacterSystem characterSystem;
        private bool legacyUnavailableReported;
        private int sceneStyleHash = int.MinValue;
        private int characterShapeHash = int.MinValue;
        private int pumpedFrame = -1;
        private double pumpedTime = -1;

        /// <summary>Scene channel: captured materials rebuilt as 2D colour regions.</summary>
        public ProjectedShapePass SceneChannel => projectedPass;

        /// <summary>Character channel: sprites and characters rebuilt with their own style.</summary>
        public CartoonCharacterSystem CharacterChannel => characterSystem;

        public override void Create()
        {
            // Create() recreates the scene channel only, which is the contract existing tooling
            // relies on when it wants a fresh scene capture.
            //
            // The character channel is deliberately NOT rebuilt here: its sprite cache and its
            // runtime drivers survive, so a scene-channel reset can never discard character work
            // and no single settings object can reset both channels at once. Call
            // ResetChannelState() when a full reset of both channels is actually wanted.
            projectedPass?.Dispose();
            projectedPass = new ProjectedShapePass();
            characterSystem ??= new CartoonCharacterSystem();

            DisposeMaterials();
            paintHistory?.Dispose();
            paintHistory = null;

            if (settings.projectedShapes)
                return;

            CreateLegacyResources();
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            if (cameraData.cameraType == CameraType.Preview)
                return;

            // Tick can create renderers, rebuild meshes and switch the original sprite off.
            // URP calls AddRenderPasses AFTER camera and shadow culling: changing those
            // objects there invalidates the native culling results (and can crash Submit).
            // Run even with Master off, so existing projections restore their source sprites.
            PumpChannels();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!settings.enabled || renderingData.cameraData.cameraType == CameraType.Preview)
                return;

            if (settings.projectedShapes)
            {
                if (projectedPass == null)
                    return;
                // The scene channel is an independent switch: turning it off keeps the original
                // URP frame without disturbing the character channel.
                if (!settings.SceneChannelActive)
                    return;
                projectedPass.Setup(settings);
                renderer.EnqueuePass(projectedPass);
                return;
            }

            if (capturePass == null)
            {
                // Create() skipped the legacy resources; build them on first use so that
                // switching the mode at runtime does not require a domain reload.
                CreateLegacyResources();
                if (capturePass == null)
                {
                    if (!legacyUnavailableReported)
                    {
                        legacyUnavailableReported = true;
                        Debug.LogWarning(
                            "Cartoon Projection: legacy v1 shaders are unavailable; " +
                            "keep 'Projected 2D Shapes' enabled or restore the missing shaders.");
                    }
                    return;
                }
            }

            capturePass.Setup(settings);
            simplifyPass.Setup(settings);
            lightingPass.Setup(settings);
            outlinePass.Setup(settings);
            compositePass.Setup(settings);
            renderer.EnqueuePass(capturePass);
            renderer.EnqueuePass(paintHistoryPass);
            renderer.EnqueuePass(simplifyPass);
            renderer.EnqueuePass(lightingPass);
            renderer.EnqueuePass(outlinePass);
            renderer.EnqueuePass(compositePass);
        }

        /// <summary>
        /// Drives the character channel and reacts to style changes. Runs once per frame even
        /// when the scene pass is disabled, so a character still animates and rebuilds while the
        /// scene channel is off, and vice versa.
        /// </summary>
        private void PumpChannels()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            // Batch tooling renders through Camera.Render without advancing frameCount, so a
            // real-time gap is used as the fallback trigger.
            if (Time.frameCount == pumpedFrame && now - pumpedTime < 0.010)
                return;
            pumpedFrame = Time.frameCount;
            pumpedTime = now;

            if (characterSystem != null)
            {
                // The tooling switch belongs to the scene pass; the character channel mirrors it
                // so batch captures sample sprites synchronously as well.
#if UNITY_EDITOR
                characterSystem.SynchronousSampling = ProjectedShapePass.UseSynchronousReadback;
#else
                characterSystem.SynchronousSampling = false;
#endif
                characterSystem.Tick(settings, now);
            }

            int sceneHash = SceneStyleHash(settings);
            if (sceneHash != sceneStyleHash)
            {
                sceneStyleHash = sceneHash;
                // Only the scene channel reacts: a character parameter change recomputes the
                // same scene hash and therefore leaves completed scene results alone.
                projectedPass?.InvalidateSceneResults();
            }

            var character = settings.character;
            int shapeHash = character?.ShapeHash() ?? 0;
            if (shapeHash != characterShapeHash)
            {
                characterShapeHash = shapeHash;
                // Colour-only parameters are excluded from the hash, so palette tweaks reuse
                // cached geometry instead of rebuilding it.
                characterSystem?.InvalidateShapes();
            }
        }

        /// <summary>Everything that changes the scene image or its geometry. Character fields are absent by design.</summary>
        private static int SceneStyleHash(CartoonRenderSettings value)
        {
            if (value == null)
                return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + value.projectionWidth;
                hash = hash * 31 + value.projectionUpdatesPerSecond;
                hash = hash * 31 + (value.projectionAdaptiveRefresh ? 1 : 0);
                hash = hash * 31 + value.projectionIdleUpdatesPerSecond.GetHashCode();
                hash = hash * 31 + (value.projectionLowLatency ? 1 : 0);
                hash = hash * 31 + value.projectionMotionWidth;
                hash = hash * 31 + value.projectionMotionUpdatesPerSecond;
                hash = hash * 31 + value.projectionSettleSeconds.GetHashCode();
                hash = hash * 31 + value.projectionMaximumResultAge.GetHashCode();
                hash = hash * 31 + value.projectionColorStep;
                hash = hash * 31 + value.projectionMinimumArea;
                hash = hash * 31 + value.projectionContourTolerance.GetHashCode();
                hash = hash * 31 + (int)value.backgroundMode;
                hash = hash * 31 + value.backgroundColor.GetHashCode();
                hash = hash * 31 + (int)value.surfaceMode;
                return hash;
            }
        }

        /// <summary>
        /// Explicit full reset for tooling. Never called from Create(), so ordinary enable/reload
        /// cycles keep both channels' caches and results.
        /// </summary>
        public void ResetChannelState()
        {
            projectedPass?.Dispose();
            projectedPass = new ProjectedShapePass();
            characterSystem?.Dispose();
            characterSystem = new CartoonCharacterSystem();
            sceneStyleHash = int.MinValue;
            characterShapeHash = int.MinValue;
            CartoonRenderRegistry.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            DisposeMaterials();
            projectedPass?.Dispose();
            projectedPass = null;
            characterSystem?.Dispose();
            characterSystem = null;
            paintHistory?.Dispose();
            paintHistory = null;
        }

        private void CreateLegacyResources()
        {
            captureMaterial = CreateMaterial("Hidden/CartoonProjection/Capture");
            paintHistoryMaterial = CreateMaterial("Hidden/CartoonProjection/PaintHistory");
            simplifyMaterial = CreateMaterial("Hidden/CartoonProjection/Simplify");
            lightingMaterial = CreateMaterial("Hidden/CartoonProjection/Lighting");
            outlineMaterial = CreateMaterial("Hidden/CartoonProjection/Outline");
            compositeMaterial = CreateMaterial("Hidden/CartoonProjection/Composite");
            if (captureMaterial == null || paintHistoryMaterial == null || simplifyMaterial == null ||
                lightingMaterial == null || outlineMaterial == null || compositeMaterial == null)
                return;
            paintHistory = new CartoonPaintHistory();
            capturePass = new CartoonCapturePass(captureMaterial);
            capturePass.SetPaintHistory(paintHistory);
            paintHistoryPass = new CartoonPaintHistoryPass(paintHistoryMaterial);
            simplifyPass = new CartoonSimplifyPass(simplifyMaterial);
            lightingPass = new CartoonLightingPass(lightingMaterial);
            outlinePass = new CartoonOutlinePass(outlineMaterial);
            compositePass = new CartoonCompositePass(compositeMaterial);
        }

        private static Material CreateMaterial(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Cartoon Projection shader not found: {shaderName}");
                return null;
            }
            return CoreUtils.CreateEngineMaterial(shader);
        }

        private void DisposeMaterials()
        {
            CoreUtils.Destroy(captureMaterial);
            CoreUtils.Destroy(paintHistoryMaterial);
            CoreUtils.Destroy(simplifyMaterial);
            CoreUtils.Destroy(lightingMaterial);
            CoreUtils.Destroy(outlineMaterial);
            CoreUtils.Destroy(compositeMaterial);
            captureMaterial = null;
            paintHistoryMaterial = null;
            simplifyMaterial = null;
            lightingMaterial = null;
            outlineMaterial = null;
            compositeMaterial = null;
            capturePass = null;
            paintHistoryPass = null;
            simplifyPass = null;
            lightingPass = null;
            outlinePass = null;
            compositePass = null;
        }
    }
}
