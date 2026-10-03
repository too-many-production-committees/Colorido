using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection
{
    /// <summary>
    /// Turns a sampled sprite into cached local-space geometry by reusing the scene renderer's
    /// ProjectedShapeBuilder, so a 2D character and the scene share one partition/contour
    /// algorithm. Nothing here touches the GPU or the scene capture path.
    /// </summary>
    public static class CartoonSpriteShapeFactory
    {
        public static CartoonSpriteShape Build(Sprite sprite, CartoonSpriteSampler.Geometry geometry,
            CartoonSpriteSampler.Sample sample, CartoonCharacterSettings style)
        {
            if (sprite == null || !sample.success || style == null)
                return null;

            int width = sample.width, height = sample.height;
            Color32[] pixels = sample.pixels;

            // Detail protection works through the builder's identity channel: regions only merge
            // into a neighbour carrying the same identity, so pinning identity to the quantised
            // colour keeps small high-contrast islands (eyes, mouths) from being absorbed.
            var identities = new Color32[pixels.Length];
            if (style.preserveDetailRegions)
            {
                int step = Mathf.Max(1, style.colorStep);
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 c = pixels[i];
                    identities[i] = new Color32((byte)(c.r / step), (byte)(c.g / step), (byte)(c.b / step), 0);
                }
            }
            else
            {
                var one = new Color32(1, 0, 0, 0);
                for (int i = 0; i < identities.Length; i++)
                    identities[i] = one;
            }

            ProjectedShapeBuilder.Result result;
            try
            {
                result = ProjectedShapeBuilder.Build(pixels, identities, width, height,
                    style.colorStep, style.minimumArea, style.contourTolerance);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return null;
            }

            var shape = new CartoonSpriteShape
            {
                sprite = sprite,
                shapeHash = style.ShapeHash(),
                sampleWidth = width,
                sampleHeight = height,
                sampledPixels = width * height,
                palette = BuildPaletteFromColors(result.colors),
                regions = result.regions,
                sourceEdges = result.sourceEdges,
                simplifiedEdges = result.simplifiedEdges,
                buildMilliseconds = result.milliseconds
            };

            float invTextureWidth = geometry.textureWidth > 0 ? 1f / geometry.textureWidth : 0f;
            float invTextureHeight = geometry.textureHeight > 0 ? 1f / geometry.textureHeight : 0f;

            Vector3 ToLocal(float u, float v) => new Vector3(
                geometry.boundsMin.x + u * geometry.boundsSize.x,
                geometry.boundsMin.y + v * geometry.boundsSize.y,
                0f);

            Vector2 ToUv(float u, float v) => new Vector2(
                (geometry.rectPosition.x + u * geometry.rectSize.x) * invTextureWidth,
                (geometry.rectPosition.y + v * geometry.rectSize.y) * invTextureHeight);

            int vertexCount = result.vertices.Length;
            shape.vertices = new Vector3[vertexCount];
            shape.uv = new Vector2[vertexCount];
            shape.vertexLabel = new int[vertexCount];
            shape.colors = result.colors;
            shape.triangles = result.triangles;

            var labelOfColor = new Dictionary<int, int>();
            for (int i = 0; i < shape.palette.Length; i++)
            {
                int packed = Pack(shape.palette[i]);
                if (!labelOfColor.ContainsKey(packed))
                    labelOfColor[packed] = i;
            }

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 v = result.vertices[i];
                shape.vertices[i] = ToLocal(v.x, v.y);
                shape.uv[i] = ToUv(v.x, v.y);
                shape.vertexLabel[i] = labelOfColor.TryGetValue(Pack(result.colors[i]), out int label) ? label : 0;
            }

            int lineCount = result.lines.Length;
            shape.contourVertices = new Vector3[lineCount];
            for (int i = 0; i < lineCount; i++)
            {
                Vector3 v = result.lines[i];
                shape.contourVertices[i] = ToLocal(v.x, v.y);
            }

            return shape;
        }

        static Color32[] BuildPaletteFromColors(Color32[] colors)
        {
            var palette = new List<Color32>();
            var seen = new HashSet<int>();
            for (int i = 0; i < colors.Length; i++)
            {
                if (seen.Add(Pack(colors[i])))
                    palette.Add(colors[i]);
            }
            return palette.ToArray();
        }

        static int Pack(Color32 color) => (color.r << 16) | (color.g << 8) | color.b;
    }
}
