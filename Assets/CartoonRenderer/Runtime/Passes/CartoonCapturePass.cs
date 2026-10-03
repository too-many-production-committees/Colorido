using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    // Preserves the camera color for material-aware composition, then re-rasterizes opaque
    // geometry into paint color, depth, normal(+NdotL) and identity buffers. In
    // MaterialColorBlocks mode the paint color already contains the per-triangle
    // shadow/base/highlight layers (ANGJustinl fork); no fullscreen lighting multiply.
    internal sealed class CartoonCapturePass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public RendererListHandle rendererList;
            public Material material;
            public float facetedNormals;
            public Vector4 lightDirection;
            public Vector4 texelSize;
            public float paintLayerMode;
            public float shadowEnter;
            public float shadowExit;
            public float highlightEnter;
            public float highlightExit;
            public float shadowStrength;
            public float highlightStrength;
            public float hysteresis;
            public float heroStatic;
            public TextureHandle history;
        }

        private static readonly int PrevNdotLId = Shader.PropertyToID("_CartoonPrevNdotL");

        private static readonly List<ShaderTagId> ShaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private readonly Material material;
        private CartoonRenderSettings settings;
        private CartoonPaintHistory paintHistory;

        public CartoonCapturePass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public void Setup(CartoonRenderSettings value) => settings = value;

        public void SetPaintHistory(CartoonPaintHistory history) => paintHistory = history;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var lights = frameData.Get<UniversalLightData>();
            var cartoon = frameData.GetOrCreate<CartoonFrameData>();
            float scale = settings.EffectiveScale;
            cartoon.width = Mathf.Max(1, Mathf.RoundToInt(camera.cameraTargetDescriptor.width * scale));
            cartoon.height = Mathf.Max(1, Mathf.RoundToInt(camera.cameraTargetDescriptor.height * scale));

            TextureDesc sceneColorDesc = resources.activeColorTexture.GetDescriptor(renderGraph);
            sceneColorDesc.name = "Cartoon Source Material Color";
            sceneColorDesc.clearBuffer = false;
            cartoon.sceneColor = renderGraph.CreateTexture(sceneColorDesc);
            renderGraph.AddCopyPass(resources.activeColorTexture, cartoon.sceneColor, "Copy Source Material Color");

            Color background = camera.camera.clearFlags == CameraClearFlags.SolidColor
                ? camera.camera.backgroundColor.linear
                : new Color(0.78f, 0.86f, 0.9f, 1f);
            float farDepth = SystemInfo.usesReversedZBuffer ? 0f : 1f;
            cartoon.baseColor = CartoonRenderGraphUtility.CreateTexture(renderGraph, cartoon.width, cartoon.height,
                camera.cameraTargetDescriptor.graphicsFormat, "Shape Fill Color", background);
            cartoon.depth = CartoonRenderGraphUtility.CreateTexture(renderGraph, cartoon.width, cartoon.height,
                GraphicsFormat.R32_SFloat, "Shape Device Depth", new Color(farDepth, farDepth, farDepth, farDepth));
            cartoon.normal = CartoonRenderGraphUtility.CreateTexture(renderGraph, cartoon.width, cartoon.height,
                GraphicsFormat.R16G16B16A16_SFloat, "Shape World Normal", Color.clear);
            // R = object id, G = baked region id, B = paint layer, A = policy flag.
            cartoon.objectId = CartoonRenderGraphUtility.CreateTexture(renderGraph, cartoon.width, cartoon.height,
                GraphicsFormat.R16G16B16A16_UNorm, "Shape Object + Region + Layer ID", Color.clear);
            cartoon.rasterDepth = CartoonRenderGraphUtility.CreateDepthTexture(renderGraph, cartoon.width, cartoon.height,
                "Shape Raster Depth");

            // Paint layer hysteresis: import the previous frame's NdotL buffer.
            bool useHysteresis = settings.paintLayerHysteresis
                                 && settings.paintLayerMode != CartoonPaintLayerMode.BaseOnly
                                 && paintHistory != null;
            var historyHandle = TextureHandle.nullHandle;
            if (useHysteresis)
            {
                paintHistory.width = cartoon.width;
                paintHistory.height = cartoon.height;
                cartoon.paintHistory = paintHistory;
                historyHandle = renderGraph.ImportTexture(paintHistory.Previous);
            }

            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, rendering, camera, lights,
                camera.defaultOpaqueSortFlags);
            drawing.overrideMaterial = material;
            drawing.overrideMaterialPassIndex = 0;
            var parameters = new RendererListParams(rendering.cullResults, drawing,
                new FilteringSettings(RenderQueueRange.opaque));

            Vector3 lightDirection = settings.fixedLightDirection.normalized;
            if (!settings.useFixedLightDirection && lights.mainLightIndex >= 0
                && lights.mainLightIndex < lights.visibleLights.Length)
                lightDirection = -lights.visibleLights[lights.mainLightIndex].localToWorldMatrix.GetColumn(2);

            using var builder = renderGraph.AddRasterRenderPass<PassData>("2D Shape Part Projection", out var passData);
            passData.rendererList = renderGraph.CreateRendererList(parameters);
            passData.material = material;
            passData.facetedNormals = settings.UsesFacetedNormals ? 1f : 0f;
            passData.lightDirection = new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f);
            passData.texelSize = new Vector4(1f / cartoon.width, 1f / cartoon.height, 0f, 0f);
            passData.paintLayerMode = (float)(int)settings.paintLayerMode;
            passData.shadowEnter = settings.shadowThresholdEnter;
            passData.shadowExit = Mathf.Max(settings.shadowThresholdEnter, settings.shadowThresholdExit);
            passData.highlightEnter = settings.highlightThresholdEnter;
            passData.highlightExit = Mathf.Min(settings.highlightThresholdEnter, settings.highlightThresholdExit);
            passData.shadowStrength = settings.shadowStrength;
            passData.highlightStrength = settings.highlightStrength;
            passData.hysteresis = useHysteresis ? 1f : 0f;
            passData.heroStatic = settings.heroDetailStaticLayers ? 1f : 0f;
            passData.history = historyHandle;
            builder.UseRendererList(passData.rendererList);
            if (historyHandle.IsValid())
                builder.UseTexture(historyHandle, AccessFlags.Read);
            builder.SetRenderAttachment(cartoon.baseColor, 0, AccessFlags.Write);
            builder.SetRenderAttachment(cartoon.depth, 1, AccessFlags.Write);
            builder.SetRenderAttachment(cartoon.normal, 2, AccessFlags.Write);
            builder.SetRenderAttachment(cartoon.objectId, 3, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(cartoon.rasterDepth, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                data.material.SetFloat("_FacetedNormals", data.facetedNormals);
                data.material.SetVector("_PaintLightDirection", data.lightDirection);
                data.material.SetVector("_PaintTexelSize", data.texelSize);
                data.material.SetFloat("_PaintLayerMode", data.paintLayerMode);
                data.material.SetFloat("_ShadowThresholdEnter", data.shadowEnter);
                data.material.SetFloat("_ShadowThresholdExit", data.shadowExit);
                data.material.SetFloat("_HighlightThresholdEnter", data.highlightEnter);
                data.material.SetFloat("_HighlightThresholdExit", data.highlightExit);
                data.material.SetFloat("_ShadowStrength", data.shadowStrength);
                data.material.SetFloat("_HighlightStrength", data.highlightStrength);
                data.material.SetFloat("_PaintHysteresis", data.hysteresis);
                data.material.SetFloat("_HeroStaticLayers", data.heroStatic);
                if (data.history.IsValid())
                    // The history is consumed only by this override material. Keep the
                    // binding local; Render Graph forbids undeclared global mutations.
                    data.material.SetTexture(PrevNdotLId, data.history);
                context.cmd.DrawRendererList(data.rendererList);
            });
        }
    }
}
