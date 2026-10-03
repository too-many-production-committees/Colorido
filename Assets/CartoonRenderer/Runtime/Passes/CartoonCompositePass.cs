using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    internal sealed class CartoonCompositePass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle baseColor;
            public TextureHandle sceneColor;
            public TextureHandle depth;
            public TextureHandle objectId;
            public TextureHandle normal;
            public TextureHandle lighting;
            public TextureHandle outline;
            public CartoonDebugView debugView;
            public Color outlineColor;
            public float silhouetteStrength;
            public float occlusionStrength;
            public float detailStrength;
            public float regionBoundaryStrength;
            public float heroDetailOutlineStrength;
            public float outlineEnabled;
            public int backgroundMode;
            public Color backgroundColor;
        }

        private readonly Material material;
        private CartoonRenderSettings settings;

        public CartoonCompositePass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public void Setup(CartoonRenderSettings value) => settings = value;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cartoon = frameData.Get<CartoonFrameData>();
            using var builder = renderGraph.AddRasterRenderPass<PassData>("Cartoon Composite + Point Upscale", out var passData);
            passData.material = material;
            passData.baseColor = cartoon.baseColor;
            passData.sceneColor = cartoon.sceneColor;
            passData.depth = cartoon.depth;
            passData.objectId = cartoon.objectId;
            passData.normal = cartoon.normal;
            passData.lighting = cartoon.lighting;
            passData.outline = cartoon.outline;
            passData.debugView = settings.debugView;
            passData.outlineColor = settings.outlineColor;
            passData.silhouetteStrength = settings.silhouetteStrength;
            passData.occlusionStrength = settings.silhouetteOnly ? 0f : settings.occlusionStrength;
            passData.detailStrength = settings.silhouetteOnly ? 0f : settings.normalEdgeStrength;
            passData.regionBoundaryStrength = settings.silhouetteOnly || !settings.outlineRegionBoundaries
                ? 0f
                : settings.regionBoundaryStrength;
            passData.heroDetailOutlineStrength = settings.heroDetailOutlineStrength;
            passData.outlineEnabled = settings.outlineEnabled ? 1f : 0f;
            passData.backgroundMode = (int)settings.backgroundMode;
            passData.backgroundColor = settings.backgroundColor.linear;
            builder.UseTexture(passData.baseColor, AccessFlags.Read);
            builder.UseTexture(passData.sceneColor, AccessFlags.Read);
            builder.UseTexture(passData.depth, AccessFlags.Read);
            builder.UseTexture(passData.objectId, AccessFlags.Read);
            builder.UseTexture(passData.normal, AccessFlags.Read);
            builder.UseTexture(passData.lighting, AccessFlags.Read);
            builder.UseTexture(passData.outline, AccessFlags.Read);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                data.material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
                data.material.SetTexture("_CartoonBaseColor", data.baseColor);
                data.material.SetTexture("_CartoonSceneColor", data.sceneColor);
                data.material.SetTexture("_CartoonDepth", data.depth);
                data.material.SetTexture("_CartoonObjectId", data.objectId);
                data.material.SetTexture("_CartoonNormal", data.normal);
                data.material.SetTexture("_CartoonLighting", data.lighting);
                data.material.SetTexture("_CartoonOutline", data.outline);
                data.material.SetInt("_DebugView", (int)data.debugView);
                data.material.SetColor("_OutlineColor", data.outlineColor);
                data.material.SetFloat("_SilhouetteStrength", data.silhouetteStrength);
                data.material.SetFloat("_OcclusionStrength", data.occlusionStrength);
                data.material.SetFloat("_DetailStrength", data.detailStrength);
                data.material.SetFloat("_RegionBoundaryStrength", data.regionBoundaryStrength);
                data.material.SetFloat("_HeroDetailOutlineStrength", data.heroDetailOutlineStrength);
                data.material.SetFloat("_OutlineEnabled", data.outlineEnabled);
                data.material.SetInt("_BackgroundMode", data.backgroundMode);
                data.material.SetColor("_BackgroundColor", data.backgroundColor);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
            });
        }
    }
}
