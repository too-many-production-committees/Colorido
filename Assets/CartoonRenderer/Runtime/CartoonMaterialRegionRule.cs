using System;
using UnityEngine;

namespace CartoonProjection
{
    [Serializable]
    public sealed class CartoonMaterialRegionRule
    {
        public string materialGuid;
        public string materialName;

        public CartoonRegionPolicy policy = CartoonRegionPolicy.Standard;

        [Tooltip("Keep the source material alpha-test behaviour in the capture pass.")]
        public bool preserveAlphaCutout = true;

        [Range(1, 32)] public int maxRegions = CartoonRegionPolicyDefaults.StandardMaxRegions;
        [Range(0.1f, 4f)] public float colorMergeScale = 1f;
        [Range(0, 256)] public int minRegionTriangles = CartoonRegionPolicyDefaults.StandardMinTriangles;
        [Range(0f, 0.1f)] public float minRegionAreaRatio = 0.002f;

        [Tooltip("Fixed-base materials (painted eye shadow, pre-shaded decals) skip dynamic shadow/highlight classification entirely.")]
        public bool fixedBaseLayer = false;
        [Tooltip("Allow the two-band faceted shadow on this material.")]
        public bool allowDiscreteLighting = true;
        [Tooltip("Allow internal region boundary lines for this material (off by default).")]
        public bool allowInternalOutline = false;

        [Tooltip("Optional palette the region colors snap to after clustering.")]
        public Color[] manualPalette = Array.Empty<Color>();
        [Tooltip("Force every region of this material to a single override color.")]
        public bool useColorOverride = false;
        public Color colorOverride = Color.white;

        public void ApplyPolicyDefaults(CartoonRegionPolicy value, bool keepUserTuning = false)
        {
            policy = value;
            if (keepUserTuning)
                return;
            maxRegions = CartoonRegionPolicyDefaults.MaxRegions(value);
            minRegionTriangles = CartoonRegionPolicyDefaults.MinTriangles(value);
            colorMergeScale = 1f;
            // Painted shadows are usually part of hero materials (eye shadow decal etc.);
            // they must never be re-classified dynamically.
            if (value == CartoonRegionPolicy.HeroDetail)
                fixedBaseLayer = true;
        }

        public bool UsesAlphaCutout(Material material) =>
            preserveAlphaCutout && material != null && material.renderQueue >= 2450 && material.renderQueue <= 2550;
    }
}
