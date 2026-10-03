using System;
using UnityEngine;

namespace CartoonProjection
{
    public enum CartoonDebugView
    {
        Final = 0,
        BaseColor = 1,
        Depth = 2,
        Normal = 3,
        Lighting = 4,
        Silhouette = 5,
        Occlusion = 6,
        Detail = 7,
        Outline = 8,
        // Appended in v2 (color blocks); appended values keep serialized ints stable.
        SourceMaterial = 9,
        SampledAlbedo = 10,
        RegionColor = 11,
        RegionId = 12,
        ObjectIdView = 13,
        FacetedNormal = 14,
        LightingBands = 15,
        // Paint-layer views (v2.1, ANGJustinl fork): layer depth reuses Depth,
        // layer contours reuse Outline, final paint composition reuses Final.
        NdotL = 16,
        PaintLayerId = 17,
        ShadowMask = 18,
        BaseMask = 19,
        HighlightMask = 20
    }

    [Serializable]
    public sealed class CartoonRenderSettings
    {
        [Header("Master")]
        public bool enabled = true;

        [Header("Channels")]
        [Tooltip("Scene channel: capture scene materials and rebuild them as 2D colour regions. The projection fields below are this channel's style.")]
        public bool sceneChannelEnabled = true;
        [Tooltip("Character channel: rebuild marked characters with their own style. Off keeps every character's original art.")]
        public CartoonCharacterSettings character = new();

        [Header("Projected 2D Shapes (new pipeline)")]
        public bool projectedShapes = true;
        [Range(128, 640)] public int projectionWidth = 640;
        [Range(1, 60)] public int projectionUpdatesPerSecond = 8;
        [Tooltip("Reduce scene rebuilds after the camera settles. Does not throttle character animation or the final settled-detail capture.")]
        public bool projectionAdaptiveRefresh = true;
        [Tooltip("Scene rebuilds per second while the camera is stationary. 0 holds the result until the camera/style changes or HintSceneContentChanged is called. Unnotified dynamic content needs a nonzero rate.")]
        [Range(0f, 2f)] public float projectionIdleUpdatesPerSecond = 0.5f;
        [Header("Projection Low Latency")]
        [Tooltip("Use a faster low-resolution projection while the camera moves, then restore full detail. Orthographic pan/zoom reuses aligned polygons.")]
        public bool projectionLowLatency = true;
        [Range(128, 640)] public int projectionMotionWidth = 320;
        [Range(1, 60)] public int projectionMotionUpdatesPerSecond = 30;
        [Range(0.05f, 1f)] public float projectionSettleSeconds = 0.2f;
        [Tooltip("Reject very old/cut-camera results, rather than overlaying unrelated screen-space shapes. Does not block GPU readback.")]
        [Range(0.1f, 2f)] public float projectionMaximumResultAge = 0.35f;
        [Range(8, 128)] public int projectionColorStep = 40;
        [Range(1, 128)] public int projectionMinimumArea = 4;
        [Range(0, 8)] public float projectionContourTolerance = 1.5f;
        public bool projectionShowContours = false;
        [Range(0.1f, 1f)] public float renderScale = 0.5f;
        public bool pixelationEnabled = false;

        [Header("Surface")]
        public CartoonSurfaceMode surfaceMode = CartoonSurfaceMode.OriginalMaterial;
        // Legacy v1 field kept for serialization compatibility; superseded by surfaceMode.
        [HideInInspector, Tooltip("Legacy v1 switch. OriginalMaterial mode reads this; new modes ignore it.")]
        public bool preserveSourceMaterials = true;
        public CartoonBackgroundMode backgroundMode = CartoonBackgroundMode.Camera;
        public Color backgroundColor = new(0.93f, 0.94f, 0.95f, 1f);

        [Header("Color Blocks")]
        public bool enableMaterialRegions = true;
        [Tooltip("Use geometric (per-triangle) normals for faceted shading instead of smooth vertex normals.")]
        public bool facetedLighting = true;
        [Range(0.25f, 4f)] public float colorMergeMultiplier = 1f;
        [Range(0f, 64f)] public float minimumScreenRegionArea = 12f;
        [Tooltip("Screen-space approximation pass: removes tiny region islands and jaggy block edges inside the low-resolution buffer.")]
        public bool shapeSimplification = false;
        [Range(0.5f, 1.5f)] public float paletteSaturation = 1f;
        [Range(0.5f, 1.5f)] public float paletteBrightness = 1f;
        [Tooltip("Hero-detail regions (eyes, mouth, ribbons) skip palette shifts and get lighter shadows.")]
        public bool preserveHeroDetails = true;

        [Header("Lighting")]
        [Tooltip("Discrete lighting for OriginalMaterial mode (legacy v1 behaviour).")]
        public bool lightingEnabled = false;
        [Range(2, 4)] public int colorBands = 3;
        [Range(0f, 1f)] public float shadowThreshold = 0.32f;
        [Range(0f, 1f)] public float highlightThreshold = 0.78f;
        [Tooltip("Lighting mode for OriginalMaterial mode only; color-block modes paint layers in the capture pass.")]
        public CartoonLightingMode lightingMode = CartoonLightingMode.TwoBandFaceted;
        [Range(0.5f, 1f)] public float shadowValue = 0.82f;
        [Range(0.9f, 1.1f)] public float shadowSaturation = 1f;
        [Range(-0.05f, 0.05f)] public float shadowTemperature = 0f;

        [Header("Paint Layers (ANGJustinl fork)")]
        public CartoonPaintLayerMode paintLayerMode = CartoonPaintLayerMode.ShadowBase;
        [Tooltip("Classify with a fixed world light direction instead of the scene main light.")]
        public bool useFixedLightDirection = true;
        public Vector3 fixedLightDirection = new(0.35f, 0.8f, 0.45f);
        [Tooltip("NdotL below this enters the shadow layer.")]
        [Range(-1f, 1f)] public float shadowThresholdEnter = -0.30f;
        [Tooltip("Shadow layer is kept until NdotL exceeds this (hysteresis exit).")]
        [Range(-1f, 1f)] public float shadowThresholdExit = -0.15f;
        [Tooltip("NdotL above this enters the highlight layer.")]
        [Range(-1f, 1f)] public float highlightThresholdEnter = 0.62f;
        [Tooltip("Highlight layer is kept until NdotL drops below this (hysteresis exit).")]
        [Range(-1f, 1f)] public float highlightThresholdExit = 0.55f;
        [Tooltip("Fraction of lightness removed for the shadow layer (OKLab L).")]
        [Range(0f, 1f)] public float shadowStrength = 0.38f;
        [Tooltip("Fraction of lightness added for the highlight layer (OKLab L).")]
        [Range(0f, 1f)] public float highlightStrength = 0.24f;
        [Tooltip("Per-pixel threshold hysteresis using the previous frame's NdotL; disables per-frame layer flicker near thresholds.")]
        public bool paintLayerHysteresis = true;
        [Tooltip("Force HeroDetail materials into the base layer (eyes, mouth keep their painted look).")]
        public bool heroDetailStaticLayers = false;

        [Header("Outline (internal-resolution pixels)")]
        public bool outlineEnabled = true;
        [Range(0.5f, 6f)] public float outlineWidth = 2f;
        [Range(0f, 3f)] public float silhouetteStrength = 1.35f;
        [Range(0f, 3f)] public float occlusionStrength = 1f;
        [Range(0f, 3f)] public float normalEdgeStrength = 0f;
        [Range(0f, 3f)] public float depthEdgeStrength = 1f;
        [Range(0.0001f, 0.1f)] public float depthThreshold = 0.03f;
        [Range(0.01f, 1f)] public float normalThreshold = 0.22f;
        public Color outlineColor = new(0.035f, 0.04f, 0.055f, 1f);
        [Tooltip("Draw lines between color regions of the same object (off by default).")]
        public bool outlineRegionBoundaries = false;
        [Range(0f, 3f)] public float regionBoundaryStrength = 0f;
        [Tooltip("Only keep the outer silhouette; drops occlusion and region lines.")]
        public bool silhouetteOnly = false;
        [Range(0f, 3f)] public float heroDetailOutlineStrength = 0f;

        [Header("Inspection")]
        public CartoonDebugView debugView = CartoonDebugView.Final;

        public float EffectiveScale => pixelationEnabled ? Mathf.Clamp(renderScale, 0.1f, 1f) : 1f;

        /// <summary>Master switch AND scene channel switch; the scene style stays untouched when only the character channel changes.</summary>
        public bool SceneChannelActive => enabled && sceneChannelEnabled && projectedShapes;

        /// <summary>The character channel is subordinate to the master switch and cannot be forced on per character.</summary>
        public bool CharacterChannelActive => enabled && character != null && character.enabled;

        // OriginalMaterial keeps the legacy preserveSourceMaterials switch so old
        // renderer assets render exactly as before; the new modes never use camera color.
        public bool UsesSceneColor =>
            surfaceMode == CartoonSurfaceMode.OriginalMaterial && preserveSourceMaterials;

        public bool UsesVertexRegionColor =>
            surfaceMode == CartoonSurfaceMode.MaterialColorBlocks && enableMaterialRegions;

        public bool UsesFacetedNormals =>
            surfaceMode != CartoonSurfaceMode.OriginalMaterial && facetedLighting;
    }
}
