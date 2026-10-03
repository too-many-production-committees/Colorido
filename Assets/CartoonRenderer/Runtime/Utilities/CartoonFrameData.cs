using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace CartoonProjection
{
    // RenderGraph frame-local handoff point. Passes between Capture and Lighting can
    // replace any of these handles without coupling the passes.
    public sealed class CartoonFrameData : ContextItem
    {
        public TextureHandle baseColor;
        public TextureHandle sceneColor;
        public TextureHandle depth;
        public TextureHandle normal;
        public TextureHandle objectId;
        public TextureHandle rasterDepth;
        public TextureHandle lighting;
        public TextureHandle outline;
        public int width;
        public int height;
        public CartoonPaintHistory paintHistory;

        public override void Reset()
        {
            baseColor = sceneColor = depth = normal = objectId = rasterDepth = lighting = outline = TextureHandle.nullHandle;
            width = height = 0;
            paintHistory = null;
        }
    }
}
