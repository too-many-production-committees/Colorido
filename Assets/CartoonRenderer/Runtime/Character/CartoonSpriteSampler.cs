using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CartoonProjection
{
    /// <summary>
    /// Reads a Sprite's pixels without ever changing project texture import settings.
    ///
    /// Readable textures are copied straight from their pixel data. Non-readable textures go
    /// through a non-blocking GPU readback of just the sprite's rect. Sampling never upscales,
    /// so a small low-resolution frame is not blown up into meaningless detail.
    /// </summary>
    public static class CartoonSpriteSampler
    {
        public readonly struct Sample
        {
            /// <summary>Sampled pixels, row 0 = bottom, matching ProjectedShapeBuilder's expectation.</summary>
            public readonly Color32[] pixels;
            public readonly int width, height;
            /// <summary>Sprite rect size in source texture pixels, before any downscaling.</summary>
            public readonly int sourceWidth, sourceHeight;
            public readonly bool success;
            public readonly string error;

            public Sample(Color32[] pixels, int width, int height, int sourceWidth, int sourceHeight, string error)
            {
                this.pixels = pixels; this.width = width; this.height = height;
                this.sourceWidth = sourceWidth; this.sourceHeight = sourceHeight;
                this.success = pixels != null && width > 0 && height > 0;
                this.error = error;
            }
        }

        /// <summary>
        /// Unity's Sprite accessors are main-thread only, so everything the background build needs
        /// is captured here as plain data before the build is handed to a worker thread.
        /// </summary>
        public readonly struct Geometry
        {
            public readonly Vector2 boundsMin, boundsSize;
            public readonly Vector2 rectPosition, rectSize;
            public readonly float textureWidth, textureHeight;

            public Geometry(Vector2 boundsMin, Vector2 boundsSize, Vector2 rectPosition, Vector2 rectSize,
                float textureWidth, float textureHeight)
            {
                this.boundsMin = boundsMin; this.boundsSize = boundsSize;
                this.rectPosition = rectPosition; this.rectSize = rectSize;
                this.textureWidth = textureWidth; this.textureHeight = textureHeight;
            }
        }

        public static Geometry ReadGeometry(Sprite sprite)
        {
            if (sprite == null)
                return default;
            var bounds = sprite.bounds;
            var rect = sprite.textureRect;
            var texture = sprite.texture;
            return new Geometry(bounds.min, bounds.size, rect.position, rect.size,
                texture ? texture.width : 0f, texture ? texture.height : 0f);
        }

        static Material readbackMaterial;

        /// <summary>Describes the source rect of a sprite, or why it cannot be sampled.</summary>
        public static bool TryDescribe(Sprite sprite, out RectInt rect, out Texture texture, out string error)
        {
            rect = default;
            texture = null;
            error = null;
            if (sprite == null)
            {
                error = "sprite is null";
                return false;
            }

            texture = sprite.texture;
            if (texture == null)
            {
                error = "sprite has no texture";
                return false;
            }

            // A sprite rotated inside an atlas would need the rotation undone per pixel;
            // those are reported instead of silently sampled wrong.
            if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
            {
                error = "atlas-rotated sprite is not supported";
                return false;
            }

            Rect source = sprite.textureRect;
            int x = Mathf.Clamp(Mathf.RoundToInt(source.x), 0, texture.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(source.y), 0, texture.height - 1);
            int width = Mathf.Clamp(Mathf.RoundToInt(source.width), 1, texture.width - x);
            int height = Mathf.Clamp(Mathf.RoundToInt(source.height), 1, texture.height - y);
            rect = new RectInt(x, y, width, height);
            return true;
        }

        public static void SampleSize(in RectInt rect, int maximumSampleSize, out int width, out int height, out int stride)
        {
            int longest = Mathf.Max(rect.width, rect.height);
            // Never upscale: a 96px frame stays 96px even when the cap is 256.
            stride = longest <= maximumSampleSize ? 1 : Mathf.Max(1, Mathf.CeilToInt(longest / (float)maximumSampleSize));
            width = Mathf.Max(1, rect.width / stride);
            height = Mathf.Max(1, rect.height / stride);
        }

        /// <summary>
        /// Blocking sample. Used for readable textures and by editor tooling, which cannot
        /// resolve the asynchronous callbacks. Returns success=false with a reason otherwise.
        /// </summary>
        public static Sample Read(Sprite sprite, int maximumSampleSize)
        {
            if (!TryDescribe(sprite, out var rect, out var texture, out string error))
                return new Sample(null, 0, 0, 0, 0, error);
            if (!texture.isReadable)
                return new Sample(null, 0, 0, 0, 0, "texture is not readable");

            SampleSize(rect, maximumSampleSize, out int width, out int height, out int stride);
            var pixels = new Color32[width * height];
            try
            {
                if (texture is not Texture2D readable)
                    return new Sample(null, 0, 0, 0, 0, "texture is not a Texture2D");
                var source = readable.GetPixelData<Color32>(0);
                BoxDownscale(source, texture.width, rect, stride, pixels, width, height);
            }
            catch (Exception e)
            {
                return new Sample(null, 0, 0, 0, 0, "pixel read failed: " + e.Message);
            }
            return new Sample(pixels, width, height, rect.width, rect.height, null);
        }

        /// <summary>
        /// Non-blocking GPU sample for textures that are not CPU readable. The callback runs
        /// on the main thread once the readback completes.
        /// </summary>
        public static void Request(Sprite sprite, int maximumSampleSize, Action<Sample> onComplete)
        {
            if (onComplete == null)
                return;

            if (!TryDescribe(sprite, out var rect, out var texture, out string error))
            {
                onComplete(new Sample(null, 0, 0, 0, 0, error));
                return;
            }

            RenderTexture target = null;
            try
            {
                target = RenderTexture.GetTemporary(
                    new RenderTextureDescriptor(rect.width, rect.height)
                    {
                        graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                        depthBufferBits = 0,
                        msaaSamples = 1,
                        useMipMap = false,
                        autoGenerateMips = false
                    });

                var material = ReadbackMaterial();
                material.SetTextureScale("_MainTex", new Vector2(rect.width / (float)texture.width, rect.height / (float)texture.height));
                material.SetTextureOffset("_MainTex", new Vector2(rect.x / (float)texture.width, rect.y / (float)texture.height));
                Graphics.Blit(texture, target, material);

                SampleSize(rect, maximumSampleSize, out int width, out int height, out int stride);
                RenderTexture captured = target;
                target = null;
                AsyncGPUReadback.Request(captured, 0, TextureFormat.RGBA32, request =>
                {
                    Sample result;
                    if (request.hasError)
                    {
                        result = new Sample(null, 0, 0, 0, 0, "GPU readback failed");
                    }
                    else
                    {
                        var source = request.GetData<Color32>();
                        var pixels = new Color32[width * height];
                        var fullRect = new RectInt(0, 0, captured.width, captured.height);
                        BoxDownscale(source, captured.width, fullRect, stride, pixels, width, height);
                        result = new Sample(pixels, width, height, rect.width, rect.height, null);
                    }
                    RenderTexture.ReleaseTemporary(captured);
                    onComplete(result);
                });
            }
            catch (Exception e)
            {
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                onComplete(new Sample(null, 0, 0, 0, 0, "GPU sample failed: " + e.Message));
            }
        }

        /// <summary>Average filter. stride = 1 copies texels straight across.</summary>
        static void BoxDownscale(NativeArray<Color32> source, int sourceWidth, in RectInt rect, int stride, Color32[] destination, int width, int height)
        {
            if (stride <= 1)
            {
                for (int y = 0; y < height; y++)
                {
                    int sourceRow = (rect.y + y) * sourceWidth + rect.x;
                    for (int x = 0; x < width; x++)
                        destination[y * width + x] = source[Mathf.Min(sourceRow + x, source.Length - 1)];
                }
                return;
            }

            int block = stride * stride;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0, count = 0;
                    for (int sy = 0; sy < stride; sy++)
                    {
                        int sourceY = rect.y + y * stride + sy;
                        if (sourceY >= rect.y + rect.height) break;
                        int row = sourceY * sourceWidth + rect.x + x * stride;
                        for (int sx = 0; sx < stride; sx++)
                        {
                            int sourceX = x * stride + sx;
                            if (sourceX >= rect.width) break;
                            int index = row + sx;
                            if (index < 0 || index >= source.Length) continue;
                            Color32 c = source[index];
                            r += c.r; g += c.g; b += c.b; a += c.a; count++;
                        }
                    }
                    if (count == 0) count = 1;
                    destination[y * width + x] = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));

                    // Transparency wins inside a block so sprite edges stay smooth instead of
                    // bleeding opaque background colour into the silhouette.
                    byte minAlpha = 255;
                    for (int sy = 0; sy < stride; sy++)
                    {
                        int sourceY = rect.y + y * stride + sy;
                        if (sourceY >= rect.y + rect.height) break;
                        int row = sourceY * sourceWidth + rect.x + x * stride;
                        for (int sx = 0; sx < stride; sx++)
                        {
                            if (x * stride + sx >= rect.width) break;
                            int index = row + sx;
                            if (index < 0 || index >= source.Length) continue;
                            if (source[index].a < minAlpha) minAlpha = source[index].a;
                        }
                    }
                    if (minAlpha != 255)
                    {
                        Color32 c = destination[y * width + x];
                        destination[y * width + x] = new Color32(c.r, c.g, c.b, minAlpha);
                    }
                }
            }
        }

        static Material ReadbackMaterial()
        {
            if (readbackMaterial != null)
                return readbackMaterial;
            var shader = Shader.Find("Hidden/CartoonProjection/SpriteReadback");
            if (shader == null)
                throw new InvalidOperationException("Hidden/CartoonProjection/SpriteReadback shader missing");
            readbackMaterial = CoreUtils.CreateEngineMaterial(shader);
            readbackMaterial.hideFlags = HideFlags.HideAndDontSave;
            return readbackMaterial;
        }
    }
}
