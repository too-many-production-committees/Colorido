using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    internal sealed class CartoonLightingPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle baseColor;
            public TextureHandle normal;
            public TextureHandle depth;
            public Vector3 lightDirection;
            public Color lightColor;
            public int colorBands;
            public float shadowThreshold;
            public float highlightThreshold;
            public float lightingEnabled;
            public int surfaceMode;
            public int lightingMode;
            public float shadowValue;
            public float shadowSaturation;
            public float shadowTemperature;
            public float paletteSaturation;
            public float paletteBrightness;
            public float preserveHeroDetails;
        }

        private readonly Material material;
        private CartoonRenderSettings settings;

        public CartoonLightingPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public void Setup(CartoonRenderSettings value) => settings = value;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cartoon = frameData.Get<CartoonFrameData>();
            cartoon.lighting = CartoonRenderGraphUtility.CreateTexture(renderGraph, cartoon.width, cartoon.height,
                cartoon.baseColor.GetDescriptor(renderGraph).format, "Cartoon Lighting", Color.clear);

            Vector3 direction = new(0.3f, 0.8f, 0.4f);
            Color color = Color.white;
            var lightData = frameData.Get<UniversalLightData>();
            if (lightData.mainLightIndex >= 0 && lightData.mainLightIndex < lightData.visibleLights.Length)
            {
                var light = lightData.visibleLights[lightData.mainLightIndex];
                direction = -light.localToWorldMatrix.GetColumn(2);
                color = light.finalColor;
            }

            using var builder = renderGraph.AddRasterRenderPass<PassData>("Cartoon Lighting Quantization", out var passData);
            passData.material = material;
            passData.baseColor = settings.UsesSceneColor ? cartoon.sceneColor : cartoon.baseColor;
            passData.normal = cartoon.normal;
            passData.depth = cartoon.depth;
            passData.lightDirection = direction;
            passData.lightColor = color;
            passData.colorBands = Mathf.Clamp(settings.colorBands, 2, 4);
            passData.shadowThreshold = settings.shadowThreshold;
            passData.highlightThreshold = Mathf.Max(settings.shadowThreshold + 0.01f, settings.highlightThreshold);
            passData.lightingEnabled = settings.lightingEnabled ? 1f : 0f;
            passData.surfaceMode = (int)settings.surfaceMode;
            passData.lightingMode = (int)settings.lightingMode;
            passData.shadowValue = settings.shadowValue;
            passData.shadowSaturation = settings.shadowSaturation;
            passData.shadowTemperature = settings.shadowTemperature;
            passData.paletteSaturation = settings.paletteSaturation;
            passData.paletteBrightness = settings.paletteBrightness;
            passData.preserveHeroDetails = settings.preserveHeroDetails ? 1f : 0f;
            builder.UseTexture(passData.baseColor, AccessFlags.Read);
            builder.UseTexture(passData.normal, AccessFlags.Read);
            builder.UseTexture(passData.depth, AccessFlags.Read);
            builder.SetRenderAttachment(cartoon.lighting, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                data.material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
                data.material.SetTexture("_CartoonBaseColor", data.baseColor);
                data.material.SetTexture("_CartoonNormal", data.normal);
                data.material.SetTexture("_CartoonDepth", data.depth);
                data.material.SetVector("_LightDirection", new Vector4(data.lightDirection.x, data.lightDirection.y, data.lightDirection.z, 0f));
                data.material.SetColor("_LightColor", data.lightColor);
                data.material.SetInt("_ColorBands", data.colorBands);
                data.material.SetFloat("_ShadowThreshold", data.shadowThreshold);
                data.material.SetFloat("_HighlightThreshold", data.highlightThreshold);
                data.material.SetFloat("_LightingEnabled", data.lightingEnabled);
                data.material.SetInt("_SurfaceMode", data.surfaceMode);
                data.material.SetInt("_LightingMode", data.lightingMode);
                data.material.SetFloat("_ShadowValue", data.shadowValue);
                data.material.SetFloat("_ShadowSaturation", data.shadowSaturation);
                data.material.SetFloat("_ShadowTemperature", data.shadowTemperature);
                data.material.SetFloat("_PaletteSaturation", data.paletteSaturation);
                data.material.SetFloat("_PaletteBrightness", data.paletteBrightness);
                data.material.SetFloat("_PreserveHeroDetails", data.preserveHeroDetails);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
            });
        }
    }
}
