using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    internal sealed class CartoonOutlinePass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle depth;
            public TextureHandle normal;
            public TextureHandle objectId;
            public Vector4 texelSize;
            public float outlineWidth;
            public float depthThreshold;
            public float normalThreshold;
            public float depthStrength;
            public float normalStrength;
            public float enabled;
        }

        private readonly Material material;
        private CartoonRenderSettings settings;

        public CartoonOutlinePass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public void Setup(CartoonRenderSettings value) => settings = value;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cartoon = frameData.Get<CartoonFrameData>();
            cartoon.outline = CartoonRenderGraphUtility.CreateTexture(renderGraph, cartoon.width, cartoon.height,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, "Cartoon Outline Masks", Color.clear);

            using var builder = renderGraph.AddRasterRenderPass<PassData>("Cartoon Outline Detection", out var passData);
            passData.material = material;
            passData.depth = cartoon.depth;
            passData.normal = cartoon.normal;
            passData.objectId = cartoon.objectId;
            passData.texelSize = new Vector4(1f / cartoon.width, 1f / cartoon.height, cartoon.width, cartoon.height);
            passData.outlineWidth = settings.outlineWidth;
            passData.depthThreshold = settings.depthThreshold;
            passData.normalThreshold = settings.normalThreshold;
            passData.depthStrength = settings.silhouetteOnly ? 0f : settings.depthEdgeStrength;
            passData.normalStrength = settings.silhouetteOnly ? 0f : settings.normalEdgeStrength;
            passData.enabled = settings.outlineEnabled ? 1f : 0f;
            builder.UseTexture(passData.depth, AccessFlags.Read);
            builder.UseTexture(passData.normal, AccessFlags.Read);
            builder.UseTexture(passData.objectId, AccessFlags.Read);
            builder.SetRenderAttachment(cartoon.outline, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                data.material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
                data.material.SetTexture("_CartoonDepth", data.depth);
                data.material.SetTexture("_CartoonNormal", data.normal);
                data.material.SetTexture("_CartoonObjectId", data.objectId);
                data.material.SetVector("_InternalTexelSize", data.texelSize);
                data.material.SetFloat("_OutlineWidth", data.outlineWidth);
                data.material.SetFloat("_DepthThreshold", data.depthThreshold);
                data.material.SetFloat("_NormalThreshold", data.normalThreshold);
                data.material.SetFloat("_DepthEdgeStrength", data.depthStrength);
                data.material.SetFloat("_NormalEdgeStrength", data.normalStrength);
                data.material.SetFloat("_OutlineEnabled", data.enabled);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
            });
        }
    }
}
