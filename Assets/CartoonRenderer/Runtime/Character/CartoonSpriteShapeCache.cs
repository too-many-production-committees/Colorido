using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection
{
    /// <summary>
    /// A sprite rebuilt as simplified colour regions in the sprite's own local space.
    /// Positions and UVs are baked, so drawing only needs the character's current transform.
    /// </summary>
    public sealed class CartoonSpriteShape
    {
        public Sprite sprite;
        public int shapeHash;
        public int sampleWidth, sampleHeight;

        /// <summary>Mesh vertices in sprite local space (pivot at the origin).</summary>
        public Vector3[] vertices;
        /// <summary>Atlas UVs matching <see cref="vertices"/>.</summary>
        public Vector2[] uv;
        /// <summary>Palette index per vertex, for colour-only restyling without a rebuild.</summary>
        public int[] vertexLabel;
        public int[] triangles;
        public Color32[] colors;

        /// <summary>Simplified shared contour segments, in sprite local space, as a line list.</summary>
        public Vector3[] contourVertices;

        public Color32[] palette;

        public int regions, sourceEdges, simplifiedEdges;
        public double buildMilliseconds;

        /// <summary>Sampled pixels, kept only while queued for tooling inspection.</summary>
        public int sampledPixels;

        public int VertexCount => vertices?.Length ?? 0;
        public int TriangleCount => (triangles?.Length ?? 0) / 3;
    }

    /// <summary>
    /// Bounded least-recently-used cache of sprite shapes. Entries are keyed by sprite, shape
    /// style and texture version, so replaying an animation frame is free while a style change
    /// only invalidates the shapes it actually affects.
    /// </summary>
    public sealed class CartoonSpriteShapeCache
    {
        readonly struct Key : System.IEquatable<Key>
        {
            public readonly int spriteId, shapeHash, textureVersion;

            public Key(int spriteId, int shapeHash, int textureVersion)
            {
                this.spriteId = spriteId; this.shapeHash = shapeHash; this.textureVersion = textureVersion;
            }

            public bool Equals(Key other) => spriteId == other.spriteId && shapeHash == other.shapeHash && textureVersion == other.textureVersion;

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode()
            {
                unchecked { return spriteId * 397 ^ shapeHash * 31 ^ textureVersion; }
            }
        }

        sealed class Entry
        {
            public CartoonSpriteShape shape;
            public int pixels;
            public double lastUsed;
        }

        readonly Dictionary<Key, Entry> entries = new();

        public int Count => entries.Count;
        public int Pixels { get; private set; }
        public long Hits { get; private set; }
        public long Misses { get; private set; }
        public long Evictions { get; private set; }
        public double LastLookupMilliseconds { get; private set; }

        public double HitRate
        {
            get
            {
                long total = Hits + Misses;
                return total == 0 ? 0 : Hits / (double)total;
            }
        }

        public void Clear()
        {
            entries.Clear();
            Pixels = 0;
        }

        public void ResetStatistics()
        {
            Hits = 0;
            Misses = 0;
            Evictions = 0;
        }

        /// <summary>
        /// Increments the miss counter and returns whether the entry existed, so callers can
        /// tell "already cached" from "needs preparing" without a second dictionary probe.
        /// </summary>
        public bool TryGet(Sprite sprite, int shapeHash, double now, out CartoonSpriteShape shape)
        {
            shape = null;
            if (sprite == null)
                return false;

            var key = new Key(sprite.GetInstanceID(), shapeHash, TextureVersion(sprite));
            if (!entries.TryGetValue(key, out var entry))
            {
                Misses++;
                return false;
            }

            Hits++;
            entry.lastUsed = now;
            shape = entry.shape;
            return true;
        }

        public void Store(Sprite sprite, int shapeHash, CartoonSpriteShape shape, double now,
            int capacity, int pixelBudget)
        {
            if (sprite == null || shape == null)
                return;

            var key = new Key(sprite.GetInstanceID(), shapeHash, TextureVersion(sprite));
            int pixels = Mathf.Max(1, shape.sampledPixels);

            if (entries.TryGetValue(key, out var existing))
            {
                Pixels -= existing.pixels;
                existing.shape = shape;
                existing.pixels = pixels;
                existing.lastUsed = now;
                Pixels += pixels;
            }
            else
            {
                entries[key] = new Entry { shape = shape, pixels = pixels, lastUsed = now };
                Pixels += pixels;
            }

            Trim(now, Mathf.Max(1, capacity), Mathf.Max(1, pixelBudget));
        }

        /// <summary>Drops entries whose sprite was destroyed, then enforces both budgets.</summary>
        public void Trim(double now, int capacity, int pixelBudget)
        {
            if (entries.Count == 0)
                return;

            List<Key> dead = null;
            foreach (var pair in entries)
            {
                if (pair.Value.shape.sprite)
                    continue;
                (dead ??= new List<Key>()).Add(pair.Key);
            }
            if (dead != null)
            {
                foreach (var key in dead)
                {
                    Pixels -= entries[key].pixels;
                    entries.Remove(key);
                    Evictions++;
                }
            }

            while (entries.Count > capacity || Pixels > pixelBudget)
            {
                Key oldest = default;
                double oldestTime = double.MaxValue;
                bool found = false;
                foreach (var pair in entries)
                {
                    if (pair.Value.lastUsed >= oldestTime)
                        continue;
                    oldestTime = pair.Value.lastUsed;
                    oldest = pair.Key;
                    found = true;
                }
                if (!found)
                    break;

                Pixels -= entries[oldest].pixels;
                entries.Remove(oldest);
                Evictions++;
            }
        }

        static int TextureVersion(Sprite sprite)
        {
            var texture = sprite.texture;
            if (texture == null)
                return 0;
            // updateCount moves when a texture is rewritten, which is exactly when cached
            // shapes made from it become stale.
            unchecked { return texture.GetInstanceID() * 397 ^ (int)texture.updateCount; }
        }
    }
}
