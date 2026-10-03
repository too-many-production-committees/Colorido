using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    // Phase-G approximation: filters the projected color/id buffers inside the internal
    // resolution so tiny region islands and jaggy block edges merge into neighbours.
    // Runs between Capture and Lighting; the filtered handles replace the originals in
    // CartoonFrameData so Lighting, Outline and Composite stay consistent.
    internal sealed class CartoonSimplifyPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle objectId;
            public TextureHandle baseColor;
            public Vector4 texelSize;
            public float minimumArea;
        }

        private readonly Material material;
        private CartoonRenderSettings settings;

        public CartoonSimplifyPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public void Setup(CartoonRenderSettings value) => settings = value;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cartoon = frameData.Get<CartoonFrameData>();
            if (!settings.shapeSimplification || settings.minimumScreenRegionArea <= 0.01f)
                return; // pass-through: downstream passes keep using the raw buffers

            var filteredColor = CartoonRenderGraphUtility.CreateTexture(renderGraph, cartoon.width, cartoon.height,
                cartoon.baseColor.GetDescriptor(renderGraph).format, "Region Filtered Color", Color.clear);
            var filteredObjectId = CartoonRenderGraphUtility.CreateTexture(renderGraph, cartoon.width, cartoon.height,
                cartoon.objectId.GetDescriptor(renderGraph).format, "Region Filtered IDs", Color.clear);

            using var builder = renderGraph.AddRasterRenderPass<PassData>("Region Area Filter (Simplify)", out var passData);
            passData.material = material;
            passData.objectId = cartoon.objectId;
            passData.baseColor = cartoon.baseColor;
            passData.texelSize = new Vector4(1f / cartoon.width, 1f / cartoon.height, cartoon.width, cartoon.height);
            passData.minimumArea = Mathf.Clamp(settings.minimumScreenRegionArea, 1f, 25f);
            builder.UseTexture(passData.objectId, AccessFlags.Read);
            builder.UseTexture(passData.baseColor, AccessFlags.Read);
            builder.SetRenderAttachment(filteredColor, 0, AccessFlags.Write);
            builder.SetRenderAttachment(filteredObjectId, 1, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                data.material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
                data.material.SetTexture("_CartoonBaseColor", data.baseColor);
                data.material.SetTexture("_CartoonObjectId", data.objectId);
                data.material.SetVector("_InternalTexelSize", data.texelSize);
                data.material.SetFloat("_MinimumArea", data.minimumArea);
                data.material.SetFloat("_IdTolerance", 0.001f);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
            });

            cartoon.baseColor = filteredColor;
            cartoon.objectId = filteredObjectId;
        }
    }
}
