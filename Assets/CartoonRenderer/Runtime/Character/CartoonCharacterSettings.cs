using System;
using UnityEngine;

namespace CartoonProjection
{
    /// <summary>
    /// Style for the character channel. Kept separate from the scene style so changing one
    /// never invalidates the other channel's caches or results.
    /// </summary>
    [Serializable]
    public sealed class CartoonCharacterSettings
    {
        [Tooltip("Project-wide character channel. Off means every character keeps its original art, regardless of per-character mode.")]
        public bool enabled = false;

        [Header("Shape (affects cached geometry)")]
        [Tooltip("Colour quantisation step applied when grouping a sprite into regions.")]
        [Range(4, 128)] public int colorStep = 40;
        [Tooltip("Regions below this pixel count merge into the nearest similar neighbour.")]
        [Range(1, 256)] public int minimumArea = 4;
        [Tooltip("Contour simplification tolerance, in sprite pixels.")]
        [Range(0f, 8f)] public float contourTolerance = 1.5f;
        [Tooltip("Longest side used when sampling a sprite. Larger frames are downscaled; smaller frames are never upscaled.")]
        [Range(32, 1024)] public int maximumSampleSize = 256;
        [Tooltip("Keep small high-contrast islands such as eyes and mouths: regions only merge into the same colour bucket.")]
        public bool preserveDetailRegions = true;

        [Header("Colour (applied when drawing, no rebuild needed)")]
        [Range(0.5f, 1.5f)] public float paletteSaturation = 1f;
        [Range(0.5f, 1.5f)] public float paletteBrightness = 1f;
        public bool showContours = false;

        [Header("Update")]
        [Tooltip("Smallest gap between uncached sprite builds. Keep zero for animation: 0.2 caps new builds at 5 per second. Cached frames switch immediately regardless of this value.")]
        [Range(0f, 1f)] public float minimumRebuildInterval = 0f;
        [Tooltip("Newest sprite frames to prepare per frame. Keeps a busy scene responsive.")]
        [Range(1, 4)] public int preparesPerFrame = 2;

        [Header("Cache")]
        [Tooltip("Maximum cached sprite shapes. Least recently used entries are dropped first.")]
        [Range(4, 512)] public int cacheCapacity = 64;
        [Tooltip("Maximum total sampled pixels across the cache. Bounds memory independently of entry count.")]
        [Range(65536, 16777216)] public int cachePixelBudget = 4194304;

        [Header("Inspection")]
        public bool logCacheActivity = false;

        /// <summary>
        /// Identity of everything that changes the cached geometry. Colour-only parameters are
        /// deliberately absent so palette tweaks reuse the existing meshes.
        /// </summary>
        public int ShapeHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + colorStep;
                hash = hash * 31 + minimumArea;
                hash = hash * 31 + contourTolerance.GetHashCode();
                hash = hash * 31 + maximumSampleSize;
                hash = hash * 31 + (preserveDetailRegions ? 1 : 0);
                return hash;
            }
        }

        public bool MatchesShape(CartoonCharacterSettings other) => other != null && ShapeHash() == other.ShapeHash();

        public void CopyShapeFrom(CartoonCharacterSettings other)
        {
            if (other == null)
                return;
            colorStep = other.colorStep;
            minimumArea = other.minimumArea;
            contourTolerance = other.contourTolerance;
            maximumSampleSize = other.maximumSampleSize;
            preserveDetailRegions = other.preserveDetailRegions;
        }
    }

    /// <summary>
    /// Optional per-character style override. A single character can be tuned without
    /// touching the shared character channel style or any other character.
    /// </summary>
    [CreateAssetMenu(menuName = "Cartoon Projection/Character Style Override", fileName = "CartoonCharacterStyle")]
    public sealed class CartoonCharacterStyleOverride : ScriptableObject
    {
        public bool overrideShape = true;
        [Range(4, 128)] public int colorStep = 40;
        [Range(1, 256)] public int minimumArea = 4;
        [Range(0f, 8f)] public float contourTolerance = 1.5f;
        [Range(32, 1024)] public int maximumSampleSize = 256;
        public bool preserveDetailRegions = true;

        public void ApplyTo(CartoonCharacterSettings target)
        {
            if (target == null || !overrideShape)
                return;
            target.colorStep = colorStep;
            target.minimumArea = minimumArea;
            target.contourTolerance = contourTolerance;
            target.maximumSampleSize = maximumSampleSize;
            target.preserveDetailRegions = preserveDetailRegions;
        }
    }
}
