using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    // Copies this frame's NdotL (capture normal buffer alpha) into the paint history
    // ping-pong buffer so the next frame's capture can apply threshold hysteresis.
    internal sealed class CartoonPaintHistoryPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle normal;
            public TextureHandle target;
        }

        private readonly Material material;

        public CartoonPaintHistoryPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cartoon = frameData.Get<CartoonFrameData>();
            var history = cartoon.paintHistory;
            if (history?.Current == null || cartoon.normal == TextureHandle.nullHandle)
                return;

            var target = renderGraph.ImportTexture(history.Current);
            using var builder = renderGraph.AddRasterRenderPass<PassData>("Paint History Copy", out var passData);
            passData.material = material;
            passData.normal = cartoon.normal;
            passData.target = target;
            builder.UseTexture(passData.normal, AccessFlags.Read);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                data.material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
                data.material.SetTexture("_CartoonNormal", data.normal);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
            });
            history.Swap();
        }
    }
}
