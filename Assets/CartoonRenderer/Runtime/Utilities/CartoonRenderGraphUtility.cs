using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace CartoonProjection
{
    internal static class CartoonRenderGraphUtility
    {
        public static TextureHandle CreateTexture(RenderGraph graph, int width, int height,
            GraphicsFormat format, string name, Color clearColor)
        {
            var desc = new TextureDesc(width, height)
            {
                name = name,
                colorFormat = format,
                clearBuffer = true,
                clearColor = clearColor,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            return graph.CreateTexture(desc);
        }

        public static TextureHandle CreateDepthTexture(RenderGraph graph, int width, int height, string name)
        {
            var desc = new TextureDesc(width, height)
            {
                name = name,
                format = GraphicsFormat.D32_SFloat,
                clearBuffer = true,
                clearColor = Color.black,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            return graph.CreateTexture(desc);
        }
    }
}
